namespace Aholo.Core;

/// <summary>Local splats and their full affine transform into the source scene. SH stays in the local frame.</summary>
public sealed record GaussianPart(string Name, GaussianSplat[] Splats, GaussianTransform Transform, bool Antialiased = false);

/// <summary>Source metadata. Null axis/units means unspecified, never a guessed physical unit.</summary>
public sealed record GaussianScene(IReadOnlyList<GaussianPart> Parts, string? UpAxis = null,
    double? MetersPerUnit = null, string Format = "", IReadOnlyList<string>? Warnings = null);
