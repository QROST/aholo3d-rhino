using System.IO.Pipes;
using System.Security.Cryptography;
namespace Aholo3D.Core;

public sealed partial class Aholo3DClient : IRealtimePreviewClient
{
    private sealed record ControlEndpoint(string Name,string Nonce)
    {public override string ToString()=>"Private realtime endpoint (authentication omitted)";}
    private ControlEndpoint? realtimeEndpoint;
    private ControlEndpoint NewEndpoint()=>new("aholo3d-rt-"+Guid.NewGuid().ToString("N"),Convert.ToHexString(RandomNumberGenerator.GetBytes(32)));
    public async Task<RealtimePreviewPacket> RealtimeAsync(string method,object payload,CancellationToken ct=default)
    {
        // This separate pipe never waits behind an upload on the ordinary RPC semaphore.
        var endpoint=Volatile.Read(ref realtimeEndpoint)??throw new InvalidOperationException("Restore the connection before using cloud preview.");
        var epoch=Volatile.Read(ref _credentialEpoch);
        using var timeout=CancellationTokenSource.CreateLinkedTokenSource(ct);timeout.CancelAfter(TimeSpan.FromSeconds(35));
        using var pipe=new NamedPipeClientStream(".",endpoint.Name,PipeDirection.InOut,PipeOptions.Asynchronous|PipeOptions.CurrentUserOnly);
        await pipe.ConnectAsync(timeout.Token).ConfigureAwait(false);
        var request=new RealtimeControlRequest(1,Guid.NewGuid(),endpoint.Nonce,method,Aholo3DJson.Element(payload),epoch);
        await RealtimeWire.WriteJsonAsync(pipe,request,RealtimeWire.MaximumRequestBytes,timeout.Token).ConfigureAwait(false);
        await pipe.FlushAsync(timeout.Token).ConfigureAwait(false);
        var result=await RealtimeWire.ReadPacketAsync(pipe,timeout.Token).ConfigureAwait(false);
        if(!ReferenceEquals(endpoint,Volatile.Read(ref realtimeEndpoint)))throw new IOException("The background process changed. Inspect cloud preview cleanup before continuing.");
        if(result.Reply.Error is not null)throw new RealtimeControlException(result.Reply.Error);
        return result;
    }
}
