# Validation record

Maintainer: CuriousArc · qrost. Candidate: 0.1.0-beta.1. Date: 2026-11-01.

## Verified locally

- Release solution build on Apple Silicon macOS with .NET SDK 8.0.129: zero warnings/errors; RhinoCommon 8.32.26160.13001 compile target.
- Offline automated suite: 71 passing tests, zero failures. Coverage includes raw World/RenderCloud/mesh envelopes, lossless task IDs, uncertainty after failed create replies, API/upload credential separation, single/multipart uploads, all supported PLY encodings, malformed/truncated input, more than one million streamed points, GLB/USD coordinate conversion, task-journal recovery and failed downloads.
- Actual child-process pipe smoke: empty-history response, correlated request IDs, unknown-method error envelope and clean EOF exit; no credential or service methods called.
- Build script syntax and Python packaging syntax checked. Public README/docs/NOTICE attribution uses CuriousArc and qrost; no developer home paths included.

Yak reports a benign content-name warning because assembly `Aholo.Rhino` differs from package slug `aholo-rhino`; both identities are intentional. Windows PowerShell packaging remains unexecuted.

Package-specific SHA-256 and archive verification receipts are generated beside each candidate in `artifacts/`. Package verification compares every payload to its staged source and validates Yak-normalized manifest fields and checks identity, Rhino target, platform and .NET 8 runtime configuration. Package generation does not install or publish the plugin.

## Not yet verified

Installation/loading of this new plugin in Rhino; Rhino UI, Undo/Redo, save/reopen and large-file acceptance; Windows execution; Intel Mac execution; authenticated World generation; authenticated RenderCloud/mesh-ingestion tests; visual/camera/scale matching of cloud output. No service credits were consumed and no package was published.

A Windows-tagged package uses the same platform-neutral managed assemblies; that does not establish Windows compatibility at runtime. The cloud model transform assumes the fast ingestion pipeline preserves glTF units/axes, which its public contract does not explicitly guarantee. Follow [host acceptance](host-acceptance.md) before promoting a release.
