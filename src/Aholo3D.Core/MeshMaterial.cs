namespace Aholo3D.Core;

public enum MeshColorEncoding { Linear, Srgb }

/// <summary>Supported untextured glTF material constants. Alpha is coverage, not glass transmission.</summary>
public sealed record MeshMaterial(double R, double G, double B, double Alpha = 1,
    double Metallic = 0, double Roughness = .75, MeshColorEncoding ColorEncoding = MeshColorEncoding.Linear)
{
    public void Validate()
    {
        Unit(R,nameof(R)); Unit(G,nameof(G)); Unit(B,nameof(B)); Unit(Alpha,nameof(Alpha));
        Unit(Metallic,nameof(Metallic)); Unit(Roughness,nameof(Roughness));
        if (!Enum.IsDefined(typeof(MeshColorEncoding),ColorEncoding)) throw new ArgumentOutOfRangeException(nameof(ColorEncoding));
    }
    public double[] LinearBaseColor()
    {
        Validate();
        return ColorEncoding == MeshColorEncoding.Linear ? new[]{R,G,B,Alpha} : new[]{Linear(R),Linear(G),Linear(B),Alpha};
    }
    private static double Linear(double value) => value <= .04045 ? value / 12.92 : Math.Pow((value + .055) / 1.055,2.4);
    private static void Unit(double value,string field)
    { if (!double.IsFinite(value) || value < 0 || value > 1) throw new ArgumentOutOfRangeException(field,"Material constants must be finite values from zero to one."); }
}
