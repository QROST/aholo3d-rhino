using Aholo3D.Core;

internal static class MeshFixtures
{
    internal static int Run(string directory)
    {
        Directory.CreateDirectory(directory);
        byte[] image=Convert.FromBase64String("iVBORw0KGgoAAAANSUhEUgAAAAIAAAACCAYAAABytg0kAAAAF0lEQVR4nAXBAQEAAACCIKb33EAkQxUHQdUHepwfNGcAAAAASUVORK5CYII=");
        var mesh=new MeshData(new float[]{0,0,0,.125f,0,0,0,.25f,.5f},new[]{0,1,2},255,255,255)
        {Normals=new float[]{0,-2,1,0,-2,1,0,-2,1},TexCoords=new float[]{0,1,1,1,0,0},Material=new(.5,.25,.125,.6,.2,.4),
            BaseColorTexture=new(image,MeshTextureWrap.Clamp,MeshTextureWrap.Repeat,MeshTextureFilter.Nearest)};
        var parts=new[]{new UsdMeshPart("大坐标 / smooth",mesh,new(1e12+.125,-2e12+.25,3e12+.5)),
            new UsdMeshPart("ignore alpha",mesh with{BaseColorTexture=new(image,useAlpha:false)},new(0,0,10)),
            new UsdMeshPart("plain",mesh with{Normals=null,TexCoords=null,BaseColorTexture=null},new(0,0,20))};
        foreach(string extension in new[]{"usda","usd","usdc","usdz"})
            UsdMeshWriter.Write(Path.Combine(directory,"meshes."+extension),parts);
        // Exercise failure after publishing the new companion directory, before replacing the root layer.
        var previousAssets=Directory.GetDirectories(directory,"usd-assets-*").OrderBy(x=>x).ToArray();
        string blocked=Path.Combine(directory,"blocked.usda");Directory.CreateDirectory(blocked);
        try { UsdMeshWriter.Write(blocked,parts); throw new InvalidOperationException("Expected directory collision."); }
        catch(IOException) { }
        if(!previousAssets.SequenceEqual(Directory.GetDirectories(directory,"usd-assets-*").OrderBy(x=>x)))throw new InvalidOperationException("Failed export left a companion directory.");
        Directory.Delete(blocked);
        var oldImages=previousAssets.SelectMany(Directory.GetFiles).ToDictionary(x=>x,File.ReadAllBytes);
        UsdMeshWriter.Write(Path.Combine(directory,"meshes.usda"),parts);
        if(oldImages.Any(x=>!File.Exists(x.Key)||!x.Value.SequenceEqual(File.ReadAllBytes(x.Key))))throw new InvalidOperationException("Replacement modified an older texture bundle.");
        Console.WriteLine("Four mesh USD formats written by the production writer; no Rhino or cloud call.");return 0;
    }
}
