using System.Text.Json;
using Aholo3D.Core;
namespace Aholo3D.Tests;

public sealed class RenderAdvancedControlsTests
{
    internal static RenderModelPlacement Shaped(bool rotated)=>new(10,20,30,2,rotated?90:0)
    {AxisScaleX=-1,AxisScaleY=1.5,AxisScaleZ=2,ShearXY=.5,ShearXZ=.25,ShearYZ=-.5,RotationXDegrees=rotated?90:0,RotationYDegrees=rotated?90:0};
    private static double[] Apply(double[] row,double x,double y,double z)=>new[]{x*row[0]+y*row[4]+z*row[8]+row[12],x*row[1]+y*row[5]+z*row[9]+row[13],x*row[2]+y*row[6]+z*row[10]+row[14]};
    private static void Near(double[] expected,double[] actual)
    {Assert.Equal(expected.Length,actual.Length);for(int i=0;i<expected.Length;i++)Assert.True(Math.Abs(expected[i]-actual[i])<=1e-9*Math.Max(1,Math.Abs(expected[i])), $"component {i}: {expected[i]} != {actual[i]}");}
    [Fact]public void OldModelAndLightJsonKeepDefaultBehavior()
    {
        var model=JsonSerializer.Deserialize<RenderModelPlacement>("{\"x\":10,\"y\":20,\"z\":30,\"scale\":2,\"rotationDegrees\":90}",Aholo3DJson.Options)!;
        Assert.Equal(1,model.AxisScaleX);Assert.Equal(1,model.AxisScaleY);Assert.Equal(1,model.AxisScaleZ);Assert.Equal(0,model.RotationXDegrees);Assert.Equal(0,model.ShearXZ);
        Near(new[]{6010d,2020,4030},Apply(model.Pose(true),1,2,3));
        const string legacy="{\"kind\":\"Sphere\",\"transform\":[1,0,0,0,0,1,0,0,0,0,1,0,0,0,3000,1],\"color\":[1,1,1],\"intensity\":2,\"radius\":50}";
        var light=JsonSerializer.Deserialize<RenderLight>(legacy,Aholo3DJson.Options)!;light.Validate();
        Assert.Equal(0,light.Exposure);Assert.Equal(1,light.Specular);Assert.True(light.AffectReflections);Assert.False(light.DoubleSided);
    }
    [Theory]
    [InlineData(false,false,6.5,1,50)] [InlineData(true,false,-14490,-8980,-11970)]
    [InlineData(false,true,30,1,33.5)] [InlineData(true,true,-11990,-8980,14530)]
    public void SignedScaleShearAndRotationMatchHandCalculatedWorldPoints(bool gltf,bool rotated,double x,double y,double z)
        =>Near(new[]{x,y,z},Apply(Shaped(rotated).Pose(gltf),2,-3,5));
    [Fact]public void ParameterizedTransformsMatchAnIndependentSequentialPointOracle()
    {
        var random=new Random(73061);
        double Value()=>random.NextDouble()*4-2;
        for(int iteration=0;iteration<200;iteration++)
        {
            var model=new RenderModelPlacement(Value()*100,Value()*100,Value()*100,.2+random.NextDouble()*3,Value()*360)
            {RotationXDegrees=Value()*360,RotationYDegrees=Value()*360,AxisScaleX=(iteration%2==0?-1:1)*(.2+random.NextDouble()*3),AxisScaleY=.2+random.NextDouble()*3,AxisScaleZ=.2+random.NextDouble()*3,ShearXY=Value(),ShearXZ=Value(),ShearYZ=Value()};
            double px=Value(),py=Value(),pz=Value();bool gltf=iteration%3==0;
            double x=gltf?1000*px:px,y=gltf?-1000*pz:py,z=gltf?1000*py:pz;
            x*=model.Scale*model.AxisScaleX;y*=model.Scale*model.AxisScaleY;z*=model.Scale*model.AxisScaleZ;
            x+=model.ShearXY*y+model.ShearXZ*z;y+=model.ShearYZ*z;
            double rx=model.RotationXDegrees%360*Math.PI/180,ry=model.RotationYDegrees%360*Math.PI/180,rz=model.RotationDegrees%360*Math.PI/180;
            (y,z)=(y*Math.Cos(rx)-z*Math.Sin(rx),y*Math.Sin(rx)+z*Math.Cos(rx));
            (x,z)=(x*Math.Cos(ry)+z*Math.Sin(ry),-x*Math.Sin(ry)+z*Math.Cos(ry));
            (x,y)=(x*Math.Cos(rz)-y*Math.Sin(rz),x*Math.Sin(rz)+y*Math.Cos(rz));
            Near(new[]{x+model.X,y+model.Y,z+model.Z},Apply(model.Pose(gltf),px,py,pz));
        }
    }
    [Fact]public void ZeroOrNonfiniteParametersFailButValidCombinedScaleAndLargeAnglesRemainUsable()
    {
        var invalid=new[]{new RenderModelPlacement{AxisScaleX=0},new RenderModelPlacement{AxisScaleY=double.NaN},new RenderModelPlacement{ShearXZ=double.PositiveInfinity},new RenderModelPlacement{RotationXDegrees=double.NaN},new RenderModelPlacement{RotationYDegrees=double.PositiveInfinity}};
        foreach(var model in invalid)Assert.Throws<ArgumentException>(()=>model.Pose(false));
        var cancelling=new RenderModelPlacement(Scale:1e200){AxisScaleX=1e-200,AxisScaleY=-1e-200,AxisScaleZ=1e-200};
        Near(new[]{2d,3,5},Apply(cancelling.Pose(false),2,-3,5));
        var large=new RenderModelPlacement(RotationDegrees:1e200){RotationXDegrees=-1e200,RotationYDegrees=1e200};
        Assert.Equal((large with{RotationDegrees=1e200%360,RotationXDegrees=-1e200%360,RotationYDegrees=1e200%360}).Pose(false),large.Pose(false));
    }
    [Fact]public void ZeroLightRotationClonesWithoutChangingCapturedPoseOrSettings()
    {
        var light=RealtimeSceneTests.Light() with{Exposure=-2,Specular=2.5,AffectReflections=false};
        var unchanged=light.RotateWorld(0,360,-720);
        Assert.Equal(light.Transform,unchanged.Transform);Assert.NotSame(light.Transform,unchanged.Transform);Assert.NotSame(light.Color,unchanged.Color);
        Assert.Equal(light.Exposure,unchanged.Exposure);Assert.Equal(light.Specular,unchanged.Specular);Assert.False(unchanged.AffectReflections);
        unchanged.Transform[12]=999;unchanged.Color[0]=999;Assert.Equal(1000,light.Transform[12]);Assert.Equal(1,light.Color[0]);
    }
    [Fact]public void WorldLightRotationPreservesPositionDimensionsAndCapturedDirection()
    {
        var captured=(RealtimeSceneTests.Light() with{Kind="Rect",Width=500,Height=700,DoubleSided=true,Exposure=1,Specular=.25}).RotateWorld(90,0,0);
        var rotated=captured.RotateWorld(0,0,90);rotated.Validate();
        Near(new[]{-1d,0,0},new[]{-rotated.Transform[8],-rotated.Transform[9],-rotated.Transform[10]});
        Assert.Equal(captured.Transform.Skip(12),rotated.Transform.Skip(12));Assert.Equal(500,rotated.Width);Assert.Equal(700,rotated.Height);Assert.True(rotated.DoubleSided);Assert.Equal(.25,rotated.Specular);
        Assert.Throws<ArgumentException>(()=>captured.RotateWorld(double.NaN,0,0));
    }
    [Fact]public void AdvancedLightFieldsSerializeAtStablePathsAndExplicitlyReset()
    {
        var rect=RealtimeSceneTests.Light() with{Kind="Rect",Width=500,Height=700,Exposure=-1,Specular=2.5,AffectReflections=false,DoubleSided=true};
        var before=RealtimeLayoutTests.Layout() with{Lights=new[]{new RealtimeLightSlot("Light_key",rect)}};
        string initial=RealtimeScene.Build(new(Guid.NewGuid(),"mesh",RealtimePreviewManagerTests.Camera(),Scene:before));
        Assert.Contains("float inputs:exposure = -1",initial);Assert.Contains("float inputs:specular = 2.5",initial);
        Assert.Contains("int \"manycore:affectReflections\" = 0",initial);Assert.Contains("bool \"manycore:doubleFace\" = true",initial);
        var after=before with{Lights=new[]{new RealtimeLightSlot("Light_key",rect with{Exposure=0,Specular=1,AffectReflections=true,DoubleSided=false})}};
        string patch=RealtimeLayout.Patch(before,after);Assert.Contains("over RectLight \"Light_key\"",patch);
        Assert.Contains("int \"manycore:affectReflections\" = 1",patch);Assert.Contains("bool \"manycore:doubleFace\" = false",patch);
        Assert.DoesNotContain("manycore:doubleFace",RenderLightsUsda.Build(new[]{RealtimeSceneTests.Light()}));
        Assert.NotEqual(RealtimeLayout.Fingerprint(before),RealtimeLayout.Fingerprint(after));
    }
    [Fact]public void AdvancedFieldsSurviveWireAndSceneSnapshots()
    {
        var scene=new RealtimeSceneLayout(new[]{new RealtimeAssetSlot("Model","mesh",Model:Shaped(true))},new[]{new RealtimeLightSlot("Light_a",RealtimeSceneTests.Light() with{Exposure=-2.5,Specular=3,AffectReflections=false})});
        var copy=JsonSerializer.Deserialize<RealtimeSceneLayout>(JsonSerializer.Serialize(scene,Aholo3DJson.Options),Aholo3DJson.Options)!;
        Assert.Equal(RealtimeLayout.Fingerprint(scene),RealtimeLayout.Fingerprint(copy));
        var snapshot=RealtimeLayout.Snapshot(copy);copy.Lights[0].Light.Transform[12]=999;
        Assert.Equal(RealtimeLayout.Fingerprint(scene),RealtimeLayout.Fingerprint(snapshot));
    }
    [Theory][InlineData(double.NaN)][InlineData(double.PositiveInfinity)][InlineData(double.MaxValue)]
    public void NonfiniteOrUnrepresentableAdvancedLightValuesAreRejected(double value)
    {
        var light=RealtimeSceneTests.Light();
        Assert.Throws<ArgumentException>(()=>(light with{Exposure=value}).Validate());
        Assert.Throws<ArgumentException>(()=>(light with{Specular=value}).Validate());
    }
    [Fact]public void ExposureAllowsNegativeAndFractionalValuesWithoutInventingASpecularUpperLimit()
    {
        var light=RealtimeSceneTests.Light();(light with{Exposure=-1.5,Specular=2.5}).Validate();
        (light with{Intensity=1e-30,Exposure=200}).Validate();
        (light with{Intensity=0,Exposure=0}).Validate();
        Assert.Throws<ArgumentException>(()=>(light with{Exposure=1000}).Validate());
        Assert.Throws<ArgumentException>(()=>(light with{Specular=-1}).Validate());
        Assert.Throws<ArgumentException>(()=>(light with{DoubleSided=true}).Validate());
    }
}
