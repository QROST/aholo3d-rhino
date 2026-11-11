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
