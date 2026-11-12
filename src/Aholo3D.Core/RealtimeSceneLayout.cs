using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
namespace Aholo3D.Core;

public sealed record RealtimeAssetSlot(string Id,string MeshId,bool Gaussian=false,bool GltfCoordinates=true,RenderModelPlacement? Model=null);
public sealed record RealtimeLightSlot(string Id,RenderLight Light);
public sealed record RealtimeSceneLayout(RealtimeAssetSlot[] Assets,RealtimeLightSlot[] Lights,double EnvironmentIntensity=1);
public sealed record RealtimeCompositionRequest(Guid Id,long Revision,long BaseSceneRevision,RealtimeSceneLayout Scene);

/// <summary>Stable cloud prim identities and complete acknowledged scene snapshots, separate from camera updates.</summary>
public static class RealtimeLayout
{
    public static RealtimeSceneLayout FromStart(RealtimeStartRequest request)=>Snapshot(request.Scene??new(
        new[]{new RealtimeAssetSlot("Model",request.MeshId,request.Gaussian,request.GltfCoordinates,request.Model??new())},
        (request.Lights??Array.Empty<RenderLight>()).Select((light,i)=>new RealtimeLightSlot("Light_"+i.ToString(CultureInfo.InvariantCulture),light)).ToArray(),request.EnvironmentIntensity));
    public static RealtimeSceneLayout Snapshot(RealtimeSceneLayout scene)
    {
        ArgumentNullException.ThrowIfNull(scene);ArgumentNullException.ThrowIfNull(scene.Assets);ArgumentNullException.ThrowIfNull(scene.Lights);
        if(!double.IsFinite(scene.EnvironmentIntensity)||scene.EnvironmentIntensity<=0||!float.IsFinite((float)scene.EnvironmentIntensity)||(float)scene.EnvironmentIntensity<=0)
            throw new ArgumentOutOfRangeException(nameof(scene.EnvironmentIntensity),"Environment brightness must be a positive finite float.");
        var assets=scene.Assets.Select(asset=>
        {
            ArgumentNullException.ThrowIfNull(asset);Slot(asset.Id,true);UsdaSceneBuilder.ValidateMeshId(asset.MeshId);
            var model=asset.Model??new();_=model.Pose(asset.GltfCoordinates);return asset with{Model=model};
        }).OrderBy(a=>a.Id,StringComparer.Ordinal).ToArray();
        var lights=scene.Lights.Select(slot=>
        {
            ArgumentNullException.ThrowIfNull(slot);Slot(slot.Id,false);ArgumentNullException.ThrowIfNull(slot.Light);
            var light=slot.Light;ArgumentNullException.ThrowIfNull(light.Transform);ArgumentNullException.ThrowIfNull(light.Color);
            light=light with{Transform=(double[])light.Transform.Clone(),Color=(double[])light.Color.Clone()};light.Validate();return slot with{Light=light};
        }).OrderBy(l=>l.Id,StringComparer.Ordinal).ToArray();
        if(assets.Select(a=>a.Id).Distinct(StringComparer.Ordinal).Count()!=assets.Length||lights.Select(l=>l.Id).Distinct(StringComparer.Ordinal).Count()!=lights.Length)
            throw new ArgumentException("Scene object identities must be unique.");
        return new(assets,lights,scene.EnvironmentIntensity);
    }
    private static void Slot(string id,bool asset)
    {
        if(id is null||!Regex.IsMatch(id,asset?@"\A(?:Model|Model_[A-Za-z0-9_]+)\z":@"\ALight_[A-Za-z0-9_]+\z"))
            throw new ArgumentException("Invalid scene object identity.");
    }
    public static string Fingerprint(RealtimeSceneLayout scene)=>Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(JsonSerializer.Serialize(Snapshot(scene),Aholo3DJson.Options))));
    public static RealtimeSceneLayout LegacyChange(RealtimeStartRequest original,RealtimeSceneRequest request)
    {
        ArgumentNullException.ThrowIfNull(request.Model);ArgumentNullException.ThrowIfNull(request.Lights);
        var initial=original.Lights??Array.Empty<RenderLight>();
        if(request.Lights.Length!=initial.Length||request.Lights.Where((l,i)=>l is null||l.Kind!=initial[i].Kind).Any())
            throw new ArgumentException("Scene edits must retain the original light slots and types.");
        return Snapshot(new(new[]{new RealtimeAssetSlot("Model",original.MeshId,original.Gaussian,original.GltfCoordinates,request.Model)},
            request.Lights.Select((light,i)=>new RealtimeLightSlot("Light_"+i.ToString(CultureInfo.InvariantCulture),light)).ToArray(),request.EnvironmentIntensity));
    }
    public static string Build(RealtimeStartRequest request)
    {
        var scene=FromStart(request);
        if(scene.Assets.Length==0)throw new ArgumentException("Add a prepared cloud asset before starting a preview.");
        return UsdaSceneBuilder.BuildScene(string.Concat(scene.Assets.Select(a=>Model(a,"def",true))),
            request.Camera,request.Width,request.Height,scene.EnvironmentIntensity,Lights(scene.Lights,"def"));
    }
    public static string Patch(RealtimeSceneLayout previous,RealtimeSceneLayout next,IReadOnlySet<string>? usedAssets=null,IReadOnlySet<string>? usedLights=null)
    {
        previous=Snapshot(previous);next=Snapshot(next);
        var nextAssets=next.Assets.Select(a=>a.Id).ToHashSet(StringComparer.Ordinal);var nextLights=next.Lights.Select(l=>l.Id).ToHashSet(StringComparer.Ordinal);
        var oldAssets=previous.Assets.ToDictionary(a=>a.Id,StringComparer.Ordinal);var oldLights=previous.Lights.ToDictionary(l=>l.Id,StringComparer.Ordinal);
        foreach(var asset in next.Assets)
        {
            if(oldAssets.TryGetValue(asset.Id,out var old))
            {
                if(asset.MeshId!=old.MeshId||asset.Gaussian!=old.Gaussian)throw new ArgumentException("Add a new scene object to replace an asset or change its type.");
            }
            else if(usedAssets?.Contains(asset.Id)==true)throw new ArgumentException("Deleted scene object identities cannot be reused in this preview.");
        }
        foreach(var light in next.Lights)
        {
            if(oldLights.TryGetValue(light.Id,out var old))
            {
                if(light.Light.Kind!=old.Light.Kind)throw new ArgumentException("Add a new scene light to change its type.");
            }
            else if(usedLights?.Contains(light.Id)==true)throw new ArgumentException("Deleted scene object identities cannot be reused in this preview.");
        }
        var text=new StringBuilder("#usda 1.0\nover \"World\"\n{\n");
        foreach(var old in previous.Assets.Where(a=>!nextAssets.Contains(a.Id)))
            text.Append("    over \"").Append(old.Id).Append("\"\n    {\n").Append(Delete(old.Gaussian?"ParticleField3DGaussianSplat":"Mesh","Geom")).Append("    }\n");
        foreach(var asset in next.Assets)text.Append(Model(asset,oldAssets.ContainsKey(asset.Id)?"over":"def",!oldAssets.ContainsKey(asset.Id)));
        text.Append("    over \"Lights\"\n    {\n");
        foreach(var old in previous.Lights.Where(l=>!nextLights.Contains(l.Id)))text.Append(Delete(old.Light.Kind+"Light",old.Id));
        foreach(var light in next.Lights)text.Append(RenderLightsUsda.BuildLight(light.Light,light.Id,oldLights.ContainsKey(light.Id)?"over":"def"));
        text.Append("    }\n}\n");
        string intensity=F(next.EnvironmentIntensity);
        text.Append("over Scope \"Render\"\n{\n    over RenderSettings \"MainRenderSettings\"\n    {\n        custom float3 manycore:environment:giIntensity = (")
            .Append(intensity).Append(", ").Append(intensity).Append(", ").Append(intensity).Append(")\n    }\n}\n");
        return text.ToString();
    }
    private static string Model(RealtimeAssetSlot asset,string specifier,bool payload)
    {
        string type=asset.Gaussian?"ParticleField3DGaussianSplat":"Mesh",target=asset.Gaussian?"GaussianSplatData":"mesh";
        string metadata=payload?$" (\n            payload = @manycore:/mesh/{asset.MeshId}@</{target}>\n        )":"";
        return $$"""
    {{specifier}} {{(specifier=="def"?"Xform ":"")}}"{{asset.Id}}"
    {
        matrix4d xformOp:transform = ({{RenderModelPlacement.FormatPose(asset.Model!.Pose(asset.GltfCoordinates))}})
        uniform token[] xformOpOrder = ["xformOp:transform"]
        {{specifier}} {{type}} "Geom"{{metadata}} {}
    }

""";
    }
    private static string Lights(RealtimeLightSlot[] lights,string specifier)=>"    "+specifier+" Xform \"Lights\"\n    {\n"+string.Concat(lights.Select(l=>RenderLightsUsda.BuildLight(l.Light,l.Id,specifier)))+"    }\n";
    private static string Delete(string type,string id)=>$$"""
        over {{type}} "{{id}}" (
            customData = {
                string "manycore:operation:type" = "del"
            }
        ) {}

""";
    private static string F(double value)=>value.ToString("R",CultureInfo.InvariantCulture);
}
