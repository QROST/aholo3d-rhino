# Validation record

Maintainer: CuriousArc · qrost. Current local candidate: 0.2.0-alpha.8. Earlier entries below retain their original scope.

## Integrated candidate alpha.8 — 2026-11-11 UTC

Regional runtime source `097b2b7` implements International/China gateways, regional API Key and OAuth storage, Settings selection, task/cleanup provenance and independent account-attempt cancellation. Release build: zero warnings/errors. Full offline suite: **982 passed, zero failures/skips**. Two synthetic local-loopback PKCE login paths, an actual private control pipe under a held foreground gate, separate-process OAuth storage fixtures and offline regional contract comparison all pass. No real account/browser, Keychain/DPAPI, Rhino UI, Windows runtime or paid-service acceptance is claimed. Package receipts separately bind clean source, platform payload and SHA-256; no installation or publication is part of this checkpoint.

## Integrated candidate alpha.7 — 2026-11-11 UTC

Adds remembered international OAuth sessions with independent OS-protected storage, local restoration, explicit refresh and cross-instance sign-out. Runtime source `6a28504` passes **946 offline tests**, zero failures/skips, with a clean Release build; an actual separate-process synthetic-store fixture also passes save/sign-out ordering and stale refresh rejection. Public China contracts are now snapshotted and structurally verified, but China runtime routing is not enabled. Both staged payloads contain 58 files. Native OS credential stores, browser authorization, Rhino interaction and live services remain pending; archive checks bind the exact clean metadata commit separately.

## Integrated candidate alpha.6 — 2026-11-11 UTC

Includes task-linked USD placement with pinned result identity/receipt-first recovery, the local `AholoImportMesh` command, explicit placement modes and candidate cancellation. Runtime source `8f5963c` passes **921 offline tests**, zero failures/skips, with zero Release warnings/errors. The earlier real OpenUSD decoder/writer checks retain their scope; their parsers were not changed in this checkpoint. Package/metadata checks run separately from the runtime tests and record exact source/archive identity. Native command timing, physical placement, material/texture appearance, Windows execution and authorized service acceptance remain pending.

## Integrated candidate alpha.5 — 2026-11-11 UTC

This candidate adds local tessellated mesh USD/USDA/USDC/USDZ export and strict static mesh import to candidate placement. Runtime source `d29a8de` passes **878 offline tests**, zero failures/skips, with a clean Release build and actual OpenUSD four-format exchange/negative-fixture checks. Mac/Windows staging contains 48 files; final archive hashes and clean source identity are recorded separately by the package tool. Native Rhino material/embedded-image behavior, worker execution, Windows and real service acceptance remain pending. This checkpoint makes no complete-parity, installation or publication claim.

## Integrated candidate alpha.4 — 2026-11-11 UTC

This candidate includes smooth/custom normals, self-contained direct base-color PNG/JPEG export, ordered realtime model/environment/light-intensity controls, actual OpenUSD camera/scene patch verification and core 3MF placement. Runtime code at `a85b04e` passes **853 offline tests**, with zero failures/skips and a clean Release build. Official glTF validation and real OpenUSD parsing are recorded below. Package metadata and final archive checks are performed separately after this checkpoint. No new native Rhino or authenticated service acceptance is implied, and the earlier alpha.3 packages do not contain these changes.

The detailed remaining format, renderer, account, native-host and service gates are tracked in [capabilities](api-capabilities.md), [mesh formats](mesh-formats.md) and [integrated acceptance](integrated-acceptance.md). Candidate packaging is local; installation, pushing and publication are separate actions.

## Integrated development candidate alpha.3 — 2026-11-10 UTC

Versioned Release build passed with zero warnings/errors; **784 .NET tests passed**, zero failed/skipped, on Apple Silicon macOS. Tests cover compatible GS references, upload missing-part selection, spatial DSL geometry, cloud history/recovery, mesh candidates/placement policy, mesh PLY/STL classification, real local IPC, realtime cleanup, OAuth callback/credential isolation and PBR/light snapshots. Deterministic GS alpha/opacity assets pass verification.

An independent local OpenUSD check parsed a production-generated scene with RectLight, SphereLight and camera schemas. It verified centered rectangle dimensions, reflected emission direction, millimetre transforms, sphere radius and normalization. This is real local USD parsing, not evidence of RenderCloud ingestion or native Rhino visuals. Official OAuth public discovery was fetched read-only; no real sign-in, private account credentials or charged requests were used.

Packaging verifies both platform-tagged archives against the allowlisted files and records commit/dirty state, SHA-256 and pending host/service acceptance in `artifacts/latest-package-{mac,win}.json`. Those receipts are the authority for whether an archive was produced; source compilation on a Mac does not prove Windows execution. No GUI automation, installation, push or publication is part of this candidate preparation.

The current product is not full end-user parity solely because all HTTP routes exist. See the [current parity table](api-capabilities.md#product-parity-at-alpha8) and [manual acceptance checklist](integrated-acceptance.md). At the alpha.3 checkpoint, native cloud depth composition, World GS ingestion and progressive LOD retained contract gates; texture/normal export, additional formats, realtime controls and account parity still needed development. Subsequent implementation and current remaining scope are recorded above and below.

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


## Realtime scene refinement (2026-11-11 UTC)

Release compilation has zero warnings/errors. Full suite: **810 passed**, zero failed/skipped. Queue/IPC tests hold acknowledgements to verify camera–scene–camera ordering, bounded coalescing, duplicate revisions, explicit epoch rejection, cleanup after lost replies and no automatic replay. Source snapshot tests verify mutable caller arrays cannot change the start scene.

Actual OpenUSD parsing found and drove repair of invalid same-line nested camera `over` nodes. After repair, **124 affected realtime/geometry tests passed**, and the committed integration check parsed/composed production camera, scene and Gaussian patches, verifying final transforms, environment, lighting and preserved camera independently of mocked HTTP. Run the Release build, then `fixtures/render-verify.py` with the configured private OpenUSD interpreter. The script invokes `Aholo.Reference --render-fixtures` in a temporary directory; it performs no network or Rhino calls. Native/live and Windows acceptance remain pending; alpha.3 archives predate these source changes.


## Core 3MF placement (2026-11-11 UTC)

Release solution build: zero warnings/errors. Full suite: **853 passed**, zero failed/skipped. New coverage includes native-format selection, immutable snapshot/hash, core components/base materials, primary/secondary relationships, valid parent-relative and UTF-8 package paths, duplicates, missing/external targets, extensions, DTDs, malformed archives and preflight cancellation. The committed deterministic `fixtures/mesh-core.3mf` and generator provide a native acceptance sample; its bytes and preflight pass, not its native rendering.

Remote Sol supplied the package/format boundary review; a later implementation review timed out and was not retried. Independent local review reproduced a UTF-8 continuation-byte rejection, which was fixed and regression tested. Both Mac and Windows staging checks passed for 46 allowlisted files, including the new OPC assemblies/license and fixture. This was a Mac build/staging check, not Windows native execution. No installation, publication, authenticated request or desktop automation was performed.


## Local mesh USD export after alpha.4 (2026-11-11 UTC)

Release build: zero warnings/errors. **860 offline .NET tests passed**, zero failures/skips, including mesh transport, invalid geometry/asset paths, source-image deduplication, large double origins and public export cancellation with inherited process pipes. Actual OpenUSD 26.11 checked all four production formats after moving outputs and deleting the original location: coordinates, normals/winding, UVs, material/alpha graphs, texture bytes, USDZ alignment and enclosed dependencies. Real export replacement failure cleans only the new companion directory, and successful replacement preserves prior texture files. Run `fixtures/mesh-usd-verify.py` with the configured USD interpreter to repeat.

No GUI automation, native Rhino export, authenticated service call, installation or publication occurred. Mac/Windows native capture and independent renderer appearance remain pending. These source changes are newer than the alpha.4 package receipts.


## Local mesh USD import after alpha.4 (2026-11-11 UTC)

Final Release build: zero warnings/errors. **878 offline .NET tests passed**, zero failed/skipped. Protocol tests cover topology/attribute cardinality, image/hash retention, Y/Z physical conversion, double transforms, malformed paths, symlinks, non-finite values, cancellation admission and the extreme-anisotropy rejection boundary. Actual OpenUSD 26.11 checks pass for all four production-export formats through both the Python decoder and public C# reader; external/absolute images, sublayers, privately frozen source bytes, normals/UV seams, five interpolations, quads, native instances and 15 unsupported/malformed cases are covered. `fixtures/mesh-usd-import-verify.py` reproduces these checks without Rhino or network calls.

Both platform staging checks passed for **48 allowlisted files**. Two completed remote Sol reviews supplied candidates, checked against actual APIs and tests; one earlier batch did not reach inference after the automatic permission check timed out. Fixed RhinoCommon compilation and read-only SDK inspection do not establish native import, embedded-image persistence, worker thread behavior, UI responsiveness or Windows execution. Native/live service acceptance remains pending; no installation, push or publication occurred.


## Task-linked mesh USD workflow (2026-11-11 UTC)

**910 offline tests passed**, zero failed/skipped; Release build has zero warnings/errors. The 32 new cases cover all eight mesh format routes, World GS exclusion, pinned identity/hash/slot after reordered downloads, offline caches, original-account epochs, cancellation, receipt-before-acquisition and exactly-once prepared-asset release on success/failure/duplicate/deferral. Fixed-SDK compilation also checks UI-thread host integration; it does not establish native USD visuals, panel event timing or Mac/Windows acceptance. No authenticated request, installation, push or publication was performed.


## Local mesh command and placement modes (2026-11-11 UTC)

**921 offline .NET tests passed**, zero failed/skipped; Release compilation has zero warnings/errors. Eleven additional transform cases cover captured tilted planes, 90-degree rotation, default anchoring, original-size planar geometry, preserved scene coordinates, ignored inactive numeric fields, invalid bounds/planes, tiny positive heights and representable results with extreme coordinate intermediates. Fixed RhinoCommon compilation validates the new command API and cancellation wiring.

Remote `gpt-6.1-sol` supplied the mode design, regression candidates and implementation reviews; primary code inspection and local tests verified/adapted them. A local read-only SDK/lifecycle review found missing candidate-accept cancellation, now wired through native commit guards. These are not native event/visual tests: interactive/scripted command timing, panel layout, canceled native import rollback, physical unit placement, texture persistence and both-platform behavior still require the explicit host checklist. No live account request, desktop automation, installation or publication occurred.


## Remembered OAuth accounts after alpha.6 (2026-11-11 UTC)

Release build has zero warnings/errors; **946 offline .NET tests passed**, zero failed/skipped. Twenty-five new cases cover opt-in, process restart, no automatic network restoration/refresh, token rotation, corrupt/missing records, incomplete writes/deletes, safe process-memory fallback, API Key isolation, duplicate refresh admission and cancellation/sign-out races.

`python3 fixtures/oauth-coordinator-verify.py` additionally passed actual separate .NET process checks: sign-out waits for an in-flight secure-save fixture, an old refresh cannot commit after another process signs out, API Key scope is preserved and coordination files contain no fixture token. The harness uses disposable synthetic files, not real OS credentials. Three remote Sol tasks completed; primary source inspection and local tests verified their candidates. No native Keychain/DPAPI interaction, browser authorization, paid cloud operation, Rhino UI automation, installation or publication was performed. The alpha.6 archives predate this source change.


## China regional contract evidence (2026-11-11 UTC)

Read-only public fetches verified five official service schemas plus resource/authorization discovery. SHA-256 inventories preserve the original bytes. `scripts/check-region-contracts.py` passes the 30-path normalized request/response/security comparison, gateway/OUS checks and PKCE discovery checks. Documentation/examples/SDK operation names/server declarations are excluded from the structural comparison and the `/global` prefix is normalized; this does not prove regional permissions, quotas or server behavior. Both package staging checks pass with 58 allowlisted files. Region switching and regional credentials/tasks are not implemented by this documentation commit.


## Combined GS + mesh export after alpha.8 (2026-11-11 UTC)

Release build: zero warnings/errors. **988 offline .NET tests passed**, zero failed/skipped. New tests cover mixed/single-type transports, full input preservation, invalid second-part geometry, empty/pre-cancelled writes, and cancellation during a converter wait while a child retains output pipes. The latter is a real local process fixture with a synthetic interpreter, not a Rhino cancellation test. Previous destinations and workspace cleanup are checked.

Actual OpenUSD 26.11 independently verified **24 production scene files** across USD/USDA/USDC/USDZ, Y/Z axes, millimetre/foot units and mixed/GS-only/mesh-only scenes. Every SH0–3 coefficient, mixed-degree padding, orientation/scale/opacity, double placement, large-origin mesh detail, normals, UV/material/alpha, fixed source image hash, relocated dependencies and USDZ alignment passed. Real final-rename failure cleans the new companion bundle; replacement retains older texture bytes. Existing 18 GS checks, four-format mesh export and four-format mesh import with 15 strict rejection cases also passed after the author refactor.

A completed remote/gpt-6.1-sol design review informed the shared stage architecture. Local independent reviews found the irrelevant-parent-material issue, now fixed with lazy inheritance; the primary inspected changes and ran all reported checks. The first new SH oracle used a wrong assumption about the in-memory coefficient layout; it was corrected against the actual public data model and the entire 24-file check rerun. No native capture/render result is inferred from those numeric tests. Nested block capture, native material inheritance, document-close events and Mac/Windows visual acceptance remain pending. No installation, authenticated service call, push or publication occurred.


## Combined GS + mesh import after alpha.9 (2026-11-11 UTC)

Release build: zero warnings/errors. **1003 offline .NET tests passed**, zero failed/skipped. New coverage checks complete-scene manifests, consistent source hashes/units/axes, missing or unexpected child payloads, empty scenes and cancellation while a child process retains output pipes.

Actual OpenUSD 26.11 checks passed for **24 public-reader round trips** across four formats and both geometry types. They verify every SH coefficient, opacity/orientation/scale, physical placement, mesh detail, UV/material/alpha and original bitmap bytes. A single frozen dependency snapshot remains usable after the original root and texture files are deleted. Native USD instance placements, the packaged 2-metre mixed demo and **14 whole-scene rejection cases** pass; a failure after writing one geometry payload cannot publish a complete-scene manifest. Existing **19 GS checks** and four-format mesh import/export with 15 mesh rejection cases also pass. Run `fixtures/scene-usd-import-verify.py` with the configured private USD interpreter after the Release build.

Two independent local read-only reviews informed and rechecked rollback-content signatures, cancellation and Undo ownership. The primary inspected the changes, implemented repairs and ran the reported checks. Remote model/status discovery found only 135 daily output tokens available; no remote inference was attempted for this increment and no quota was changed. Compilation and transport checks do not prove native material/resource rollback, viewport visibility, one Undo/Redo, embedded-image persistence or Windows execution. These remain explicit manual host checks. No desktop automation, authenticated service call, installation, push or publication occurred.
