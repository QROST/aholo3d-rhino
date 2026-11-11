using Aholo.Core;

namespace Aholo.Tests;

public sealed class PolygonTriangulatorTests
{
    private static GaussianVector[] Concave()=>[new(0,0,0),new(3,0,0),new(3,3,0),new(1.5,1,0),new(0,3,0)];
    [Fact]
    public void ConcavePolygonUsesOriginalCornersAndPreservesEmptyNotch()
    {
        var p=Concave();var result=PolygonTriangulator.Triangulate(p);
        Assert.Equal(new[]{1,2,3,0,1,3,0,3,4},result.Corners);Assert.Equal(new GaussianVector(0,0,1),result.UnitNormal);Oracle(p,result,6);
    }
    [Fact]
    public void WindingCollinearBoundaryAndDeepConcavityRemainValid()
    {
        var reverse=Concave().Reverse().ToArray();var result=PolygonTriangulator.Triangulate(reverse);Oracle(reverse,result,-6);Assert.Equal(new GaussianVector(0,0,-1),result.UnitNormal);
        GaussianVector[] collinear=[new(0,0,0),new(1,0,0),new(2,0,0),new(2,1,0),new(1,1,0),new(0,1,0)];Oracle(collinear,PolygonTriangulator.Triangulate(collinear),2);
        GaussianVector[] u=[new(0,0,0),new(4,0,0),new(4,4,0),new(3,4,0),new(3,1,0),new(1,1,0),new(1,4,0),new(0,4,0)];Oracle(u,PolygonTriangulator.Triangulate(u),10);
    }
    [Theory]
    [InlineData(0)][InlineData(1)][InlineData(2)]
    public void PrincipalPlaneNormalMatchesOriginalWinding(int axis)
    {
        var p=Concave().Select(v=>axis==0?v:axis==1?new GaussianVector(v.Y,v.Z,v.X):new GaussianVector(v.Z,v.X,v.Y)).ToArray();
        var result=PolygonTriangulator.Triangulate(p);
        Assert.Equal(new[]{1,2,3,0,1,3,0,3,4},result.Corners);
        Assert.Equal(axis==0?new(0,0,1):axis==1?new(0,1,0):new GaussianVector(1,0,0),result.UnitNormal);
    }
    [Theory]
    [InlineData(1e-200)][InlineData(1e-160)][InlineData(1e-150)][InlineData(1e150)][InlineData(1e-20)][InlineData(1e20)]
    public void ExtremeScalesUseRelativeGeometryAndExactPredicates(double scale)
    {
        var p=Concave().Select(x=>x*scale).ToArray();var result=PolygonTriangulator.Triangulate(p);
        Assert.Equal(new[]{1,2,3,0,1,3,0,3,4},result.Corners);Oracle(p.Select(x=>x/scale).ToArray(),result,6);
    }
    [Fact]
    public void LargeTranslationAndNearCancellationDoNotLoseWinding()
    {
        var original=Concave();var translated=original.Select(v=>v*256+new GaussianVector(1e12,-1e12,1e12)).ToArray();
        Assert.Equal(PolygonTriangulator.Triangulate(original).Corners,PolygonTriangulator.Triangulate(translated).Corners);
        double huge=Math.Pow(2,52);GaussianVector[] thin=[new(0,0,0),new(huge,huge,0),new(huge,huge+1,0),new(0,1,0)];
        Oracle(thin,PolygonTriangulator.Triangulate(thin),huge,centroid:false);
    }
    public static IEnumerable<object[]> InvalidPolygons()
    {
        yield return [new GaussianVector[]{new(0,0,0),new(2,2,0),new(0,2,0),new(2,0,0),new(3,0,0),new(3,-1,0)}];
        yield return [new GaussianVector[]{new(0,0,0),new(3,0,0),new(3,3,0),new(1.5,1,.1),new(0,3,0)}];
        yield return [new GaussianVector[]{new(0,0,0),new(1,0,0),new(2,0,0)}];
        yield return [new GaussianVector[]{new(0,0,0),new(3,0,0),new(3,3,0),new(0,3,0),new(0,0,0)}];
        yield return [new GaussianVector[]{new(0,0,0),new(2,0,0),new(1,0,0),new(2,2,0),new(0,2,0)}];
        yield return [new GaussianVector[]{new(0,0,0),new(2,0,0),new(2,2,0),new(1,0,0),new(0,2,0)}];
        yield return [new GaussianVector[]{new(0,0,0),new(1,0,0),new(double.NaN,1,0)}];
    }
    [Theory][MemberData(nameof(InvalidPolygons))]
    public void InvalidFacesAreRejectedWithoutFallback(GaussianVector[] polygon)=>Assert.Throws<InvalidDataException>(()=>PolygonTriangulator.Triangulate(polygon));
    [Fact]
    public void CancellationDuringInputTraversalStopsBeforeQuadraticWork()
    {
        using var token=new CancellationTokenSource();var list=new CancelingPoints(token);
        Assert.Throws<OperationCanceledException>(()=>PolygonTriangulator.Triangulate(list,token.Token));Assert.True(list.Reads>=1000);
        Assert.Throws<OperationCanceledException>(()=>PolygonTriangulator.Triangulate(Array.Empty<GaussianVector>(),token.Token));
    }
    private sealed class CancelingPoints(CancellationTokenSource token):IReadOnlyList<GaussianVector>
    {
        public int Reads;public int Count=>20000;
        public GaussianVector this[int index]{get{if(++Reads==1000)token.Cancel();double angle=2*Math.PI*index/Count;return new(Math.Cos(angle),Math.Sin(angle),0);}}
        public IEnumerator<GaussianVector> GetEnumerator()=>throw new NotSupportedException();
        System.Collections.IEnumerator System.Collections.IEnumerable.GetEnumerator()=>GetEnumerator();
    }
    private static void Oracle(GaussianVector[] p,PolygonTriangles result,double expectedArea,bool centroid=true)
    {
        Assert.Equal((p.Length-2)*3,result.Corners.Length);Assert.Equal(Enumerable.Range(0,p.Length),result.Corners.Distinct().Order());
        var edges=new Dictionary<(int,int),int>();double sum=0;
        for(int i=0;i<result.Corners.Length;i+=3)
        {
            int[] t=result.Corners.AsSpan(i,3).ToArray();var a=p[t[0]];var b=p[t[1]];var c=p[t[2]];
            double area=((b.X-a.X)*(c.Y-a.Y)-(b.Y-a.Y)*(c.X-a.X))/2;
            Assert.True(Math.Sign(area)==Math.Sign(expectedArea));sum+=area;
            for(int j=0;j<3;j++){int x=t[j],y=t[(j+1)%3];var key=(Math.Min(x,y),Math.Max(x,y));edges[key]=edges.GetValueOrDefault(key)+1;}
            if(centroid)
            {
                double x=(a.X+b.X+c.X)/3,y=(a.Y+b.Y+c.Y)/3;bool inside=false;
                for(int j=0;j<p.Length;j++)
                {var from=p[j];var to=p[(j+1)%p.Length];if((from.Y>y)!=(to.Y>y)&&x<(to.X-from.X)*(y-from.Y)/(to.Y-from.Y)+from.X)inside=!inside;}
                Assert.True(inside);
            }
        }
        Assert.True(Math.Abs(sum/expectedArea-1)<1e-12);
        var boundary=Enumerable.Range(0,p.Length).Select(i=>(Math.Min(i,(i+1)%p.Length),Math.Max(i,(i+1)%p.Length))).ToHashSet();
        foreach(var edge in boundary)Assert.Equal(1,edges[edge]);
        foreach(var edge in edges)Assert.Equal(boundary.Contains(edge.Key)?1:2,edge.Value);
    }
}
