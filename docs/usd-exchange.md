# Local Gaussian USD exchange

Aholo imports and exports static GS scenes using the official OpenUSD 26.11 `ParticleField3DGaussianSplat` schema. This is GS interchange, not a general Rhino geometry exporter. No cloud/account is used.

- `.usd` and `.usdc` exports are binary USDC, `.usda` is text, and `.usdz` is a self-contained, officially packaged USDC-root archive.
- Every supported splat and SH0–3 coefficient is retained. Multiple instances remain separate parts even when they share the same source cache. Local positions, anisotropic scales, WXYZ orientations, opacity, full affine placements, units and up axis remain explicit.
- The official geometry fields have float32 precision. A local-origin shift combined with matrix4d placement reduces large-coordinate precision loss. Geometry is not promised to be bit-exact for arbitrary double inputs. Nonrepresentable values fail instead of silently creating invalid data.
- Standard USD placements describe the scene independently of optional `gaussianSplatting` custom metadata. That metadata retains the antialias flag and local-origin bookkeeping between compatible readers; external renderers may ignore those hints. Current native rendering does not implement exact antialias compensation.
- Export takes a consistent snapshot of current top-level block placements and standard document units. It does not reapply original import scaling or axis conversion. Selected scope rejects empty/mixed selection; explicit all scope includes hidden/locked GS as visible exported assets. Ordinary geometry and nested blocks are not exported.
- All data preparation happens in the background. Any missing/corrupt GS resource fails the whole export. Cancellation or failed conversion leaves an existing destination intact; successful output replaces it atomically. A document edit after capture does not change the snapshot being exported.
- Import rejects unsupported animation, materials/visibility/instancer semantics, unknown Gaussian schemas and incomplete dependencies. Mesh/Points are never presented as GS. Export does not author an explicit USD extent; automatic framing/culling in external viewers still needs validation. A generic USDZ/Quick Look viewer is not guaranteed to support the Gaussian schema just because it can open mesh USDZ.

## Manual acceptance

1. Import a colored SH3 PLY. Duplicate it; move, rotate and nonuniformly scale the copies. Select only GS and export USDZ.
2. Import that USDZ into a fresh document with the same units and then with different standard units. Check full counts, physical placement, color from several views and opacity.
3. Repeat `.usd`, `.usdc`, `.usda`; verify no companion cache file is needed beside the exported scene.
4. Test selected/all scope, empty and mixed selection, hidden/locked GS, missing cache, cancellation and an existing destination. Ordinary geometry should never silently enter this GS export.
5. While exporting, move an object or change document units; the output must remain the captured state. Close the document during a large export and verify cancellation/cleanup.
6. Restart, reopen the 3DM and repeat. Record macOS and Windows results separately. Local build/roundtrip checks do not establish native Rhino UI or third-party viewer acceptance.

## Authoritative format references

- [OpenUSD Gaussian schema](https://github.com/PixarAnimationStudios/OpenUSD/blob/v26.11/pxr/usd/usdVol/schema.usda)
- [USDZ specification](https://openusd.org/release/spec_usdz.html)
- [Official pinned runtime](https://pypi.org/project/usd-core/26.11/)

Maintained by CuriousArc · qrost.

The optional OpenUSD 26.11 runtime is installed separately and governed by the TOST-1.0 terms distributed with its official wheel. The Yak archive ships our bridge/setup scripts, not the OpenUSD runtime. RhinoCommon/Eto are supplied by Rhino.
