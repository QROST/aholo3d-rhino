namespace Aholo.Core;

/// <summary>View-dependent area compensation for a filtered 2D Gaussian, without changing source opacity.</summary>
public static class GaussianAntialiasing
{
    /// <summary>
    /// sqrt(det(raw covariance) / det(filtered covariance)). Call after the filtered covariance has been validated.
    /// Balance the diagonal exponents before forming the raw determinant so tiny footprints retain representable opacity.
    /// A raw determinant lost to roundoff is clamped to zero, as in the standard antialiased splat formulation.
    /// </summary>
    public static double Compensation(double xx,double xy,double yy,double filteredDeterminant)
    {
        if(!double.IsFinite(xx)||!double.IsFinite(xy)||!double.IsFinite(yy)||xx<0||yy<0||
            !double.IsFinite(filteredDeterminant)||filteredDeterminant<=0)
            throw new ArgumentException("Gaussian antialiasing requires finite covariance and a positive filtered determinant.");
        if(xx==0||yy==0)return 0;
        int xExponent=Math.ILogB(xx),yExponent=Math.ILogB(yy),exponent=(xExponent+yExponent)>>1;
        double a=Math.ScaleB(xx,-xExponent),c=Math.ScaleB(yy,xExponent-2*exponent),b=Math.ScaleB(xy,-exponent);
        double square=b*b;
        if(!double.IsFinite(square))throw new ArgumentException("Gaussian antialiasing covariance exceeds numeric precision.");
        // A compensated difference of products avoids subtracting two separately rounded determinants.
        double determinant=Math.FusedMultiplyAdd(a,c,-square)-Math.FusedMultiplyAdd(b,b,-square);
        double factor=Math.ScaleB(Math.Sqrt(Math.Max(0,determinant))/Math.Sqrt(filteredDeterminant),exponent);
        return Math.Clamp(factor,0,1);
    }
}
