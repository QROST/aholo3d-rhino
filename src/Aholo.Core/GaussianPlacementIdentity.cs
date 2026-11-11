using System.Globalization;
using System.Text;
namespace Aholo.Core;

/// <summary>Per-occurrence display identity, distinct from the shared immutable source asset.</summary>
public readonly record struct GaussianPlacementIdentity(string Path,string Hash,GaussianTransform Transform,bool Antialiased)
{
    public void AppendTo(StringBuilder key)
    {
        ArgumentNullException.ThrowIfNull(key);
        // Length-prefix variable strings so identity is independent of separators inside producer paths.
        key.Append(Path.Length.ToString(CultureInfo.InvariantCulture)).Append(':').Append(Path).Append(Hash.Length.ToString(CultureInfo.InvariantCulture)).Append(':').Append(Hash).Append(':').Append(Antialiased?'1':'0').Append(':');
        var a=Transform.Linear;var t=Transform.Translation;
        Append(a.M11);Append(a.M12);Append(a.M13);Append(t.X);
        Append(a.M21);Append(a.M22);Append(a.M23);Append(t.Y);
        Append(a.M31);Append(a.M32);Append(a.M33);Append(t.Z);key.Append(';');
        void Append(double value)=>key.Append(value.ToString("R",CultureInfo.InvariantCulture)).Append(',');
    }
}
