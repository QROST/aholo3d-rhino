using Aholo.Core;
namespace Aholo.Tests;

public sealed class MeshPlacementTransformTests
{
    private static GaussianVector Min=>new(10,20,30);
    private static GaussianVector Max=>new(14,26,32);
    private static GaussianVector Anchor=>new(12,23,30);
    private static readonly GaussianMatrix Tilt=new(0,0,1,1,0,0,0,1,0);
    private static void Equal(GaussianVector expected,GaussianVector actual)
    {Assert.Equal(expected.X,actual.X,10);Assert.Equal(expected.Y,actual.Y,10);Assert.Equal(expected.Z,actual.Z,10);}
    [Theory]
    [InlineData(MeshPlacementMode.FitHeight,5)]
    [InlineData(MeshPlacementMode.SourceSizeOnPlane,1)]
    public void CapturedTiltPointRotationAndScaleComposeInOrder(MeshPlacementMode mode,double scale)
    {
        var point=new GaussianVector(100,200,300);var transform=MeshPlacementTransform.Create(mode,Min,Max,Tilt,point,10,90);
        Equal(point,transform.TransformPoint(Anchor));
        Equal(point+new GaussianVector(0,0,scale),transform.TransformPoint(Anchor+new GaussianVector(1,0,0)));
        Equal(point+new GaussianVector(2*scale,0,0),transform.TransformPoint(Anchor+new GaussianVector(0,0,2)));
    }
    [Fact]
    public void SceneCoordinatesPreserveFarOriginAndIgnoreDisabledControls()
    {
        var min=new GaussianVector(1e12,-1e12,50);var max=min+new GaussianVector(2,3,0);
        var result=MeshPlacementTransform.Create(MeshPlacementMode.SourceCoordinates,min,max,default,new(double.NaN,0,0),double.NaN,double.NaN);
        Assert.Equal(GaussianTransform.Identity,result);Assert.Equal(min,result.TransformPoint(min));Assert.Equal(max,result.TransformPoint(max));
    }
    [Fact]
    public void OriginalSizeAllowsPlanarMeshAndIgnoresHeight()
    {
        var min=new GaussianVector(10,20,30);var max=new GaussianVector(14,26,30);
        var result=MeshPlacementTransform.Create(MeshPlacementMode.SourceSizeOnPlane,min,max,GaussianMatrix.Identity,GaussianVector.Zero,double.NaN,0);
        Equal(new(-2,-3,0),result.TransformPoint(min));Equal(new(2,3,0),result.TransformPoint(max));
        Assert.Throws<InvalidOperationException>(()=>MeshPlacementTransform.Create(MeshPlacementMode.FitHeight,min,max,GaussianMatrix.Identity,GaussianVector.Zero,10,0));
    }
    [Fact]
    public void DefaultHeightMatchesExistingAnchorPolicy()
    {
        var transform=MeshPlacementTransform.Create(MeshPlacementMode.FitHeight,Min,Max,GaussianMatrix.Identity,GaussianVector.Zero,10,0);
        Equal(new(-10,-15,0),transform.TransformPoint(Min));Equal(new(10,15,10),transform.TransformPoint(Max));
    }
    [Theory]
    [InlineData(MeshPlacementMode.FitHeight)]
    [InlineData(MeshPlacementMode.SourceSizeOnPlane)]
    [InlineData(MeshPlacementMode.SourceCoordinates)]
    public void InvalidBoundsAreRejectedInEveryMode(MeshPlacementMode mode)
    {
        Assert.Throws<InvalidOperationException>(()=>MeshPlacementTransform.Create(mode,new(double.NaN,0,0),Max,GaussianMatrix.Identity,GaussianVector.Zero,10,0));
        Assert.Throws<InvalidOperationException>(()=>MeshPlacementTransform.Create(mode,Max,Min,GaussianMatrix.Identity,GaussianVector.Zero,10,0));
    }
    [Fact]
    public void DegeneratePlaneAndNumericOverflowAreExplicitFailures()
    {
        Assert.Throws<InvalidOperationException>(()=>MeshPlacementTransform.Create(MeshPlacementMode.SourceSizeOnPlane,Min,Max,default,GaussianVector.Zero,1,0));
        Assert.Throws<InvalidOperationException>(()=>MeshPlacementTransform.Create(MeshPlacementMode.FitHeight,new(0,0,0),new(1,1,1e-300),GaussianMatrix.Identity,GaussianVector.Zero,1e300,0));
        Assert.Throws<InvalidOperationException>(()=>MeshPlacementTransform.Create(MeshPlacementMode.SourceSizeOnPlane,new(-1e308,0,0),new(0,1,1),GaussianMatrix.Identity,new(1.5e308,0,0),1,0));
    }
    [Fact]
    public void OppositeExtremeBoundsCanFitWhenTheResultIsRepresentable()
    {
        var transform=MeshPlacementTransform.Create(MeshPlacementMode.FitHeight,new(0,0,-1e308),new(1,1,1e308),GaussianMatrix.Identity,GaussianVector.Zero,1e308,0);
        Assert.Equal(.5,transform.Linear.M33);Assert.Equal(0,transform.TransformPoint(new(.5,.5,-1e308)).Z);Assert.Equal(1e308,transform.TransformPoint(new(.5,.5,1e308)).Z);
    }
    [Fact]
    public void LargeSameSignMidpointAndTinyPositiveHeightAreNotArtificiallyCapped()
    {
        var result=MeshPlacementTransform.Create(MeshPlacementMode.SourceSizeOnPlane,new(1e308,0,0),new(1.1e308,1,1),GaussianMatrix.Identity,GaussianVector.Zero,1,0);
        Assert.True(result.Translation.IsFinite);
        var tiny=MeshPlacementTransform.Create(MeshPlacementMode.FitHeight,new(0,0,0),new(1e-20,1e-20,1e-20),GaussianMatrix.Identity,GaussianVector.Zero,1,0);
        Equal(new(.5,.5,1),tiny.TransformPoint(new(1e-20,1e-20,1e-20)));
    }
}
