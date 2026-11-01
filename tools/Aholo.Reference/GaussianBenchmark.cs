using System.Diagnostics;
using System.Text.Json;
using Aholo.Core;

internal static class GaussianBenchmark
{
    public static int Run(string path)
    {
        long allocatedBefore = GC.GetTotalAllocatedBytes(true);
        var clock = Stopwatch.StartNew();
        GaussianSplat[] source = PlyReader.ReadGaussians(path).ToArray();
        double parseMs = clock.Elapsed.TotalMilliseconds; clock.Restart();
        GaussianPreparedAsset asset = GaussianPreparedAsset.Prepare(source);
        double prepareMs = clock.Elapsed.TotalMilliseconds;
        if (source.Length == 0) throw new InvalidDataException("Empty Gaussian asset.");
        double minX=source.Min(s=>s.Center.X), maxX=source.Max(s=>s.Center.X),
            minY=source.Min(s=>s.Center.Y), maxY=source.Max(s=>s.Center.Y),
            minZ=source.Min(s=>s.Center.Z), maxZ=source.Max(s=>s.Center.Z);
        double extent = Math.Max(1, Math.Max(maxX-minX, Math.Max(maxY-minY,maxZ-minZ)));
        var center = new GaussianVector((minX+maxX)/2,(minY+maxY)/2,(minZ+maxZ)/2);
        var camera = new GaussianCamera(center-new GaussianVector(0,0,2*extent),new(1,0,0),new(0,1,0),new(0,0,1),
            true,1024,768,700,700,511.5,383.5,.01*extent,10*extent);
        var context = GaussianProjectionContext.Create(camera,GaussianTransform.Identity);
        var values = new GaussianProjectedValue[asset.Count];
        clock.Restart();
        int visible = context.Project(asset,values);
        double projectMs = clock.Elapsed.TotalMilliseconds;
        int[] indices = Enumerable.Range(0,visible).ToArray(), scratch = new int[visible];
        clock.Restart();
        GaussianDepthSorter.SortBackToFront(indices,values,scratch);
        double sortMs = clock.Elapsed.TotalMilliseconds;
        for(int i=1;i<visible;i++)
            if(values[indices[i-1]].Depth < values[indices[i]].Depth) throw new InvalidDataException("Incorrect depth ordering.");
        clock.Restart();
        // Same quad reconstruction and atlas lookup as the native adapter, without host API calls.
        double checksum=0; double minColor=double.PositiveInfinity,maxColor=double.NegativeInfinity;
        foreach(int index in indices)
        {
            GaussianProjectedValue p=values[index];
            var uv=GaussianOpacityAtlas.GetUvBounds(p.Opacity);
            for(int corner=0;corner<4;corner++)
            {
                double a=corner is 0 or 3 ? -1:1,b=corner is 0 or 1 ? -1:1;
                GaussianVector point=context.WorldAtPixel(p.CenterX+a*p.Axis1X+b*p.Axis2X,p.CenterY+a*p.Axis1Y+b*p.Axis2Y,p.Depth);
                checksum+=point.X+point.Y+point.Z+uv.U0;
            }
            minColor=Math.Min(minColor,Math.Min(p.Color.R,Math.Min(p.Color.G,p.Color.B)));
            maxColor=Math.Max(maxColor,Math.Max(p.Color.R,Math.Max(p.Color.G,p.Color.B)));
        }
        double quadMs=clock.Elapsed.TotalMilliseconds;
        Console.WriteLine(JsonSerializer.Serialize(new {
            Scope="Offline CPU only; synthetic overview camera. No Rhino/Metal upload, draw, navigation or visual acceptance.",
            InputCount=source.Length,VisibleCount=visible,ShDegrees=source.Select(s=>s.Degree).Distinct().Order().ToArray(),
            ParseMs=parseMs,PrepareMs=prepareMs,ProjectMs=projectMs,SortMs=sortMs,QuadReconstructionMs=quadMs,
            TotalManagedAllocatedBytes=GC.GetTotalAllocatedBytes(true)-allocatedBefore,
            ProcessPeakWorkingSetBytes=Process.GetCurrentProcess().PeakWorkingSet64 is > 0 and var peak ? (long?)peak : null,
            PlannedBatchesOf2048=(visible+2047)/2048,SharedMaterials=1,SharedTextures=1,
            RadianceMin=minColor,RadianceMax=maxColor,Checksum=checksum
        },new JsonSerializerOptions{WriteIndented=true}));
        return 0;
    }
}
