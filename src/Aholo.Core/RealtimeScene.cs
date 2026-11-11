namespace Aholo.Core;

public static class RealtimeScene
{
    public static string Build(RealtimeStartRequest request)=>RealtimeLayout.Build(request);
    public static RealtimeStartRequest Snapshot(RealtimeStartRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);ArgumentNullException.ThrowIfNull(request.Camera);ArgumentNullException.ThrowIfNull(request.Camera.Transform);
        if(request.Lights is { } lights&&lights.Any(l=>l is null||l.Transform is null||l.Color is null))throw new ArgumentException("Light snapshot arrays must be present.");
        return request with
        {
            Camera=request.Camera with{Transform=(double[])request.Camera.Transform.Clone()},
            Scene=request.Scene is null?null:RealtimeLayout.Snapshot(request.Scene),
            Lights=request.Lights?.Select(l=>l with{Transform=(double[])l.Transform.Clone(),Color=(double[])l.Color.Clone()}).ToArray()
        };
    }
    public static string ScenePatch(RealtimeStartRequest original,RealtimeSceneRequest request)
        =>RealtimeLayout.Patch(RealtimeLayout.FromStart(original),RealtimeLayout.LegacyChange(original,request));
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
