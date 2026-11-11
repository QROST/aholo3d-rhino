# Aholo for Rhino

Worlds, objects, Gaussian viewing and cloud rendering inside Rhino 8, by **CuriousArc · qrost**.

[简体中文](readme.zh-CN.md) · [Source repository](https://github.com/QROST/aholo3d-rhino)

The repository and checkout directory are now **`aholo3d-rhino`**. The existing Yak package remains **`aholo-rhino`** so installed users keep the same update channel. Plug-in identity, commands, credentials, state/cache and optional USD runtime locations remain compatible. See [repository rename and compatibility](docs/repository-rename.md).

**0.2.0-alpha.24 — local development candidate, not installed or published.** The previous `0.1.1-beta.2` Yak release was unlisted at the user's request. The new interface and service workflows have local build/mock-test evidence; their live service behavior and native Rhino layout are not yet accepted. Earlier Mac GS acceptance applies only to the earlier implementation. CuriousArc's independent integration is not an official Aholo or McNeel product.

See [implementation progress](docs/implementation-progress.md), [API coverage and remaining scope](docs/api-capabilities.md) and [integrated acceptance](docs/integrated-acceptance.md).

## Interface language

English is the default and fallback. At plugin startup, a valid Rhino interface language takes precedence over the system UI language. Chinese Rhino variants (including Simplified and Traditional Chinese locales) use Simplified Chinese; other Rhino languages use English. The system UI language is used only when Rhino cannot provide a valid language. Restart Rhino after changing its language.

Panels, command messages, GS diagnostics and known task errors follow this selection. User prompts, names, paths, task IDs and stored status codes remain unchanged. Unknown external or technical errors are preserved rather than guessed at. Language selection and embedded resources are covered by automated tests; native layout in both languages still needs manual Rhino acceptance.

## What works in this version

- Create worlds from text/images, or reconstruct from at least 20 photos or supported video.
- Use all ten public Lux3D create operations: object/image/four-view generation, material transfer, part splitting, format conversion, articulation, rigging and retargeting. Advanced options stay collapsed; each operation keeps its draft.
- Upload an indoor PLY for SpatialUnderstanding, download its DSL result and place editable wall surfaces, opening outlines and oriented boxes as one Block.
- Browse one searchable, paginated local Library and World/Lux3D/Spatial cloud history. Download typed outputs, compare up to three cached candidates and place supported static GLB, polygon mesh PLY, STL, supported 3MF, static OBJ/typed OBJ ZIP or USD meshes as a native Block, using a construction plane/point, height and rotation with no artificial vertex/face ceiling. Animation/rig files can be downloaded; native animation playback is not implemented.
- Follow persisted jobs, restore World/Lux3D/Spatial/render records by their original service IDs, and download original PLY/SPZ, panorama or available LOD metadata. LOD metadata is retained without assuming an unpublished progressive-loading schema.
- Import a full 3DGS PLY as a normal Rhino block reference and draw anisotropic Gaussian ellipses with opacity and SH 0–3 view-dependent colors in **Aholo GS (Experimental)**. Source files and parameters are retained. Scale, axis conversion, transforms and Undo are implemented; detailed document-lifecycle checks remain open.
- Run `AholoGsProbe` for an offline native-viewport experiment, `AholoImportGs` for a local GS PLY/SPLAT/SPZ or USD/USDZ scene, and `AholoGsDiagnostics` for status. `AholoImportPly` is a separate center-point diagnostic only.
- Upload selected Mesh/Brep/Extrusion objects and ordinary nested Blocks, preserve basic colors and supported PBR constants, optionally include selected rectangular/point lights, and submit an offline RenderCloud job using the active perspective camera. Download and open the resulting image.
- Store an API Key in macOS Keychain or Windows current-user DPAPI if requested; otherwise keep it only in the background process.
- Preserve task IDs and uncertain submission records rather than automatically repeating a request that might already have been charged.

**World files are 3D Gaussian Splatting data, not ordinary triangle meshes.** World placement now uses the experimental GS renderer by default. A PLY without complete Gaussian fields is rejected; it is never silently substituted with a point cloud. The original PLY/SPZ is retained; the renderer decodes PLY, SPLAT and SPZ v1–4 (SH0–3). Unknown formats/degrees fail explicitly without discarding data.

This is an **experimental renderer**. Beta.3 replaces the per-Gaussian native resource path that caused a severe memory/navigation failure in beta.2. Projection and global depth sorting run on a cancellable background worker; native meshes are transferred in small UI slices, with up to 2,048 splats per mesh and one shared opacity atlas/material. The batch size is a transfer unit, not a geometry limit: all visible splats are retained. While a changed camera is being prepared, the block shows a bounding box; the complete GS frame returns after preparation. Saving a 3dm does not render GS into its thumbnail. See the [freeze repair](docs/gs-freeze-repair.md) and [display stability repair](docs/gs-visibility-stability.md).

The user confirmed red/teal probe colors, colored world display, faster navigation and basic clipping-plane interaction on the local Apple Silicon Mac. After the visibility and startup fixes in beta.4/beta.5, the user confirmed the retest passed on 2026-11-02 and approved version 0.1.0. Detailed alpha/depth/sorting and Windows acceptance remain pending. Arbitrary interleaving with transparent Rhino objects, layout details, printing/view capture, stereo and Raytraced integration are not supported. Display RGB is 8-bit and clips HDR before blending; opacity uses 256 atlas levels (at most 0.5/255 opacity rounding), without altering the source. The CPU oracle retains floating-point data and is not a claim of pixel-identical host rendering. Basic clipping-plane interaction was observed by the user on Mac; multi-plane/edge behavior is still experimental.

GS parts marked antialiased in SPZ, compatible USD metadata or saved GS references now receive view-dependent opacity compensation. Original data remains unchanged; the display still uses 256 opacity levels. [Behavior and manual acceptance](docs/gs-antialiasing.md).

Ordinary nested blocks now participate in experimental GS display. Each complete instance path keeps its own accumulated transform, while repeated copies share the immutable source asset. Parent/object/layer visibility is checked; no extra source-unit conversion is applied at draw time. Nested proxy bounds currently remain visible so ordinary mesh siblings cannot be hidden by the GS renderer. Mac/Windows native nesting, block editing and Undo/Redo acceptance is still pending; see the [nested-block checklist](docs/gs-host-acceptance.md#nested-block-display-after-alpha10).

Render → **Open cloud preview** opens a separate modeless window for an already-ingested RenderCloud mesh or GS asset ID. It follows the original perspective viewport camera and shows the latest JPEG with its receive time; images can lag behind navigation. Streaming has an independent local control channel, cancellation and durable cleanup records. **This is not native mixed-depth viewport compositing.** The public stream provides no depth, alpha or camera-associated frame IDs; arbitrary World PLY/SPZ ingestion into a cloud mesh ID also lacks a validated contract. Full material graphs, procedural textures and glass transmission remain outside cloud scene export. Offline export supports PBR constants and selected rectangular/point lights as described below. World generation is best suited to indoor scenes according to the official documentation.

See the [staged plan and evidence](docs/decisions/0001-native-gaussian-viewport.md) and [GS acceptance procedure](docs/gs-host-acceptance.md).

Existing GS modes are checked and reused, initialization waits for UI idle, and a GS failure does not block the panel. Run `AholoGsMode` to retry locally if initialization is unavailable. Version 0.1.0 retained the tested beta.5 implementation. Version 0.1.1-beta.1 added local GS exchange while keeping the cloud task workflow; 0.1.1-beta.2 adds automatic interface language selection.

## Requirements and validation status

| Item | Status |
| --- | --- |
| Rhino | Rhino 8.32 or later; host assembly targets .NET 7 |
| Background runtime | .NET 8 Runtime matching the host architecture |
| Service | Regional Aholo API Key (International or China) with access to each selected service and sufficient credits |
| macOS | Apple Silicon: user confirmed World generation/import, GS colors/navigation/basic clipping, and passed the beta.5 startup/display retest; detailed matrix remains open |
| Windows | Cross-platform source and Windows credential storage included; Windows build, installer execution and Rhino host acceptance pending |
| Intel Mac | Not yet host-tested |

Installing the package does not require the .NET SDK. USD import/export additionally requires the optional private Python/OpenUSD runtime described below. Building from source requires the .NET 8 SDK; packaging/verification scripts additionally use Python 3.9+ and Rhino's Yak CLI.

## Authentication and cost

The official REST API currently documents **a raw regional API Key in the `Authorization` header**. Manycore OAuth sign-in does not replace this credential for World, Lux3D or RenderCloud. Settings → **Account sign-in (optional)** uses the active region’s official discovery, dynamic registration and PKCE browser flow. **Remember this account on this device** is off by default. When enabled, the complete session is saved in macOS Keychain or Windows current-user DPAPI and restored locally without automatic refresh or browser launch. Otherwise it lasts only in this process. Explicit sign-out removes that region’s shared saved account across Rhino instances; closing the dialog cancels only its unfinished attempt. Refresh is explicit, and an interrupted/ambiguous refresh requires a new sign-in. None of these actions revokes provider tokens or changes the API Key. Generation/history/rendering remain API-Key-only. International and China endpoints are implemented with separate credentials and task ownership; actual regional account permissions and live workflows still require acceptance.

Region selection is explicit and independent of language. Switching revokes queued render continuation and stops task polling/live previews, retaining remote tasks and the other region’s saved credentials. An already admitted foreground request may finish against its original region. Legacy unmarked data remains International. Shared API credential state now uses schema 2; older builds cannot restore it. Close older Rhino instances before upgrading and avoid mixing old/new background processes. Account cancellation uses independent private IPC so uploads do not delay it or get terminated by it.

Create/manage the Key in the official Aholo platform, following the [official quickstart](https://labs.aholo.ai/api-docs/en/quickstart). Never paste a Key into an issue or commit it to source control. Settings validates the Key through the selected World, Lux3D or Spatial history service without creating a task. Each service has its own permissions and credits.

Generating a world or starting a render can consume your Aholo service credits. Clicking the corresponding submit button authorizes that flow, including the selected file/geometry upload and mesh processing for rendering. Closing the panel or stopping local waiting does **not** cancel a cloud task or refund credits. Offline render `requestId` is a tracing ID, **not an idempotency key**.

## Install

This alpha is built locally; it has not been installed or published automatically. The earlier Yak preview is unlisted. Build a local package with the scripts below and install the matching platform archive only after review. Restart Rhino manually after installation.

1. Run `AholoPanel`, open **Settings**, select **International** or **China**, enter that region’s API Key and choose a validation service. **Validate and connect** applies the selected region; **Apply service region** restores its saved Key. Local GS import/export and local history work without a Key.
2. Choose an operation in **Create**, fill its fields and submit once. **Advanced options** contains optional model/output settings.
3. In **Library**, follow status, select a result and download it. The exact path appears in feedback; **Open result folder** reveals its directory.
4. Use **Place in current document** for a supported GLB, mesh PLY/STL/supported 3MF/OBJ/typed OBJ ZIP/USD, spatial DSL or Gaussian PLY/SPZ. Mesh candidates use the selected CPlane/point, height and rotation; Settings controls GS units/axis. Placement supports Undo and repeated clicks do not duplicate an existing receipt. Optional automatic mesh placement belongs only to the original live generation intent; closing/changing document or connection revokes it. The generation face-count range is a service rule, not an import limit.
5. In **Render**, submit selected supported geometry from a perspective view. The original live session may advance mesh processing once; after restart/reset, use **Continue original render** explicitly. Query/download never creates another paid request.
6. For an already-ingested cloud asset, open the separate cloud preview window. Starting a stream may use service credits. Stop it there; if cleanup is not confirmed, retry the original session from its cleanup list. A lost create reply is not automatically retried.
7. `AholoCancel` or the footer button stops local waiting/preparation. It does not cancel remote work. Closing the panel preserves the shared session and task history. Background import/export commands report an operation ID and a final result; the footer also shows their active work. [Cancellation, scripting and shutdown behavior](docs/operation-lifecycle.md).

Library initially recommends a supported model result when a task returns both an archive and a mesh. Your explicit result selection is kept per task while the panel is open, including across refreshes. Result details distinguish saved files, missing cache and download-only formats, and explain the next action. Downloading does not place geometry. For an uncached mesh, **Place** can still check an earlier placement while offline; a new placement needs the file or the original account/region. Reported World progress appears as a percentage; missing/zero progress remains a waiting state with local record age and last-query information, without an estimated finish time.

3MF placement uses an independent static mesh reader for core geometry, components/transforms, base RGBA, direct PNG/JPEG textures and uniform triangle color groups. It retains the original source/hash and shares the self-contained candidate, Block and Undo flow. Gradient colors, mirror tiling, layered/composite materials and production/slice/vendor extensions remain unsupported. Native texture/opacity and persistence acceptance is pending; see [format boundaries and acceptance](docs/mesh-formats.md).

## Local mesh USD / USDZ exchange

Select model-space meshes, surfaces, extrusions or ordinary nested Blocks and run **`AholoExportMesh`**, or choose **Library → Export selected mesh USD**. This uses the same optional OpenUSD runtime as GS exchange and makes no cloud request. Orthographic views and read-only documents are supported. The panel can explicitly disable bitmap export for material constants only.

USDZ is a portable single file. Textured `.usd`, `.usdc` and `.usda` exports have a uniquely named `usd-assets-…` folder beside them: keep that folder with the scene. Replacing a scene retains older texture folders because other copies may still reference them. Failed/canceled conversion preserves the previous output. Export uses the captured model coordinates in millimetres/Z-up, rebased float32 geometry with double-precision origins, smooth normals, supported PBR constants and direct UV bitmaps. Ordinary block transforms are baked into separate mesh parts; the editable block hierarchy is not retained. USD texture filtering is viewer-defined, with source filters kept as metadata. See [USD exchange boundaries](docs/usd-exchange.md).

Run **`AholoImportMesh`** or use **Library → Load local mesh** for ordinary mesh `.usd`, `.usda`, `.usdc` or `.usdz`. Supported static triangles/quads and simple planar polygons, face-material subsets, transforms, normals, UV seams, basic Preview Surface materials and direct sRGB PNG/JPEG bitmaps enter the existing candidate preview and Block/Undo flow. Source files/dependencies are privately snapshotted; the derived 3DM embeds its images and records source hashes separately. Choose **Fit height on construction plane**, **Original size on construction plane** or **Preserve scene coordinates**. The last mode retains scene position after converting source units/axes to the document; the original-size and scene-coordinate modes accept planar meshes. The import command opens a candidate for review; it does not immediately insert it. Native Rhino appearance, embedded-image persistence and Windows execution remain pending acceptance.

This is a strict static subset: subdivision, nonplanar or self-intersecting faces with five or more corners, overlapping/invalid material subsets, animation/skinning, hidden/proxy geometry and unsupported shading fail explicitly. Import flattens hierarchy and displays meshes two-sided; the source culling flag is retained as metadata. USD task results use the same preview/direct-placement path, with stable output identity and receipt checks before download. Use `AholoImportScene` for complete mixed GS/mesh files. Faces with more than four corners are triangulated using their original corner attributes; valid non-overlapping material groups retain their effective bindings. Triangles, convex planar quads and genuinely warped quads keep the native route. Valid planar concave quads are triangulated with original corner attributes. Missing normals produce one flat normal per original face, following USD semantics; authored normals are retained. See [mesh format boundaries](docs/mesh-formats.md).

## Combined GS + mesh USD exchange

Use **`AholoExportScene`** or **Library → Export selected scene USD** to export selected GS and mesh/surface/extrusion objects together, including GS inside ordinary nested blocks. A single snapshot preserves repeated instances, complete SH and physical placement; meshes retain the supported materials and textures. USDZ is a single portable file; plain USD formats keep images in their adjacent folder. Pure GS or pure mesh selections also work. No API Key is needed. This is static exchange, with native capture/viewer acceptance pending; editable source hierarchy is not retained. The separate GS and mesh import commands reject mixed scenes. See [exchange details](docs/usd-exchange.md).

Use **`AholoImportScene`** or **Library → Import USD scene** for a complete static USD/USDA/USDC/USDZ scene. It reads one private dependency snapshot, validates both asset types before touching the document, and imports immediately at the source physical position and size. Meshes become one independent Block; GS remain top-level instances so the native GS display can find them. One Undo record covers the combined operation. Units/color-setting changes or invalid assets stop the import. Pure GS and pure mesh files also work. Unlike the local mesh candidate action, this command directly places the complete scene.

For an offline check, import [`fixtures/mixed-scene.usda`](fixtures/mixed-scene.usda): two red/cyan anisotropic Gaussians and a blue box exactly 2 metres high. Check source scale, orbit, opaque occlusion, one Undo/Redo, then save/reopen. Native visual, resource-persistence and rollback acceptance must be recorded separately on Mac and Windows.

## Local GS USD / USDZ import and export

No API Key or network call is needed for local GS exchange. `AholoImportGs` and the panel's local import action accept full Gaussian PLY, headerless 32-byte SPLAT, SPZ v1–4 and static official OpenUSD `.usd`, `.usda`, `.usdc`, `.usdz` scenes. Stage units, up axis, multiple parts and full transforms are respected; the original cloud task PLY placement remains available.

Select top-level GS blocks and run **`AholoExportGs`**, or use the panel's export action. The panel offers selected GS or all top-level GS, including hidden/locked blocks as visible exported assets. Empty selection and mixed non-GS selection are rejected. `.usdz` is self-contained; `.usd`/`.usdc` are binary, `.usda` is text. Export preserves all splats, complete SH0–3, opacity and full object transforms in current Rhino units/Z-up. Official geometry fields use float32 precision; double transforms and local-origin shifting reduce large-coordinate loss, but arbitrary double inputs are not bit-exact. Set a standard length unit first. This GS-only route excludes ordinary meshes, arbitrary vendor GS schemas, animation and nested Rhino blocks; use `AholoExportScene` for mixed/nested export.

In Settings, **Check USD readiness** reports the local runtime path and version. The setup command is shown there and runs only when you execute it.

Install the optional pinned runtime once using Python 3.11–3.14:

```sh
python3.12 scripts/setup-usd.py
python3.12 scripts/setup-usd.py --check
```

On Windows use `py -3.12`. The script installs `usd-core==26.11` in `~/Library/Application Support/Aholo-Rhino/usd-runtime` on macOS or `%LOCALAPPDATA%\Aholo-Rhino\usd-runtime` on Windows, without modifying system Python. `--wheel-dir DIRECTORY` supports offline installation; `AHOLO_USD_PYTHON` can override the Python path before Rhino starts. After setup, exchange is entirely local. See [exchange details and acceptance steps](docs/usd-exchange.md).

## Rendering conventions and current limits

Rhino geometry uses original render meshes for supported surface textures and default tessellation otherwise, then converts to standard glTF metres/Y-up. Missing original caches are prepared using the object's/document's mesh settings. RenderCloud consumes an external mesh asset referenced by USDA, not inline USD vertex arrays. The scene applies the inverse conversion to millimetres/Z-up, as required by the renderer. **Whether the service's fast GLB ingestion preserves this convention still requires a real account test.** Verify a known-size cube and camera before using cloud output for scale-critical work.

The camera export preserves the viewport's horizontal field of view and uses the selected output aspect ratio. Orthographic and shifted-frustum cameras are rejected explicitly. Export preserves smooth/custom vertex normals through nonuniform and mirrored block transforms, legacy base colors and PBR base RGB, metallic, roughness and alpha coverage; PBR colors follow Rhino input-color preprocessing once. Base-color bitmaps can be embedded from local sRGB RGB JPEG or RGB/RGBA PNG files using mesh surface UVs, original-cache Brep surface UVs, consistent-material extrusion surface UVs when recoverable, or explicit plane/box/cylinder/sphere mappings on Mesh, Brep and Extrusion objects. Channel selection follows the requested channel, then channel 1, then supported surface parameterization. Native mapping and bitmap transforms are each applied once; wrap/filter and the PBR texture-alpha flag are preserved. Reused image bytes are deduplicated. When texture alpha is disabled but material opacity is partial, GLB embeds a private RGB copy that preserves every original PNG color sample; the source image and USD texture bytes remain unchanged. Non-sRGB/profile-tagged/EXIF images, missing files, non-Modulate blending, procedural/WCS/parent-block mapping, legacy channel 0, missing original surface metadata or different extrusion face materials combined with surface UVs fail before upload. Disable bitmap export to deliberately use material constants only. Embedded-only 3DM bitmaps can resolve through Rhino’s bitmap cache; native persistence and visual parity still require acceptance. Image checks are structural, not a full JPEG decode or native visual acceptance. Export captures supported Brep face materials and mapped extrusion wall/cap materials as separate parts, including effective block-parent materials. Complex material graphs, glass transmission and point clouds remain unsupported; new native face-material behavior still needs acceptance. Selected world rectangular and point lights can be included explicitly. Rectangles preserve center, orientation and dimensions through supported block transforms. Point lights use an explicit sphere radius in millimetres before block scaling; nonuniform point-light scale and sheared rectangles are rejected. Intensity is a relative multiplier, not photometric matching to Rhino lumens. Unsupported selected light types fail before upload. Ordinary nested Blocks are traversed without changing source geometry or assignments; runtime render caches may be populated; GS blocks are rejected before upload. Supported selected geometry is listed in the panel before submission; unsupported selection types cause an error.

## Data and credentials

The background process uses private stdin/stdout pipes plus a current-user named pipe with a per-process nonce for realtime preview; it exposes no local HTTP control port. Optional OAuth temporarily binds an OS-selected IPv4 loopback port solely for the browser callback, then closes it. Only the fixed gateway bound to the Key’s selected region receives that Key. Uploads use a separate short-lived OUS token; downloads use unauthenticated HTTPS with redirects disabled and public-address checks.

Prompt/reference images and selected render geometry are sent to Aholo when submitted. Job JSON, input snapshots and downloaded results stay under `Aholo-Rhino` in the OS local application-data folder. The export subfolder can contain intermediate GLB files. GS imports create a SHA-256-named local PLY snapshot. The Rhino document saves the asset hash and local path, not embedded splat data; share the matching PLY/cache with the 3dm. Missing or changed assets keep a visible bounding-box placeholder. The document may therefore contain local filesystem paths. Task records do not contain Keys or signed asset URLs. Disconnect removes the saved Key, while preserving local work. There is no automatic telemetry. Storage protection does not defend against other processes running as the same user or administrators.

Local GS preparation timings, batch counts and sanitized errors are written asynchronously to `Aholo-Rhino/logs/gs-viewport.log`, rotated at about 1 MiB with one previous file. `AholoGsDiagnostics` displays its exact path. The renderer does not log prompts, Keys or signed URLs.

The local Library searches and pages across retained records, without a latest-100-only cutoff. Realtime cleanup journals retain original session identifiers but no API Key or frame data. JPEG frames stay in memory; closure attempts server cleanup, but a crash/network failure can leave an unresolved session that requires attention. An interrupted pre-submit upload is not automatically restarted. Large exports/imports may require substantial memory and disk space.

## Build

```sh
dotnet build Aholo.sln -c Release
dotnet test tests/Aholo.Tests/Aholo.Tests.csproj -c Release
python3 scripts/create-gs-assets.py --check
dotnet run --project tools/Aholo.Reference -c Release -- --output artifacts/gs-reference
./scripts/build-package.sh --platform mac
# A Windows-tagged candidate may be packaged from the same platform-neutral assemblies:
./scripts/build-package.sh --platform win
```

On Windows, run `powershell -File scripts/build-package.ps1` (Python 3 required). This Windows script has not yet been executed on Windows.

The packaging script creates a fresh staging directory, checks the archive and writes SHA-256 receipts. It does not install, upload or publish anything. A platform tag does not imply completed host validation.

[Architecture and API evidence](docs/architecture.md) · [Validation record](docs/validation.md) · [Host acceptance](docs/host-acceptance.md) · [API snapshots](docs/api/README.md)

Cloud preview → **Edit cloud scene** manages multiple prepared cloud mesh/GS IDs and rectangular/point lights before or during streaming. Add, edit or remove items in a private draft, then Save/Apply; Cancel leaves the preview unchanged. Model controls include world-mm XYZ offset, XYZ rotation, uniform/independent axis scale, optional shear and coordinate convention. Lights expose position, linear RGB, intensity, dimensions and incremental world-axis rotation; selected Rhino lights can be captured without modifying the document. Advanced settings add exposure, specular contribution, reflection influence and rectangular two-sided emission. Zero rotation retains captured orientation; new rectangles face local -Z. Environment brightness is an absolute positive multiplier.

Updates retain object paths and use one ordered queue with camera motion. Only a server acknowledgement commits the local scene; a new JPEG is not an acknowledgement. Replacing an existing asset/type requires a new item, and deleted identities are never reused. Switching accounts while editing rejects the stale draft. An uncertain edit closes the original session without replay; the provider does not promise atomic rollback or per-object readback. Cloud deletion and visual appearance remain live acceptance gates. Mirror/shear fidelity and advanced light flags remain live validation gates; native mixed-depth compositing is still unavailable. See [cloud scene editing](docs/realtime-scene-editing.md).

## License and attribution

Apache-2.0. See [LICENSE](LICENSE) and [NOTICE](NOTICE). Runtime discovery, pipe communication, credential protection and download transport draw on the existing Lux3D/Tripo integrations. Maintained by **CuriousArc · qrost**. Report issues through the project maintainer with version, platform, task status and reproducible steps; omit credentials and signed URLs.

Settings → **Restore saved connection** recovers a stopped background connection. Changing or clearing a Key invalidates other running instances before their next service request. Already-sent requests can finish. A prepared render requires an explicit continuation after reconnect; it is never automatically resubmitted.

## OBJ imports

Load local `.obj`/OBJ `.zip` files through Library or `AholoImportMesh`, or choose a typed `obj_zip` task result. Select one model, source units and Y/Z up before preparation. Static planar polygons, smoothing/authored normals, supported MTL colors/opacity and PNG/JPEG diffuse maps enter the shared candidate/Block workflow without Python. Legacy lighting is a reported PBR approximation; unsupported geometry/material effects fail explicitly. Generic ZIP and FBX task outputs remain download-only. Each task result keeps one placement; import its ZIP locally to choose a different entry. See [format limits and manual acceptance](docs/obj-import-development.md). New native Mac/Windows behavior remains unaccepted.
