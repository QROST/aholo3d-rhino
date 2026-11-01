# Validation record

Maintainer: CuriousArc · qrost. Candidate: 0.1.0-beta.3. Date: 2026-11-01.

## Verified locally

- Release solution build on Apple Silicon macOS with .NET SDK 8.0.129: zero warnings/errors; RhinoCommon 8.32.26160.13001 compile target.
- Offline automated suite: 209 passing tests, zero failures. Coverage includes raw World/RenderCloud/mesh envelopes, lossless task IDs, uncertainty after failed create replies, API/upload credential separation, single/multipart uploads, all supported PLY encodings, malformed/truncated input, more than one million streamed points, GLB/USD coordinate conversion, task-journal recovery and failed downloads.
- Prior beta.1 child-process pipe smoke (not rerun for this GS-only sidecar integration): empty-history response, correlated request IDs, unknown-method error envelope and clean EOF exit; no credential or service methods called.
- New GS suite: 41 passing cases covering strict source fields/SH order/encodings, covariance, camera projection, SH parity and inverse instance transform, smooth alpha and opaque depth before/between/behind layers. The original 29 geometry cases still pass, including a million-point stream.
- New realtime suite: 67 passing cases covering REST envelopes, credential isolation/redaction, stream fragmentation, ordered/coalesced patches, cancellation, shutdown races and explicit failed-cleanup retries. No live service was contacted.
- Portable CPU oracle generated four 257×257 PNGs and verified center values against closed-form alpha/depth calculations. These are numerical reference images, not Rhino screenshots. Both deterministic 3-sigma alpha textures verify byte-for-byte against their generator.
- Batch repair adds 28 projection/sorting/cancellation cases against the unchanged scalar oracle, including full 212,864-item synthetic workloads, plus two opacity-atlas precision/UV cases. The hot projection/sort path allocates no per-Gaussian managed objects after warm-up. The real local 212,864-splat PLY also passed an offline full-data benchmark; these checks do not measure native frame rate.
- A separate managed packet check repeated the colored fixture to 212,864 splats and verified 104 batches, 212,864 faces, 851,456 colored vertices, face order, UV bounds and cancellation. It creates no native Mesh/Material and is not a GPU result.
- Native host adapter compiles against RhinoCommon 8.32 with double-precision quad vertices, deterministic equal-depth ordering, cancellable document reloads, per-view resources and visible proxy fallback. Shared texture alpha, vertex color and deferred rendering order remain unverified on the repaired native path.
- Build script syntax and Python packaging syntax checked. Public README/docs/NOTICE attribution uses CuriousArc and qrost; no developer home paths included.

Yak reports a benign content-name warning because assembly `Aholo.Rhino` differs from package slug `aholo-rhino`; both identities are intentional. Windows PowerShell packaging remains unexecuted.

Package-specific SHA-256 and archive verification receipts are generated beside each candidate in `artifacts/`. Package verification compares every payload to its staged source and validates Yak-normalized manifest fields and checks identity, Rhino target, platform and .NET 8 runtime configuration. Package generation does not install or publish the plugin.

## Not yet verified

Beta.3 native GS color/alpha/depth/SH appearance, navigation, memory plateau, Undo/Redo and save/reopen; Windows execution; Intel Mac execution; authenticated RenderCloud/mesh-ingestion and realtime WebSocket tests; World GS to cloud mesh-ID bridge; visual/camera/scale matching of cloud output. The user separately confirmed beta.2 World generation/download/import but reported white GS and unusable navigation. This repair made no authenticated service calls and published no package.

A Windows-tagged package uses the same platform-neutral managed assemblies; that does not establish Windows compatibility at runtime. The cloud model transform assumes the fast ingestion pipeline preserves glTF units/axes, which its public contract does not explicitly guarantee. Follow [host acceptance](host-acceptance.md) before promoting a release.

## Stage outcome

M0 (plan) and M1 (data/reference math) have local evidence. M2 has a compiled, packaged native-viewport experiment; its host acceptance gate remains open. M3 has a protocol/session library with offline tests; it is not wired to a cloud viewport. M4–M6 (dedicated GPU/performance, coherent realtime cloud viewport, release acceptance) remain pending the documented host and service gates. See [the decision record](decisions/0001-native-gaussian-viewport.md) and [GS manual acceptance](gs-host-acceptance.md).

The beta.2 failure and beta.3 repair are documented in [the incident record](gs-freeze-repair.md).

Local CLI installation, if performed, is recorded separately in `artifacts/` with installed-file hash verification. It does not establish that Rhino loaded or rendered the plugin successfully. No Windows execution, Mac/Windows native occlusion result, production performance number or cloud viewport is claimed by this validation record.
