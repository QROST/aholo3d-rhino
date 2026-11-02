# Validation record

Maintainer: CuriousArc · qrost. Version: 0.1.0. Date: 2026-11-02.

## User-reported Mac acceptance and version finalization

On 2026-11-02, the user confirmed the beta.5 retest passed and approved finalizing version 0.1.0. The tested baseline is commit `1ae31ea` (0.1.0-beta.5). In context, this accepts the reported startup/loading and original-world GS display regressions on the local Apple Silicon Mac. Earlier user feedback separately confirmed World generation/download/import, red/teal probe and world colors, faster navigation and basic clipping-plane interaction.

Version 0.1.0 changes version metadata and documentation only; the runtime source remains the tested beta.5 implementation. This is user-reported acceptance of that workflow, not a recorded pass for every matrix row, Windows/Intel Mac, repeated cold starts, all document lifecycle operations or cloud rendering. Full `hostAcceptance`/`serviceAcceptance` fields in packaging receipts remain pending for those broader gates.

## Verified locally

- Release solution build on Apple Silicon macOS with .NET SDK 8.0.129: zero warnings/errors; RhinoCommon 8.32.26160.13001 compile target.
- Offline automated suite: 244 passing tests, zero failures. Coverage includes raw World/RenderCloud/mesh envelopes, lossless task IDs, uncertainty after failed create replies, API/upload credential separation, single/multipart uploads, all supported PLY encodings, malformed/truncated input, more than one million streamed points, GLB/USD coordinate conversion, task-journal recovery and failed downloads.
- Prior beta.1 child-process pipe smoke (not rerun for this GS-only sidecar integration): empty-history response, correlated request IDs, unknown-method error envelope and clean EOF exit; no credential or service methods called.
- New GS suite: 41 passing cases covering strict source fields/SH order/encodings, covariance, camera projection, SH parity and inverse instance transform, smooth alpha and opaque depth before/between/behind layers. The original 29 geometry cases still pass, including a million-point stream.
- New realtime suite: 67 passing cases covering REST envelopes, credential isolation/redaction, stream fragmentation, ordered/coalesced patches, cancellation, shutdown races and explicit failed-cleanup retries. No live service was contacted.
- Portable CPU oracle generated four 257×257 PNGs and verified center values against closed-form alpha/depth calculations. These are numerical reference images, not Rhino screenshots. Both deterministic 3-sigma alpha textures verify byte-for-byte against their generator.
- Batch repair adds 28 projection/sorting/cancellation cases against the unchanged scalar oracle, including full 212,864-item synthetic workloads, plus two opacity-atlas precision/UV cases. The hot projection/sort path allocates no per-Gaussian managed objects after warm-up. The real local 212,864-splat PLY also passed an offline full-data benchmark; these checks do not measure native frame rate.
- Display-stability repair adds 12 regression cases: equivalent logical/physical DPI, half-pixel intrinsics, depth-range union/contraction, monotonic floating-point noise, real camera changes, large-coordinate moves and invalid input. A scheduler simulation completes all 104 upload batches across 256 alternating observations with only one depth expansion. This validates the reusable policy, not Rhino callback scheduling.
- A separate managed packet check repeated the colored fixture to 212,864 splats and verified 104 batches, 212,864 faces, 851,456 colored vertices, face order, UV bounds and cancellation. It creates no native Mesh/Material and is not a GPU result.
- Native host adapter compiles against RhinoCommon 8.32 with double-precision quad vertices, deterministic equal-depth ordering, cancellable document reloads, per-view resources and visible proxy fallback. The user subsequently confirmed beta.3 probe/world colors and improved navigation on Mac. Detailed alpha/depth, ordering and other backends remain unverified.
- Startup repair adds 23 tests for mode reuse, missing/stale IDs, required settings, failed update/readback and retry without duplicate creation. These test the exact registration policy via a fake store; the user subsequently passed the ordinary startup retest. Timer/disposal fault injection and a systematic persistence matrix remain unverified.
- Build script syntax and Python packaging syntax checked. Public README/docs/NOTICE attribution uses CuriousArc and qrost; no developer home paths included.

Yak reports a benign content-name warning because assembly `Aholo.Rhino` differs from package slug `aholo-rhino`; both identities are intentional. Windows PowerShell packaging remains unexecuted.

Package-specific SHA-256 and archive verification receipts are generated beside each candidate in `artifacts/`. Package verification compares every payload to its staged source and validates Yak-normalized manifest fields and checks identity, Rhino target, platform and .NET 8 runtime configuration. Package generation does not install or publish the plugin.

## Not yet verified

Detailed native alpha/depth/SH, memory plateau, Undo/Redo and save/reopen; Windows execution; Intel Mac execution; authenticated RenderCloud/mesh-ingestion and realtime WebSocket tests; World GS to cloud mesh-ID bridge; visual/camera/scale matching of cloud output. The user confirmed World generation/download/import and later beta.3 colored GS with substantially improved navigation and basic clipping-plane interaction. Beta.4 had a display-mode registration failure; the user confirmed the subsequent beta.5 retest passed. Finalization made no authenticated service calls and published no package.

A Windows-tagged package uses the same platform-neutral managed assemblies; that does not establish Windows compatibility at runtime. The cloud model transform assumes the fast ingestion pipeline preserves glTF units/axes, which its public contract does not explicitly guarantee. Follow [host acceptance](host-acceptance.md) before promoting a release.

## Stage outcome

M0 (plan) and M1 (data/reference math) have local evidence. M2 has a native-viewport experiment with the local Mac startup/display regression retest accepted by the user; the detailed multi-platform acceptance matrix remains open. M3 has a protocol/session library with offline tests; it is not wired to a cloud viewport. M4–M6 (dedicated GPU/performance, coherent realtime cloud viewport, release acceptance) remain pending the documented host and service gates. See [the decision record](decisions/0001-native-gaussian-viewport.md) and [GS manual acceptance](gs-host-acceptance.md).

The beta.2 failure and beta.3 repair are documented in [the incident record](gs-freeze-repair.md); beta.4 addresses the subsequent [display instability](gs-visibility-stability.md).

Local CLI installation, if performed, is recorded separately in `artifacts/` with installed-file hash verification. It does not establish that Rhino loaded or rendered the plugin successfully. This record makes no claim of Windows execution, completion of the detailed native occlusion matrix, a measured production frame rate or a cloud viewport.
