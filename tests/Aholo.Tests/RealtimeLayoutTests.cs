using Aholo.Core;
namespace Aholo.Tests;

public sealed class RealtimeLayoutTests
{
    internal static RealtimeAssetSlot Asset(string id="Model",bool gs=false)=>new(id,gs?"gaussian-id":"mesh-id",gs,!gs,new(10,20,30));
    internal static RealtimeLightSlot Light(string id="Light_a",bool rect=false)=>new(id,RealtimeSceneTests.Light() with{Kind=rect?"Rect":"Sphere",Width=500,Height=700});
    internal static RealtimeSceneLayout Layout()=>new(new[]{Asset()},Array.Empty<RealtimeLightSlot>());
    [Fact]public void InitializationHasMixedAssetsAndStableEmptyLightContainer()
    {
        var scene=new RealtimeSceneLayout(new[]{Asset(),Asset("Model_gs",true)},Array.Empty<RealtimeLightSlot>());
        string content=RealtimeScene.Build(new(Guid.NewGuid(),"mesh-id",RealtimePreviewManagerTests.Camera(),Scene:scene));
        Assert.Contains("def Mesh \"Geom\"",content);Assert.Contains("def ParticleField3DGaussianSplat \"Geom\"",content);
        Assert.Contains("@manycore:/mesh/mesh-id@</mesh>",content);Assert.Contains("@manycore:/mesh/gaussian-id@</GaussianSplatData>",content);
        Assert.Contains("def Xform \"Lights\"",content);Assert.Contains("def Xform \"Model_gs\"",content);
    }
    [Fact]public void DeleteMiddleLightAndModelKeepsOtherPathsAndUsesTypedDeleteMarkers()
    {
        var before=new RealtimeSceneLayout(new[]{Asset(),Asset("Model_g",true)},new[]{Light("Light_a"),Light("Light_b",true),Light("Light_c")});
        var after=before with{Assets=new[]{before.Assets[1]},Lights=new[]{before.Lights[0],before.Lights[2]}};
        string patch=RealtimeLayout.Patch(before,after);
        Assert.Contains("over Mesh \"Geom\" (",patch);Assert.Contains("over RectLight \"Light_b\" (",patch);Assert.Equal(2,patch.Split("manycore:operation:type").Length-1);
        Assert.Contains("over SphereLight \"Light_a\"",patch);Assert.Contains("over SphereLight \"Light_c\"",patch);Assert.Contains("over ParticleField3DGaussianSplat \"Geom\" {}",patch);
        Assert.DoesNotContain("payload",patch);Assert.DoesNotContain("Cameras",patch);Assert.DoesNotContain("def ",patch);
    }
    [Fact]public void FirstAndLastLightChangesKeepContainerAndExplicitOperations()
    {
        var none=Layout();var one=none with{Lights=new[]{Light()}};
        Assert.Contains("def SphereLight \"Light_a\"",RealtimeLayout.Patch(none,one));
        Assert.Contains("over SphereLight \"Light_a\" (",RealtimeLayout.Patch(one,none));
        Assert.Contains("\"manycore:operation:type\" = \"del\"",RealtimeLayout.Patch(one,none));
        Assert.Contains("def RectLight \"Light_new\"",RealtimeLayout.Patch(none,none with{Lights=new[]{Light("Light_new",true)}}));
    }
    [Theory]
    [InlineData("Cameras")] [InlineData("Model_/bad")] [InlineData("Model_x\"{}")] [InlineData("../Model")]
    public void ArbitraryPathsAndSyntaxCannotBecomeAssetSlots(string id)
        =>Assert.Throws<ArgumentException>(()=>RealtimeLayout.Snapshot(new(new[]{Asset(id)},Array.Empty<RealtimeLightSlot>())));
    [Theory]
    [InlineData("Light_../x")] [InlineData("Model")] [InlineData("Light_a\n")]
    public void ArbitraryPathsCannotBecomeLightSlots(string id)
        =>Assert.Throws<ArgumentException>(()=>RealtimeLayout.Snapshot(Layout() with{Lights=new[]{Light(id)}}));
    [Fact]public void DuplicateIdsAndPayloadOrTypeReplacementFailBeforePatch()
    {
        var scene=Layout();Assert.Throws<ArgumentException>(()=>RealtimeLayout.Snapshot(scene with{Assets=new[]{Asset(),Asset()}}));
        Assert.Throws<ArgumentException>(()=>RealtimeLayout.Snapshot(scene with{Lights=new[]{Light(),Light()}}));
        Assert.Throws<ArgumentException>(()=>RealtimeLayout.Patch(scene,scene with{Assets=new[]{Asset() with{MeshId="other"}}}));
        Assert.Throws<ArgumentException>(()=>RealtimeLayout.Patch(scene,scene with{Assets=new[]{Asset() with{Gaussian=true}}}));
        Assert.Throws<ArgumentException>(()=>RealtimeLayout.Patch(scene with{Lights=new[]{Light()}},scene with{Lights=new[]{Light(rect:true)}}));
    }
    [Fact]public void RetiredSlotsCannotBeReintroducedButFreshNamesCan()
    {
        var empty=new RealtimeSceneLayout(Array.Empty<RealtimeAssetSlot>(),Array.Empty<RealtimeLightSlot>());
        Assert.Throws<ArgumentException>(()=>RealtimeLayout.Patch(empty,Layout(),new HashSet<string>{"Model"}));
        Assert.Throws<ArgumentException>(()=>RealtimeLayout.Patch(empty,empty with{Lights=new[]{Light()}},usedLights:new HashSet<string>{"Light_a"}));
        string patch=RealtimeLayout.Patch(empty,new(new[]{Asset("Model_new")},new[]{Light("Light_new")}),new HashSet<string>{"Model"},new HashSet<string>{"Light_a"});
        Assert.Contains("def Xform \"Model_new\"",patch);Assert.Contains("def SphereLight \"Light_new\"",patch);
    }
    [Fact]public void SnapshotAndFingerprintOwnArraysAndIgnorePresentationOrder()
    {
        var input=new RealtimeSceneLayout(new[]{Asset(),Asset("Model_b",true)},new[]{Light("Light_b"),Light("Light_a")});
        var snapshot=RealtimeLayout.Snapshot(input);string hash=RealtimeLayout.Fingerprint(snapshot);
        Assert.Equal(hash,RealtimeLayout.Fingerprint(input with{Assets=input.Assets.Reverse().ToArray(),Lights=input.Lights.Reverse().ToArray()}));
        input.Lights[0].Light.Color[0]=100;input.Lights[0].Light.Transform[12]=999;input.Assets[0]=Asset() with{MeshId="changed"};
        Assert.Equal(hash,RealtimeLayout.Fingerprint(snapshot));Assert.NotEqual(hash,RealtimeLayout.Fingerprint(input));
    }
    [Fact]public void EmptyFinalSceneIsAllowedButEmptyInitialAssetSceneIsRejected()
    {
        var empty=new RealtimeSceneLayout(Array.Empty<RealtimeAssetSlot>(),Array.Empty<RealtimeLightSlot>());
        Assert.Contains("manycore:operation:type",RealtimeLayout.Patch(Layout(),empty));
        Assert.Throws<ArgumentException>(()=>RealtimeScene.Build(new(Guid.NewGuid(),"",RealtimePreviewManagerTests.Camera(),Scene:empty)));
        Assert.Throws<ArgumentOutOfRangeException>(()=>RealtimeLayout.Snapshot(empty with{EnvironmentIntensity=0}));
    }
}
