using Aholo.Core;
namespace Aholo.Tests;

public sealed class SpatialLayoutTests
{
    private static SpatialLayout Parse(string value)=>SpatialDsl.Read(new StringReader(value));
    [Fact]
    public void ForwardWallReferencesAndClassCommasArePreserved()
    {
        var layout=Parse("\uFEFF\nwindow_0=Window(wall_0,2,0,1.5,2,1);\nwall_0=Wall(0,0,0,4e0,0,0,3,0)\nbbox_0=Bbox(dining chair, blue,0,0,0,0,2,4,6)");
        Assert.Equal(3,layout.Entities.Count);Assert.Equal("dining chair, blue",Assert.IsType<SpatialBox>(layout.Entities[2]).Class);
        var shapes=SpatialDsl.Shapes(layout);
        Assert.Equal(new GaussianVector(1,0,1),shapes[0].Points[0]);Assert.Equal(new GaussianVector(3,0,2),shapes[0].Points[2]);
        Assert.Equal(new GaussianVector(-1,-2,-3),shapes[2].Points[0]);Assert.Equal(new GaussianVector(1,2,3),shapes[2].Points[6]);
    }
    [Fact]
    public void BoxAnglesUseRadiansAndFullDimensions()
    {
        var shape=Assert.Single(SpatialDsl.Shapes(Parse("box=Bbox(chair,10,20,30,1.5707963267948966,2,4,6)")));
        Assert.Equal(12,shape.Points[0].X,10);Assert.Equal(19,shape.Points[0].Y,10);Assert.Equal(27,shape.Points[0].Z);
    }
    [Fact]
    public void UnknownTypesAreReportedWithoutDiscardingKnownEntities()
    {
        var layout=Parse("unknown=Future(foo,bar)\nw=Wall(0,0,0,1,0,0,2,0)");
        Assert.Single(layout.Warnings);Assert.Single(layout.Entities);
    }
    [Theory]
    [InlineData("wall=Wall(0,0,0,0,0,1,2,0)")]
    [InlineData("wall=Wall(0,0,0,1,0,0,0,0)")]
    [InlineData("wall=Wall(0,0,0,1,0,0,2,-1)")]
    [InlineData("box=Bbox(chair,0,0,0,0,1,0,1)")]
    [InlineData("box=Bbox(chair,0,0,0,NaN,1,1,1)")]
    [InlineData("box=Bbox(chair,0,0,0,1e999,1,1,1)")]
    [InlineData("box=Bbox(0,0,0,0,1,1,1)")]
    [InlineData("box=Bbox(,,,,,,,)")]
    [InlineData("bad id=Wall(0,0,0,1,0,0,2,0)")]
    public void InvalidKnownEntitiesHaveLineNumbers(string text)
        => Assert.Contains("2",Assert.Throws<InvalidDataException>(()=>Parse("\n"+text)).Message);
    [Fact]
    public void MissingOrNonWallReferenceReportsTheOpeningLine()
    {
        var error=Assert.Throws<InvalidDataException>(()=>Parse("b=Bbox(chair,0,0,0,0,1,1,1)\n\nd=Door(b,0,0,0,1,1)"));
        Assert.Contains("3",error.Message);
    }
    [Fact]
    public void UnknownTypesDoNotReserveIdsButKnownDuplicatesStillFail()
    {
        var layout=Parse("w=Future(x)\nbad id=Future(x)\nw=Wall(0,0,0,1,0,0,2,0)");
        Assert.Single(layout.Entities); Assert.Equal(2,layout.Warnings.Count);
        Assert.Throws<InvalidDataException>(()=>Parse("w=Wall(0,0,0,1,0,0,2,0)\nw=Wall(0,0,0,2,0,0,2,0)"));
    }
    [Fact]
    public void DerivedCoordinateOverflowFailsBeforeHostMutation()
        => Assert.Throws<InvalidDataException>(()=>SpatialDsl.Shapes(Parse("w=Wall(0,0,1.7e308,1,0,1.7e308,1.7e308,0)")));
    [Fact]
    public void CancellationAndUnsupportedOnlyFilesDoNotProduceGeometry()
    {
        Assert.Throws<OperationCanceledException>(()=>SpatialDsl.Read(new StringReader("w=Wall(0,0,0,1,0,0,2,0)"),new CancellationToken(true)));
        Assert.Throws<InvalidDataException>(()=>Parse("w=Future(x)"));
    }
}
