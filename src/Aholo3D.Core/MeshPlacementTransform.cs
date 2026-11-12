namespace Aholo3D.Core;

public enum MeshPlacementMode { FitHeight, SourceSizeOnPlane, SourceCoordinates }

/// <summary>Placement in document units, after source units/axes and scene transforms are resolved.</summary>
public static class MeshPlacementTransform
{
    public static GaussianTransform Create(MeshPlacementMode mode, GaussianVector min, GaussianVector max,
        GaussianMatrix plane, GaussianVector point, double height, double rotationDegrees)
    {
        if(!min.IsFinite||!max.IsFinite||min.X>max.X||min.Y>max.Y||min.Z>max.Z)
            throw Invalid();
        if(mode==MeshPlacementMode.SourceCoordinates)return GaussianTransform.Identity;
        if(mode is not (MeshPlacementMode.FitHeight or MeshPlacementMode.SourceSizeOnPlane)||!point.IsFinite||!double.IsFinite(rotationDegrees)||!IsPlane(plane))
            throw Invalid();
        double scale=1;
        if(mode==MeshPlacementMode.FitHeight)
        {
            double sourceHeight=max.Z-min.Z;
            if(!double.IsFinite(height)||height<=0||sourceHeight<=0)
                throw new InvalidOperationException(UiText.Get("Fit height needs a model with positive height. For a planar mesh, choose original size or scene coordinates."));
            // Opposite finite extremes may overflow their difference while the ratio is representable.
            scale=double.IsPositiveInfinity(sourceHeight)?(height/(max.Z/2-min.Z/2))/2:height/sourceHeight;
            if(!double.IsFinite(scale)||scale<=0)throw Invalid();
        }
        double angle=Math.IEEERemainder(rotationDegrees,360)*Math.PI/180,c=Math.Cos(angle),s=Math.Sin(angle);
        var local=new GaussianMatrix(scale*c,-scale*s,0,scale*s,scale*c,0,0,0,scale);
        var linear=plane*local;
        // Divide each term first so a valid large same-sign midpoint does not overflow.
        var anchor=new GaussianVector(min.X/2+max.X/2,min.Y/2+max.Y/2,min.Z);
        var transform=new GaussianTransform(linear,point-linear.Transform(anchor));
        if(!linear.IsFinite||!transform.Translation.IsFinite)throw Invalid();
        for(int corner=0;corner<8;corner++)
            if(!transform.TransformPoint(new((corner&1)==0?min.X:max.X,(corner&2)==0?min.Y:max.Y,(corner&4)==0?min.Z:max.Z)).IsFinite)throw Invalid();
        return transform;
    }
    private static bool IsPlane(GaussianMatrix m)
    {
        if(!m.IsFinite)return false;
        var x=new GaussianVector(m.M11,m.M21,m.M31);var y=new GaussianVector(m.M12,m.M22,m.M32);var z=new GaussianVector(m.M13,m.M23,m.M33);
        var cross=new GaussianVector(x.Y*y.Z-x.Z*y.Y,x.Z*y.X-x.X*y.Z,x.X*y.Y-x.Y*y.X);
        return Math.Abs(GaussianVector.Dot(x,x)-1)<1e-9&&Math.Abs(GaussianVector.Dot(y,y)-1)<1e-9&&Math.Abs(GaussianVector.Dot(x,y))<1e-9&&(cross-z).Length<1e-9;
    }
    private static InvalidOperationException Invalid()=>new(UiText.Get("The placement bounds, plane, point or scale exceed valid numeric coordinates."));
}
