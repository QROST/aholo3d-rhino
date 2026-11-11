using System.Globalization;
namespace Aholo.Core;

/// <summary>World millimetres/Z-up placement, applied after the asset's coordinate conversion.</summary>
public sealed record RenderModelPlacement(double X=0,double Y=0,double Z=0,double Scale=1,double RotationDegrees=0)
{
    public double[] Pose(bool gltfCoordinates)
    {
        if(!double.IsFinite(X)||!double.IsFinite(Y)||!double.IsFinite(Z)||!double.IsFinite(RotationDegrees)||!double.IsFinite(Scale)||Scale<=0)
            throw new ArgumentException("Model placement must be finite with a positive scale.");
        double angle=(RotationDegrees%360)*Math.PI/180,c=Math.Cos(angle)*Scale,s=Math.Sin(angle)*Scale;
        var placement=new GaussianMatrix(c,-s,0,s,c,0,0,0,Scale);
        var basis=gltfCoordinates?new GaussianMatrix(1000,0,0,0,0,-1000,0,1000,0):GaussianMatrix.Identity;
        var a=placement*basis;_=a.Inverse();
        return new[]{a.M11,a.M21,a.M31,0,a.M12,a.M22,a.M32,0,a.M13,a.M23,a.M33,0,X,Y,Z,1};
    }
    internal static string FormatPose(double[] matrix)
    {
        if(matrix.Length!=16||matrix.Any(x=>!double.IsFinite(x))||matrix[3]!=0||matrix[7]!=0||matrix[11]!=0||matrix[15]!=1)
            throw new ArgumentException("Model pose must be a finite affine row-vector matrix.");
        _=new GaussianMatrix(matrix[0],matrix[4],matrix[8],matrix[1],matrix[5],matrix[9],matrix[2],matrix[6],matrix[10]).Inverse();
        return string.Join(", ",Enumerable.Range(0,4).Select(row=>"("+string.Join(", ",matrix.Skip(row*4).Take(4).Select(x=>x.ToString("R",CultureInfo.InvariantCulture)))+")"));
    }
}
