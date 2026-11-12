using System.Buffers;
using System.Drawing;
using Aholo3D.Core;
using Rhino.Geometry;
namespace Aholo3D.Rhino;

/// <summary>Worker-owned managed arrays. No document or display API is touched while preparing these.</summary>
internal sealed record GaussianNativeBatchData(Point3d[] Vertices, Vector3f[] Normals, Point2f[] Uv, Color[] Colors, MeshFace[] Faces, int[] FaceIndices);
internal sealed record GaussianNativePlacement(GaussianInstanceOccurrence Instance,string Hash,GaussianPreparedAsset Asset,bool Antialiased)
{
    internal GaussianTransform Transform=>Instance.World!.Value;
}
internal sealed record GaussianNativeRequest(uint Document, Guid Viewport, long Generation, GaussianCamera Camera, string Key, GaussianNativePlacement[] Placements);
internal sealed record GaussianNativePreparation(GaussianNativeRequest Request, int InputCount, int VisibleCount, GaussianNativeBatchData?[] Batches, double Milliseconds);

internal static class GaussianNativeBatchBuilder
{
    // Scheduling/transfer size only. Every visible input is included; this is not an import or rendering-count cap.
    internal const int SplatsPerBatch = 2048;
    internal static GaussianNativePreparation Prepare(GaussianNativeRequest request, CancellationToken ct)
    {
        var clock=System.Diagnostics.Stopwatch.StartNew();
        int count=0;
        foreach(var placement in request.Placements) count=checked(count+placement.Asset.Count);
        var values=ArrayPool<GaussianProjectedValue>.Shared.Rent(Math.Max(1,count));
        int[]? indices=null, scratch=null;
        try
        {
            int visible=0; long ordinal=0;
            for(int i=0;i<request.Placements.Length;i++)
            {
                ct.ThrowIfCancellationRequested(); var placement=request.Placements[i];
                var context=GaussianProjectionContext.Create(request.Camera,placement.Transform,i,antialiased:placement.Antialiased);
                visible+=context.Project(placement.Asset,values.AsSpan(visible,placement.Asset.Count),ordinal,ct);
                ordinal+=placement.Asset.Count;
            }
            indices=ArrayPool<int>.Shared.Rent(Math.Max(1,visible)); scratch=ArrayPool<int>.Shared.Rent(Math.Max(1,visible));
            for(int i=0;i<visible;i++) { if((i&1023)==0)ct.ThrowIfCancellationRequested(); indices[i]=i; }
            GaussianDepthSorter.SortBackToFront(indices.AsSpan(0,visible),values.AsSpan(0,visible),scratch.AsSpan(0,visible),ct);
            var result=new GaussianNativeBatchData?[(visible+SplatsPerBatch-1)/SplatsPerBatch];
            var world=GaussianProjectionContext.Create(request.Camera,GaussianTransform.Identity);
            var normal=new Vector3f((float)-request.Camera.Forward.X,(float)-request.Camera.Forward.Y,(float)-request.Camera.Forward.Z);
            for(int batch=0;batch<result.Length;batch++)
            {
                ct.ThrowIfCancellationRequested();
                int start=batch*SplatsPerBatch, length=Math.Min(SplatsPerBatch,visible-start);
                var vertices=new Point3d[length*4];var normals=new Vector3f[length*4];var uv=new Point2f[length*4];var colors=new Color[length*4];
                var faces=new MeshFace[length];var faceIndices=new int[length];
                for(int i=0;i<length;i++)
                {
                    if((i&127)==0)ct.ThrowIfCancellationRequested();
                    GaussianProjectedValue p=values[indices[start+i]];
                    var color=Color.FromArgb(Byte(p.Color.R),Byte(p.Color.G),Byte(p.Color.B));
                    GaussianAtlasUv bounds=GaussianOpacityAtlas.GetUvBounds(p.Opacity); int at=i*4;
                    Corner(0,-1,-1);Corner(1,1,-1);Corner(2,1,1);Corner(3,-1,1);
                    faces[i]=new MeshFace(at,at+3,at+2,at+1);faceIndices[i]=i;
                    void Corner(int corner,int a,int b)
                    {
                        GaussianVector point=world.WorldAtPixel(p.CenterX+a*p.Axis1X+b*p.Axis2X,p.CenterY+a*p.Axis1Y+b*p.Axis2Y,p.Depth);
                        vertices[at+corner]=new Point3d(point.X,point.Y,point.Z);normals[at+corner]=normal;colors[at+corner]=color;
                        // Pixel-center atlas bounds prevent sampling adjacent opacity tiles.
                        double u=a<0?bounds.U0:bounds.U1, v=b<0?bounds.V0:bounds.V1;
                        uv[at+corner]=new Point2f((float)u,(float)v);
                    }
                }
                result[batch]=new(vertices,normals,uv,colors,faces,faceIndices);
            }
            ct.ThrowIfCancellationRequested();
            return new(request,count,visible,result,clock.Elapsed.TotalMilliseconds);
        }
        finally
        {
            ArrayPool<GaussianProjectedValue>.Shared.Return(values);
            if(indices is not null)ArrayPool<int>.Shared.Return(indices);
            if(scratch is not null)ArrayPool<int>.Shared.Return(scratch);
        }
    }
    private static int Byte(double value)=>(int)Math.Round(Math.Clamp(value,0,1)*255);
}
