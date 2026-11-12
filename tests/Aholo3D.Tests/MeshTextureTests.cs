using System.Text.Json;
using Aholo3D.Core;
namespace Aholo3D.Tests;

public sealed class MeshTextureTests
{
    private const string Png = "iVBORw0KGgoAAAANSUhEUgAAAAIAAAACCAYAAABytg0kAAAAF0lEQVR4nAXBAQEAAACCIKb33EAkQxUHQdUHepwfNGcAAAAASUVORK5CYII=";
    private const string Jpeg = "/9j/4AAQSkZJRgABAQAAAQABAAD/2wBDAAgGBgcGBQgHBwcJCQgKDBQNDAsLDBkSEw8UHRofHh0aHBwgJC4nICIsIxwcKDcpLDAxNDQ0Hyc5PTgyPC4zNDL/2wBDAQkJCQwLDBgNDRgyIRwhMjIyMjIyMjIyMjIyMjIyMjIyMjIyMjIyMjIyMjIyMjIyMjIyMjIyMjIyMjIyMjIyMjL/wAARCAACAAIDASIAAhEBAxEB/8QAHwAAAQUBAQEBAQEAAAAAAAAAAAECAwQFBgcICQoL/8QAtRAAAgEDAwIEAwUFBAQAAAF9AQIDAAQRBRIhMUEGE1FhByJxFDKBkaEII0KxwRVS0fAkM2JyggkKFhcYGRolJicoKSo0NTY3ODk6Q0RFRkdISUpTVFVWV1hZWmNkZWZnaGlqc3R1dnd4eXqDhIWGh4iJipKTlJWWl5iZmqKjpKWmp6ipqrKztLW2t7i5usLDxMXGx8jJytLT1NXW19jZ2uHi4+Tl5ufo6erx8vP09fb3+Pn6/8QAHwEAAwEBAQEBAQEBAQAAAAAAAAECAwQFBgcICQoL/8QAtREAAgECBAQDBAcFBAQAAQJ3AAECAxEEBSExBhJBUQdhcRMiMoEIFEKRobHBCSMzUvAVYnLRChYkNOEl8RcYGRomJygpKjU2Nzg5OkNERUZHSElKU1RVVldYWVpjZGVmZ2hpanN0dXZ3eHl6goOEhYaHiImKkpOUlZaXmJmaoqOkpaanqKmqsrO0tba3uLm6wsPExcbHyMnK0tPU1dbX2Nna4uPk5ebn6Onq8vP09fb3+Pn6/9oADAMBAAIRAxEAPwD0LwrY2kvhDRJJLWB3ewgZmaMEkmNckmiiivynF/7xU9X+Z89X/iy9X+Z//9k=";
    private static MeshData Triangle(MeshTexture? texture=null)=>new(new[]{0f,0,0,1000,0,0,0,1000,0},new[]{0,1,2},255,255,255)
    {TexCoords=new[]{0f,1,1,1,0,0},Normals=new[]{0f,0,1,0,0,1,0,0,1},BaseColorTexture=texture};
    [Fact]
    public void EmbeddedImagesPreserveBytesSamplerAlphaAlignmentAndDeduplicate()
    {
        string path=Path.GetTempFileName();byte[] png=Convert.FromBase64String(Png),jpeg=Convert.FromBase64String(Jpeg);
        var first=new MeshTexture(png,MeshTextureWrap.Clamp,MeshTextureWrap.Repeat,MeshTextureFilter.Nearest);
        var duplicate=new MeshTexture(png,useAlpha:false);png[0]=0; // A source buffer edit cannot mutate the snapshot.
        try
        {
            GlbWriter.Write(path,new[]{Triangle(first),Triangle(duplicate),Triangle(new(jpeg)),Triangle()});
            using var input=new BinaryReader(File.OpenRead(path));input.BaseStream.Position=12;int n=input.ReadInt32();input.ReadUInt32();
            using var doc=JsonDocument.Parse(input.ReadBytes(n));var root=doc.RootElement;
            int bin=input.ReadInt32();input.ReadUInt32();long start=input.BaseStream.Position;
            var images=root.GetProperty("images");Assert.Equal(2,images.GetArrayLength());Assert.Equal(3,root.GetProperty("textures").GetArrayLength());
            for(int i=0;i<2;i++)
            {
                var view=root.GetProperty("bufferViews")[images[i].GetProperty("bufferView").GetInt32()];
                long offset=view.GetProperty("byteOffset").GetInt64();Assert.Equal(0,offset%4);Assert.False(view.TryGetProperty("target",out _));
                input.BaseStream.Position=start+offset;var bytes=input.ReadBytes(view.GetProperty("byteLength").GetInt32());
                Assert.Equal(Convert.FromBase64String(i==0?Png:Jpeg),bytes);Assert.Equal(i==0?"image/png":"image/jpeg",images[i].GetProperty("mimeType").GetString());
                Assert.False(images[i].TryGetProperty("uri",out _));
            }
            var samplers=root.GetProperty("samplers");Assert.Equal(33071,samplers[0].GetProperty("wrapS").GetInt32());Assert.Equal(10497,samplers[0].GetProperty("wrapT").GetInt32());Assert.Equal(9728,samplers[0].GetProperty("minFilter").GetInt32());
            Assert.Equal("BLEND",root.GetProperty("materials")[0].GetProperty("alphaMode").GetString());
            Assert.Equal("OPAQUE",root.GetProperty("materials")[1].GetProperty("alphaMode").GetString());
            Assert.Equal("OPAQUE",root.GetProperty("materials")[2].GetProperty("alphaMode").GetString());
            Assert.False(root.GetProperty("materials")[3].GetProperty("pbrMetallicRoughness").TryGetProperty("baseColorTexture",out _));
            Assert.Equal(start+bin,input.BaseStream.Length);Assert.Equal(0,bin%4);
        }
        finally{File.Delete(path);}
    }
    [Fact]
    public void InvalidImageAndSamplerAreRejected()
    {
        byte[] png=Convert.FromBase64String(Png),jpeg=Convert.FromBase64String(Jpeg);
        Assert.Throws<InvalidDataException>(()=>new MeshTexture(new byte[20]));
        Assert.Throws<InvalidDataException>(()=>new MeshTexture(png[..^1]));
        png[35]^=1;Assert.Throws<InvalidDataException>(()=>new MeshTexture(png));
        Assert.Throws<InvalidDataException>(()=>new MeshTexture(jpeg[..^2]));
        Assert.Throws<ArgumentException>(()=>new MeshTexture(jpeg,wrapU:(MeshTextureWrap)7));
        Assert.Throws<OperationCanceledException>(()=>new MeshTexture(Convert.FromBase64String(Png),ct:new CancellationToken(true)));
    }
    private static byte[] Chunk(string type,byte[] data)
    {
        using var output=new MemoryStream();byte[] n=new byte[4];System.Buffers.Binary.BinaryPrimitives.WriteInt32BigEndian(n,data.Length);output.Write(n);
        byte[] body=System.Text.Encoding.ASCII.GetBytes(type).Concat(data).ToArray();output.Write(body);uint crc=0xffffffff;
        foreach(byte b in body){crc^=b;for(int i=0;i<8;i++)crc=(crc&1)!=0?(crc>>1)^0xedb88320:crc>>1;}
        System.Buffers.Binary.BinaryPrimitives.WriteUInt32BigEndian(n,~crc);output.Write(n);return output.ToArray();
    }
    private static byte[] InsertPng(byte[] chunk,bool afterImage=false)
    {
        byte[] png=Convert.FromBase64String(Png);int offset=afterImage?png.Length-12:33;
        return png[..offset].Concat(chunk).Concat(png[offset..]).ToArray();
    }
    [Fact]
    public void ColorMetadataIsNotSilentlyReinterpretedAsSrgb()
    {
        Assert.Throws<InvalidDataException>(()=>new MeshTexture(InsertPng(Chunk("iCCP",new byte[]{1,2,3}))));
        Assert.Throws<InvalidDataException>(()=>new MeshTexture(InsertPng(Chunk("gAMA",new byte[]{0,1,134,160}))));
        Assert.Throws<InvalidDataException>(()=>new MeshTexture(InsertPng(Chunk("eXIf",new byte[]{1,2,3}))));
        Assert.Equal("image/png",new MeshTexture(InsertPng(Chunk("gAMA",new byte[]{0,0,177,143}))).MimeType);
        byte[] jpeg=Convert.FromBase64String(Jpeg);byte[] body=System.Text.Encoding.ASCII.GetBytes("ICC_PROFILE\0test"),size=new byte[2];
        System.Buffers.Binary.BinaryPrimitives.WriteUInt16BigEndian(size,(ushort)(body.Length+2));
        byte[] tagged=jpeg[..2].Concat(new byte[]{255,226}).Concat(size).Concat(body).Concat(jpeg[2..]).ToArray();
        Assert.Throws<InvalidDataException>(()=>new MeshTexture(tagged));
    }
    [Fact]
    public void PaletteStructureMustBeValidEvenForOptionalTrueColorPalette()
    {
        foreach(int length in new[]{0,1,2,769,771})Assert.Throws<InvalidDataException>(()=>new MeshTexture(InsertPng(Chunk("PLTE",new byte[length]))));
        byte[] valid=Chunk("PLTE",new byte[]{255,0,0});
        Assert.Throws<InvalidDataException>(()=>new MeshTexture(InsertPng(valid.Concat(valid).ToArray())));
        Assert.Throws<InvalidDataException>(()=>new MeshTexture(InsertPng(valid,true)));
        Assert.Equal("image/png",new MeshTexture(InsertPng(valid)).MimeType);
    }
    [Fact]
    public void MissingUvDoesNotOverwriteOutput()
    {
        string path=Path.GetTempFileName();File.WriteAllText(path,"keep");
        try
        {
            var bitmap=new MeshTexture(Convert.FromBase64String(Png),useAlpha:false);
            Assert.Throws<ArgumentException>(()=>GlbWriter.Write(path,new[]{Triangle(bitmap) with{TexCoords=null}}));
            Assert.Equal("keep",File.ReadAllText(path));
        }
        finally{File.Delete(path);}
    }
    [Fact]
    public void LocalSourceDetectsChangeAndLoadedSnapshotSurvivesFileDeletion()
    {
        string path=Path.GetTempFileName(),output=Path.GetTempFileName();File.WriteAllBytes(path,Convert.FromBase64String(Png));
        try
        {
            var file=new FileInfo(path);var source=new MeshTextureSource(path,file.Length,file.LastWriteTimeUtc.Ticks,MeshTextureWrap.Repeat,MeshTextureWrap.Clamp,MeshTextureFilter.Linear,MeshTextureFilter.Linear);
            var snapshot=source.Load();File.AppendAllText(path,"changed");Assert.Throws<IOException>(()=>source.Load());
            File.Delete(path);GlbWriter.Write(output,new[]{Triangle(snapshot)});Assert.True(new FileInfo(output).Length>0);
            Assert.Throws<OperationCanceledException>(()=>source.Load(new CancellationToken(true)));
        }
        finally{File.Delete(path);File.Delete(output);}
    }
    [Theory]
    [InlineData("https://example.invalid/image.png")]
    [InlineData("file:///tmp/image.png")]
    [InlineData("//server/share/image.png")]
    [InlineData("\\\\server\\share\\image.png")]
    [InlineData("relative/image.png")]
    public void NonlocalPathsFailBeforeAnyFileAccess(string path)=>Assert.Throws<InvalidDataException>(()=>MeshTextureSource.RequireLocalPath(path));
}
