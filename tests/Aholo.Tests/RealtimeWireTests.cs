using System.Buffers.Binary;
using Aholo.Core;
namespace Aholo.Tests;
public sealed class RealtimeWireTests
{
    [Fact]
    public async Task BinaryFrameCanExceedOrdinaryRpcSizeWithoutBase64OrPermanentFiles()
    {
        byte[] jpeg=new byte[3*1024*1024];jpeg[0]=255;jpeg[1]=216;jpeg[^2]=255;jpeg[^1]=217;
        var packet=new RealtimePreviewPacket(new(State:new(new RealtimePreviewRecord{Id=Guid.NewGuid()},7,DateTimeOffset.UtcNow)),jpeg);
        using var stream=new MemoryStream();await RealtimeWire.WritePacketAsync(stream,packet,default);stream.Position=0;
        var decoded=await RealtimeWire.ReadPacketAsync(stream,default);Assert.Equal(packet.Reply.State,decoded.Reply.State);Assert.Equal(jpeg,decoded.Jpeg);
    }
    [Theory]
    [InlineData(-1)]
    [InlineData(33554433)]
    public async Task InvalidLengthsAreRejectedBeforeAllocatingFrame(int length)
    {
        byte[] bytes=new byte[4];BinaryPrimitives.WriteInt32LittleEndian(bytes,length);using var stream=new MemoryStream(bytes);
        await Assert.ThrowsAsync<IOException>(()=>RealtimeWire.ReadBytesAsync(stream,RealtimeWire.MaximumJpegBytes,default));
    }
    [Fact]
    public async Task TruncatedPayloadAndCancelledReadsCannotBecomeFrames()
    {
        using var stream=new MemoryStream(new byte[]{4,0,0,0,255});await Assert.ThrowsAsync<EndOfStreamException>(()=>RealtimeWire.ReadBytesAsync(stream,100,default));
        stream.Position=0;await Assert.ThrowsAnyAsync<OperationCanceledException>(()=>RealtimeWire.ReadBytesAsync(stream,100,new CancellationToken(true)));
    }
    [Fact]
    public void CameraPatchUsesPublishedOverNodesAndNoGeometryPayload()
    {
        var request=new RealtimeStartRequest(Guid.NewGuid(),"mesh",RealtimePreviewManagerTests.Camera());string patch=RealtimeScene.CameraPatch(request);
        Assert.Contains("over \"MainCamera\"",patch);Assert.DoesNotContain("payload",patch);Assert.Contains("focalLength",patch);
        string gs=RealtimeScene.Build(request with{Gaussian=true,GltfCoordinates=false});Assert.Contains("ParticleField3DGaussianSplat",gs);Assert.Contains("</GaussianSplatData>",gs);Assert.DoesNotContain("(1000, 0, 0, 0)",gs);
    }
}
