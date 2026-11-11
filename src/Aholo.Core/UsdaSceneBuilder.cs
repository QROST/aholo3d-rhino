using System.Globalization;
using System.Text;

namespace Aholo.Core;

public static class UsdaSceneBuilder
{
    public static string Build(string meshId, RenderCamera camera, int width, int height, double environmentIntensity = 1, IReadOnlyList<RenderLight>? lights = null, double[]? modelTransform = null)
    {
        ArgumentNullException.ThrowIfNull(camera);
        if (string.IsNullOrWhiteSpace(meshId) || meshId.Any(c => !((c >= 'A' && c <= 'Z') || (c >= 'a' && c <= 'z') || (c >= '0' && c <= '9') || c is '-' or '_')))
            throw new ArgumentException("Mesh ID contains invalid characters.", nameof(meshId));
        if (width <= 0 || height <= 0 || width > 4500 || height > 4500) throw new ArgumentOutOfRangeException(nameof(width), "RenderCloud's documented default maximum is 4500 pixels per side.");
        if (camera.Transform is not { Length: 16 } || camera.Transform.Any(x => !double.IsFinite(x))) throw new ArgumentException("Camera pose must contain 16 finite values.", nameof(camera));
        if (camera.Transform[3] != 0 || camera.Transform[7] != 0 || camera.Transform[11] != 0 || camera.Transform[15] != 1)
            throw new ArgumentException("Camera pose must be an affine row-vector matrix.", nameof(camera));
        PositiveFloat(camera.FocalLength, nameof(camera.FocalLength));
        PositiveFloat(camera.HorizontalAperture, nameof(camera.HorizontalAperture));
        PositiveFloat(camera.VerticalAperture, nameof(camera.VerticalAperture));
        PositiveFloat(camera.Near, nameof(camera.Near));
        PositiveFloat(camera.Far, nameof(camera.Far));
        PositiveFloat(environmentIntensity, nameof(environmentIntensity));
        if (camera.Far <= camera.Near) throw new ArgumentException("Far clipping plane must exceed near clipping plane.", nameof(camera));
        string lightScene=RenderLightsUsda.Build(lights);
        string modelPose=RenderModelPlacement.FormatPose(modelTransform??new RenderModelPlacement().Pose(true));
        var pose = new StringBuilder();
        for (var row = 0; row < 4; row++)
        {
            if (row > 0) pose.Append(", ");
            pose.Append('(').Append(string.Join(", ", camera.Transform.Skip(row * 4).Take(4).Select(F))).Append(')');
        }
        // The inverse of GlbWriter's canonical glTF conversion. This assumes mesh ingestion preserves
        // glTF coordinates; confirm against the live fast-upload pipeline before claiming visual parity.
        return $$"""
#usda 1.0
(
    defaultPrim = "World"
)

def Xform "World"
{
    def Xform "Model"
    {
        matrix4d xformOp:transform = ({{modelPose}})
        uniform token[] xformOpOrder = ["xformOp:transform"]
        def Mesh "Geom" (
            payload = @manycore:/mesh/{{meshId}}@</mesh>
        ) {}
    }
{{lightScene}}    def Xform "Cameras"
    {
        def Camera "MainCamera"
        {
            token projection = "perspective"
            float2 clippingRange = ({{F(camera.Near)}}, {{F(camera.Far)}})
            float focalLength = {{F(camera.FocalLength)}}
            float horizontalAperture = {{F(camera.HorizontalAperture)}}
            float verticalAperture = {{F(camera.VerticalAperture)}}
            float fStop = 0
            matrix4d xformOp:transform = ({{pose}})
            uniform token[] xformOpOrder = ["xformOp:transform"]
        }
    }
}

def Scope "Render"
{
    def RenderSettings "MainRenderSettings"
    {
        rel camera = </World/Cameras/MainCamera>
        int2 resolution = ({{width}}, {{height}})
        custom float3 manycore:environment:giIntensity = ({{F(environmentIntensity)}}, {{F(environmentIntensity)}}, {{F(environmentIntensity)}})
    }
}
""";
    }
    private static string F(double value) => value.ToString("R", CultureInfo.InvariantCulture);
    private static void PositiveFloat(double value, string name)
    {
        if (!double.IsFinite(value) || value <= 0 || !float.IsFinite((float)value) || (float)value <= 0) throw new ArgumentOutOfRangeException(name, "Value must be positive and representable as a finite float.");
    }
}
