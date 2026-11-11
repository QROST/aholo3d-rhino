using System.Buffers.Binary;
using System.IO.Compression;
using System.Text;
using System.Text.Json;
using Aholo.Core;
namespace Aholo.Tests;

public sealed class PngOpaqueCopyTests
{
    private static readonly (int X,int Y,int Dx,int Dy)[] Passes={(0,0,8,8),(4,0,8,8),(0,4,4,8),(2,0,4,4),(0,2,2,4),(1,0,2,2),(0,1,1,2)};
    private static byte[] Pixel(int x,int y)=>new[]{(byte)(31+x*19+y*7),(byte)(197+x*3+y*11),(byte)(83+x*13+y*5),(byte)(((x+y)%4)*85)};
    private static byte[] Chunk(string type,byte[] data)
    {
        byte[] result=new byte[12+data.Length];BinaryPrimitives.WriteInt32BigEndian(result,data.Length);Encoding.ASCII.GetBytes(type).CopyTo(result,4);data.CopyTo(result,8);
        uint crc=uint.MaxValue;foreach(byte b in result.AsSpan(4,data.Length+4)){crc^=b;for(int i=0;i<8;i++)crc=(crc&1)!=0?(crc>>1)^0xedb88320:crc>>1;}
        BinaryPrimitives.WriteUInt32BigEndian(result.AsSpan(8+data.Length),~crc);return result;
    }
    private static byte[] Package(int width,int height,bool interlace,byte[] compressed,int split=7,byte[]? metadata=null)
    {
        byte[] header=new byte[13];BinaryPrimitives.WriteInt32BigEndian(header,width);BinaryPrimitives.WriteInt32BigEndian(header.AsSpan(4),height);header[8]=8;header[9]=6;header[12]=(byte)(interlace?1:0);
        using var result=new MemoryStream();result.Write(new byte[]{137,80,78,71,13,10,26,10});result.Write(Chunk("IHDR",header));if(metadata is not null)result.Write(metadata);
        result.Write(Chunk("IDAT",Array.Empty<byte>()));for(int at=0;at<compressed.Length;at+=split)result.Write(Chunk("IDAT",compressed.Skip(at).Take(split).ToArray()));result.Write(Chunk("IEND",Array.Empty<byte>()));return result.ToArray();
    }
    private static byte[] Deflate(byte[] input)
    {using var output=new MemoryStream();using(var z=new ZLibStream(output,CompressionLevel.SmallestSize,true))z.Write(input);return output.ToArray();}
    private static byte[] Scanlines(int width,int height,bool interlace,int filter)
    {
        using var result=new MemoryStream();
        foreach(var pass in interlace?Passes:new[]{(X:0,Y:0,Dx:1,Dy:1)})
        {
            if(pass.X>=width||pass.Y>=height)continue;byte[]? previous=null;
            for(int y=pass.Y;y<height;y+=pass.Dy)
            {
                byte[] row=Enumerable.Range(0,width).Where(x=>x>=pass.X&&(x-pass.X)%pass.Dx==0).SelectMany(x=>Pixel(x,y)).ToArray();
                previous??=new byte[row.Length];int kind=filter<0?y%5:filter;result.WriteByte((byte)kind);
                for(int i=0;i<row.Length;i++)
                {
                    int a=i>=4?row[i-4]:0,b=previous[i],c=i>=4?previous[i-4]:0,p=a+b-c;
                    int pred=kind switch{0=>0,1=>a,2=>b,3=>(a+b)/2,_=>new[]{a,b,c}.OrderBy(v=>Math.Abs(p-v)).First()};
                    result.WriteByte(unchecked((byte)(row[i]-pred)));
                }
                previous=row;
            }
        }
        return result.ToArray();
    }
    private static Dictionary<string,List<byte[]>> Chunks(byte[] bytes)
    {
        var chunks=new Dictionary<string,List<byte[]>>();for(int at=8;at<bytes.Length;)
        {int size=BinaryPrimitives.ReadInt32BigEndian(bytes.AsSpan(at));string type=Encoding.ASCII.GetString(bytes,at+4,4);if(!chunks.TryGetValue(type,out var list))chunks.Add(type,list=new());list.Add(bytes.AsSpan(at+8,size).ToArray());at+=size+12;}return chunks;
    }
    private static byte[] Bytes(MeshTexture image){using var output=new MemoryStream();image.CopyEncodedTo(output);return output.ToArray();}
    private static void VerifyRgb(byte[] bytes,int width,int height,bool interlace)
    {
        var chunks=Chunks(bytes);Assert.Equal(2,chunks["IHDR"][0][9]);Assert.Equal(interlace?1:0,chunks["IHDR"][0][12]);
        using var input=new MemoryStream(chunks["IDAT"].SelectMany(b=>b).ToArray());using var z=new ZLibStream(input,CompressionMode.Decompress);
        foreach(var pass in interlace?Passes:new[]{(X:0,Y:0,Dx:1,Dy:1)})
        {
            if(pass.X>=width||pass.Y>=height)continue;
            for(int y=pass.Y;y<height;y+=pass.Dy)
            {
                Assert.Equal(0,z.ReadByte());
                for(int x=pass.X;x<width;x+=pass.Dx)foreach(byte b in Pixel(x,y).Take(3))Assert.Equal(b,z.ReadByte());
            }
        }
        Assert.Equal(-1,z.ReadByte());
    }
    [Theory]
    [InlineData(0,false)][InlineData(1,false)][InlineData(2,false)][InlineData(3,false)][InlineData(4,false)]
    [InlineData(0,true)][InlineData(1,true)][InlineData(2,true)][InlineData(3,true)][InlineData(4,true)]
    public void AllFiltersRetainExactRgbIncludingFullyTransparentPixels(int filter,bool interlace)
    {
        byte[] source=Package(19,13,interlace,Deflate(Scanlines(19,13,interlace,filter)),1);var image=new MeshTexture(source,MeshTextureWrap.Clamp,MeshTextureWrap.Repeat,MeshTextureFilter.Nearest,MeshTextureFilter.Linear,false);
        var converted=image.WithoutAlpha();VerifyRgb(Bytes(converted),19,13,interlace);Assert.False(converted.HasAlpha);Assert.False(converted.UseAlpha);
        Assert.Equal(image.WrapU,converted.WrapU);Assert.Equal(image.WrapV,converted.WrapV);Assert.Equal(image.MinFilter,converted.MinFilter);Assert.Equal(image.MagFilter,converted.MagFilter);
        Assert.True(image.HasAlpha);Assert.Equal(source,Bytes(image));Assert.NotEqual(image.Sha256,converted.Sha256);
    }
    [Theory]
    [InlineData(1,1)][InlineData(1,9)][InlineData(9,1)][InlineData(2,3)][InlineData(3,2)][InlineData(7,7)]
    public void SmallAndOddAdam7PassesSkipEmptyRows(int width,int height)
    {var image=new MeshTexture(Package(width,height,true,Deflate(Scanlines(width,height,true,-1))));VerifyRgb(Bytes(image.WithoutAlpha()),width,height,true);}
    [Fact]
    public void LegalTrailingCompressedPaddingIsIgnoredAndSourceColorDeclarationsArePreserved()
    {
        byte[] metadata=Chunk("sRGB",new byte[]{1}).Concat(Chunk("sBIT",new byte[]{8,7,6,5})).ToArray();
        var source=new MeshTexture(Package(2,2,false,Deflate(Scanlines(2,2,false,4)).Concat(new byte[]{42,0,0,1}).ToArray(),metadata:metadata));
        var rgb=Bytes(source.WithoutAlpha());VerifyRgb(rgb,2,2,false);Assert.Equal(new byte[]{1},Chunks(rgb)["sRGB"][0]);Assert.Equal(new byte[]{8,7,6},Chunks(rgb)["sBIT"][0]);
    }
    [Fact]
    public void EveryTruncatedZlibPrefixFailsEvenWhenAllPixelsWereAlreadyInflated()
    {
        byte[] compressed=Deflate(Scanlines(2,2,false,0));
        for(int length=0;length<compressed.Length;length++)
        {
            var source=new MeshTexture(Package(2,2,false,compressed[..length]));
            Assert.Throws<InvalidDataException>(()=>source.WithoutAlpha());
        }
    }
    [Fact]
    public void BadAdlerShortExtraAndBadFilterDecodedStreamsFail()
    {
        byte[] raw=Scanlines(2,2,false,0),badFilter=(byte[])raw.Clone(),checksum=Deflate(raw);badFilter[0]=5;checksum[^1]^=1;
        foreach(byte[] compressed in new[]{checksum,Deflate(raw[..^1]),Deflate(raw.Concat(new byte[]{0}).ToArray()),Deflate(badFilter),new byte[]{0,0,0}})
            Assert.Throws<InvalidDataException>(()=>new MeshTexture(Package(2,2,false,compressed)).WithoutAlpha());
    }
    [Theory]
    [InlineData("acTL")][InlineData("fcTL")][InlineData("fdAT")]
    public void AnimatedPngIsNotSilentlyFlattenedByAlphaConversion(string type)
    {Assert.Throws<InvalidDataException>(()=>new MeshTexture(Package(1,1,false,Deflate(Scanlines(1,1,false,0)),metadata:Chunk(type,new byte[8]))).WithoutAlpha());}
    [Fact]
    public void ActualRowCapacityAndCancellationFailBeforeUnboundedAllocation()
    {
        Assert.Throws<InvalidDataException>(()=>new MeshTexture(Package(1<<30,1,false,Deflate(new byte[1]))).WithoutAlpha());
        var source=new MeshTexture(Package(1,1,false,Deflate(Scanlines(1,1,false,0))));Assert.Throws<OperationCanceledException>(()=>source.WithoutAlpha(new CancellationToken(true)));
    }
    private static MeshData Triangle(MeshTexture image,double alpha)=>new(new float[]{0,0,0,1000,0,0,0,1000,0},new[]{0,1,2},255,255,255)
    {TexCoords=new float[]{0,0,1,0,0,1},Material=new(1,1,1,alpha),BaseColorTexture=image};
    [Theory]
    [InlineData(false)][InlineData(true)]
    public void GlbMixedAlphaStrategiesDeduplicatePayloadsButRetainEachSamplerAndFactor(bool reverse)
    {
        byte[] source=Package(3,2,true,Deflate(Scanlines(3,2,true,4)));var real=new MeshTexture(source);var ignored=new MeshTexture(source,useAlpha:false);var clamped=new MeshTexture(source,MeshTextureWrap.Clamp,MeshTextureWrap.Clamp,MeshTextureFilter.Nearest,MeshTextureFilter.Nearest,false);
        var meshes=new[]{Triangle(real,.6),Triangle(ignored,1),Triangle(ignored,.4),Triangle(clamped,0)};if(reverse)Array.Reverse(meshes);
        string path=Path.GetTempFileName();try
        {
            GlbWriter.Write(path,meshes);byte[] data=File.ReadAllBytes(path);int jsonLength=BitConverter.ToInt32(data,12),bin=28+jsonLength;
            using var json=JsonDocument.Parse(data.AsMemory(20,jsonLength));var root=json.RootElement;var images=root.GetProperty("images");var views=root.GetProperty("bufferViews");var textures=root.GetProperty("textures");var samplers=root.GetProperty("samplers");var materials=root.GetProperty("materials");
            Assert.Equal(2,images.GetArrayLength());Assert.Equal(data.Length,BitConverter.ToInt32(data,8));Assert.Equal(data.Length-bin,root.GetProperty("buffers")[0].GetProperty("byteLength").GetInt32());
            for(int i=0;i<meshes.Length;i++)
            {
                var mesh=meshes[i];bool convert=mesh.BaseColorTexture!.UseAlpha==false&&mesh.Material!.Alpha<1;var pbr=materials[i].GetProperty("pbrMetallicRoughness");Assert.Equal(mesh.Material!.Alpha,pbr.GetProperty("baseColorFactor")[3].GetDouble());
                Assert.Equal(mesh.Material.Alpha<1?"BLEND":"OPAQUE",materials[i].GetProperty("alphaMode").GetString());
                var texture=textures[pbr.GetProperty("baseColorTexture").GetProperty("index").GetInt32()];var view=views[images[texture.GetProperty("source").GetInt32()].GetProperty("bufferView").GetInt32()];int offset=view.GetProperty("byteOffset").GetInt32(),length=view.GetProperty("byteLength").GetInt32();Assert.Equal(0,offset%4);
                byte[] image=data.AsSpan(bin+offset,length).ToArray();if(convert)VerifyRgb(image,3,2,true);else Assert.Equal(source,image);
                var sampler=samplers[texture.GetProperty("sampler").GetInt32()];Assert.Equal((int)mesh.BaseColorTexture.WrapU,sampler.GetProperty("wrapS").GetInt32());Assert.Equal((int)mesh.BaseColorTexture.MinFilter,sampler.GetProperty("minFilter").GetInt32());
                int accessor=root.GetProperty("meshes")[i].GetProperty("primitives")[0].GetProperty("attributes").GetProperty("POSITION").GetInt32();var vertexView=views[root.GetProperty("accessors")[accessor].GetProperty("bufferView").GetInt32()];int start=bin+vertexView.GetProperty("byteOffset").GetInt32();Assert.Equal(1f,BitConverter.ToSingle(data,start+12));
            }
            Assert.Equal(source,Bytes(ignored));Assert.True(ignored.HasAlpha);Assert.False(ignored.UseAlpha);
        }
        finally{File.Delete(path);}
    }
    [Fact]
    public void ConversionOrLaterGeometryFailurePreservesExistingTargetAndLeavesNoTemporaryFile()
    {
        string folder=Path.Combine(Path.GetTempPath(),"aholo-png-"+Guid.NewGuid());Directory.CreateDirectory(folder);string path=Path.Combine(folder,"model.glb");File.WriteAllText(path,"keep");
        try
        {
            var valid=new MeshTexture(Package(2,2,false,Deflate(Scanlines(2,2,false,0))),useAlpha:false);var invalid=new MeshTexture(Package(2,2,false,Deflate(new byte[1])),useAlpha:false);
            Assert.Throws<InvalidDataException>(()=>GlbWriter.Write(path,new[]{Triangle(valid,.4),Triangle(invalid,.5)}));
            Assert.Throws<ArgumentException>(()=>GlbWriter.Write(path,new[]{Triangle(valid,.4),Triangle(valid,.5) with{Indices=new[]{0,1,99}}}));
            Assert.Throws<OperationCanceledException>(()=>GlbWriter.Write(path,new[]{Triangle(valid,.4)},new CancellationToken(true)));
            Assert.Equal("keep",File.ReadAllText(path));Assert.Single(Directory.GetFiles(folder));
        }
        finally{Directory.Delete(folder,true);}
    }
    private sealed class CancelDuringWrite(MeshData mesh, Action onWrite):IReadOnlyList<MeshData>
    {
        private int reads;
        public int Count=>1;
        public MeshData this[int index]
        {
            get
            {
                if(index!=0)throw new ArgumentOutOfRangeException(nameof(index));
                if(++reads==2)onWrite();
                return mesh;
            }
        }
        public IEnumerator<MeshData> GetEnumerator(){yield return this[0];}
        System.Collections.IEnumerator System.Collections.IEnumerable.GetEnumerator()=>GetEnumerator();
    }
    [Fact]
    public void CancellationAfterTemporaryFileCreationKeepsPriorTargetAndCleansUp()
    {
        string folder=Path.Combine(Path.GetTempPath(),"aholo-png-cancel-"+Guid.NewGuid());Directory.CreateDirectory(folder);
        string path=Path.Combine(folder,"model.glb");File.WriteAllText(path,"keep");
        try
        {
            var texture=new MeshTexture(Package(2,2,false,Deflate(Scanlines(2,2,false,0))),useAlpha:false);
            using var cancellation=new CancellationTokenSource();bool observed=false;
            var meshes=new CancelDuringWrite(Triangle(texture,.4),()=>
            {
                Assert.Single(Directory.GetFiles(folder,"*.tmp"));observed=true;cancellation.Cancel();
            });
            Assert.Throws<OperationCanceledException>(()=>GlbWriter.Write(path,meshes,cancellation.Token));
            Assert.True(observed);Assert.Equal("keep",File.ReadAllText(path));Assert.Single(Directory.GetFiles(folder));
        }
        finally{Directory.Delete(folder,true);}
    }

}
