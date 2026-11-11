# Local USD exchange

Aholo supports static GS import/export, ordinary static mesh import/export and combined GS + mesh export using pinned OpenUSD 26.11. These routes use no cloud account. Combined GS + mesh import remains open; the separate importers deliberately reject mixed contents.

## Gaussian route

`AholoImportGs` / `AholoExportGs` use the official `ParticleField3DGaussianSplat` schema.

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


## Tessellated mesh export route

`AholoExportMesh` and the panel's local mesh export action capture selected model-space Mesh/Brep/Extrusion and ordinary nested blocks. The selected document may be read-only and the view orthographic. Page-space roots are rejected because their units differ. GS objects are rejected rather than substituted with their bounding boxes. Shared cloud capture still requires its original perspective camera and preserves the existing cloud path.

The private snapshot uses millimetres/Z-up. A double-precision mesh copy receives block transforms before local-origin rebasing, so float-only block translations do not destroy detail first. The USD output stores float32 local positions and normals, with double-precision translation. Surfaces are tessellated; mesh quads use Rhino's native triangulation. Names, triangle winding, smooth/custom normals, supported PBR factors and direct mesh UV PNG/JPEG bytes are retained. Standard USD `subdivisionScheme=none`, `orientation=rightHanded`, extents, material binding and vertex interpolation are authored explicitly. Native source geometry is not changed. Block hierarchy, NURBS parametrization, layers, per-face/complex materials, procedural mappings, glass transmission and lights are not preserved by this mesh route.

A private binary transport avoids imposing the GLB container's 32-bit total-size limit on USD. There is no arbitrary vertex/face ceiling. Normal process cancellation/timeout, available memory and native array/file limits still apply. Texture assets are hash-deduplicated. USD `st` coordinates use the specified lower-left origin, converted once from the capture's glTF UV convention. `UsdPreviewSurface` uses linear factors and `UsdUVTexture` uses sRGB image data; alpha policy is encoded with explicit texture scale/bias, including ignored image alpha with partial material opacity. Sampler wrapping is authored. Standard Preview Surface has no portable min/mag filter controls: source settings are retained in custom metadata and the UI reports viewer-defined filtering. Exact cross-renderer appearance is not claimed.

USDZ packages all images in a single file. Plain USD has a unique adjacent texture directory. Both image bundle and pending root file are staged on the destination filesystem; the root-file rename is the commit point. Failure before it removes only the new bundle and preserves existing output. Cancellation after that commit is success, not a claimed rollback. Older bundles are deliberately retained because other scene copies may refer to them. The public transport helper writes into a caller-owned empty directory which its caller must remove on failure; the user-facing writer always cleans its workspace.

### Mesh validation and pending host acceptance

Build Release, then run `fixtures/mesh-usd-verify.py` with the configured OpenUSD interpreter. It generates all four formats through the production C# writer and independently checks world coordinates at large offsets, normals/winding, UV orientation data, PBR/alpha connections, relocated texture hashes, USDZ 64-byte alignment and package-contained dependencies. It also exercises root replacement failure after companion publication and preservation of older texture bundles. This does not run Rhino or render pixels.

Still pending on Mac and Windows: selected mesh plus tessellated surface and nested/mirrored blocks; a float-only mesh block translated far from the origin; named/unnamed parts; readonly and orthographic export; model/page unit differences; asymmetric texture colors/UV/alpha in an independent renderer; cancellation, document closure and source-file changes while capturing. Ordinary mesh import now has a separate static route below; combined GS+mesh import remains subsequent implementation work. Combined export is described below.

References: [UsdPreviewSurface and USD UV orientation](https://openusd.org/release/spec_usdpreviewsurface.html), [UsdGeomMesh schema](https://openusd.org/release/api/class_usd_geom_mesh.html), [USDZ packaging](https://openusd.org/release/api/usdz_package_8h.html).

## Static ordinary mesh import route

Use `AholoImportMesh`, Library → Load local mesh, or the preview/place action for a USD task result. The route shares candidate comparison and one Block/Undo acceptance with other meshes, but first creates an independent self-contained 3DM rather than calling an unknown native USD importer. Source physical units and transforms are resolved before the user’s placement transform; editable hierarchy is flattened. Choose fit-height, original-size/CPlane or scene-coordinate placement. Scene-coordinate mode preserves the resolved physical scene positions; original-size and scene-coordinate modes also accept planar meshes. See [mesh formats](mesh-formats.md) for the strict supported subset and source/derived hash separation.

Run `fixtures/mesh-usd-import-verify.py` with the configured USD interpreter after Release compilation. It exercises the real public C# writer and reader for four formats, independent source snapshots, external layers/images, double origins, alpha and UVs; it also checks quads, native instances, all five supported interpolation modes and 15 rejection cases with actual OpenUSD. The worker SDK path compiles but does not constitute native acceptance. The production parser compares root/dependency hashes from copied bytes, so changing the original after capture cannot substitute a different parsed scene under the old source hash.

Manual host acceptance: load a textured export with mirrored/nonuniformly scaled meshes and UV seams; compare candidate and accepted Block at the chosen height. Clear candidates so temporary bitmap paths disappear, save/reopen the document and inspect textures, source fields, Undo/Redo and repeat acceptance. Repeat with independent temporary paths, a changed unit/color configuration during loading, cancellation, large meshes and on Windows. Preview is a supported-material approximation rather than a full USD renderer; test rendered appearance separately. No such native acceptance is claimed by offline checks.


## Combined scene export

Run `AholoExportScene` or Library → Export selected scene USD for a selection containing GS, meshes, surfaces, extrusions and ordinary nested blocks. Pure GS and pure mesh selections also work. One UI-thread snapshot captures the selection, document units and accumulated transforms. GS instances inside ordinary blocks are treated as GS leaves, never bbox meshes; repeated instances are separate parts. Invalid references, unsupported selected geometry, page-space roots and unknown units fail the operation. GS cache data and mesh bitmaps are resolved in background from the captured references. Unrelated parent bitmap materials are not resolved for GS or meshes that override them.

The output uses the captured document units and Z-up whenever GS are present. Canonical millimetre/Z-up mesh data receives exactly one double-precision root conversion, so GS local geometry, SH frames and placements remain untouched. The portable core also supports explicitly declared Y-up GS stages. Without GS, the output uses millimetres/Z-up. This is a flattened static scene: original editable block/layer/NURBS hierarchy, lights, animation and unsupported materials are not retained. No total geometry quota or implicit point-cloud conversion is introduced.

All four containers share the existing immutable image-bundle and atomic root publication protocol. A successful final rename is success even if cancellation follows immediately. The operation registers against the original document before starting background work, and `AholoCancel` or document closure cancels uncommitted work. Missing resources and failed conversion preserve the previous destination. Current imports remain separate GS-only and mesh-only routes: a mixed scene cannot yet be reimported with one operation. In particular, do not treat mixed export as a completed mixed-scene round trip.

`fixtures/scene-usd-verify.py` uses actual OpenUSD and the public C# writer to verify 24 files: all four containers, mixed Y/Z stages in millimetres/feet, and single-type scenes. It checks every SH0–3 coefficient including mixed-degree padding, scale/orientation/opacity, full transforms, large-coordinate local detail, UV/material/alpha, fixed source image hash, relocated dependencies and USDZ alignment. Publication-failure and prior-bundle retention checks are included. A separate process fixture tests cancellation during the converter wait and workspace cleanup. Neither establishes native visual or event acceptance.

Manual checks on both native platforms:

1. Select a mesh and GS, then an ordinary block containing both; include repeated GS instances, two nested transforms, nonuniform scale and mirrors. Export via both command and panel, with English and Chinese Rhino UI.
2. Use millimetre, metre and inch documents, including distant origins; inspect physical placement and units in an independent compatible viewer. Compare counts/full SH using OpenUSD when that viewer lacks the GS schema.
3. Give the ordinary parent an unsupported or missing bitmap while its children are GS or explicitly override material. Export should succeed. A mesh actually inheriting that unsupported texture should still report it.
4. Try damaged GS markers/cache, unsupported selected geometry, empty selection, page-space geometry, readonly document and orthographic view. Failures must preserve old files and must not export bbox proxies.
5. Cancel while preparing a large scene; close the original document during export; edit objects/change units after capture. Verify captured-state output or cancellation, no writes to another document, and no success after an uncommitted failure.
