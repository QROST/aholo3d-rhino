using System.Globalization;
namespace Aholo.Core;

/// <summary>World millimetres/Z-up placement, applied after the asset's coordinate conversion.</summary>
public sealed record RenderModelPlacement(double X=0,double Y=0,double Z=0,double Scale=1,double RotationDegrees=0)
{
    public double RotationXDegrees { get; init; }
    public double RotationYDegrees { get; init; }
    public double AxisScaleX { get; init; } = 1;
    public double AxisScaleY { get; init; } = 1;
    public double AxisScaleZ { get; init; } = 1;
    public double ShearXY { get; init; }
    public double ShearXZ { get; init; }
    public double ShearYZ { get; init; }

    public double[] Pose(bool gltfCoordinates)
    {
        if(!double.IsFinite(X)||!double.IsFinite(Y)||!double.IsFinite(Z)||!double.IsFinite(Scale)||Scale<=0)
            throw new ArgumentException("Model placement must be finite with a positive scale.");
        if(!double.IsFinite(AxisScaleX)||!double.IsFinite(AxisScaleY)||!double.IsFinite(AxisScaleZ)||AxisScaleX==0||AxisScaleY==0||AxisScaleZ==0||
            !double.IsFinite(ShearXY)||!double.IsFinite(ShearXZ)||!double.IsFinite(ShearYZ))
            throw new ArgumentException("Axis scales must be finite and nonzero; shear values must be finite.");
        // Column-vector construction: asset basis, diagonal scale, upper-triangular shear,
        // world-axis X then Y then Z rotation, then the separate world translation.
        double x=Scale*AxisScaleX,y=Scale*AxisScaleY,z=Scale*AxisScaleZ;
        var shaped=new GaussianMatrix(x,ShearXY*y,ShearXZ*z,0,y,ShearYZ*z,0,0,z);
        var placement=Rotation(RotationXDegrees,RotationYDegrees,RotationDegrees)*shaped;
        var basis=gltfCoordinates?new GaussianMatrix(1000,0,0,0,0,-1000,0,1000,0):GaussianMatrix.Identity;
        var a=placement*basis;_=a.Inverse();
        return RowPose(a,new(X,Y,Z));
    }
    internal static GaussianMatrix Rotation(double x,double y,double z)
    {
        if(!double.IsFinite(x)||!double.IsFinite(y)||!double.IsFinite(z))throw new ArgumentException("Rotation angles must be finite.");
        static (double C,double S) Angle(double value){double angle=(value%360)*Math.PI/180;return(Math.Cos(angle),Math.Sin(angle));}
        var rx=Angle(x);var ry=Angle(y);var rz=Angle(z);
        return new GaussianMatrix(rz.C,-rz.S,0,rz.S,rz.C,0,0,0,1)*
            new GaussianMatrix(ry.C,0,ry.S,0,1,0,-ry.S,0,ry.C)*
            new GaussianMatrix(1,0,0,0,rx.C,-rx.S,0,rx.S,rx.C);
    }
    internal static double[] RowPose(GaussianMatrix a,GaussianVector position)=>new[]{a.M11,a.M21,a.M31,0,a.M12,a.M22,a.M32,0,a.M13,a.M23,a.M33,0,position.X,position.Y,position.Z,1};
    internal static string FormatPose(double[] matrix)
    {
        if(matrix.Length!=16||matrix.Any(x=>!double.IsFinite(x))||matrix[3]!=0||matrix[7]!=0||matrix[11]!=0||matrix[15]!=1)
            throw new ArgumentException("Model pose must be a finite affine row-vector matrix.");
        _=new GaussianMatrix(matrix[0],matrix[4],matrix[8],matrix[1],matrix[5],matrix[9],matrix[2],matrix[6],matrix[10]).Inverse();
        return string.Join(", ",Enumerable.Range(0,4).Select(row=>"("+string.Join(", ",matrix.Skip(row*4).Take(4).Select(x=>x.ToString("R",CultureInfo.InvariantCulture)))+")"));
    }
}
