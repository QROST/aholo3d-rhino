using Aholo3D.Core;

// Reads synthetic oracle inputs only. No host access or network requests.
internal static class PngAlphaFixtures
{
    internal static int Run(string directory)
    {
        foreach(string path in Directory.GetFiles(Path.Combine(directory,"input"),"*.png"))
        {
            byte[] encoded=File.ReadAllBytes(path);
            var ignored=new MeshTexture(encoded,useAlpha:false);
            string stem=Path.Combine(directory,Path.GetFileNameWithoutExtension(path));
            using(var output=File.Create(stem+"-rgb.png"))ignored.WithoutAlpha().CopyEncodedTo(output);
            MeshData Triangle(MeshTexture texture,double alpha)=>new(new float[]{0,0,0,1000,0,0,0,1000,0},new[]{0,1,2},255,255,255)
            {TexCoords=new float[]{0,0,1,0,0,1},BaseColorTexture=texture,Material=new(1,1,1,alpha)};
            GlbWriter.Write(stem+".glb",new[]{Triangle(new(encoded),.6),Triangle(ignored,1),Triangle(ignored,.4),
                Triangle(new(encoded,MeshTextureWrap.Clamp,MeshTextureWrap.Clamp,MeshTextureFilter.Nearest,MeshTextureFilter.Nearest,false),0)});
            using var original=new MemoryStream();ignored.CopyEncodedTo(original);
            if(!original.ToArray().SequenceEqual(encoded))throw new InvalidOperationException("Source was changed.");
        }
        Console.WriteLine("Production RGB copies and GLB alpha-policy fixtures written; no Rhino or cloud call.");return 0;
    }
}
