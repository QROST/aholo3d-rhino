using System.Buffers.Binary;
using System.IO.Compression;
using System.Text;
namespace Aholo.Core;

/// <summary>Private 8-bit RGBA-to-RGB conversion. RGB samples are not premultiplied, blended or color-converted.</summary>
internal static class PngOpaqueCopy
{
    private static InvalidDataException Invalid()=>new(UiText.Get("The PNG pixel stream is invalid or truncated; the previous export was preserved."));
    private static InvalidDataException Capacity()=>new(UiText.Get("The PNG row or encoded image exceeds the runtime byte-array capacity."));
    private static readonly (int X,int Y,int Dx,int Dy)[] Adam7={(0,0,8,8),(4,0,8,8),(0,4,4,8),(2,0,4,4),(0,2,2,4),(1,0,2,2),(0,1,1,2)};
    public static byte[] Convert(ReadOnlyMemory<byte> encoded,CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();
        // Caller is an immutable MeshTexture, whose PNG signature/chunk structure/CRC were checked already.
        var header=encoded.Slice(16,13).ToArray();
        if(header[8]!=8||header[9]!=6)throw Invalid();
        int width=checked((int)BinaryPrimitives.ReadUInt32BigEndian(header)),height=checked((int)BinaryPrimitives.ReadUInt32BigEndian(header.AsSpan(4)));
        var segments=new List<ReadOnlyMemory<byte>>();var colorChunks=new List<(string Type,byte[] Data)>();var seen=new HashSet<string>(StringComparer.Ordinal);
        for(int offset=8;offset<encoded.Length;)
        {
            ct.ThrowIfCancellationRequested();int length=checked((int)BinaryPrimitives.ReadUInt32BigEndian(encoded.Span[offset..]));
            string type=Encoding.ASCII.GetString(encoded.Span.Slice(offset+4,4));var data=encoded.Slice(offset+8,length);
            if(type=="IDAT")segments.Add(data);
            else if(type is "acTL" or "fcTL" or "fdAT")throw new InvalidDataException(UiText.Get("Animated PNG alpha normalization is unsupported. Use a static RGB/RGBA image."));
            else if(type is "sRGB" or "gAMA" or "cHRM" or "cICP" or "sBIT")
            {
                if(!seen.Add(type))throw Invalid();
                if(type=="sBIT")
                {
                    if(data.Length!=4||data.Span.ToArray().Any(v=>v is <1 or >8))throw Invalid();
                    colorChunks.Add((type,data.Slice(0,3).ToArray()));
                }
                else colorChunks.Add((type,data.ToArray()));
            }
            offset=checked(offset+length+12);
        }
        header[9]=2;
        using var output=new MemoryStream();output.Write(encoded.Span[..8]);WriteChunk(output,"IHDR",header,ct);
        foreach(var chunk in colorChunks)WriteChunk(output,chunk.Type,chunk.Data,ct);
        using var source=new IdatSource(segments,ct);
        using var inflate=new ZLibStream(source,CompressionMode.Decompress,leaveOpen:true);
        using(var chunks=new IdatOutput(output,ct))
        {
            using(var compress=new ZLibStream(chunks,CompressionLevel.Optimal,leaveOpen:true))
            {
                var passes=header[12]==0?new[]{(X:0,Y:0,Dx:1,Dy:1)}:Adam7;
                foreach(var pass in passes)
                {
                    int columns=Extent(width,pass.X,pass.Dx),rows=Extent(height,pass.Y,pass.Dy);
                    if(columns==0||rows==0)continue;
                    long rgbaLength=(long)columns*4,rgbLength=(long)columns*3;
                    if(rgbaLength>Array.MaxLength||rgbLength>Array.MaxLength)throw Capacity();
                    byte[] previous=new byte[(int)rgbaLength],current=new byte[(int)rgbaLength],rgb=new byte[(int)rgbLength];
                    for(int row=0;row<rows;row++)
                    {
                        ct.ThrowIfCancellationRequested();int filter=inflate.ReadByte();if(filter is <0 or >4)throw Invalid();
                        ReadExact(inflate,current,ct);
                        for(int i=0;i<current.Length;i++)
                        {
                            if((i&16383)==0)ct.ThrowIfCancellationRequested();
                            int left=i>=4?current[i-4]:0,up=previous[i],corner=i>=4?previous[i-4]:0;
                            int prediction=filter switch{0=>0,1=>left,2=>up,3=>(left+up)/2,_=>Paeth(left,up,corner)};
                            current[i]=unchecked((byte)(current[i]+prediction));
                        }
                        for(int x=0;x<columns;x++)
                        {
                            if((x&4095)==0)ct.ThrowIfCancellationRequested();
                            rgb[x*3]=current[x*4];rgb[x*3+1]=current[x*4+1];rgb[x*3+2]=current[x*4+2];
                        }
                        compress.WriteByte(0); // Emit unfiltered RGB rows; retain Adam7 pass order and image dimensions.
                        for(int at=0;at<rgb.Length;)
                        {ct.ThrowIfCancellationRequested();int count=Math.Min(65536,rgb.Length-at);compress.Write(rgb,at,count);at+=count;}
                        (current,previous)=(previous,current);
                    }
                }
                // Require exact decompressed size and a complete zlib stream/checksum. Legal compressed trailing padding is ignored.
                if(inflate.ReadByte()!=-1)throw Invalid();
            }
            chunks.Flush();
        }
        WriteChunk(output,"IEND",ReadOnlySpan<byte>.Empty,ct);ct.ThrowIfCancellationRequested();return output.ToArray();
    }
    private static int Extent(int size,int start,int step)=>size<=start?0:(int)(((long)size-start+step-1)/step);
    private static int Paeth(int left,int up,int corner)
    {
        int p=left+up-corner,a=Math.Abs(p-left),b=Math.Abs(p-up),c=Math.Abs(p-corner);
        return a<=b&&a<=c?left:b<=c?up:corner;
    }
    private static void ReadExact(Stream source,byte[] target,CancellationToken ct)
    {
        for(int at=0;at<target.Length;)
        {ct.ThrowIfCancellationRequested();int read=source.Read(target,at,Math.Min(65536,target.Length-at));if(read<=0)throw Invalid();at+=read;}
    }
    private static void WriteChunk(Stream output,string type,ReadOnlySpan<byte> data,CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();if(output.Position+12L+data.Length>Array.MaxLength)throw Capacity();
        Span<byte> header=stackalloc byte[8];BinaryPrimitives.WriteUInt32BigEndian(header,(uint)data.Length);Encoding.ASCII.GetBytes(type,header[4..]);output.Write(header);
        uint crc=Update(0xffffffff,header[4..]);
        for(int at=0;at<data.Length;)
        {ct.ThrowIfCancellationRequested();int count=Math.Min(16384,data.Length-at);var block=data.Slice(at,count);crc=Update(crc,block);output.Write(block);at+=count;}
        Span<byte> checksum=stackalloc byte[4];BinaryPrimitives.WriteUInt32BigEndian(checksum,~crc);output.Write(checksum);
    }
    private static uint Update(uint crc,ReadOnlySpan<byte> bytes)
    {
        foreach(byte value in bytes){crc^=value;for(int bit=0;bit<8;bit++)crc=(crc&1)!=0?(crc>>1)^0xedb88320:crc>>1;}return crc;
    }
    // .NET 7/8 decompression can return zero at physical EOF before zlib is Finished.
    // Throw instead: a complete zlib decoder returns EOF without asking this source for more bytes.
    private sealed class IdatSource(IReadOnlyList<ReadOnlyMemory<byte>> segments,CancellationToken ct):Stream
    {
        private int segment,offset;
        public override int Read(Span<byte> buffer)
        {
            ct.ThrowIfCancellationRequested();if(buffer.IsEmpty)return 0;
            while(segment<segments.Count&&offset==segments[segment].Length){segment++;offset=0;}
            if(segment==segments.Count)throw Invalid();
            int count=Math.Min(buffer.Length,segments[segment].Length-offset);segments[segment].Span.Slice(offset,count).CopyTo(buffer);offset+=count;return count;
        }
        public override int Read(byte[] buffer,int offset,int count)=>Read(buffer.AsSpan(offset,count));
        public override bool CanRead=>true;public override bool CanWrite=>false;public override bool CanSeek=>false;
        public override long Length=>throw new NotSupportedException();public override long Position{get=>throw new NotSupportedException();set=>throw new NotSupportedException();}
        public override void Flush(){}public override long Seek(long offset,SeekOrigin origin)=>throw new NotSupportedException();
        public override void SetLength(long value)=>throw new NotSupportedException();public override void Write(byte[] buffer,int offset,int count)=>throw new NotSupportedException();
    }
    private sealed class IdatOutput(Stream output,CancellationToken ct):Stream
    {
        private readonly byte[] buffer=new byte[65536];private int used;
        public override void Write(ReadOnlySpan<byte> data)
        {
            while(!data.IsEmpty)
            {
                ct.ThrowIfCancellationRequested();int count=Math.Min(buffer.Length-used,data.Length);data[..count].CopyTo(buffer.AsSpan(used));used+=count;data=data[count..];if(used==buffer.Length)Flush();
            }
        }
        public override void Write(byte[] data,int offset,int count)=>Write(data.AsSpan(offset,count));
        public override void Flush(){if(used==0)return;WriteChunk(output,"IDAT",buffer.AsSpan(0,used),ct);used=0;}
        public override bool CanRead=>false;public override bool CanWrite=>true;public override bool CanSeek=>false;
        public override long Length=>throw new NotSupportedException();public override long Position{get=>throw new NotSupportedException();set=>throw new NotSupportedException();}
        public override long Seek(long offset,SeekOrigin origin)=>throw new NotSupportedException();public override int Read(byte[] buffer,int offset,int count)=>throw new NotSupportedException();public override void SetLength(long value)=>throw new NotSupportedException();
    }
}
