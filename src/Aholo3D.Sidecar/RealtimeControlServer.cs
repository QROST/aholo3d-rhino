using System.IO.Pipes;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Aholo3D.Core;
namespace Aholo3D.Sidecar;

public sealed class RealtimeControlServer : IAsyncDisposable
{
    private readonly string pipeName;
    private readonly byte[] nonce;
    private readonly RealtimePreviewManager manager;
    private readonly Func<string,Guid,CancellationToken,Task<AccountSessionState>>? cancelAccount;
    private readonly CancellationTokenSource lifetime=new();
    private readonly List<NamedPipeServerStream> pipes=new();
    private Task? loop;
    public RealtimeControlServer(string pipeName,string nonce,RealtimePreviewManager manager,Func<string,Guid,CancellationToken,Task<AccountSessionState>>? cancelAccount=null)
    {
        if(!pipeName.StartsWith("aholo3d-rt-",StringComparison.Ordinal)||pipeName.Any(c=>!char.IsAsciiLetterOrDigit(c)&&c!='-')||nonce.Length!=64||nonce.Any(c=>!Uri.IsHexDigit(c)))throw new ArgumentException("Invalid private realtime endpoint.");
        this.pipeName=pipeName;this.nonce=Encoding.ASCII.GetBytes(nonce);this.manager=manager;this.cancelAccount=cancelAccount;
    }
    public void Start()
    {
        if(loop is not null)return;
        try
        {
            // Keep listener instances alive across clients. Disposing the last Unix pipe
            // instance between accepts can disconnect clients already in its backlog.
            for(int i=0;i<4;i++)pipes.Add(new(pipeName,PipeDirection.InOut,4,PipeTransmissionMode.Byte,PipeOptions.Asynchronous|PipeOptions.CurrentUserOnly));
            loop=Task.WhenAll(pipes.Select(AcceptAsync));
        }
        catch{foreach(var pipe in pipes)pipe.Dispose();throw;}
    }
    private async Task AcceptAsync(NamedPipeServerStream pipe)
    {
        try
        {
            while(!lifetime.IsCancellationRequested)
            {
                await pipe.WaitForConnectionAsync(lifetime.Token).ConfigureAwait(false);
                try{await ServeAsync(pipe).ConfigureAwait(false);}
                finally{if(pipe.IsConnected)pipe.Disconnect();}
            }
        }
        catch(OperationCanceledException)when(lifetime.IsCancellationRequested){}
        finally{pipe.Dispose();}
    }
    private async Task ServeAsync(NamedPipeServerStream pipe)
    {
        {
            using var deadline=CancellationTokenSource.CreateLinkedTokenSource(lifetime.Token);deadline.CancelAfter(TimeSpan.FromSeconds(30));
            try
            {
                var request=await RealtimeWire.ReadJsonAsync<RealtimeControlRequest>(pipe,RealtimeWire.MaximumRequestBytes,deadline.Token).ConfigureAwait(false);
                byte[] received=Encoding.ASCII.GetBytes(request.Nonce??"");
                if(request.Version!=1||request.RequestId==Guid.Empty||received.Length!=nonce.Length||!CryptographicOperations.FixedTimeEquals(received,nonce))throw new IOException("Private realtime authentication failed.");
                RealtimePreviewPacket response;
                try
                {
                    var p=request.Payload;
                    response=request.Method switch
                    {
                        "cancelAccount" when cancelAccount is not null=>new(new(Account:await cancelAccount(p.GetProperty("region").GetString()??"",p.GetProperty("attemptId").GetGuid(),deadline.Token).ConfigureAwait(false))),
                        "start"=>await manager.StartAsync(Read<RealtimeStartRequest>(p),request.Epoch,deadline.Token).ConfigureAwait(false),
                        "get"=>manager.Get(p.GetProperty("id").GetGuid(),p.GetProperty("afterFrame").GetInt64(),p.TryGetProperty("includeScene",out var includeScene)&&includeScene.GetBoolean()),
                        "pending"=>manager.Pending(),
                        "confirmExternalResolution"=>await manager.ConfirmExternalResolutionAsync(p.GetProperty("id").GetGuid(),request.Epoch,deadline.Token).ConfigureAwait(false),
                        "composition"=>await manager.CompositionAsync(Read<RealtimeCompositionRequest>(p),request.Epoch,deadline.Token).ConfigureAwait(false),
                        "scene"=>await manager.SceneAsync(Read<RealtimeSceneRequest>(p),request.Epoch,deadline.Token).ConfigureAwait(false),
                        "camera"=>await manager.CameraAsync(Read<RealtimeCameraRequest>(p),request.Epoch,deadline.Token).ConfigureAwait(false),
                        "stop"=>await manager.StopAsync(p.GetProperty("id").GetGuid(),request.Epoch,deadline.Token).ConfigureAwait(false),
                        _=>throw new ArgumentException("Unknown realtime operation.")
                    };
                }
                catch(Exception error)
                {
                    string message=error is Provider.ProviderException or InvalidOperationException?error.Message:
                        error is OperationCanceledException?"Realtime control timed out. Inspect the original session; no new session was submitted.":"Realtime control failed. Check the original session and local journal.";
                    response=new(new(Error:message));
                }
                await RealtimeWire.WritePacketAsync(pipe,response,deadline.Token).ConfigureAwait(false);
            }
            catch(Exception error)when(error is IOException or OperationCanceledException or JsonException or ArgumentException){}
        }
    }
    private static T Read<T>(JsonElement value)=>value.Deserialize<T>(Aholo3DJson.Options)??throw new ArgumentException("Missing realtime request.");
    public async ValueTask DisposeAsync()
    {
        lifetime.Cancel();if(loop is not null)await loop.ConfigureAwait(false);

    }
}
