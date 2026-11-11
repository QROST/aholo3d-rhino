using Aholo.Core;

internal static class SceneFixtures
{
    internal static int Run(string directory)
    {
        Directory.CreateDirectory(directory);
        byte[] image=Convert.FromBase64String("iVBORw0KGgoAAAANSUhEUgAAAAIAAAACCAYAAABytg0kAAAAF0lEQVR4nAXBAQEAAACCIKb33EAkQxUHQdUHepwfNGcAAAAASUVORK5CYII=");
        var mesh=new MeshData(new float[]{0,0,0,.125f,0,0,0,.25f,.5f},new[]{0,1,2},255,255,255)
        {Normals=new float[]{0,-2,1,0,-2,1,0,-2,1},TexCoords=new float[]{0,1,1,1,0,0},Material=new(.5,.25,.125,.6,.2,.4),
            BaseColorTexture=new(image,MeshTextureWrap.Clamp,MeshTextureWrap.Repeat,MeshTextureFilter.Nearest)};
        var parts=new[]{new UsdMeshPart("duplicate / name",mesh,new(1e12+.125,-2e12+.25,3e12+.5)),
            new UsdMeshPart("duplicate / name",mesh with{BaseColorTexture=new(image,useAlpha:false)},new(0,0,10)),
            new UsdMeshPart("",mesh with{Normals=null,TexCoords=null,BaseColorTexture=null},new(304.8,609.6,914.4))};
        foreach(string axis in new[]{"Y","Z"})foreach(double units in new[]{.001,.3048})foreach(string extension in new[]{"usda","usd","usdc","usdz"})
        {
            var result=UsdSceneWriter.Write(Path.Combine(directory,$"mixed-{axis}-{(units==.001?"mm":"ft")}.{extension}"),Scene(axis,units),parts);
            if(result.Meshes!=3||result.Triangles!=3||result.GaussianParts!=5||result.Gaussians!=12)throw new InvalidOperationException("Incorrect scene counts.");
        }
        foreach(string extension in new[]{"usda","usd","usdc","usdz"})
        {
            UsdSceneWriter.Write(Path.Combine(directory,"gs-only."+extension),Scene("Z",.001),Array.Empty<UsdMeshPart>());
            UsdSceneWriter.Write(Path.Combine(directory,"mesh-only."+extension),null,parts);
        }
        var oldAssets=Directory.GetDirectories(directory,"usd-assets-*").OrderBy(x=>x).ToArray();
        var oldImages=oldAssets.SelectMany(Directory.GetFiles).ToDictionary(x=>x,File.ReadAllBytes);
        string blocked=Path.Combine(directory,"blocked.usda");Directory.CreateDirectory(blocked);
        try{UsdSceneWriter.Write(blocked,Scene("Y",.3048),parts);throw new InvalidOperationException("Expected final commit failure.");}catch(IOException){}
        if(!oldAssets.SequenceEqual(Directory.GetDirectories(directory,"usd-assets-*").OrderBy(x=>x)))throw new InvalidOperationException("Failed export leaked a new image bundle.");
        Directory.Delete(blocked);
        string target=Path.Combine(directory,"mixed-Y-ft.usda");byte[] oldRoot=File.ReadAllBytes(target);
        using(var cancel=new CancellationTokenSource())
        {
            cancel.Cancel();try{UsdSceneWriter.Write(target,Scene("Y",.3048),parts,cancel.Token);throw new InvalidOperationException("Expected cancellation.");}catch(OperationCanceledException){}
        }
        if(!oldRoot.SequenceEqual(File.ReadAllBytes(target)))throw new InvalidOperationException("Cancellation changed the root.");
        UsdSceneWriter.Write(target,Scene("Y",.3048),parts);
        if(oldImages.Any(x=>!File.Exists(x.Key)||!x.Value.SequenceEqual(File.ReadAllBytes(x.Key))))throw new InvalidOperationException("Replacement changed a prior texture bundle.");
        Console.WriteLine("24 production scene files written; publication failure, cancellation and prior texture retention checked. No Rhino/cloud acceptance.");return 0;
    }
    internal static int VerifyImports(string directory)
    {
        int files=0;
        foreach(string path in Directory.GetFiles(directory).Where(p=>new[]{".usd",".usda",".usdc",".usdz"}.Contains(Path.GetExtension(p))))
        {
            string name=Path.GetFileName(path);bool hasGs=!name.StartsWith("mesh-only."),hasMesh=!name.StartsWith("gs-only.");
            string axis=name.StartsWith("mixed-Y-")?"Y":"Z";double units=name.Contains("-ft.")?.3048:.001;
            using var source=File.OpenRead(path);string hash=Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(source));
            var scene=UsdSceneReader.Read(path,hash);
            if((scene.Gaussians is not null)!=hasGs||(scene.Meshes is not null)!=hasMesh||!scene.SourceSha256.Equals(hash,StringComparison.OrdinalIgnoreCase))throw new InvalidDataException("Scene identity/type mismatch.");
            if(hasGs)
            {
                var actual=scene.Gaussians!;var expected=Scene(axis,units);
                if(actual.Parts.Count!=5||actual.UpAxis!=axis||actual.MetersPerUnit!=units)throw new InvalidDataException("GS metadata mismatch.");
                for(int part=0;part<5;part++)
                {
                    var a=actual.Parts[part];var e=expected.Parts[part];
                    if(a.Name!=e.Name||a.Antialiased!=e.Antialiased||a.Splats.Length!=e.Splats.Length)throw new InvalidDataException("GS part mismatch.");
                    for(int i=0;i<a.Splats.Length;i++)
                    {
                        var before=e.Splats[i];var after=a.Splats[i];
                        Close(a.Transform.TransformPoint(after.Center),e.Transform.TransformPoint(before.Center),2e-6);
                        Close(after.LogScale,before.LogScale,1e-7);
                        if(after.Rotation!=before.Rotation||after.OpacityLogit!=before.OpacityLogit)throw new InvalidDataException("GS orientation/opacity mismatch.");
                        float[] padded=before.Coefficients.ToArray().Concat(Enumerable.Repeat(0f,after.Coefficients.Length-before.Coefficients.Length)).ToArray();
                        if(!padded.SequenceEqual(after.Coefficients.ToArray()))throw new InvalidDataException("GS SH coefficients changed.");
                    }
                }
            }
            if(hasMesh)
            {
                var meshes=scene.Meshes!;if(meshes.Parts.Count!=3||meshes.UpAxis!=axis||meshes.MetersPerUnit!=units)throw new InvalidDataException("Mesh metadata mismatch.");
                var origins=new[]{new GaussianVector(1e12+.125,-2e12+.25,3e12+.5),new GaussianVector(0,0,10),new GaussianVector(304.8,609.6,914.4)};
                for(int i=0;i<3;i++)
                {
                    var p=meshes.Parts[i];var t=meshes.ToMillimetres(p.Transform);
                    if(!p.Positions.SequenceEqual(new float[]{0,0,0,.125f,0,0,0,.25f,.5f})||!p.Indices.SequenceEqual(new[]{0,1,2}))throw new InvalidDataException("Mesh geometry changed.");
                    for(int j=0;j<3;j++)Close(t.TransformPoint(new(p.Positions[j*3],p.Positions[j*3+1],p.Positions[j*3+2])),origins[i]+new GaussianVector(p.Positions[j*3],p.Positions[j*3+1],p.Positions[j*3+2]),i==0?.004:1e-10);
                    if(i<2&& (p.Texture?.Sha256.ToLowerInvariant()!="ce261ebea422e4b82e9aa5f2e86a4b1f90ebae7d267d16ebf0cb6002604117c6"||p.Texture.UseAlpha!=(i==0)||!p.TexCoords!.SequenceEqual(new float[]{0,0,1,0,0,1})))throw new InvalidDataException("Mesh image/UV/alpha changed.");
                    if(Math.Abs(p.Material.Alpha-.6)>1e-6)throw new InvalidDataException("Mesh material alpha changed.");
                }
            }
            try{UsdSceneReader.Read(path,new string('0',64));throw new InvalidOperationException("Expected source-hash rejection.");}catch(InvalidDataException){}
            files++;
        }
        if(files!=24)throw new InvalidDataException("Expected 24 production fixture files.");
        Console.WriteLine("24 files read through the public scene reader: full SH, both geometry types, provenance, placement and bitmap bytes verified. No native Rhino acceptance.");return 0;
    }
    private static void Close(GaussianVector actual,GaussianVector expected,double tolerance)
    {
        if(Math.Abs(actual.X-expected.X)>tolerance||Math.Abs(actual.Y-expected.Y)>tolerance||Math.Abs(actual.Z-expected.Z)>tolerance)throw new InvalidDataException($"Coordinate mismatch: {actual} / {expected}.");
    }
    private static GaussianSplat Splat(int degree,int index)=>new(new(1e9+.01+index*.013,-2e8+.04-index*.027,3e7+.05+index*.031),
        new(Math.Log(2e-5),Math.Log(7e-6),Math.Log(3e-5)),new(.5,.5,-.5,.5),index==0?-double.MaxValue:double.MaxValue,degree,
        Enumerable.Range(0,3*(degree+1)*(degree+1)).Select(n=>(float)((n/3)*.125-.5+(n%3)*.0625)).ToArray());
    private static GaussianScene Scene(string axis,double units)
    {
        var affine=new GaussianTransform(new(-2,.25,0,.5,3,.75,0,-.125,4),new(10,-5,20));
        var names=new[]{"Duplicate 用户","Duplicate 用户","","a / not a path"};
        var parts=Enumerable.Range(0,4).Select(d=>new GaussianPart(names[d],new[]{Splat(d,0),Splat(d,1)},affine,d%2==0))
            .Append(new GaussianPart("Mixed degree",Enumerable.Range(0,4).Select(d=>Splat(d,d)).ToArray(),GaussianTransform.Identity)).ToArray();
        return new(parts,axis,units,"USD");
    }
}
