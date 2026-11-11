using System.Numerics;

namespace Aholo.Core;

public sealed record PolygonTriangles(int[] Corners,GaussianVector UnitNormal);

/// <summary>Simple planar polygons, retaining source-corner indices. No geometry-count limit or fan fallback.</summary>
public static class PolygonTriangulator
{
    private readonly record struct Point(double X,double Y);
    private static InvalidDataException Invalid(string message)=>new(UiText.Get(message));
    public static PolygonTriangles Triangulate(IReadOnlyList<GaussianVector> points,CancellationToken ct=default)
    {
        ct.ThrowIfCancellationRequested();int count=points.Count;
        if(count<3)throw Invalid("A polygon requires at least three corners.");
        var local=new GaussianVector[count];double extent=0;
        for(int i=0;i<count;i++)
        {
            if((i&4095)==0)ct.ThrowIfCancellationRequested();
            if(!points[i].IsFinite)throw Invalid("Polygon coordinates must be finite.");
            local[i]=points[i]-points[0];extent=Math.Max(extent,Maximum(local[i]));
        }
        if(!double.IsFinite(extent)||extent==0)throw Invalid("Polygon extent is degenerate or exceeds numeric precision.");
        int farthest=0;double distance=0;
        for(int i=0;i<count;i++)
        {
            if((i&4095)==0)ct.ThrowIfCancellationRequested();local[i]/=extent;
            double d=GaussianVector.Dot(local[i],local[i]);if(d>distance){distance=d;farthest=i;}
        }
        GaussianVector normal=default;double largest=0;
        for(int i=0;i<count;i++)
        {if((i&4095)==0)ct.ThrowIfCancellationRequested();var cross=Cross(local[farthest],local[i]);double size=Maximum(cross);if(size>largest){normal=cross;largest=size;}}
        if(largest==0)throw Invalid("Collinear polygon has no area.");normal=normal.Normalized();
        int axis=Math.Abs(normal.X)>=Math.Abs(normal.Y)&&Math.Abs(normal.X)>=Math.Abs(normal.Z)?0:Math.Abs(normal.Y)>=Math.Abs(normal.Z)?1:2;
        var projected=new Point[count];var unique=new HashSet<Point>();
        for(int i=0;i<count;i++)
        {
            if((i&4095)==0)ct.ThrowIfCancellationRequested();
            if(Math.Abs(GaussianVector.Dot(normal,local[i]))>1e-6)throw Invalid("Nonplanar polygons must be tessellated before import.");
            var p=points[i];projected[i]=axis==0?new(p.Y,p.Z):axis==1?new(p.X,p.Z):new(p.X,p.Y);
            if(!unique.Add(projected[i]))throw Invalid("Polygon corners repeat or collapse in projection.");
        }
        long checks=0;
        for(int i=0;i<count;i++)
        {
            ct.ThrowIfCancellationRequested();var previous=projected[(i+count-1)%count];var point=projected[i];var next=projected[(i+1)%count];
            if(Orient(previous,point,next)==0&&!Between(point,previous,next))throw Invalid("Polygon boundary doubles back.");
            for(int j=i+1;j<count;j++)
            {
                if((++checks&4095)==0)ct.ThrowIfCancellationRequested();
                if(j==i+1||i==0&&j==count-1)continue;
                if(Intersects(point,next,projected[j],projected[(j+1)%count]))throw Invalid("Polygon boundary intersects or touches itself.");
            }
        }
        int extreme=0;
        for(int i=1;i<count;i++){if((i&4095)==0)ct.ThrowIfCancellationRequested();if(projected[i].X<projected[extreme].X||projected[i].X==projected[extreme].X&&projected[i].Y<projected[extreme].Y)extreme=i;}
        int winding=Orient(projected[(extreme+count-1)%count],projected[extreme],projected[(extreme+1)%count]);
        if(winding==0)throw Invalid("Polygon has no stable winding.");
        var remaining=Enumerable.Range(0,count).ToList();var result=new List<int>();
        while(remaining.Count>3)
        {
            ct.ThrowIfCancellationRequested();bool found=false;
            for(int position=0;position<remaining.Count;position++)
            {
                if((++checks&4095)==0)ct.ThrowIfCancellationRequested();
                int a=remaining[(position+remaining.Count-1)%remaining.Count],b=remaining[position],c=remaining[(position+1)%remaining.Count];
                if(Orient(projected[a],projected[b],projected[c])!=winding)continue;
                bool blocked=false;
                foreach(int p in remaining)
                {
                    if((++checks&4095)==0)ct.ThrowIfCancellationRequested();if(p==a||p==b||p==c)continue;
                    if(winding*Orient(projected[a],projected[b],projected[p])>=0&&winding*Orient(projected[b],projected[c],projected[p])>=0&&winding*Orient(projected[c],projected[a],projected[p])>=0){blocked=true;break;}
                }
                if(blocked)continue;result.Add(a);result.Add(b);result.Add(c);remaining.RemoveAt(position);found=true;break;
            }
            if(!found)throw Invalid("Polygon cannot be triangulated without losing source corners.");
        }
        if(Orient(projected[remaining[0]],projected[remaining[1]],projected[remaining[2]])!=winding)throw Invalid("Polygon triangulation ended with a degenerate face.");
        result.AddRange(remaining);double component=axis==0?normal.X:axis==1?-normal.Y:normal.Z;
        if(Math.Sign(component)!=winding)normal=-normal;ct.ThrowIfCancellationRequested();return new(result.ToArray(),normal);
    }
    private static double Maximum(GaussianVector p)=>Math.Max(Math.Abs(p.X),Math.Max(Math.Abs(p.Y),Math.Abs(p.Z)));
    private static GaussianVector Cross(GaussianVector a,GaussianVector b)=>new(a.Y*b.Z-a.Z*b.Y,a.Z*b.X-a.X*b.Z,a.X*b.Y-a.Y*b.X);
    private static bool Between(Point p,Point a,Point b)=>p.X>=Math.Min(a.X,b.X)&&p.X<=Math.Max(a.X,b.X)&&p.Y>=Math.Min(a.Y,b.Y)&&p.Y<=Math.Max(a.Y,b.Y);
    private static bool Intersects(Point a,Point b,Point c,Point d)
    {
        if(Math.Max(a.X,b.X)<Math.Min(c.X,d.X)||Math.Max(c.X,d.X)<Math.Min(a.X,b.X)||Math.Max(a.Y,b.Y)<Math.Min(c.Y,d.Y)||Math.Max(c.Y,d.Y)<Math.Min(a.Y,b.Y))return false;
        int x=Orient(a,b,c),y=Orient(a,b,d),u=Orient(c,d,a),v=Orient(c,d,b);
        return x==0&&Between(c,a,b)||y==0&&Between(d,a,b)||u==0&&Between(a,c,d)||v==0&&Between(b,c,d)||x*y<0&&u*v<0;
    }
    private static int Orient(Point a,Point b,Point c)
    {
        double left=(a.X-c.X)*(b.Y-c.Y),right=(a.Y-c.Y)*(b.X-c.X),determinant=left-right;
        const double machineEpsilon=2.2204460492503131e-16;
        const double minimumNormal=2.2250738585072014e-308;
        bool relativeBoundApplies=(left==0||Math.Abs(left)>=minimumNormal)&&(right==0||Math.Abs(right)>=minimumNormal);
        if(relativeBoundApplies&&Math.Abs(determinant)>(Math.Abs(left)+Math.Abs(right))*8*machineEpsilon)return Math.Sign(determinant);
        // Every finite double is an integer times a power of two. One common exponent makes the sign exact.
        var numbers=new[]{Dyadic(a.X),Dyadic(a.Y),Dyadic(b.X),Dyadic(b.Y),Dyadic(c.X),Dyadic(c.Y)};
        int exponent=numbers.Where(x=>!x.Integer.IsZero).Select(x=>x.Exponent).DefaultIfEmpty(0).Min();
        var q=numbers.Select(x=>x.Integer.IsZero?BigInteger.Zero:x.Integer<<(x.Exponent-exponent)).ToArray();
        return ((q[0]-q[4])*(q[3]-q[5])-(q[1]-q[5])*(q[2]-q[4])).Sign;
    }
    private static (BigInteger Integer,int Exponent) Dyadic(double value)
    {
        ulong bits=(ulong)BitConverter.DoubleToInt64Bits(value);int encoded=(int)((bits>>52)&0x7ff);
        ulong mantissa=bits&0x000fffffffffffffUL;int exponent=encoded==0?-1074:encoded-1023-52;
        if(encoded!=0)mantissa|=1UL<<52;BigInteger integer=mantissa;if((bits>>63)!=0)integer=-integer;return(integer,exponent);
    }
}
