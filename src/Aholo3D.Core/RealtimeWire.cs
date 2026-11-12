using System.Buffers.Binary;
using System.Text.Json;
namespace Aholo3D.Core;

/// <summary>Length-delimited private IPC; JPEG bytes never enter the ordinary JSON line protocol.</summary>
public static class RealtimeWire
{
    public const int MaximumRequestBytes=128*1024;
    public const int MaximumReplyBytes=2*1024*1024;
    public const int MaximumJpegBytes=32*1024*1024;
    public static async Task WriteBytesAsync(Stream stream,ReadOnlyMemory<byte> bytes,int maximum,CancellationToken ct)
    {
        if(bytes.Length>maximum)throw new IOException("Realtime IPC payload exceeds its wire capacity.");
        byte[] size=new byte[4];BinaryPrimitives.WriteInt32LittleEndian(size,bytes.Length);
        await stream.WriteAsync(size,ct).ConfigureAwait(false);await stream.WriteAsync(bytes,ct).ConfigureAwait(false);
    }
    public static async Task<byte[]> ReadBytesAsync(Stream stream,int maximum,CancellationToken ct)
    {
        byte[] size=new byte[4];await stream.ReadExactlyAsync(size,ct).ConfigureAwait(false);
        int count=BinaryPrimitives.ReadInt32LittleEndian(size);
        if(count<0||count>maximum)throw new IOException("Invalid realtime IPC payload length.");
        byte[] bytes=new byte[count];await stream.ReadExactlyAsync(bytes,ct).ConfigureAwait(false);return bytes;
    }
    public static Task WriteJsonAsync<T>(Stream stream,T value,int maximum,CancellationToken ct)
        =>WriteBytesAsync(stream,JsonSerializer.SerializeToUtf8Bytes(value,Aholo3DJson.Options),maximum,ct);
    public static async Task<T> ReadJsonAsync<T>(Stream stream,int maximum,CancellationToken ct)
        =>JsonSerializer.Deserialize<T>(await ReadBytesAsync(stream,maximum,ct).ConfigureAwait(false),Aholo3DJson.Options)??throw new IOException("Empty realtime IPC response.");
    public static async Task WritePacketAsync(Stream stream,RealtimePreviewPacket packet,CancellationToken ct)
    {
        await WriteJsonAsync(stream,packet.Reply,MaximumReplyBytes,ct).ConfigureAwait(false);
        await WriteBytesAsync(stream,packet.Jpeg??Array.Empty<byte>(),MaximumJpegBytes,ct).ConfigureAwait(false);
        await stream.FlushAsync(ct).ConfigureAwait(false);
    }
    public static async Task<RealtimePreviewPacket> ReadPacketAsync(Stream stream,CancellationToken ct)
    {
        var reply=await ReadJsonAsync<RealtimePreviewReply>(stream,MaximumReplyBytes,ct).ConfigureAwait(false);
        byte[] jpeg=await ReadBytesAsync(stream,MaximumJpegBytes,ct).ConfigureAwait(false);
        if(jpeg.Length>0&&(jpeg.Length<4||jpeg[0]!=0xff||jpeg[1]!=0xd8||jpeg[^2]!=0xff||jpeg[^1]!=0xd9))throw new IOException("Invalid realtime JPEG payload.");
        return new(reply,jpeg.Length==0?null:jpeg);
    }
}
