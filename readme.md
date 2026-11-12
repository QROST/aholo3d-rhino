# Aholo3D for Rhino

Create worlds and objects, view Gaussian scenes, and render through Aholo3D from Rhino 8.

**CuriousArc · qrost** · [简体中文](readme.zh-CN.md) · [Documentation](docs/index.md)

Aholo3D for Rhino is an independent, Apache-2.0 plugin. It is not an official Aholo3D, Manycore or McNeel product.

## What you can do

- **Create worlds:** generate from text or a reference image; reconstruct from supported photos or video.
- **Create objects:** use Lux3D for meshes, images, material transfer, part splitting, format conversion, rigging and animation-file generation.
- **Work with results:** browse local and cloud history, download original outputs, preview static meshes and place them as Rhino blocks with Undo.
- **View Gaussians:** import PLY, SPZ or SPLAT and use the experimental native GS display mode.
- **Exchange scenes:** import and export supported static mesh and Gaussian USD/USDZ scenes.
- **Render:** submit supported Rhino geometry for cloud rendering, or open a separate live cloud-preview window.

The Create, Library and Render pages share one workspace. Settings holds account, region, import and storage options. The interface defaults to English and uses Simplified Chinese when Rhino—or the system fallback—uses Chinese.

## Requirements

| Component | Requirement |
| --- | --- |
| Rhino | Rhino 8.32 or later in the Rhino 8 series |
| Platform | macOS or Windows |
| Background service | .NET 8 runtime |
| Cloud features | Aholo3D API Key for the selected region, service permissions and available credits |
| Local USD/USDZ tools | Optional OpenUSD runtime; setup is available in Settings |

**Preview release:** core workflows have local automated checks and prior macOS testing. The latest interface and Windows native workflows still need host verification. See [testing](docs/testing.md).

## Install

1. If upgrading from **`aholo-rhino`**, uninstall that old package in Rhino's PackageManager and restart. The two names share a plugin identity and must not be loaded together. Preserve the plugin data directory.
2. Install a released **`aholo3d-rhino`** package through `_PackageManager`, enabling prereleases when necessary. Release files belong on the [Releases page](https://github.com/QROST/aholo3d-rhino/releases); if none are available, use the [source build instructions](docs/development.md).
3. Restart Rhino and run `Aholo3DPanel`.

For local Yak files and upgrade details, see [installation](docs/installation.md).

## First task

1. Open **Settings**, choose International or China, and validate your API Key.
2. In **Create**, choose an operation and enter its inputs. Invalid values are shown before submission.
3. Generate once, then follow the task in **Library**.
4. Use the primary result button to download, place geometry or open an image. More result actions contains secondary tools.

**OAuth sign-in is optional and does not replace the API Key.** Signing in does not by itself grant generation access or credits. Cloud services may charge credits; local GS viewing and supported local mesh tools do not require a cloud account.

For restart recovery of input text and options, save the Rhino document and enable **Remember creation drafts for this document** in Settings. URLs and file selections are not restored. [Data and privacy](docs/privacy.md).

## Important boundaries

- GS viewing is experimental. Native cloud-image depth compositing, Raytraced integration, print/layout capture and arbitrary transparent-object overlap are not supported.
- USD/USDZ support is a static subset, not a general USD editor. A viewer that opens mesh USDZ may not support Gaussian USDZ.
- Rigging/animation results can be downloaded; animation playback in Rhino is not implemented.
- Service generation limits are distinct from Rhino import. The plugin adds no artificial total vertex, face or Gaussian-count limit.
- A lost submission reply is not automatically retried. Recover the original task to avoid duplicate charges.

See [supported formats](docs/formats.md), [API coverage](docs/api-capabilities.md) and [troubleshooting](docs/troubleshooting.md).

## Contribute and support

Report reproducible problems through [GitHub Issues](https://github.com/QROST/aholo3d-rhino/issues). Include the plugin version, Rhino version, OS and a small non-sensitive example. Never attach API Keys, tokens or private projects.

[Contributing](CONTRIBUTING.md) · [Security](SECURITY.md) · [Apache-2.0 license](LICENSE) · [Third-party notices](NOTICE)
