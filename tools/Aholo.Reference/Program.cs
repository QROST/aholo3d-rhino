using System.Buffers.Binary;
using System.IO.Compression;
using System.Text;
using System.Text.Json;
using Aholo.Core;

// This portable image oracle does not load Rhino or make network calls.
if (args.Length == 2 && args[0] == "--scene-fixtures") return SceneFixtures.Run(args[1]);
if (args.Length == 2 && args[0] == "--mesh-import-fixture") return MeshImportFixtures.Run(args[1]);
if (args.Length == 2 && args[0] == "--mesh-fixtures") return MeshFixtures.Run(args[1]);
if (args.Length == 2 && args[0] == "--render-fixtures") return RenderFixtures.Run(args[1]);
if (args.Length == 2 && args[0] == "--benchmark") return GaussianBenchmark.Run(args[1]);
if (args.Length != 2 || args[0] != "--output")
{
    Console.Error.WriteLine("Usage: Aholo.Reference --output DIRECTORY | --render-fixtures DIRECTORY | --mesh-fixtures DIRECTORY | --scene-fixtures DIRECTORY");
    return 2;
}
string directory = Path.GetFullPath(args[1]);
Directory.CreateDirectory(directory);
const int size = 257;
var camera = new GaussianCamera(new(0,0,0),new(1,0,0),new(0,1,0),new(0,0,1),true,size,size,180,180,128,128,.1,10);
float[] Coefficients(double r, double g, double b)
{
    double dc = 1 / Math.Sqrt(4 * Math.PI);
    return [(float)((r-.5)/dc), (float)((g-.5)/dc), (float)((b-.5)/dc)];
}
GaussianSplat Splat(double depth, double scale, double r, double g, double b) => new(new(0,0,depth),
    new(Math.Log(.3*scale),Math.Log(.12*scale),Math.Log(.1*scale)),new(Math.Cos(Math.PI/12),0,0,Math.Sin(Math.PI/12)),0,0,Coefficients(r,g,b));
ProjectedGaussian[] splats = [GaussianProjector.Project(Splat(2,1,1,0,0),camera,GaussianTransform.Identity)!,
    GaussianProjector.Project(Splat(4,2,0,0,1),camera,GaussianTransform.Identity)!];
var cases = new[] {
    (Name:"alpha-overlap", Depth:float.PositiveInfinity, Background:GaussianColor.Black, Expected:new double[]{.5,0,.25,1}),
    (Name:"opaque-front", Depth:1f, Background:new GaussianColor(0,1,0), Expected:new double[]{0,1,0,1}),
    (Name:"opaque-middle", Depth:3f, Background:new GaussianColor(0,1,0), Expected:new double[]{.5,.5,0,1}),
    (Name:"opaque-behind", Depth:5f, Background:new GaussianColor(0,1,0), Expected:new double[]{.5,.25,.25,1})
};
var samples = new List<object>();
foreach (var test in cases)
{
    var rendered = GaussianReferenceRenderer.Render(splats,size,size,
        Enumerable.Repeat(test.Depth,size*size).ToArray(),Enumerable.Repeat(test.Background,size*size).ToArray());
    float[] center = rendered.Rgba.AsSpan((128*size+128)*4,4).ToArray();
    if (center.Where((v,i)=>Math.Abs(v-test.Expected[i])>1e-6).Any()) throw new InvalidOperationException("Closed-form oracle failed: "+test.Name);
    WritePng(Path.Combine(directory,test.Name+".png"),rendered);
    samples.Add(new { test.Name, CenterLinearRgba=center, ExpectedLinearRgba=test.Expected, File=test.Name+".png" });
}
File.WriteAllText(Path.Combine(directory,"reference.json"),JsonSerializer.Serialize(new {
    Description="CPU mathematical reference only; no Rhino host or GPU acceptance. PNG is raw linear RGB mapped to 8-bit without a gamma transform.",
    Width=size,Height=size,Camera=camera, Samples=samples
},new JsonSerializerOptions{WriteIndented=true}));
Console.WriteLine("Four CPU reference PNGs and closed-form center samples verified. No Rhino/GPU test was performed.");
return 0;

static void WritePng(string path, GaussianReferenceImage image)
{
    using var output = File.Create(path);
    output.Write(new byte[]{137,80,78,71,13,10,26,10});
    byte[] header = new byte[13];
    BinaryPrimitives.WriteInt32BigEndian(header.AsSpan(0,4),image.Width);
    BinaryPrimitives.WriteInt32BigEndian(header.AsSpan(4,4),image.Height);
    header[8]=8;header[9]=6;
    Chunk(output,"IHDR",header);
    using var compressed=new MemoryStream();
    using(var z=new ZLibStream(compressed,CompressionLevel.SmallestSize,true))
    {
        byte[] row = new byte[1+4*image.Width];
        for(int y=0;y<image.Height;y++)
        {
            for(int x=0;x<image.Width;x++)
                for(int c=0;c<4;c++) row[1+x*4+c]=(byte)Math.Round(Math.Clamp(image.Rgba[(y*image.Width+x)*4+c],0,1)*255);
            z.Write(row);
        }
    }
    Chunk(output,"IDAT",compressed.ToArray());Chunk(output,"IEND",[]);
}
static void Chunk(Stream output,string type,byte[] data)
{
    byte[] length=new byte[4];BinaryPrimitives.WriteInt32BigEndian(length,data.Length);output.Write(length);
    byte[] name=Encoding.ASCII.GetBytes(type);output.Write(name);output.Write(data);
    uint crc=0xffffffff;
    foreach(byte b in name.Concat(data))
    {
        crc^=b;for(int bit=0;bit<8;bit++)crc=(crc&1)!=0?(crc>>1)^0xedb88320:crc>>1;
    }
    BinaryPrimitives.WriteUInt32BigEndian(length,~crc);output.Write(length);
}
