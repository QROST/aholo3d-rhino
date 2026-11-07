# Aholo for Rhino

World generation and RenderCloud rendering inside Rhino 8, by **CuriousArc · qrost**.

[简体中文](readme.zh-CN.md)

**0.1.1-beta.2 — macOS preview with automatic English/Chinese interface selection and GS USD/USDZ interchange.** The preceding 0.1.0 generation/display workflow was user-tested on Apple Silicon Mac; the new import/export UI and bilingual native layout still need manual acceptance. This is an independent integration, not an official Manycore, Aholo, or McNeel product. It is a new project with separate commands, plug-in identity, credentials, and cache; it does not replace Lux3D for Rhino.

## Interface language

English is the default and fallback. At plugin startup, a valid Rhino interface language takes precedence over the system UI language. Chinese Rhino variants (including Simplified and Traditional Chinese locales) use Simplified Chinese; other Rhino languages use English. The system UI language is used only when Rhino cannot provide a valid language. Restart Rhino after changing its language.

Panels, command messages, GS diagnostics and known task errors follow this selection. User prompts, names, paths, task IDs and stored status codes remain unchanged. Unknown external or technical errors are preserved rather than guessed at. Language selection and embedded resources are covered by automated tests; native layout in both languages still needs manual Rhino acceptance.

## What works in this version

- Generate a world from text, one reference image, or both using Aholo's World API.
- Follow persisted jobs, reconnect to an existing world by its ID, and download original PLY/SPZ or the available panorama.
- Import a full 3DGS PLY as a normal Rhino block reference and draw anisotropic Gaussian ellipses with opacity and SH 0–3 view-dependent colors in **Aholo GS (Experimental)**. Source files and parameters are retained. Scale, axis conversion, transforms and Undo are implemented; detailed document-lifecycle checks remain open.
- Run `AholoGsProbe` for an offline native-viewport experiment, `AholoImportGs` for a local GS PLY or USD/USDZ scene, and `AholoGsDiagnostics` for status. `AholoImportPly` is a separate center-point diagnostic only.
- Upload selected Mesh/Brep/Extrusion objects, preserve basic diffuse colors, and submit an offline RenderCloud job using the active perspective camera. Download and open the resulting image.
- Store an API Key in macOS Keychain or Windows current-user DPAPI if requested; otherwise keep it only in the background process.
- Preserve task IDs and uncertain submission records rather than automatically repeating a request that might already have been charged.

**World files are 3D Gaussian Splatting data, not ordinary triangle meshes.** World placement now uses the experimental GS renderer by default. A PLY without complete Gaussian fields is rejected; it is never silently substituted with a point cloud. The original PLY/SPZ is retained; this first renderer decodes PLY, not SPZ.

This is an **experimental renderer**. Beta.3 replaces the per-Gaussian native resource path that caused a severe memory/navigation failure in beta.2. Projection and global depth sorting run on a cancellable background worker; native meshes are transferred in small UI slices, with up to 2,048 splats per mesh and one shared opacity atlas/material. The batch size is a transfer unit, not a geometry limit: all visible splats are retained. While a changed camera is being prepared, the block shows a bounding box; the complete GS frame returns after preparation. Saving a 3dm does not render GS into its thumbnail. See the [freeze repair](docs/gs-freeze-repair.md) and [display stability repair](docs/gs-visibility-stability.md).

The user confirmed red/teal probe colors, colored world display, faster navigation and basic clipping-plane interaction on the local Apple Silicon Mac. After the visibility and startup fixes in beta.4/beta.5, the user confirmed the retest passed on 2026-11-02 and approved version 0.1.0. Detailed alpha/depth/sorting and Windows acceptance remain pending. Arbitrary interleaving with transparent Rhino objects, nested blocks, layout details, printing/view capture, stereo and Raytraced integration are not supported. Display RGB is 8-bit and clips HDR before blending; opacity uses 256 atlas levels (at most 0.5/255 opacity rounding), without altering the source. The CPU oracle retains floating-point data and is not a claim of pixel-identical host rendering. Basic clipping-plane interaction was observed by the user on Mac; multi-plane/edge behavior is still experimental.

Realtime RenderCloud REST/WebSocket transport and mock-tested session cleanup are implemented as a library foundation. **There is no realtime cloud viewport yet.** The published stream returns JPEG without depth, alpha or camera-associated frame IDs; World GS assets also need a validated bridge to cloud mesh IDs. Full-scene cloud rendering remains a planned stage, with explicit capability gates. Video/multi-image reconstruction and full Rhino PBR/textures/lights remain outside this version. World generation is best suited to indoor scenes according to the official documentation.

See the [staged plan and evidence](docs/decisions/0001-native-gaussian-viewport.md) and [GS acceptance procedure](docs/gs-host-acceptance.md).

Existing GS modes are checked and reused, initialization waits for UI idle, and a GS failure does not block the panel. Run `AholoGsMode` to retry locally if initialization is unavailable. Version 0.1.0 retained the tested beta.5 implementation. Version 0.1.1-beta.1 added local GS exchange while keeping the cloud task workflow; 0.1.1-beta.2 adds automatic interface language selection.

## Requirements and validation status

| Item | Status |
| --- | --- |
| Rhino | Rhino 8.32 or later; host assembly targets .NET 7 |
| Background runtime | .NET 8 Runtime matching the host architecture |
| Service | International Aholo API Key with World/RenderCloud access and sufficient service credits |
| macOS | Apple Silicon: user confirmed World generation/import, GS colors/navigation/basic clipping, and passed the beta.5 startup/display retest; detailed matrix remains open |
| Windows | Cross-platform source and Windows credential storage included; Windows build, installer execution and Rhino host acceptance pending |
| Intel Mac | Not yet host-tested |

Installing the package does not require the .NET SDK. USD import/export additionally requires the optional private Python/OpenUSD runtime described below. Building from source requires the .NET 8 SDK; packaging/verification scripts additionally use Python 3.9+ and Rhino's Yak CLI.

## Authentication and cost

The official REST API currently documents **a raw regional API Key in the `Authorization` header**. Manycore OAuth sign-in does not replace this credential for World or RenderCloud. This first version asks directly for the international Key and does not provide an OAuth sign-in button. China-region endpoints are not enabled in this version.

Create/manage the Key in the official Aholo platform, following the [official quickstart](https://labs.aholo.ai/api-docs/en/quickstart). Never paste a Key into an issue or commit it to source control. The connection action checks the Key through a world-list request; it does not create a world. Rendering permissions and credits may differ from World access.

Generating a world or starting a render can consume your Aholo service credits. Clicking the corresponding submit button authorizes that flow, including the selected file/geometry upload and mesh processing for rendering. Closing the panel or stopping local waiting does **not** cancel a cloud task or refund credits. Offline render `requestId` is a tracing ID, **not an idempotency key**.

## Install

This preview targets macOS; Windows publication awaits native acceptance. For published previews, enable pre-release packages in Rhino's PackageManager and search for `aholo-rhino`. For a local package, install the `.yak` produced by `scripts/build-package.sh` with Rhino's bundled Yak CLI (`yak install /absolute/path/to/package.yak`). Restart Rhino manually after installing or updating. Do not use a macOS-tagged package on Windows.

1. Run `AholoPanel`.
2. In the connection tab, enter an international API Key and choose whether to remember it.
3. In the World tab, enter a prompt and optionally choose a reference image. Submit once and refresh or enable polling until the task succeeds.
4. Download the PLY and place it in the current document using the experimental GS mode. Check the import scale: the World API reports the up axis but does not establish an absolute unit for generated scenes. The default is one Rhino document unit per PLY unit.
5. For cloud rendering, select Mesh/Brep/Extrusion objects and activate a perspective viewport. Choose the render size and submit. Keep the panel open with polling enabled, or manually refresh the job to advance mesh processing into rendering.
6. Download/open the finished image from the render history.

For the first offline GS test, use an empty document and run `AholoGsProbe`. It adds synthetic Gaussians, a Brep and a mesh without contacting Aholo; two Undo operations remove the test. `AholoImportGs` can load `fixtures/gaussian-anisotropic.ply` or `fixtures/gaussian-sh3.ply`. Use `AholoGsMode` to activate/reload the mode. [GS acceptance steps](docs/gs-host-acceptance.md) describe the expected result and current limits. `fixtures/tiny-world.ply` is only for the explicit `AholoImportPly` center-point diagnostic.

If submission is marked `SubmissionUnknown`, inspect Aholo's task history before creating another job. World IDs can be added to local history. Render requests with a lost operation ID cannot currently be reconciled automatically. A saved operation ID is safe to poll.

## Local GS USD / USDZ import and export

No API Key or network call is needed for local GS exchange. `AholoImportGs` and the panel's local import action accept full Gaussian PLY and static official OpenUSD `.usd`, `.usda`, `.usdc`, `.usdz` scenes. Stage units, up axis, multiple parts and full transforms are respected; the original cloud task PLY placement remains available.

Select top-level GS blocks and run **`AholoExportGs`**, or use the panel's export action. The panel offers selected GS or all top-level GS, including hidden/locked blocks as visible exported assets. Empty selection and mixed non-GS selection are rejected. `.usdz` is self-contained; `.usd`/`.usdc` are binary, `.usda` is text. Export preserves all splats, complete SH0–3, opacity and full object transforms in current Rhino units/Z-up. Official geometry fields use float32 precision; double transforms and local-origin shifting reduce large-coordinate loss, but arbitrary double inputs are not bit-exact. Set a standard length unit first. Ordinary mesh USDZ, arbitrary vendor GS schemas, animation and nested Rhino blocks are not included in this local GS interchange.

Install the optional pinned runtime once using Python 3.11–3.14:

```sh
python3.12 scripts/setup-usd.py
python3.12 scripts/setup-usd.py --check
```

On Windows use `py -3.12`. The script installs `usd-core==26.11` in `~/Library/Application Support/Aholo-Rhino/usd-runtime` on macOS or `%LOCALAPPDATA%\Aholo-Rhino\usd-runtime` on Windows, without modifying system Python. `--wheel-dir DIRECTORY` supports offline installation; `AHOLO_USD_PYTHON` can override the Python path before Rhino starts. After setup, exchange is entirely local. See [exchange details and acceptance steps](docs/usd-exchange.md).

## Rendering conventions and current limits

Rhino geometry is tessellated using Rhino's default meshing parameters and converted to standard glTF metres/Y-up. RenderCloud consumes an external mesh asset referenced by USDA, not inline USD vertex arrays. The scene applies the inverse conversion to millimetres/Z-up, as required by the renderer. **Whether the service's fast GLB ingestion preserves this convention still requires a real account test.** Verify a known-size cube and camera before using cloud output for scale-critical work.

The camera export preserves the viewport's horizontal field of view and uses the selected output aspect ratio. Orthographic and shifted-frustum cameras are rejected explicitly. Only basic diffuse colors are exported; Rhino material graphs, textures, custom lights, block instances and point clouds are not currently exported. Supported selected geometry is listed in the panel before submission; unsupported selection types cause an error.

## Data and credentials

The background process uses private stdin/stdout pipes; it exposes no local HTTP control port. Only the fixed international API receives the API Key. Uploads use a separate short-lived OUS token; downloads use unauthenticated HTTPS with redirects disabled and public-address checks.

Prompt/reference images and selected render geometry are sent to Aholo when submitted. Job JSON, input snapshots and downloaded results stay under `Aholo-Rhino` in the OS local application-data folder. The export subfolder can contain intermediate GLB files. GS imports create a SHA-256-named local PLY snapshot. The Rhino document saves the asset hash and local path, not embedded splat data; share the matching PLY/cache with the 3dm. Missing or changed assets keep a visible bounding-box placeholder. The document may therefore contain local filesystem paths. Task records do not contain Keys or signed asset URLs. Disconnect removes the saved Key, while preserving local work. There is no automatic telemetry. Storage protection does not defend against other processes running as the same user or administrators.

Local GS preparation timings, batch counts and sanitized errors are written asynchronously to `Aholo-Rhino/logs/gs-viewport.log`, rotated at about 1 MiB with one previous file. `AholoGsDiagnostics` displays its exact path. The renderer does not log prompts, Keys or signed URLs.

The UI shows the latest 100 local records across job types; older records remain on disk. An interrupted pre-submit upload is not automatically restarted. Large exports/imports may require substantial memory and disk space.

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

## License and attribution

Apache-2.0. See [LICENSE](LICENSE) and [NOTICE](NOTICE). Runtime discovery, pipe communication, credential protection and download transport draw on the existing Lux3D/Tripo integrations. Maintained by **CuriousArc · qrost**. Report issues through the project maintainer with version, platform, task status and reproducible steps; omit credentials and signed URLs.
