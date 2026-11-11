# OBJ import and acceptance

Maintainer: CuriousArc · qrost.

Status: the Library, `AholoImportMesh`, local OBJ/ZIP selection and typed `obj_zip` task placement now use the integrated reader. This is a development feature with offline evidence; native macOS/Windows appearance, dialogs, Undo and persistence remain pending. No Python/OpenUSD runtime is needed for OBJ.

## Source capture and selection

`ObjSourceSnapshot` copies the root OBJ or ZIP into a private workspace and hashes those exact bytes. A ZIP is frozen before the options window opens; the selected entry is read from that same copy, even if the original changes meanwhile. Multiple entries require an explicit choice. One captured selection cannot be switched after dependency parsing starts. Referenced MTL/images are captured on demand; unused images are not opened.

Archive entries form a virtual path table, never extraction destinations. Relative references stay within the source/package root. Escaping/remote paths, ambiguous case/Unicode names, file/directory collisions and special/link entries fail. The multi-file source is not an atomic source-application save, and local link checks are not a sandbox against a concurrent hostile filesystem writer. .NET decodes ZIP; entry lengths are checked, without a complete CRC/ZIP-conformance claim.

## Geometry and material boundary

- Static polygon faces retain independent positive/negative vertex, UV and normal references. Every object statement has a separate ordinal, including repeated names. Group membership partitions parts without duplicating faces.
- All faces, including quads, must be simple and planar. Cancellable triangulation retains original corners and orientation. Nonplanar/self-intersecting/degenerate faces fail; there is no fan or flattening fallback. Relative planarity tolerance is 1e-6. Very high-sided individual faces can still be expensive.
- Authored normals win. Missing normals use original-face angle-weighted smoothing before material, group and UV splits; smoothing-off faces retain hard boundaries. Zero/cancelled normals fail explicitly.
- Positions remain doubles until rebased around a common per-object double origin. Float attributes must be finite; triangles are checked again for collapse or reversed winding after conversion. There is no artificial geometry-count cap. Runtime capacities, numerical precision and available memory still apply.
- Used MTL materials support scalar/RGB `Kd`, `d`/`Tr`, extended PBR `Pm`/`Pr` and PNG/JPEG `map_Kd`. Numeric colors are explicitly interpreted as linear and images as sRGB. RGB texture values modulate Kd; image alpha does not replace dissolve. Conflicting opacity declarations fail.
- `Ka/Ks/Ns/Ni/illum 0–2` are an explicitly reported PBR preview approximation. `Pr` overrides the roughness estimate from `Ns`. Ambient/specular/optical-density and unlit appearance are not reproduced exactly. Nonzero emission, colored transmission, other illumination modes, effective additional maps and unknown used statements fail. Unused named materials are not decoded.
- UVs retain their lower-left convention. Supported texture scale/offset components are applied per part; negative/zero UV scales are legal. `-clamp on` is admitted only when all transformed face-corner UVs are inside [0,1], because edge clamping cannot reproduce its outside-domain decal behavior. Images with equal hashes and identical sampling share immutable encoded storage; material colors and UV transforms remain separate.

Lines/points/freeform geometry, non-unit rational weights, vertex-color extensions, nonzero third UV coordinates, malformed corner forms, undefined or duplicate used material definitions, unsupported bitmap/color metadata and unsupported texture operations fail explicitly. This is a documented static subset, not every OBJ/MTL extension.

The [original Wavefront OBJ specification](https://www.martinreddy.net/gfx/3d/OBJ.spec) and [original MTL documentation transcription](https://raw.githubusercontent.com/Alhadis/language-wavefront/master/docs/mtl-spec.rst) inform the parser boundary. Pm/Pr are later extensions, not original MTL fields. [Rhino's OBJ options](https://docs.mcneel.com/rhino/8/help/en-us/fileio/wavefront_obj_import_export.htm) do not establish acceptance of this independent reader.

## Rhino workflow

The bilingual options window asks for one model, source length units and Y/Z up. Defaults are the original document's units and Z up; these are visible choices, not an inferred Lux3D contract. They do not change document settings. Fit-height placement subsequently scales the interpreted source to the chosen height; select Original size or Preserve scene coordinates to test physical dimensions.

Original document/session, units, tolerance and color preprocessing are captured before background copying. They are checked around the modal window, after parsing, before native preparation and at acceptance. Cancellation closes the options window asynchronously and releases the private source. Parsing and standalone 3DM construction use worker-owned data. Rhino's native preflight and File3dm calls still have non-interruptible sections; cancellation checkpoints prevent a later unwanted commit.

The shared self-contained 3DM path embeds images and independently reads back their hashes. Source root/dependency hashes remain distinct from the derived 3DM integrity hash. Mesh metadata records source entry, object/part identity, groups, up axis and metres per unit. Candidate preview, direct placement, Block/Undo and receipts reuse the existing mesh workflow.

A typed task `obj_zip` maps to a cached `.zip`. Generic `zip` and `fbx_zip` task outputs remain downloads, not placement candidates. Local `.zip` files are accepted only through the strict OBJ package reader. One task/output slot retains one placement receipt: repeated placement selects its existing model (or reports its undone state). Import the downloaded ZIP as a new local candidate to choose another entry. Local imports create separate candidate identities.

## Reproducible host acceptance — pending

Run separately on macOS and Windows Rhino 8.32+, with English and Chinese UI. The user operates Rhino.

1. Load `fixtures/mesh-obj/scene.obj` via Library and `AholoImportMesh`. Choose metres/Z-up and Preserve scene coordinates. Expect three parts and five triangles, bounds X 0–2 m, Y 0–2 m, Z 0–1 m. The floor uses a 2×2 RGBA image whose alpha is ignored; the blue rear panel has material opacity 0.6; the front triangle is red. Verify orientation and texture corners visually.
2. Repeat in a millimetre document and with Original size/CPlane. Source bounds become 2000×2000×1000 mm. Fit-height is a separate intentional scaling operation. Repeat Y-up and confirm the proper axis rotation.
3. Load `fixtures/mesh-obj-multiple.zip`. No entry should be preselected. Choose `mesh-obj/scene.obj`, then import again locally and choose `alternate.obj` (one planar triangle; use Original size or Preserve scene coordinates).
4. Cancel the options window, cancel background parsing, close/change the original document and change units/gamma while an import is pending. No stale candidate or document object may be committed. Repeat with a replaced original ZIP after selection opens; the captured model must remain stable.
5. Accept once, Undo once, Redo once, save and reopen after source files/private extraction directories are unavailable. Check embedded image resolution, colors, transparency, normals, source metadata and no duplicate acceptance. Test typed task preview/direct placement offline after caching and confirm one receipt per result slot.
6. Test shared atlas materials with different UV/wrap settings, smooth material boundaries, hard edges, authored normals, concave polygons, invalid geometry and unsupported MTL features. Rejections must leave the document and source intact.

Regenerate/check the assets with `python3 fixtures/create-obj-sample.py [--check]`. The public reader tests verify sample geometry/materials and both archive entries; this does not validate native texture rendering or save/reopen behavior.

## Evidence

Release build and full offline-suite results are recorded in [validation](validation.md). Independent local reviews found repeated atlas allocations and inconsistent Unicode entry comparison; both have regression tests and fixes. No native host or live cloud acceptance is inferred from those checks. Remote allowance was insufficient for a useful review in this increment; the primary wrote files, ran checks and integrated read-only local reviews.
