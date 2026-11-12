using System.IO.Pipes;
using System.Security.Cryptography;
using Aholo3D.Core;
using Aholo3D.Provider;
using Aholo3D.Sidecar;
namespace Aholo3D.Tests;
public sealed class RealtimeControlServerTests
{
    [Fact]
    public async Task RealPrivatePipeRejectsWrongNonceAndServesIndependentControlRequests()
    {
        string root=Path.Combine(Path.GetTempPath(),"aholo3d-pipe-tests-"+Guid.NewGuid()),pipeName="aholo3d-rt-"+Guid.NewGuid().ToString("N"),nonce=Convert.ToHexString(RandomNumberGenerator.GetBytes(32));
        var journal=new RealtimeJournal(root);var credentials=new CredentialCoordinator(new EmptyStore(),Path.Combine(root,"credentials"));
        var manager=new RealtimePreviewManager(credentials,journal,(_,_)=>throw new InvalidOperationException("No paid operation allowed in this test."));
        try
        {
            await using var server=new RealtimeControlServer(pipeName,nonce,manager);server.Start();
            using var deadline=new CancellationTokenSource(TimeSpan.FromSeconds(5));
            async Task<RealtimePreviewPacket> Request(string authentication)
            {
                using var client=new NamedPipeClientStream(".",pipeName,PipeDirection.InOut,PipeOptions.Asynchronous|PipeOptions.CurrentUserOnly);
                await client.ConnectAsync(deadline.Token);
                await RealtimeWire.WriteJsonAsync(client,new RealtimeControlRequest(1,Guid.NewGuid(),authentication,"pending",Aholo3DJson.Element(new{}),null),RealtimeWire.MaximumRequestBytes,deadline.Token);
                await client.FlushAsync(deadline.Token);return await RealtimeWire.ReadPacketAsync(client,deadline.Token);
            }
            await Assert.ThrowsAnyAsync<IOException>(()=>Request(new string('0',64)));
            var replies=await Task.WhenAll(Request(nonce),Request(nonce));
            Assert.All(replies,r=>{Assert.Empty(r.Reply.Records!);Assert.Null(r.Jpeg);Assert.Null(r.Reply.Error);});
        }
        finally{await manager.ShutdownAsync(default);Directory.Delete(root,true);}
    }
    [Fact]
    public async Task ParentExitCancelsLongRequestLifetimeWithoutACloudCall()
    {
        using var process=System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo("dotnet","--version"){UseShellExecute=false,RedirectStandardOutput=true,RedirectStandardError=true})!;
        using var parentLost=new CancellationTokenSource();using var monitor=new ParentProcessLifetime(process.Id,parentLost);
        await monitor.Completion.WaitAsync(TimeSpan.FromSeconds(5));Assert.True(parentLost.IsCancellationRequested);
    }
    [Theory]
    [InlineData(1280,720,true)]
    [InlineData(65535,65535,false)]
    [InlineData(0,720,false)]
    public void JpegDimensionsAreCheckedBeforePlatformDecoding(int width,int height,bool accepted)
    {
        byte[] bytes={255,216,255,192,0,11,8,(byte)(height>>8),(byte)height,(byte)(width>>8),(byte)width,1,1,17,0,255,217};
        if(accepted)Assert.Equal((width,height),JpegFrameSize.Validate(bytes,4500L*4500));
        else Assert.Throws<InvalidDataException>(()=>JpegFrameSize.Validate(bytes,4500L*4500));
    }
    [Fact]
    public void PartialOrUnsupportedJpegHeadersNeverReachImageDecoder()
    {
        Assert.Throws<InvalidDataException>(()=>JpegFrameSize.Validate(new byte[]{255,216,255,192,0,12,0},2000));
        Assert.Throws<InvalidDataException>(()=>JpegFrameSize.Validate(new byte[]{255,216,255,217},2000));
    }
    private sealed class EmptyStore:ICredentialStore
    {
        public Task<string?> GetAsync(string account,CancellationToken ct=default)=>Task.FromResult<string?>(null);
        public Task SetAsync(string account,string secret,CancellationToken ct=default)=>throw new InvalidOperationException();
        public Task DeleteAsync(string account,CancellationToken ct=default)=>throw new InvalidOperationException();
    }
}
