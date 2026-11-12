using System.Globalization;
using System.Text;
namespace Aholo3D.Core;

/// <summary>Rigid light pose in cloud millimetres/Z-up; RGB is linear and intensity is uncalibrated.</summary>
public sealed record RenderLight(string Kind,double[] Transform,double[] Color,double Intensity,
    double Width=0,double Height=0,double Radius=0)
{
    public double Exposure { get; init; }
    public double Specular { get; init; } = 1;
    public bool AffectReflections { get; init; } = true;
    public bool DoubleSided { get; init; }
    public void Validate()
    {
        if(Kind is not "Rect" and not "Sphere")throw new ArgumentException("Only rectangular and spherical cloud lights are supported.");
        if(Transform is not{Length:16}||Transform.Any(x=>!double.IsFinite(x))||Transform[3]!=0||Transform[7]!=0||Transform[11]!=0||Transform[15]!=1)
            throw new ArgumentException("Light pose must be a finite affine row-vector matrix.");
        var x=new GaussianVector(Transform[0],Transform[1],Transform[2]);
        var y=new GaussianVector(Transform[4],Transform[5],Transform[6]);
        var z=new GaussianVector(Transform[8],Transform[9],Transform[10]);
        if(Math.Abs(x.Length-1)>1e-6||Math.Abs(y.Length-1)>1e-6||Math.Abs(z.Length-1)>1e-6||Math.Abs(GaussianVector.Dot(x,y))>1e-6||Math.Abs(GaussianVector.Dot(x,z))>1e-6||Math.Abs(GaussianVector.Dot(y,z))>1e-6||GaussianVector.Dot(Cross(x,y),z)<1-1e-6)
            throw new ArgumentException("Light pose must be rigid and right handed; dimensions carry its scale.");
        if(Color is not{Length:3}||Color.Any(v=>!double.IsFinite(v)||v<0||!float.IsFinite((float)v)))throw new ArgumentException("Light color must contain three finite nonnegative linear values.");
        if(!double.IsFinite(Intensity)||Intensity<0||!float.IsFinite((float)Intensity))throw new ArgumentException("Light intensity must be finite and nonnegative.");
        if(!double.IsFinite(Exposure)||!float.IsFinite((float)Exposure))throw new ArgumentException("Light exposure must be a finite float.");
        if(!double.IsFinite(Specular)||Specular<0||!float.IsFinite((float)Specular))throw new ArgumentException("Light specular contribution must be finite and nonnegative.");
        // Validate the final float-domain brightness without overflowing an intermediate 2^exposure.
        double intensity=(float)Intensity;
        double effective=intensity==0?0:Math.Pow(2,Math.Log2(intensity)+(float)Exposure);
        if(!double.IsFinite(effective)||!float.IsFinite((float)effective))throw new ArgumentException("Intensity and exposure together exceed finite light brightness.");
        if(Kind!="Rect"&&DoubleSided)throw new ArgumentException("Double-sided emission is available only for rectangular lights.");
        if(Kind=="Rect"){Positive(Width);Positive(Height);}else Positive(Radius);
    }
    /// <summary>Rotate the existing orientation around world X, Y, then Z; retain position and physical dimensions.</summary>
    public RenderLight RotateWorld(double xDegrees,double yDegrees,double zDegrees)
    {
        Validate();var rotation=RenderModelPlacement.Rotation(xDegrees,yDegrees,zDegrees);
        if(xDegrees%360==0&&yDegrees%360==0&&zDegrees%360==0)
            return this with{Transform=(double[])Transform.Clone(),Color=(double[])Color.Clone()};
        var existing=new GaussianMatrix(Transform[0],Transform[4],Transform[8],Transform[1],Transform[5],Transform[9],Transform[2],Transform[6],Transform[10]);
        var changed=this with{Transform=RenderModelPlacement.RowPose(rotation*existing,new(Transform[12],Transform[13],Transform[14])),Color=(double[])Color.Clone()};
        changed.Validate();return changed;
    }
    internal static void Positive(double value)
    {if(!double.IsFinite(value)||value<=0||!float.IsFinite((float)value)||(float)value<=0)throw new ArgumentException("Light dimensions must be positive finite floats.");}
    internal static GaussianVector Cross(GaussianVector a,GaussianVector b)=>new(a.Y*b.Z-a.Z*b.Y,a.Z*b.X-a.X*b.Z,a.X*b.Y-a.Y*b.X);
    internal static double[] Pose(GaussianVector x,GaussianVector y,GaussianVector z,GaussianVector center)=>new[]{x.X,x.Y,x.Z,0,y.X,y.Y,y.Z,0,z.X,z.Y,z.Z,0,center.X,center.Y,center.Z,1};

    public static RenderLight Rectangle(GaussianVector corner,GaussianVector length,GaussianVector width,GaussianVector direction,GaussianTransform transform,double millimetresPerUnit,double[] color,double intensity)
    {
        Positive(millimetresPerUnit);var originalNormal=Cross(length,width).Normalized();var normal=direction.Normalized();
        if(!originalNormal.IsFinite||!normal.IsFinite||Math.Abs(GaussianVector.Dot(originalNormal,normal))<1-1e-6)throw new ArgumentException("A rectangular light needs a perpendicular nonzero direction.");
        var u=transform.Linear.Transform(length)*millimetresPerUnit;var v=transform.Linear.Transform(width)*millimetresPerUnit;
        Positive(u.Length);Positive(v.Length);var x=u.Normalized();var y=v.Normalized();
        if(Math.Abs(GaussianVector.Dot(x,y))>1e-6)throw new ArgumentException("Sheared rectangular lights cannot be represented by RenderCloud.");
        var transformedNormal=transform.Linear.Inverse().Transpose().Transform(normal).Normalized();
        var z=Cross(x,y);double facing=GaussianVector.Dot(z,transformedNormal);
        if(facing>0){y=-y;z=-z;} // Cloud rectangles emit toward local -Z, including reflected blocks.
        var center=transform.TransformPoint(corner+(length+width)*.5)*millimetresPerUnit;
        var result=new RenderLight("Rect",Pose(x,y,z,center),color,intensity,u.Length,v.Length);result.Validate();return result;
    }
    public static RenderLight PointApproximation(GaussianVector point,GaussianTransform transform,double millimetresPerUnit,double radiusMillimetres,double[] color,double intensity)
    {
        Positive(millimetresPerUnit);Positive(radiusMillimetres);
        var x=transform.Linear.Transform(new(1,0,0));var y=transform.Linear.Transform(new(0,1,0));var z=transform.Linear.Transform(new(0,0,1));
        double scale=x.Length;Positive(scale);
        if(Math.Abs(y.Length/scale-1)>1e-6||Math.Abs(z.Length/scale-1)>1e-6||Math.Abs(GaussianVector.Dot(x.Normalized(),y.Normalized()))>1e-6||Math.Abs(GaussianVector.Dot(x.Normalized(),z.Normalized()))>1e-6||Math.Abs(GaussianVector.Dot(y.Normalized(),z.Normalized()))>1e-6)
            throw new ArgumentException("Nonuniform or sheared point-light scaling is unsupported.");
        var result=new RenderLight("Sphere",Pose(new(1,0,0),new(0,1,0),new(0,0,1),transform.TransformPoint(point)*millimetresPerUnit),color,intensity,Radius:radiusMillimetres*scale);
        result.Validate();return result;
    }
}

public static class RenderLightsUsda
{
    public static string Build(IReadOnlyList<RenderLight>? lights)
    {
        if(lights is null||lights.Count==0)return "";
        var text=new StringBuilder("    def Xform \"Lights\"\n    {\n");
        for(int i=0;i<lights.Count;i++)text.Append(BuildLight(lights[i],"Light_"+i.ToString(CultureInfo.InvariantCulture),"def"));
        return text.Append("    }\n").ToString();
    }
    internal static string BuildLight(RenderLight light,string id,string specifier)
    {
        ArgumentNullException.ThrowIfNull(light);light.Validate();
        string F(double value)=>value.ToString("R",CultureInfo.InvariantCulture);
        string pose=string.Join(", ",Enumerable.Range(0,4).Select(row=>"("+string.Join(", ",light.Transform.Skip(row*4).Take(4).Select(F))+")"));
        var text=new StringBuilder("        ").Append(specifier).Append(' ').Append(light.Kind).Append("Light \"").Append(id).Append("\" (\n            customData = {\n                int \"manycore:affectReflections\" = ").Append(light.AffectReflections?"1":"0").Append('\n');
        if(light.Kind=="Rect")text.Append("                bool \"manycore:doubleFace\" = ").Append(light.DoubleSided?"true":"false").Append('\n');
        text.Append("            }\n        )\n        {\n")
            .Append("            color3f inputs:color = (").Append(string.Join(", ",light.Color.Select(F))).Append(")\n")
            .Append("            float inputs:intensity = ").Append(F(light.Intensity))
            .Append("\n            float inputs:exposure = ").Append(F(light.Exposure)).Append("\n            float inputs:specular = ").Append(F(light.Specular))
            .Append("\n            bool inputs:normalize = false\n");
        if(light.Kind=="Rect")text.Append("            float inputs:width = ").Append(F(light.Width)).Append("\n            float inputs:height = ").Append(F(light.Height)).Append('\n');
        else text.Append("            float inputs:radius = ").Append(F(light.Radius)).Append('\n');
        return text.Append("            matrix4d xformOp:transform = (").Append(pose).Append(")\n            uniform token[] xformOpOrder = [\"xformOp:transform\"]\n        }\n").ToString();
    }
}
