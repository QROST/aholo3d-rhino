using System.Text.Json;
using Aholo.Core;
namespace Aholo.Tests;

public sealed class TextureUvTransformTests
{
    [Fact]
    public void BitmapPlacementPrecedesVFlipAndRetainsCoordinatesOutsideTheUnitSquare()
    {
        float[] source={.25f,.75f,-2,3};var original=(float[])source.Clone();
        var actual=new TextureUvTransform(2,-3,5,7,11,-13).ToGltf(source);
        Assert.Equal(new[]{3.25f,4,-8,-5},actual);Assert.Equal(original,source);
    }
    [Fact]
    public void IndependentRepeatRotateOffsetOracleMatchesTheCombinedTransform()
    {
        var random=new Random(51);var transform=new TextureUvTransform(0,-3,.25,2,0,.75);
        for(int i=0;i<128;i++)
        {
            float u=random.Next(-64,65)/8f,v=random.Next(-64,65)/8f;
            double repeatedU=2*u,repeatedV=3*v;
            double rotatedU=-repeatedV,rotatedV=repeatedU;
            Assert.Equal(new[]{(float)(rotatedU+.25),(float)(1-(rotatedV+.75))},transform.ToGltf(new[]{u,v}));
        }
    }
    [Fact]
    public void FourCornersUseOneFinalVConversion()
    {Assert.Equal(new float[]{0,1,1,1,0,0,1,0},TextureUvTransform.Identity.ToGltf(new float[]{0,0,1,0,0,1,1,1}));}
    [Theory]
    [InlineData(2)][InlineData(6)][InlineData(8)][InlineData(9)][InlineData(11)][InlineData(12)][InlineData(13)][InlineData(14)][InlineData(15)]
    public void WOrProjectiveMatrixComponentsCannotBeSilentlyDiscarded(int field)
    {
        double[] matrix={1,0,0,0,0,1,0,0,0,0,1,0,0,0,0,1};matrix[field]=field==15?2:.5;
        Assert.Throws<ArgumentException>(()=>TextureUvTransform.FromMatrix(matrix));
    }
    [Fact]
    public void MatrixExtractionRetainsAffineFieldsWithoutApplyingThemTwice()
    {
        double[] matrix={2,-3,0,5,7,11,0,-13,0,0,6,0,0,0,0,1};
        Assert.Equal(new TextureUvTransform(2,-3,5,7,11,-13),TextureUvTransform.FromMatrix(matrix));
        Assert.Throws<ArgumentException>(()=>TextureUvTransform.FromMatrix(matrix[..15]));
        matrix[10]=double.NaN;Assert.Throws<ArgumentException>(()=>TextureUvTransform.FromMatrix(matrix));
    }
    [Fact]
    public void InvalidArraysValuesOrTransportOverflowFailWithoutChangingInput()
    {
        Assert.Throws<ArgumentException>(()=>TextureUvTransform.Identity.ToGltf(Array.Empty<float>()));
        Assert.Throws<ArgumentException>(()=>TextureUvTransform.Identity.ToGltf(new float[]{1}));
        Assert.Throws<InvalidDataException>(()=>TextureUvTransform.Identity.ToGltf(new[]{float.NaN,0}));
        Assert.Throws<InvalidDataException>(()=>new TextureUvTransform(2,0,0,0,1,0).ToGltf(new[]{float.MaxValue,0}));
        Assert.Throws<ArgumentException>(()=>new TextureUvTransform(double.PositiveInfinity,0,0,0,1,0).ToGltf(new float[]{0,0}));
    }
    [Fact]
    public void PreCancellationDoesNotProduceOutput()
    {using var cts=new CancellationTokenSource();cts.Cancel();Assert.Throws<OperationCanceledException>(()=>TextureUvTransform.Identity.ToGltf(new float[]{0,0},cts.Token));}
    [Fact]
    public void ProductionGlbWriterKeepsTransformedCoordinatesAndVertexCorrespondence()
    {
        var uv=new TextureUvTransform(0,-3,.25,2,0,.75).ToGltf(new float[]{0,0,1,0,0,1});
        var mesh=new MeshData(new float[]{0,0,0,1000,0,0,0,1000,0},new[]{0,1,2},255,255,255){TexCoords=uv};
        string path=Path.Combine(Path.GetTempPath(),"aholo-texture-uv-"+Guid.NewGuid()+".glb");
        try
        {
            GlbWriter.Write(path,new[]{mesh});using var file=new BinaryReader(File.OpenRead(path));file.BaseStream.Position=12;
            int length=file.ReadInt32();file.ReadUInt32();using var json=JsonDocument.Parse(file.ReadBytes(length));file.ReadUInt32();file.ReadUInt32();long binary=file.BaseStream.Position;
            var root=json.RootElement;int index=root.GetProperty("meshes")[0].GetProperty("primitives")[0].GetProperty("attributes").GetProperty("TEXCOORD_0").GetInt32();
            var accessor=root.GetProperty("accessors")[index];Assert.Equal(3,accessor.GetProperty("count").GetInt32());
            var view=root.GetProperty("bufferViews")[accessor.GetProperty("bufferView").GetInt32()];file.BaseStream.Position=binary+view.GetProperty("byteOffset").GetInt64();
            Assert.Equal(new[]{.25f,.25f,.25f,-1.75f,-2.75f,.25f},Enumerable.Range(0,6).Select(_=>file.ReadSingle()));
        }
        finally{File.Delete(path);}
    }
}
