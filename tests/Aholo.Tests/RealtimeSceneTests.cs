using System.Globalization;
using Aholo.Core;
namespace Aholo.Tests;

public sealed class RealtimeSceneTests
{
    internal static RenderLight Light()=>new("Sphere",new[]{1d,0,0,0,0,1,0,0,0,0,1,0,1000,2000,3000,1},new[]{1d,.5,.25},2,Radius:50);
    [Fact]
    public void PlacementAppliesWorldTransformAfterAssetBasisAndUnitConversion()
    {
        var p=new RenderModelPlacement(10,20,30,2,90).Pose(true);
        double[] source={1,2,3,1};double[] actual=Enumerable.Range(0,4).Select(col=>Enumerable.Range(0,4).Sum(row=>source[row]*p[row*4+col])).ToArray();
        Assert.Equal(6010,actual[0],6);Assert.Equal(2020,actual[1],6);Assert.Equal(4030,actual[2],6);Assert.Equal(1,actual[3]);
        var native=new RenderModelPlacement(10,20,30,2,0).Pose(false);Assert.Equal(2,native[0]);Assert.Equal(2,native[5]);Assert.Equal(2,native[10]);Assert.Equal(10,native[12]);
    }
    [Fact]
    public void PatchesUseExistingTypedPathsWithoutCameraOrNewAssetAuthority()
    {
        var original=new RealtimeStartRequest(Guid.NewGuid(),"mesh",RealtimePreviewManagerTests.Camera(),Lights:new[]{Light()});
        var request=new RealtimeSceneRequest(original.Id,1,new(10,20,30),.8,new[]{Light() with{Intensity=0}});
        string patch=RealtimeScene.ScenePatch(original,request);
        Assert.Contains("over \"Model\"",patch);Assert.Contains("over Mesh \"Geom\"",patch);Assert.Contains("over SphereLight \"Light_0\"",patch);
        Assert.Contains("over RenderSettings \"MainRenderSettings\"",patch);Assert.Contains("(0.8, 0.8, 0.8)",patch);Assert.Contains("inputs:intensity = 0",patch);
        Assert.DoesNotContain("payload",patch);Assert.DoesNotContain("Cameras",patch);Assert.DoesNotContain("def ",patch);
        string gs=RealtimeScene.ScenePatch(original with{Gaussian=true,GltfCoordinates=false},request);Assert.Contains("over ParticleField3DGaussianSplat \"Geom\"",gs);
        Assert.DoesNotContain("1000, 0, 0, 0",gs);
        string init=RealtimeScene.Build(original with{Model=new(10,20,30)});Assert.Contains("def SphereLight \"Light_0\"",init);Assert.Contains("(10, 20, 30, 1)",init);
    }
    [Fact]
    public void InvalidPlacementOrLightSlotChangeFailsBeforePatch()
    {
        var original=new RealtimeStartRequest(Guid.NewGuid(),"mesh",RealtimePreviewManagerTests.Camera(),Lights:new[]{Light()});
        var patch=new RealtimeSceneRequest(original.Id,1,new(),1,new[]{Light()});
        Assert.Throws<ArgumentException>(()=>new RenderModelPlacement(Scale:0).Pose(true));Assert.Throws<ArgumentException>(()=>new RenderModelPlacement(X:double.NaN).Pose(true));
        Assert.Throws<ArgumentException>(()=>new RenderModelPlacement(Scale:double.Epsilon).Pose(true));
        Assert.Throws<ArgumentException>(()=>RealtimeScene.ScenePatch(original,patch with{Lights=Array.Empty<RenderLight>()}));
        Assert.Throws<ArgumentException>(()=>RealtimeScene.ScenePatch(original,patch with{Lights=new[]{Light() with{Kind="Rect",Width=1,Height=1}}}));
        Assert.Throws<ArgumentOutOfRangeException>(()=>RealtimeScene.ScenePatch(original,patch with{EnvironmentIntensity=0}));
    }
    [Fact]
    public void StartSnapshotDoesNotRetainCallerOwnedArrays()
    {
        var original=new RealtimeStartRequest(Guid.NewGuid(),"mesh",RealtimePreviewManagerTests.Camera(),Lights:new[]{Light()});
        var captured=RealtimeScene.Snapshot(original);original.Camera.Transform[0]=double.NaN;original.Lights![0].Color[0]=double.NaN;original.Lights[0].Transform[0]=double.NaN;original.Lights[0]=Light() with{Kind="bad"};
        string content=RealtimeScene.Build(captured);Assert.Contains("SphereLight",content);Assert.DoesNotContain("NaN",content);
    }
}
