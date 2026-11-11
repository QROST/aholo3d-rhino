namespace Aholo.Core;

public static class RealtimeScene
{
    public static string Build(RealtimeStartRequest request)
    {
        string scene=UsdaSceneBuilder.Build(request.MeshId,request.Camera,request.Width,request.Height,request.EnvironmentIntensity,request.Lights,(request.Model??new()).Pose(request.GltfCoordinates));
        if(request.Gaussian)
            scene=scene.Replace("def Mesh \"Geom\"","def ParticleField3DGaussianSplat \"Geom\"",StringComparison.Ordinal).Replace("@</mesh>","@</GaussianSplatData>",StringComparison.Ordinal);
        return scene;
    }
    public static RealtimeStartRequest Snapshot(RealtimeStartRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);ArgumentNullException.ThrowIfNull(request.Camera);ArgumentNullException.ThrowIfNull(request.Camera.Transform);
        if(request.Lights is { } lights&&lights.Any(l=>l is null||l.Transform is null||l.Color is null))throw new ArgumentException("Light snapshot arrays must be present.");
        return request with
        {
            Camera=request.Camera with{Transform=(double[])request.Camera.Transform.Clone()},
            Lights=request.Lights?.Select(l=>l with{Transform=(double[])l.Transform.Clone(),Color=(double[])l.Color.Clone()}).ToArray()
        };
    }
    public static string ScenePatch(RealtimeStartRequest original,RealtimeSceneRequest request)
    {
        ArgumentNullException.ThrowIfNull(request.Model);ArgumentNullException.ThrowIfNull(request.Lights);
        var initial=original.Lights??Array.Empty<RenderLight>();
        if(request.Lights.Length!=initial.Length||request.Lights.Where((l,i)=>l is null||l.Kind!=initial[i].Kind).Any())
            throw new ArgumentException("Scene edits must retain the original light slots and types.");
        _=Build(original with{Model=request.Model,EnvironmentIntensity=request.EnvironmentIntensity,Lights=request.Lights});
        string F(double value)=>value.ToString("R",System.Globalization.CultureInfo.InvariantCulture);
        // Scene edits carry no camera fields; camera coalescing cannot replace a scene barrier.
        string lightPatch=RenderLightsUsda.Build(request.Lights).Replace("def Xform \"Lights\"","over \"Lights\"",StringComparison.Ordinal)
            .Replace("def RectLight ","over RectLight ",StringComparison.Ordinal).Replace("def SphereLight ","over SphereLight ",StringComparison.Ordinal);
        return $$"""
#usda 1.0
over "World"
{
    over "Model"
    {
        matrix4d xformOp:transform = ({{RenderModelPlacement.FormatPose(request.Model.Pose(original.GltfCoordinates))}})
        uniform token[] xformOpOrder = ["xformOp:transform"]
        over {{(original.Gaussian?"ParticleField3DGaussianSplat":"Mesh")}} "Geom" {}
    }
{{lightPatch}}}
over Scope "Render"
{
    over RenderSettings "MainRenderSettings"
    {
        custom float3 manycore:environment:giIntensity = ({{F(request.EnvironmentIntensity)}}, {{F(request.EnvironmentIntensity)}}, {{F(request.EnvironmentIntensity)}})
    }
}
""";
    }
    // The public streaming contract reads camera attributes from an over node.
    public static string CameraPatch(RealtimeStartRequest request)
    {
        _=Build(request); // Reuse the full numeric/affine camera validation.
        var c=request.Camera;
        string F(double n)=>n.ToString("R",System.Globalization.CultureInfo.InvariantCulture);
        string pose=string.Join(", ",Enumerable.Range(0,4).Select(row=>"("+string.Join(", ",c.Transform.Skip(row*4).Take(4).Select(F))+")"));
        return $$"""
#usda 1.0
over "World"
{
    over "Cameras"
    {
        over "MainCamera"
        {
    token projection = "perspective"
    float2 clippingRange = ({{F(c.Near)}}, {{F(c.Far)}})
    float focalLength = {{F(c.FocalLength)}}
    float horizontalAperture = {{F(c.HorizontalAperture)}}
    float verticalAperture = {{F(c.VerticalAperture)}}
    matrix4d xformOp:transform = ({{pose}})
    uniform token[] xformOpOrder = ["xformOp:transform"]
        }
    }
}
""";
    }
}
