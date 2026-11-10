namespace Aholo.Core;

public static class RealtimeScene
{
    public static string Build(RealtimeStartRequest request)
    {
        string scene=UsdaSceneBuilder.Build(request.MeshId,request.Camera,request.Width,request.Height,request.EnvironmentIntensity);
        if(!request.GltfCoordinates)
            scene=scene.Replace("((1000, 0, 0, 0), (0, 0, 1000, 0), (0, -1000, 0, 0), (0, 0, 0, 1))","((1, 0, 0, 0), (0, 1, 0, 0), (0, 0, 1, 0), (0, 0, 0, 1))",StringComparison.Ordinal);
        if(request.Gaussian)
            scene=scene.Replace("def Mesh \"Geom\"","def ParticleField3DGaussianSplat \"Geom\"",StringComparison.Ordinal).Replace("@</mesh>","@</GaussianSplatData>",StringComparison.Ordinal);
        return scene;
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
 over "World" { over "Cameras" { over "MainCamera" {
    token projection = "perspective"
    float2 clippingRange = ({{F(c.Near)}}, {{F(c.Far)}})
    float focalLength = {{F(c.FocalLength)}}
    float horizontalAperture = {{F(c.HorizontalAperture)}}
    float verticalAperture = {{F(c.VerticalAperture)}}
    matrix4d xformOp:transform = ({{pose}})
    uniform token[] xformOpOrder = ["xformOp:transform"]
 } } }
""";
    }
}
