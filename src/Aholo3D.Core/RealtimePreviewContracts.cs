using System.Text.Json;
namespace Aholo3D.Core;

public sealed record RealtimeStartRequest(Guid Id,string MeshId,RenderCamera Camera,int Width=1280,int Height=720,
    double EnvironmentIntensity=1,bool Gaussian=false,bool GltfCoordinates=true,RenderModelPlacement? Model=null,RenderLight[]? Lights=null,RealtimeSceneLayout? Scene=null);
public sealed record RealtimeSceneRequest(Guid Id,long Revision,RenderModelPlacement Model,double EnvironmentIntensity,RenderLight[] Lights);
public sealed record RealtimeCameraRequest(Guid Id,long Revision,RenderCamera Camera);
public sealed record RealtimePreviewRecord
{
    public int Version{get;init;}=1;
    public Guid Id{get;init;}
    public string Region{get;init;}=Aholo3DRegions.International;
    public string ConnectionId{get;init;}="";
    public string Epoch{get;init;}="";
    public string MeshId{get;init;}="";
    public string? SessionId{get;init;}
    public string Stage{get;init;}="Creating";
    public bool NeedsCleanup{get;init;}=true;
    public string? Error{get;init;}
    public DateTimeOffset UpdatedAt{get;init;}=DateTimeOffset.UtcNow;
}
public sealed record RealtimePreviewState(RealtimePreviewRecord Record,long FrameSequence=0,DateTimeOffset? ReceivedAt=null,long AcknowledgedCamera=0,long AcceptedScene=0,long AcknowledgedScene=0,RealtimeSceneLayout? Scene=null);
public sealed record RealtimePreviewReply(RealtimePreviewState? State=null,RealtimePreviewRecord[]? Records=null,string? Error=null,AccountSessionState? Account=null);
public sealed record RealtimePreviewPacket(RealtimePreviewReply Reply,byte[]? Jpeg=null);
public sealed record RealtimeControlRequest(int Version,Guid RequestId,string Nonce,string Method,JsonElement Payload,string? Epoch)
{
    public override string ToString()=>"Aholo3D realtime control request (authentication omitted)";
}
public sealed class RealtimeControlException : InvalidOperationException
{public RealtimeControlException(string message):base(message){}}

public interface IRealtimePreviewClient
{
    Task<RealtimePreviewPacket> RealtimeAsync(string method,object payload,CancellationToken ct=default);
}
