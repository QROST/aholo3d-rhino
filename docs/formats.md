# Supported formats

The plugin preserves original downloads and rejects unsupported content before placement. Format restrictions are distinct from geometry counts: there is no artificial total vertex, face or Gaussian-count limit.

## Static meshes

| Format | Supported scope |
| --- | --- |
| GLB | Static meshes and supported materials/textures; no skins, animation or unsupported required extensions |
| PLY | Ordinary polygon mesh PLY; distinguished from complete Gaussian PLY |
| STL | Static geometry |
| 3MF | Core geometry, components/transforms, base colors, supported direct PNG/JPEG textures and uniform triangle colors |
| OBJ / typed OBJ ZIP | Static geometry, supported MTL appearance and local dependencies; explicit archive entry selection |
| USD / USDA / USDC / USDZ | Static mesh subset, units/axes, transforms, valid material face subsets, normals/UVs and supported Preview Surface appearance |

OBJ polygons are validated and triangulated. USD supports triangles, quads and simple planar polygons; unsupported subdivision, holes, animation, point instancers, arbitrary shader graphs and invalid material subsets fail explicitly. General ZIP and FBX task outputs remain download-only.

Imported scenes become static Rhino geometry, not a fully editable USD hierarchy. Texture and material support is limited; full procedural/WCS/material-network fidelity is not promised.

## Gaussian scenes

PLY requires complete positions, anisotropic scales, orientation, opacity and SH coefficients. SPZ v1–4 and SPLAT are supported within their defined formats. SH degrees 0–3 are retained. A point-only PLY is not silently treated as a Gaussian scene.

The experimental display keeps source parameters while using approximations for display color and opacity. Ordinary opaque geometry and basic clipping can interact with it; arbitrary transparent interleaving, Raytraced, printing, layout/view capture and every graphics backend are not supported.

Nested references retain their transforms and share cached source assets. Some nested proxy boxes can remain visible. Performance depends on scene size, view changes, hardware and available memory.

## USD / USDZ exchange

Local exchange uses optional OpenUSD 26.11 and the `ParticleField3DGaussianSplat` schema for GS. A third-party viewer must understand that schema to display Gaussian USDZ.

- Use **Import/Export GS** for GS-only data and **Import/Export Mesh** for meshes.
- Use **Import/Export Scene** for mixed GS and mesh contents.
- Selected Mesh/Brep/Extrusion and ordinary nested blocks can be exported; surfaces are tessellated.
- USDZ packages dependencies into one file. Plain USD may have a companion texture directory; keep it with the scene.
- Source units, up axes and supported affine transforms are preserved through conversion. Floating-point and host format limits still apply.
- Missing dependencies or failed conversion preserve an existing destination.

OpenUSD is installed separately under its own license. RhinoCommon/Eto are supplied by Rhino; redistributed dependency notices are in [NOTICE](../NOTICE).

## Cloud outputs

World outputs are Gaussian assets, not ordinary triangle meshes. SpatialUnderstanding outputs are placed as editable wall surfaces, opening outlines and oriented boxes—not complete structural solids.

Rigging and animation files may be downloaded, but the plugin does not play animation in Rhino. Cloud JPEG preview has no native depth compositing with Rhino geometry.
