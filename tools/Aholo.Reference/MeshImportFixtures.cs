using Aholo.Core;

internal static class MeshImportFixtures
{
    internal static int Run(string path)
    {
        var scene=UsdMeshReader.Read(path);
        if(scene.Parts.Count!=3||scene.UpAxis!="Z"||scene.MetersPerUnit!=.001)throw new InvalidDataException("Mesh fixture stage mismatch.");
        var first=scene.Parts[0];var world=scene.ToMillimetres(first.Transform);
        if(world.TransformPoint(new(0,0,0))!=new GaussianVector(1e12+.125,-2e12+.25,3e12+.5))throw new InvalidDataException("Double origin was not preserved.");
        if(first.Texture?.UseAlpha!=true||scene.Parts[1].Texture?.UseAlpha!=false||Math.Abs(scene.Parts[1].Material.Alpha-.6)>1e-6)throw new InvalidDataException("Texture alpha policy changed.");
        if(first.Texture.Sha256!=scene.Parts[1].Texture!.Sha256||first.Normals is null||first.TexCoords is null||!first.DoubleSided)throw new InvalidDataException("Mesh attributes changed.");
        if(!first.TexCoords.SequenceEqual(new float[]{0,0,1,0,0,1}))throw new InvalidDataException("USD lower-left UVs changed.");
        using var bytes=new MemoryStream();first.Texture.CopyEncodedTo(bytes);
        if(bytes.Length!=first.Texture.Length)throw new InvalidDataException("Image snapshot incomplete.");
        Console.WriteLine("Production mesh USD reader verified "+System.IO.Path.GetExtension(path)+"; native host not invoked.");return 0;
    }
}
