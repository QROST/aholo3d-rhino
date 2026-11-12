#!/usr/bin/env python3
"""Offline multi-process OAuth-coordinator checks. Uses synthetic tokens and a disposable fixture store, never OS credentials or network."""
import pathlib
import subprocess
import tempfile
import time
import xml.sax.saxutils

ROOT = pathlib.Path(__file__).resolve().parent.parent
PROGRAM = r'''
using Aholo3D.Provider;
using System.Text;
string role=args[0],root=args[1];Directory.CreateDirectory(root);
var store=new FixtureStore(root,role=="hold-save");
var coordinator=new OAuthSessionCoordinator(store,Path.Combine(root,"state"));
OAuthToken Token()=>new(){AccessToken="fixture-only-access",RefreshToken="fixture-only-refresh",ClientId="fixture-client",Scope="get_user_info",ExpiresAt=DateTimeOffset.UtcNow.AddMinutes(30)};
if(role=="seed"||role=="hold-save")
{
    await store.SetAsync(CredentialCoordinator.Account,"fixture-api-key");
    var lease=await coordinator.BeginAsync(null,false,true,default);
    await coordinator.CompleteAsync(lease,Token(),default);
}
else if(role=="hold-refresh")
{
    var saved=await coordinator.ReadAsync();
    var lease=await coordinator.BeginAsync(saved.Epoch,true,true,default);
    File.WriteAllText(Path.Combine(root,"entered"),"ready");
    await FixtureStore.Wait(root);
    try{await coordinator.CompleteAsync(lease,Token(),default);return 3;}
    catch(ProviderException error)when(error.Code=="oauth_session_changed"){}
}
else if(role=="signout")
{
    File.WriteAllText(Path.Combine(root,"logout-started"),"ready");
    await coordinator.SignOutAsync();
    File.WriteAllText(Path.Combine(root,"logout-done"),"done");
}
else if(role=="verify-empty")
{
    if((await coordinator.ReadAsync()).Token is not null)return 4;
    if(await store.GetAsync(CredentialCoordinator.Account)!="fixture-api-key")return 5;
    foreach(string path in Directory.GetFiles(Path.Combine(root,"state")))
        if(File.ReadAllText(path).Contains("fixture-only"))return 6;
}
else return 2;
return 0;
sealed class FixtureStore(string root,bool hold):ICredentialStore
{
    string PathFor(string account)=>Path.Combine(root,Convert.ToHexString(Encoding.UTF8.GetBytes(account))+".fixture");
    public Task<string?> GetAsync(string account,CancellationToken ct=default)=>Task.FromResult(File.Exists(PathFor(account))?File.ReadAllText(PathFor(account)):null);
    public async Task SetAsync(string account,string value,CancellationToken ct=default)
    {
        File.WriteAllText(PathFor(account),value);
        if(hold&&account==OAuthSessionCoordinator.Account)
        {File.WriteAllText(Path.Combine(root,"entered"),"ready");await Wait(root);}
    }
    public Task DeleteAsync(string account,CancellationToken ct=default){ct.ThrowIfCancellationRequested();File.Delete(PathFor(account));return Task.CompletedTask;}
    public static async Task Wait(string root)
    {
        using var deadline=new CancellationTokenSource(TimeSpan.FromSeconds(15));
        while(!File.Exists(Path.Combine(root,"release")))await Task.Delay(25,deadline.Token);
    }
}
'''

def wait_file(path, child):
    deadline = time.monotonic() + 12
    while not path.exists():
        if child.poll() is not None:
            raise RuntimeError(f"Fixture child exited early: {child.returncode}")
        if time.monotonic() > deadline:
            raise TimeoutError("Fixture child did not reach its synchronization point")
        time.sleep(.025)

def finish(child):
    output, error = child.communicate(timeout=15)
    if child.returncode:
        raise RuntimeError(f"Fixture child failed ({child.returncode}): {output}\n{error}")

def main():
    children = []
    with tempfile.TemporaryDirectory(prefix="aholo3d-oauth-process-") as temporary:
        directory = pathlib.Path(temporary)
        references = []
        for name, project, framework in [("Aholo3D.Provider", "Aholo3D.Provider", "net8.0"), ("Aholo3D.Core", "Aholo3D.Core", "net7.0")]:
            dll = ROOT / "src" / project / "bin/Release" / framework / (name + ".dll")
            if not dll.is_file():
                raise RuntimeError("Build Aholo3D.sln Release before running this fixture")
            references.append(f'<Reference Include="{name}"><HintPath>{xml.sax.saxutils.escape(str(dll))}</HintPath></Reference>')
        (directory / "Fixture.csproj").write_text('<Project Sdk="Microsoft.NET.Sdk"><PropertyGroup><OutputType>Exe</OutputType><TargetFramework>net8.0</TargetFramework><ImplicitUsings>enable</ImplicitUsings><Nullable>enable</Nullable></PropertyGroup><ItemGroup>' + "".join(references) + '</ItemGroup></Project>')
        # The harness restores framework references from the installed SDK only; no packages/feeds.
        (directory / "NuGet.Config").write_text('<configuration><packageSources><clear /></packageSources></configuration>')
        (directory / "Program.cs").write_text(PROGRAM)
        subprocess.run(["dotnet", "build", "Fixture.csproj", "-c", "Release", "--nologo"], cwd=directory, check=True, capture_output=True, text=True)
        dll = directory / "bin/Release/net8.0/Fixture.dll"
        def start(role, root):
            process = subprocess.Popen(["dotnet", str(dll), role, str(root)], stdout=subprocess.PIPE, stderr=subprocess.PIPE, text=True)
            children.append(process)
            return process
        try:
            saving = directory / "saving"
            writer = start("hold-save", saving)
            wait_file(saving / "entered", writer)
            logout = start("signout", saving)
            wait_file(saving / "logout-started", logout)
            time.sleep(.15)
            assert logout.poll() is None and not (saving / "logout-done").exists(), "Sign-out bypassed the process lock"
            (saving / "release").write_text("release")
            finish(writer); finish(logout); finish(start("verify-empty", saving))

            refreshing = directory / "refreshing"
            finish(start("seed", refreshing))
            refresh = start("hold-refresh", refreshing)
            wait_file(refreshing / "entered", refresh)
            finish(start("signout", refreshing))
            (refreshing / "release").write_text("release")
            finish(refresh); finish(start("verify-empty", refreshing))
            print("PASS: separate processes serialize save/sign-out, reject a late refresh, preserve API-key scope, and keep tokens out of coordination files.")
            print("Synthetic fixture storage only. No Keychain/DPAPI, browser, authenticated service or Rhino acceptance was performed.")
        finally:
            for child in children:
                if child.poll() is None:
                    child.kill()
                child.communicate(timeout=5)

if __name__ == "__main__":
    main()
