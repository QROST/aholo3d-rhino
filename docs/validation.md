# Validation record

Maintainer: CuriousArc · qrost. Current local candidate: 0.2.0-alpha.3. Earlier entries below retain their original scope.

## Integrated development candidate alpha.3 — 2026-11-10 UTC

Versioned Release build passed with zero warnings/errors; **784 .NET tests passed**, zero failed/skipped, on Apple Silicon macOS. Tests cover compatible GS references, upload missing-part selection, spatial DSL geometry, cloud history/recovery, mesh candidates/placement policy, mesh PLY/STL classification, real local IPC, realtime cleanup, OAuth callback/credential isolation and PBR/light snapshots. Deterministic GS alpha/opacity assets pass verification.

An independent local OpenUSD check parsed a production-generated scene with RectLight, SphereLight and camera schemas. It verified centered rectangle dimensions, reflected emission direction, millimetre transforms, sphere radius and normalization. This is real local USD parsing, not evidence of RenderCloud ingestion or native Rhino visuals. Official OAuth public discovery was fetched read-only; no real sign-in, private account credentials or charged requests were used.

Packaging verifies both platform-tagged archives against the allowlisted files and records commit/dirty state, SHA-256 and pending host/service acceptance in `artifacts/latest-package-{mac,win}.json`. Those receipts are the authority for whether an archive was produced; source compilation on a Mac does not prove Windows execution. No GUI automation, installation, push or publication is part of this candidate preparation.

The current product is not full end-user parity solely because all HTTP routes exist. See the [alpha.3 parity table](api-capabilities.md#product-parity-at-alpha3) and [manual acceptance checklist](integrated-acceptance.md). Native cloud depth composition, World GS ingestion and progressive LOD retain contract gates; textures/normals/complex materials, additional mesh formats/animation, realtime model/light controls, remembered OAuth and China-region parity remain development work.

## Integrated development candidate — 2026-11-09

The working candidate is 0.2.0-alpha.2. Local Release build passed with zero warnings/errors and **624 .NET tests passed**, with zero failures/skips, on Apple Silicon macOS. Format fixtures include official SPZ encoder/decoder hashes; connection checks use fake credentials/HTTP plus exited-child processes; USD tests include inherited-pipe cancellation. The installed private OpenUSD 0.26.11 runtime and Gaussian schema also passed a read-only readiness check.

macOS and Windows staged payloads each contain 41 allowlisted files, including ZstdSharp in plugin and sidecar directories and required third-party notices. Actual Yak archive receipts are generated separately in `artifacts/` and verified against every staged byte. Windows packaging on a Mac is not Windows execution. No authenticated service calls, Rhino GUI automation, installation, push or publication occurred in this follow-up. Native bilingual UI, task selection, SPZ placement, DPI behavior and multi-instance connection recovery remain pending manual acceptance. Earlier host evidence below stays scoped to its original versions. See [implementation progress](implementation-progress.md) and [integrated acceptance](integrated-acceptance.md).

## Automatic language selection preview

Version 0.1.1-beta.2 adds automatic English/Chinese interface selection to the USD exchange preview. A valid Rhino language takes precedence; system UI language is consulted only when Rhino provides no valid language. Chinese locales use Simplified Chinese; other locales and the fallback use English.

- Localization implementation is commit `2cdb656`. The release changes version metadata and documentation only.
- Rebuilt the versioned Release solution on Apple Silicon macOS: zero warnings/errors. **314 .NET tests passed**, with zero failures or skips; no service credentials were used.
- Language selection, embedded resources and known-message translation are covered by automated tests. Unknown external errors, user text, names, paths, task IDs and stored status codes retain their original content.
- This candidate targets macOS only. Native bilingual layout, the new USD import/export UI, Windows and Intel Mac acceptance remain pending. Prior user-reported Mac acceptance below still applies only to the previously tested workflow.
- No authenticated service call or desktop UI automation is part of release preparation. Package build/archive receipts and local installation receipts remain separate from native acceptance.

## USD exchange preview verification

The 0.1.1-beta.1 preview adds local GS USD/USDZ interchange while retaining the 0.1.0 service and display baseline. New UI behavior requires manual host acceptance.

- Complete Release solution build: zero warnings/errors. **295 .NET tests passed**, zero failures/skips, including the original workflow/provider/realtime suite and new scene/cache/USD reader/writer checks.
- The private OpenUSD 26.11 runtime passed **18 actual import/export tests**, including four formats, official USDZ packaging, full SH0–3, opacity endpoints, names/antialias metadata, units, reflection/shear and large-coordinate rebasing. Run `fixtures/usd-verify.py` with that runtime's Python to reproduce. Expected missing-dependency warnings belong to negative fixtures.
- Production GSRhino → Aholo → GSRhino exchange passed all four extensions (eight conversions). Full instance counts, units, names, AA, world centers/covariance and directional SH colors were checked. The common reader/writer and bridge implementations were compared after namespace/runtime adaptation.
- A private local **212,864-splat** PLY exported to USDZ and imported through Aholo retained the complete count, all SH coefficients and all activated opacity values within float32 tolerance. Maximum local-position component error was **2.384185791015625e-7 source units**; the full offline read/export/read cycle was approximately **23.1 seconds** on this machine. Private model files are not shipped.
- The shared conversion process was checked for mid-conversion cancellation, child-process cleanup, failure preservation of an existing output and successful atomic replacement.
- No authenticated service call or desktop UI automation was performed for this iteration. Windows execution, the new native UI and external viewer bounds/render support remain pending. Previous Mac acceptance below applies to the original workflow, not automatically to this preview.

## User-reported Mac acceptance and version finalization

On 2026-11-02, the user confirmed the beta.5 retest passed and approved finalizing version 0.1.0. The tested baseline is commit `1ae31ea` (0.1.0-beta.5). In context, this accepts the reported startup/loading and original-world GS display regressions on the local Apple Silicon Mac. Earlier user feedback separately confirmed World generation/download/import, red/teal probe and world colors, faster navigation and basic clipping-plane interaction.

Version 0.1.0 changes version metadata and documentation only; the runtime source remains the tested beta.5 implementation. This is user-reported acceptance of that workflow, not a recorded pass for every matrix row, Windows/Intel Mac, repeated cold starts, all document lifecycle operations or cloud rendering. Full `hostAcceptance`/`serviceAcceptance` fields in packaging receipts remain pending for those broader gates.

## Earlier 0.1.0 verification

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


## Delivery refinement after alpha.3 (2026-11-10)

Release compilation: zero warnings/errors; full suite: **802 passed**, zero failed/skipped. New coverage includes nonuniform/sheared/mirrored normal transforms, correct orientation under the glTF basis rotation, finite/cardinality checks, embedded PNG/JPEG bytes, sampler/alpha behavior, content deduplication, four-byte alignment, source-file changes and atomic failure. Malformed PNG palettes and non-sRGB/profile metadata have regression fixtures.

A separate scratch .NET harness called the production GlbWriter and the official [Khronos glTF Validator](https://github.com/KhronosGroup/glTF-Validator) 2.0.0-dev.3.10 independently checked its output: **0 errors, 0 warnings, 0 infos, 0 hints**. No Rhino automation, authenticated service calls, package installation or publication was involved. Native texture orientation, material parity and both-platform interaction remain pending in `integrated-acceptance.md`.
