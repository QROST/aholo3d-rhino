using System.Security.Cryptography;
using Aholo3D.Core;
using Rhino.DocObjects;
using Rhino.FileIO;
using Rhino.Geometry;
using RhinoDoc=global::Rhino.RhinoDoc;
namespace Aholo3D.Rhino;

/// <summary>Derived, self-contained 3DM. Source provenance is distinct from the derived file's integrity hash.</summary>
internal sealed record UsdSnapshotSettings(global::Rhino.UnitSystem Units,double Tolerance,double MillimetresToDocument,double ColorGamma);

internal sealed class UsdMeshSnapshot : IMeshImportSnapshot
{
    private int disposed;
    public string Path { get; }
    public string Sha256 { get; }
    internal UsdSnapshotSettings Settings { get; }
    internal string SourceFormat { get; }
    internal string SourceSha256 { get; }
    internal string[] Warnings { get; }
    internal string Dependencies { get; }
    private UsdMeshSnapshot(string path,string sha,string sourceFormat,UsdMeshScene scene,UsdSnapshotSettings settings)
    {Settings=settings;Path=path;Sha256=sha;SourceFormat=sourceFormat;SourceSha256=scene.SourceSha256;Warnings=scene.Warnings.ToArray();Dependencies=System.Text.Json.JsonSerializer.Serialize(scene.DependencyHashes);}
    internal static UsdSnapshotSettings CaptureSettings(RhinoDoc target)
    {
        if(global::Rhino.RhinoApp.InvokeRequired)throw new InvalidOperationException("Capture document settings on the Rhino UI thread.");
        if(target.ModelUnitSystem is global::Rhino.UnitSystem.None or global::Rhino.UnitSystem.Unset or global::Rhino.UnitSystem.CustomUnits)
            throw new InvalidOperationException(UiText.Get("Set explicit length units for the Rhino document first."));
        using var workflow=target.RenderSettings.LinearWorkflow;
        double gamma=workflow.PreProcessColors?workflow.PreProcessGamma:1;
        double factor=global::Rhino.RhinoMath.UnitScale(global::Rhino.UnitSystem.Millimeters,target.ModelUnitSystem);
        if(!double.IsFinite(factor)||factor<=0)throw new InvalidOperationException(UiText.Get("Cannot convert document units."));
        if(!double.IsFinite(gamma)||gamma<=0)throw new InvalidOperationException(UiText.Get("The document color preprocessing gamma is invalid."));
        return new(target.ModelUnitSystem,target.ModelAbsoluteTolerance,factor,gamma);
    }
    // Worker owns all standalone geometry and File3dm objects; no RhinoDoc access or shared native objects here.
    internal static IMeshImportSnapshot Create(UsdMeshScene scene,string sourceFormat,UsdSnapshotSettings settings,CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();
        string directory=System.IO.Path.Combine(System.IO.Path.GetTempPath(),"Aholo3D-Rhino",Guid.NewGuid().ToString("N"));
        try
        {
            if(OperatingSystem.IsWindows())Directory.CreateDirectory(directory);
            else Directory.CreateDirectory(directory,UnixFileMode.UserRead|UnixFileMode.UserWrite|UnixFileMode.UserExecute);
            using var file=new File3dm();file.Settings.ModelUnitSystem=settings.Units;file.Settings.ModelAbsoluteTolerance=settings.Tolerance;
            int layerIndex=file.AllLayers.AddLayer("Imported meshes",System.Drawing.Color.White);
            if(layerIndex<0)throw new InvalidOperationException("Could not create the mesh snapshot layer.");
            double factor=settings.MillimetresToDocument,gamma=settings.ColorGamma;
            var embedded=new Dictionary<string,string>(StringComparer.Ordinal);
            foreach(var part in scene.Parts)
            {
                ct.ThrowIfCancellationRequested();
                using var mesh=new Mesh();mesh.Vertices.UseDoublePrecisionVertices=true;
                var transform=scene.ToMillimetres(part.Transform);
                var normalTransform=transform.Linear.Inverse().Transpose();
                for(int i=0;i<part.Positions.Length;i+=3)
                {
                    if((i&4095)==0)ct.ThrowIfCancellationRequested();
                    var p=transform.TransformPoint(new(part.Positions[i],part.Positions[i+1],part.Positions[i+2]))*factor;
                    if(!p.IsFinite)throw new InvalidDataException("Source vertex exceeds document coordinate precision.");
                    mesh.Vertices.Add(p.X,p.Y,p.Z);
                }
                bool reverse=part.LeftHanded^Reflected(transform.Linear);int offset=0;
                foreach(int count in part.FaceCounts)
                {
                    if((offset&4095)==0)ct.ThrowIfCancellationRequested();var ix=part.Indices;
                    if(count==3)mesh.Faces.AddFace(ix[offset],ix[offset+(reverse?2:1)],ix[offset+(reverse?1:2)]);
                    else mesh.Faces.AddFace(ix[offset],ix[offset+(reverse?3:1)],ix[offset+2],ix[offset+(reverse?1:3)]);
                    offset+=count;
                }
                if(part.Normals is {} normals)
                {
                    var converted=new Vector3f[normals.Length/3];
                    for(int i=0;i<converted.Length;i++)
                    {
                        if((i&4095)==0)ct.ThrowIfCancellationRequested();
                        var n=normalTransform.Transform(new(normals[i*3],normals[i*3+1],normals[i*3+2])).Normalized();
                        converted[i]=new((float)n.X,(float)n.Y,(float)n.Z);
                    }
                    if(!mesh.Normals.SetNormals(converted))throw new InvalidDataException("Could not preserve mesh normals.");
                }
                else if(!mesh.Normals.ComputeNormals())throw new InvalidDataException("Could not compute mesh normals.");
                if(part.TexCoords is {} uv)
                {
                    var converted=new Point2f[uv.Length/2];for(int i=0;i<converted.Length;i++)converted[i]=new(uv[i*2],uv[i*2+1]);
                    if(!mesh.TextureCoordinates.SetTextureCoordinates(converted))throw new InvalidDataException("Could not preserve mesh texture coordinates.");
                }
                if(!mesh.IsValidWithLog(out string log))throw new InvalidDataException("Source mesh did not pass Rhino validation: "+log);
                using var material=new Material{Name=part.Name};material.ToPhysicallyBased();var pbr=material.PhysicallyBased;
                var color=part.Material.LinearBaseColor();
                pbr.BaseColor=new global::Rhino.Display.Color4f((float)Math.Pow(color[0],1/gamma),(float)Math.Pow(color[1],1/gamma),(float)Math.Pow(color[2],1/gamma),1);
                pbr.Alpha=color[3];pbr.Metallic=part.Material.Metallic;pbr.Roughness=part.Material.Roughness;
                if(part.Texture is {} bitmap)
                {
                    string filename=bitmap.Sha256.ToLowerInvariant()+(bitmap.MimeType=="image/png"?".png":".jpg");
                    if(!embedded.TryGetValue(filename,out string? imagePath))
                    {
                        imagePath=System.IO.Path.Combine(directory,filename);
                        using(var image=File.Create(imagePath))bitmap.CopyEncodedTo(image,ct);
                        if(!file.EmbeddedFiles.Add(imagePath))throw new InvalidDataException("Could not embed the mesh bitmap in the snapshot.");
                        embedded.Add(filename,imagePath);
                    }
                    using var texture=new Texture{FileReference=FileReference.CreateFromFullPath(imagePath),Enabled=true,
                        TextureCombineMode=TextureCombineMode.Modulate,ProjectionMode=TextureProjectionModes.MappingChannel,
                        WrapU=bitmap.WrapU==MeshTextureWrap.Repeat?TextureUvwWrapping.Repeat:TextureUvwWrapping.Clamp,
                        WrapV=bitmap.WrapV==MeshTextureWrap.Repeat?TextureUvwWrapping.Repeat:TextureUvwWrapping.Clamp,
                        MinFilter=bitmap.MinFilter==MeshTextureFilter.Nearest?TextureFilter.Nearest:TextureFilter.Linear,
                        MagFilter=bitmap.MagFilter==MeshTextureFilter.Nearest?TextureFilter.Nearest:TextureFilter.Linear};
                    if(!pbr.SetTexture(texture,TextureType.PBR_BaseColor))throw new InvalidDataException("Could not assign the mesh bitmap.");
                    pbr.UseBaseColorTextureAlphaForObjectAlphaTransparencyTexture=bitmap.UseAlpha;
                }
                pbr.SynchronizeLegacyMaterial();int materialIndex=file.AllMaterials.AddMaterial(material);
                if(materialIndex<0)throw new InvalidOperationException("Could not create the mesh snapshot material.");
                using var attr=new ObjectAttributes{Name=part.Name,LayerIndex=layerIndex,MaterialSource=ObjectMaterialSource.MaterialFromObject,MaterialIndex=materialIndex};
                attr.SetUserString("Aholo.Mesh.SourceFormat",sourceFormat);attr.SetUserString("Aholo.Mesh.SourceSha256",scene.SourceSha256);attr.SetUserString("Aholo.Mesh.SourcePrim",part.SourcePrimPath);attr.SetUserString("Aholo.Mesh.SourcePart",part.PrimPath);attr.SetUserString("Aholo.Mesh.SourceDoubleSided",part.DoubleSided?"true":"false");attr.SetUserString("Aholo.Mesh.SourceDependencies",System.Text.Json.JsonSerializer.Serialize(scene.DependencyHashes));
                if(part.GeneratedFacetNormals)attr.SetUserString("Aholo.Mesh.GeneratedFacetNormals","true");
                if(scene.SourceEntry is not null)attr.SetUserString("Aholo.Mesh.SourceEntry",scene.SourceEntry);
                attr.SetUserString("Aholo.Mesh.SourceUpAxis",scene.UpAxis);
                attr.SetUserString("Aholo.Mesh.SourceMetersPerUnit",scene.MetersPerUnit.ToString("R",System.Globalization.CultureInfo.InvariantCulture));
                if(part.SourceGroups.Count>0)attr.SetUserString("Aholo.Mesh.SourceGroups",System.Text.Json.JsonSerializer.Serialize(part.SourceGroups));
                if(file.Objects.AddMesh(mesh,attr)==Guid.Empty)throw new InvalidOperationException("Could not create the mesh snapshot mesh.");
            }
            string path=System.IO.Path.Combine(directory,"asset.3dm");ct.ThrowIfCancellationRequested();
            if(!file.WriteWithLog(path,8,out string writeLog))throw new InvalidDataException("Could not write the mesh snapshot: "+writeLog);
            // Independent readback verifies embedded bytes before disposable extraction paths disappear.
            using(var reopened=File3dm.Read(path)??throw new InvalidDataException("Could not reopen the mesh snapshot."))
            {
                if(reopened.Objects.Count!=scene.Parts.Count)throw new InvalidDataException("Mesh snapshot object count changed.");
                var verified=new HashSet<string>(StringComparer.Ordinal);
                foreach(var image in reopened.EmbeddedFiles)
                {
                    string name=System.IO.Path.GetFileName(image.Filename);
                    if(!embedded.ContainsKey(name)||!verified.Add(name))throw new InvalidDataException("Unexpected embedded mesh bitmap.");
                    string check=System.IO.Path.Combine(directory,"verify-"+name);
                    if(!image.SaveToFile(check))throw new InvalidDataException("Could not verify an embedded mesh bitmap.");
                    using(var bytes=File.OpenRead(check))if(!Convert.ToHexString(SHA256.HashData(bytes)).Equals(System.IO.Path.GetFileNameWithoutExtension(name),StringComparison.OrdinalIgnoreCase))throw new InvalidDataException("Embedded mesh bitmap changed.");
                    File.Delete(check);
                }
                if(verified.Count!=embedded.Count)throw new InvalidDataException("Mesh snapshot is missing an embedded bitmap.");
            }
            ct.ThrowIfCancellationRequested();using var stream=File.OpenRead(path);string hash=Convert.ToHexString(SHA256.HashData(stream));
            return new UsdMeshSnapshot(path,hash,sourceFormat,scene,settings);
        }
        catch{try{Directory.Delete(directory,true);}catch(IOException){}catch(UnauthorizedAccessException){}throw;}
    }
    private static bool Reflected(GaussianMatrix a)
    {
        double s=new[]{Math.Abs(a.M11),Math.Abs(a.M12),Math.Abs(a.M13),Math.Abs(a.M21),Math.Abs(a.M22),Math.Abs(a.M23),Math.Abs(a.M31),Math.Abs(a.M32),Math.Abs(a.M33)}.Max();
        return (a.M11/s)*((a.M22/s)*(a.M33/s)-(a.M23/s)*(a.M32/s))-(a.M12/s)*((a.M21/s)*(a.M33/s)-(a.M23/s)*(a.M31/s))+(a.M13/s)*((a.M21/s)*(a.M32/s)-(a.M22/s)*(a.M31/s))<0;
    }
    internal void ValidateTarget(RhinoDoc target)
    {
        var current=CaptureSettings(target);
        if(current!=Settings)
            throw new InvalidOperationException(UiText.Get("Document units or color preprocessing changed. Reload the mesh candidate before acceptance."));
    }
    public void Dispose(){if(Interlocked.Exchange(ref disposed,1)!=0)return;try{Directory.Delete(System.IO.Path.GetDirectoryName(Path)!,true);}catch(IOException){}catch(UnauthorizedAccessException){}}
}
