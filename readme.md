# Aholo for Rhino

World generation and RenderCloud rendering inside Rhino 8, by **CuriousArc · qrost**.

[简体中文](readme.zh-CN.md)

**0.1.0-beta.2 — local development preview.** This is an independent integration, not an official Manycore, Aholo, or McNeel product. It is a new project with separate commands, plug-in identity, credentials, and cache; it does not replace Lux3D for Rhino.

## What works in this version

- Generate a world from text, one reference image, or both using Aholo's World API.
- Follow persisted jobs, reconnect to an existing world by its ID, and download original PLY/SPZ or the available panorama.
- Import a full 3DGS PLY as a normal Rhino block reference and draw anisotropic Gaussian ellipses with opacity and SH 0–3 view-dependent colors in **Aholo GS (Experimental)**. Source files and parameters are retained. Scale, axis conversion, transforms and Undo are implemented; actual host behavior remains to be verified.
- Run `AholoGsProbe` for an offline native-viewport experiment, `AholoImportGs` for a local GS PLY, and `AholoGsDiagnostics` for status. `AholoImportPly` is a separate center-point diagnostic only.
- Upload selected Mesh/Brep/Extrusion objects, preserve basic diffuse colors, and submit an offline RenderCloud job using the active perspective camera. Download and open the resulting image.
- Store an API Key in macOS Keychain or Windows current-user DPAPI if requested; otherwise keep it only in the background process.
- Preserve task IDs and uncertain submission records rather than automatically repeating a request that might already have been charged.

**World files are 3D Gaussian Splatting data, not ordinary triangle meshes.** World placement now uses the experimental GS renderer by default. A PLY without complete Gaussian fields is rejected; it is never silently substituted with a point cloud. The original PLY/SPZ is retained; this first renderer decodes PLY, not SPZ.

This is a **small-scene correctness prototype**, not a production GPU engine. It projects/sorts on the CPU and draws textured Gaussian quads through Rhino's public display API. It does not impose a splat/vertex-count ceiling or silently decimate data. Large scenes can be slow or exhaust memory. The prototype needs actual Mac/Windows testing of texture alpha, sorting and native depth. Arbitrary interleaving with transparent Rhino objects, clipping planes, nested blocks, printing/view capture, stereo and Raytraced integration are not supported. Its byte-color material also clamps HDR SH colors before blending; the CPU oracle retains HDR and is not a claim of pixel-identical host rendering.

Realtime RenderCloud REST/WebSocket transport and mock-tested session cleanup are implemented as a library foundation. **There is no realtime cloud viewport yet.** The published stream returns JPEG without depth, alpha or camera-associated frame IDs; World GS assets also need a validated bridge to cloud mesh IDs. Full-scene cloud rendering remains a planned stage, with explicit capability gates. Video/multi-image reconstruction and full Rhino PBR/textures/lights remain outside this candidate. World generation is best suited to indoor scenes according to the official documentation.

See the [staged plan and evidence](docs/decisions/0001-native-gaussian-viewport.md) and [GS acceptance procedure](docs/gs-host-acceptance.md).

## Requirements and validation status

| Item | Status |
| --- | --- |
| Rhino | Rhino 8.32 or later; host assembly targets .NET 7 |
| Background runtime | .NET 8 Runtime matching the host architecture |
| Service | International Aholo API Key with World/RenderCloud access and sufficient service credits |
| macOS | Release build and offline checks on Apple Silicon; actual Rhino UI/import/cloud acceptance pending |
| Windows | Cross-platform source and Windows credential storage included; Windows build, installer execution and Rhino host acceptance pending |
| Intel Mac | Not yet host-tested |

Installing the package does not require the .NET SDK or Python. Building from source requires the .NET 8 SDK; packaging/verification scripts additionally use Python 3.9+ and Rhino's Yak CLI.

## Authentication and cost

The official REST API currently documents **a raw regional API Key in the `Authorization` header**. Manycore OAuth sign-in does not replace this credential for World or RenderCloud. This first version asks directly for the international Key and does not provide an OAuth sign-in button. China-region endpoints are not enabled in this version.

Create/manage the Key in the official Aholo platform, following the [official quickstart](https://labs.aholo.ai/api-docs/en/quickstart). Never paste a Key into an issue or commit it to source control. The connection action checks the Key through a world-list request; it does not create a world. Rendering permissions and credits may differ from World access.

Generating a world or starting a render can consume your Aholo service credits. Clicking the corresponding submit button authorizes that flow, including the selected file/geometry upload and mesh processing for rendering. Closing the panel or stopping local waiting does **not** cancel a cloud task or refund credits. Offline render `requestId` is a tracing ID, **not an idempotency key**.

## Try the local candidate

There is no public Yak listing for this project yet. Obtain the local `.yak` produced by `scripts/build-package.sh`, install it with Rhino's bundled Yak CLI (`yak install /absolute/path/to/package.yak`), then restart Rhino manually. On Windows use Rhino's `Yak.exe` with the absolute package path. Do not use a macOS-tagged package on Windows.

1. Run `AholoPanel`.
2. In the connection tab, enter an international API Key and choose whether to remember it.
3. In the World tab, enter a prompt and optionally choose a reference image. Submit once and refresh or enable polling until the task succeeds.
4. Download the PLY and place it in the current document using the experimental GS mode. Check the import scale: the World API reports the up axis but does not establish an absolute unit for generated scenes. The default is one Rhino document unit per PLY unit.
5. For cloud rendering, select Mesh/Brep/Extrusion objects and activate a perspective viewport. Choose the render size and submit. Keep the panel open with polling enabled, or manually refresh the job to advance mesh processing into rendering.
6. Download/open the finished image from the render history.

For the first offline GS test, use an empty document and run `AholoGsProbe`. It adds synthetic Gaussians, a Brep and a mesh without contacting Aholo; two Undo operations remove the test. `AholoImportGs` can load `fixtures/gaussian-anisotropic.ply` or `fixtures/gaussian-sh3.ply`. Use `AholoGsMode` to activate/reload the mode. [GS acceptance steps](docs/gs-host-acceptance.md) describe the expected result and current limits. `fixtures/tiny-world.ply` is only for the explicit `AholoImportPly` center-point diagnostic.

If submission is marked `SubmissionUnknown`, inspect Aholo's task history before creating another job. World IDs can be added to local history. Render requests with a lost operation ID cannot currently be reconciled automatically. A saved operation ID is safe to poll.

## Rendering conventions and current limits

Rhino geometry is tessellated using Rhino's default meshing parameters and converted to standard glTF metres/Y-up. RenderCloud consumes an external mesh asset referenced by USDA, not inline USD vertex arrays. The scene applies the inverse conversion to millimetres/Z-up, as required by the renderer. **Whether the service's fast GLB ingestion preserves this convention still requires a real account test.** Verify a known-size cube and camera before using cloud output for scale-critical work.

The camera export preserves the viewport's horizontal field of view and uses the selected output aspect ratio. Orthographic and shifted-frustum cameras are rejected explicitly. Only basic diffuse colors are exported; Rhino material graphs, textures, custom lights, block instances and point clouds are not currently exported. Supported selected geometry is listed in the panel before submission; unsupported selection types cause an error.

## Data and credentials

The background process uses private stdin/stdout pipes; it exposes no local HTTP control port. Only the fixed international API receives the API Key. Uploads use a separate short-lived OUS token; downloads use unauthenticated HTTPS with redirects disabled and public-address checks.

Prompt/reference images and selected render geometry are sent to Aholo when submitted. Job JSON, input snapshots and downloaded results stay under `Aholo-Rhino` in the OS local application-data folder. The export subfolder can contain intermediate GLB files. GS imports create a SHA-256-named local PLY snapshot. The Rhino document saves the asset hash and local path, not embedded splat data; share the matching PLY/cache with the 3dm. Missing or changed assets keep a visible bounding-box placeholder. The document may therefore contain local filesystem paths. Task records do not contain Keys or signed asset URLs. Disconnect removes the saved Key, while preserving local work. There is no automatic telemetry. Storage protection does not defend against other processes running as the same user or administrators.

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
