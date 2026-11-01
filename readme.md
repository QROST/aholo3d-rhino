# Aholo for Rhino

World generation and RenderCloud rendering inside Rhino 8, by **CuriousArc · qrost**.

[简体中文](readme.zh-CN.md)

**0.1.0-beta.1 — local development preview.** This is an independent integration, not an official Manycore, Aholo, or McNeel product. It is a new project with separate commands, plug-in identity, credentials, and cache; it does not replace Lux3D for Rhino.

## What works in this version

- Generate a world from text, one reference image, or both using Aholo's World API.
- Follow persisted jobs, reconnect to an existing world by its ID, and download original PLY/SPZ or the available panorama.
- Place a PLY as one colored Rhino point cloud, with explicit scale, axis conversion, Undo and zoom to the imported result. `AholoImportPly` also works offline.
- Upload selected Mesh/Brep/Extrusion objects, preserve basic diffuse colors, and submit an offline RenderCloud job using the active perspective camera. Download and open the resulting image.
- Store an API Key in macOS Keychain or Windows current-user DPAPI if requested; otherwise keep it only in the background process.
- Preserve task IDs and uncertain submission records rather than automatically repeating a request that might already have been charged.

**World files are 3D Gaussian Splatting data, not ordinary triangle meshes.** Rhino's preview displays the splat centers and RGB/DC colors. It does not reproduce Gaussian shape, opacity or view-dependent appearance. Full PLY/SPZ files remain available for compatible 3DGS tools. There is no application-imposed point or vertex ceiling; memory, Rhino/.NET container sizes, and the GLB format's 32-bit size field still apply. Unsupported or malformed data produces an error rather than silent decimation.

This version does not implement realtime RenderCloud streaming, reconstruction from video/multiple images, full Rhino PBR/textures/lights, or native Gaussian splat rendering. World generation is best suited to indoor scenes according to the current official documentation.

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
4. Download the PLY and place it in the current document. Check the preview scale: the World API reports the up axis but does not establish an absolute unit for generated scenes. The default is one Rhino document unit per PLY unit.
5. For cloud rendering, select Mesh/Brep/Extrusion objects and activate a perspective viewport. Choose the render size and submit. Keep the panel open with polling enabled, or manually refresh the job to advance mesh processing into rendering.
6. Download/open the finished image from the render history.

For a first offline test, run `AholoImportPly` and choose `fixtures/tiny-world.ply`. See [manual acceptance steps](docs/host-acceptance.md) before relying on a release.

If submission is marked `SubmissionUnknown`, inspect Aholo's task history before creating another job. World IDs can be added to local history. Render requests with a lost operation ID cannot currently be reconciled automatically. A saved operation ID is safe to poll.

## Rendering conventions and current limits

Rhino geometry is tessellated using Rhino's default meshing parameters and converted to standard glTF metres/Y-up. RenderCloud consumes an external mesh asset referenced by USDA, not inline USD vertex arrays. The scene applies the inverse conversion to millimetres/Z-up, as required by the renderer. **Whether the service's fast GLB ingestion preserves this convention still requires a real account test.** Verify a known-size cube and camera before using cloud output for scale-critical work.

The camera export preserves the viewport's horizontal field of view and uses the selected output aspect ratio. Orthographic and shifted-frustum cameras are rejected explicitly. Only basic diffuse colors are exported; Rhino material graphs, textures, custom lights, block instances and point clouds are not currently exported. Supported selected geometry is listed in the panel before submission; unsupported selection types cause an error.

## Data and credentials

The background process uses private stdin/stdout pipes; it exposes no local HTTP control port. Only the fixed international API receives the API Key. Uploads use a separate short-lived OUS token; downloads use unauthenticated HTTPS with redirects disabled and public-address checks.

Prompt/reference images and selected render geometry are sent to Aholo when submitted. Job JSON, input snapshots and downloaded results stay under `Aholo-Rhino` in the OS local application-data folder. The export subfolder can contain intermediate GLB files. Task records do not contain Keys or signed asset URLs. Disconnect removes the saved Key, while preserving local work. There is no automatic telemetry. Storage protection does not defend against other processes running as the same user or administrators.

The UI shows the latest 100 local records across job types; older records remain on disk. An interrupted pre-submit upload is not automatically restarted. Large exports/imports may require substantial memory and disk space.

## Build

```sh
dotnet build Aholo.sln -c Release
dotnet test tests/Aholo.Tests/Aholo.Tests.csproj -c Release
./scripts/build-package.sh --platform mac
# A Windows-tagged candidate may be packaged from the same platform-neutral assemblies:
./scripts/build-package.sh --platform win
```

On Windows, run `powershell -File scripts/build-package.ps1` (Python 3 required). This Windows script has not yet been executed on Windows.

The packaging script creates a fresh staging directory, checks the archive and writes SHA-256 receipts. It does not install, upload or publish anything. A platform tag does not imply completed host validation.

[Architecture and API evidence](docs/architecture.md) · [Validation record](docs/validation.md) · [Host acceptance](docs/host-acceptance.md) · [API snapshots](docs/api/README.md)

## License and attribution

Apache-2.0. See [LICENSE](LICENSE) and [NOTICE](NOTICE). Runtime discovery, pipe communication, credential protection and download transport draw on the existing Lux3D/Tripo integrations. Maintained by **CuriousArc · qrost**. Report issues through the project maintainer with version, platform, task status and reproducible steps; omit credentials and signed URLs.
