using System.Globalization;
using Aholo3D.Core;
namespace Aholo3D.Tests;

public sealed class RenderLightTests
{
    private static readonly double[] White={1,1,1};
    private static RenderLight Rect(GaussianTransform transform)=>RenderLight.Rectangle(new(1,2,3),new(2,0,0),new(0,4,0),new(0,0,-1),transform,1000,White,2);
    [Fact]
    public void RectangleConvertsCornerToCenterAndKeepsDimensionsOutsidePose()
    {
        var light=Rect(GaussianTransform.Identity);
        Assert.Equal(2000,light.Width);Assert.Equal(4000,light.Height);
        Assert.Equal(new[]{2000d,4000,3000,1},light.Transform.Skip(12));
        Assert.Equal(new[]{0d,0,1,0},light.Transform.Skip(8).Take(4));light.Validate();
    }
    [Fact]
    public void ReflectedNonuniformRectanglePreservesPhysicalEmissionDirection()
    {
        var light=Rect(new(new(-2,0,0,0,3,0,0,0,1),new(10,20,30)));
        Assert.Equal(4000,light.Width);Assert.Equal(12000,light.Height);
        Assert.Equal(new[]{6000d,32000,33000,1},light.Transform.Skip(12));
        Assert.Equal(new[]{-1d,0,0,0},light.Transform.Take(4));
        Assert.Equal(new[]{0d,-1,0,0},light.Transform.Skip(4).Take(4));
        Assert.Equal(1,light.Transform[10]);light.Validate();
    }
    [Fact]
    public void RectangleRejectsShearDegeneracyAndNonPerpendicularDirection()
    {
        Assert.Throws<ArgumentException>(()=>Rect(new(new(1,.2,0,0,1,0,0,0,1),GaussianVector.Zero)));
        Assert.Throws<ArgumentException>(()=>Rect(new(new(0,0,0,0,1,0,0,0,1),GaussianVector.Zero)));
        Assert.Throws<ArgumentException>(()=>RenderLight.Rectangle(new(),new(1,0,0),new(0,1,0),new(1,0,0),GaussianTransform.Identity,1,White,1));
    }
    [Fact]
    public void PointApproximationUsesExplicitMillimetreRadiusScaledByBlock()
    {
        var light=RenderLight.PointApproximation(new(1,2,3),new(new(-2,0,0,0,2,0,0,0,2),new(1,0,0)),1000,50,White,4);
        Assert.Equal(100,light.Radius);Assert.Equal(new[]{-1000d,4000,6000,1},light.Transform.Skip(12));
        Assert.Throws<ArgumentException>(()=>RenderLight.PointApproximation(new(),new(new(1,0,0,0,2,0,0,0,1),new()),1,50,White,1));
    }
    [Fact]
    public void UsdaLightsUseTheirOwnRigidMillimetrePosesAndUniquePaths()
    {
        var old=CultureInfo.CurrentCulture;
        try
        {
            CultureInfo.CurrentCulture=CultureInfo.GetCultureInfo("fr-FR");
            var camera=new RenderCamera(new double[]{1,0,0,0,0,1,0,0,0,0,1,0,0,0,1000,1},35,36,24,1,100000);
            string scene=UsdaSceneBuilder.Build("known-mesh",camera,1280,720,1,new[]{Rect(GaussianTransform.Identity),RenderLight.PointApproximation(new(),GaussianTransform.Identity,1,25.5,White,4)});
            Assert.Contains("def RectLight \"Light_0\"",scene);Assert.Contains("def SphereLight \"Light_1\"",scene);
            Assert.Contains("float inputs:radius = 25.5",scene);Assert.Contains("bool inputs:normalize = false",scene);
            Assert.Contains("(2000, 4000, 3000, 1)",scene);Assert.Contains("uniform token[] xformOpOrder",scene);
            Assert.True(scene.IndexOf("def Xform \"Lights\"",StringComparison.Ordinal)<scene.IndexOf("def Xform \"Cameras\"",StringComparison.Ordinal));
        }
        finally{CultureInfo.CurrentCulture=old;}
    }
    [Fact]
    public void InvalidLightSnapshotsRejectBeforeSerialization()
    {
        var light=Rect(GaussianTransform.Identity);
        Assert.Throws<ArgumentException>(()=>RenderLightsUsda.Build(new[]{light with{Color=new[]{double.NaN,1,1}}}));
        Assert.Throws<ArgumentException>(()=>RenderLightsUsda.Build(new[]{light with{Intensity=double.PositiveInfinity}}));
        Assert.Throws<ArgumentException>(()=>RenderLightsUsda.Build(new[]{light with{Width=0}}));
        var scale=(double[])light.Transform.Clone();scale[0]=2;
        Assert.Throws<ArgumentException>(()=>RenderLightsUsda.Build(new[]{light with{Transform=scale}}));
    }
}
