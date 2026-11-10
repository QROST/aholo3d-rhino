# Integration implementation ledger

Maintainer: CuriousArc · qrost. Started: 2026-11-08.

This tracks implementation of [the development plan](development-plan.md), not native acceptance or a new public release. Baseline: `a7e8e73`, 314 passing tests; source checkout `aholo3d-rhino`. Yak `0.1.1-beta.2` was unlisted at the user's request. Keep the existing package, plug-in, command, credential and saved-document identities.

## Delivery sequence

1. **M0 — validation foundation:** macOS/Windows build, test and staged-payload CI; compiled PE metadata checks without loading RhinoCommon; ignore local agent instruction files.
2. **M1/M2 — application contracts:** typed service identities, compatible journal reading, pagination, operation ownership and shared UI/command services. Refresh/download must never create paid work. Explicit render continuation must retain submission barriers.
3. **API coverage:** verify every current public World, Lux3D, Asset, RenderCloud and additional documented service operation; record exact implemented and unimplemented capabilities. Account login is distinct from API authorization. Never expose a stub as a working feature.
4. **M3 — integrated workflow:** compact Create / Library / Render navigation, Settings menu, local GS formats and robust display; Lux3D object generation and native placement with no artificial geometry count ceiling.
5. **M4/M5 — scene workflows:** reusable operation cancellation, document-safe placement, generated mesh Block snapshots for offline cloud rendering, explicit result locations and recovery.
6. **M6 — release evidence:** package and install checks, bilingual docs, manual host and authorized paid-service acceptance. Real-time cloud mixed-depth compositing stays research until its required depth contract exists.

## Current evidence

M0 local verification passed: Release build with zero warnings/errors; 317 tests passed; both mac/win staged payloads verified (32 allowlisted files each) on Apple Silicon macOS. The CI definitions are not evidence that GitHub jobs ran. Staged payload checks validate allowlisted built files, sidecar runtime metadata and copied bytes; they do not produce a Yak archive or prove native host behavior. A local Yak archive is checked separately before installation/release.

## M1 — journal and action contracts

Implemented task-scoped exclusive locks (network waits no longer hold the all-task lock), non-mutating recovery views that retain the ownership probe through reading, legacy backup before adding schema metadata, unknown-field preservation and refusal to overwrite unsupported schema versions. Local pagination/filtering and full-history World lookup are separate. Remote identifiers remain service-specific strings.

`Refresh` and `Download` only query existing jobs; processed meshes stop at `MeshReady`. `ContinueRender` is a separate intent with the existing persisted submission barrier. An unknown result cannot be replayed. An explicit continuation action is reachable in the current panel while the shared application coordinator is implemented next. Tests include >100-task restoration, live-owner visibility, concurrent independent submissions, old/unknown journals and no paid render from downloads.

Validation: Release build succeeded with zero warnings/errors; 328 tests passed on Apple Silicon macOS. Third-party source review found an ownership-probe/read race; the probe now remains held through the read. This does not claim two-Rhino-process native acceptance. UI reorganization, shared operation cancellation and complete service coverage remain in progress.

## API contract expansion

Verified the public service index and all five current OpenAPI schemas. Original World/RenderCloud/Asset snapshots are unchanged; Lux3D and SpatialUnderstanding snapshots are now pinned with hashes. Added all 16 previously missing endpoint routes, an embedded-schema create registry, cross-field validation, exact Lux int64 IDs and ordered multi-output records. World details retain cover/scene/timestamps/LOD URLs; mesh ingestion exposes fastUpload and all mesh IDs.

Release build passed with zero warnings/errors; 363 tests passed, including 31 initial API cases plus four review regressions/metadata cases. The 35-file macOS staged payload passes. Third-party review identified JSON escape equality and malformed Lux list-row handling; both are fixed and tested. API operation routing is complete at the provider level; Rhino integration and remaining optional upload controls are separately tracked in [the coverage matrix](api-capabilities.md). No real service or host acceptance is implied.

## Review and execution boundaries

The user authorized this development batch's bounded third-party analysis, code drafts, test design and reviews using necessary non-sensitive source/documentation/diffs. Credentials, task data, personal paths and full conversation are excluded. The parent applies changes and executes validation. At most two workers and 4096 output tokens per task; uncertain outcomes are inspected, not retried automatically.

The user controls Rhino for visual/native acceptance. No desktop control, real charged service submissions, push or publication is implied by local implementation. No changes to the independent GSRhino project are required.

## M2 — shared application session

Panels now share one sidecar, connection state, background task polling and operation registry per Rhino process. Local history reads bypass the sidecar queue. Closing a panel detaches its observers without disposing the shared transport; document closure cancels its local operations. GS import/export commands use the same registry, with `AholoCancel` available independently of the panel. A successful native commit is not reported as cancelled by a late cancellation.

Automatic offline-render continuation exists only for the original in-memory render intent. Pause, connection reset and restart remove that authority; recovered MeshReady tasks require the explicit continuation action. No cancellation claim is made for remote jobs. Tests cover continuation once, restart, pause/resume, transport reset, offline history, duplicate scope ownership, independent document scopes and closed observers.

Validation: Release build passed with zero warnings/errors; 370 tests passed. The remote session review timed out; its status was checked and it was not retried. Parent review and the local tests above provide this milestone's evidence. Native panel lifecycle acceptance and cross-process credential-cache invalidation remain pending.

## M3 — integrated creation, Library and native mesh bridge

All 13 registered create operations have schema-backed Eto forms, file/URL inputs, optional advanced controls and retained per-operation drafts. Generic workflows snapshot every local input before uploads, persist submission barriers, record typed remote IDs, query original tasks and decode ordered outputs. World reconstruction and Spatial DSL downloads are connected. Signed URLs never enter durable journals. Missing remote outputs retain downloaded assets; immutable hash-named downloads protect old committed bytes during re-download failures.

Create / Library / Render replaces the earlier service tabs. Settings holds credentials, selected-service validation, polling and placement defaults. The local Library supports search, type filters, pagination, exact restore selection, per-output download and visible result folders. Closing a panel detaches UI callbacks while its session survives. GLB placement adapts the existing validated snapshot/native import/limited repair/Block wrapping/Undo flow, with independent Aholo markers and no geometry quota. Nested ordinary Blocks participate in offline render capture; GS bounds are rejected.

Validation at this milestone: versioned Release build with zero warnings/errors and 475 passing .NET tests; macOS and Windows staged payloads each verified 37 allowlisted files on this Mac. Windows execution is still unverified. Third-party Sol review findings for draft loss, disposed views, settings busy handling, task reveal, dropped local artifacts, mutable re-downloads and weak upload envelopes were addressed. The suggested arbitrary transfer caps were not adopted: production calls already have a 15-minute deadline and cancellation; no geometry/file quota is introduced. Disk-full failures retain original bytes; automatic orphan cleanup remains future work. The separate mesh/render bridge review also timed out, was status-checked and not retried; parent review corrected base-color fallback and unit-aware placement defaults. No native layout/mesh/Block-render or live-service acceptance is claimed.

Next acceptance and work after M3: native bilingual UI and GLB placement/Undo; selected Block render parity; optional Asset parameters/resume and remote history recovery; realtime cloud preview/GS ingestion research. Later sections record the completed GS and credential recovery adaptations. See the integrated acceptance checklist and API matrix for exact boundaries.

## M3b — complete compressed GS input

Added local SPLAT and SPZ v1–4 decoding, preserving all input Gaussians, SH0–3, opacity endpoints and source coordinate transforms. World SPZ results can be placed directly via the shared scene import path; all parts carry the original task identity, so repeated placement focuses existing instances. Source archival, cancellation, units and one Undo are retained. Unsupported higher SH degrees fail explicitly. The existing USD exporter can export these imported GS parts; SPLAT/SPZ export is not claimed. Antialias compensation and safe-orbit metadata are disclosed but not implemented.

Release build passed with zero warnings/errors; 74 format tests passed, including hash-pinned fixtures from the official SPZ encoder/decoder. Zstandard dependency and license notices are allowlisted in both plugin and sidecar packages. No live World download or native SPZ visual acceptance was run.

## M3c — stable native sampling and cache recovery

Adapted GSRhino's native window sampling policy into the actual Rhino draw callbacks. Fractional DPI representation changes retain the accepted sampling grid; real window resizing changes frame identity, and actual camera/frustum changes still invalidate it. Empty placements do not enqueue zero-input frames. Display states distinguish missing/failed resources, no projection and partially ready visible instances. All content-addressed GS writers now use one cancellable cross-process publication protocol. A corrupt cache can recover from a separate, hash-verified complete PLY reference; previous corrupt bytes are backed up. If the recorded reference points to the same corrupt file, re-import is still required. Source archives and logs now honor the configured task data directory.

Release build passed with zero warnings/errors. All 88 relevant display/cache/localization tests passed, including concurrent repairs, lock cancellation, retained originals, rounded DPI and genuine camera changes. A native read-only review found no additional defects; it did not run Rhino. Manual viewport/resize/close-document acceptance remains pending.

## M4 — connection invalidation and explicit recovery

Added a non-secret credential epoch under the fixed OS credential scope (independent of custom task directories). Connect/disconnect writes a Changing state before modifying secure storage; an interrupted change requires explicit reconnect. Every authenticated RPC carries its expected epoch, and every service HTTP request checks it under the shared lock through response headers. Another instance's changed Key cannot silently replace the identity of an existing workflow. Already-admitted requests can finish; there is no promise to cancel remote work. Lock waits are cancellable and bounded at 30 seconds.

Idle sidecar exit now revokes the current call before restart. Session generations prevent late Render/Connect/Continue responses from restoring old authority; manual and automatic queued continuations share revocation. Settings exposes Restore saved connection and connection errors. Requests stopped before sending are distinguished from unknown submissions; the original processed mesh remains MeshReady for explicit continuation, with automatic continuation removed on error.

Release build passed with zero warnings/errors; 59 application/credential/workflow tests passed. Fixtures use fake secure storage and HTTP handlers, plus an actual exited dotnet child. Native read-only review found three recovery/ordering issues; all were fixed and regression-tested. No real Keychain/DPAPI mutation, live service request or two-Rhino-instance acceptance was performed.

## USD runtime readiness and candidate 0.2.0-alpha.2

Settings now offers an offline USD readiness check with path/version feedback and a quoted setup command. It never installs dependencies automatically. USD process cancellation/timeout includes stdout/stderr collection, including a launcher that exits while descendants retain its pipes. Custom launcher descendants may survive after their parent exits; the plugin bounds its own wait rather than claiming it can always terminate them. The existing AHOLO_USD_PYTHON override and Aholo-Rhino runtime location are unchanged.

The 30 USD-readiness/localization tests passed, including actual Unix launcher/pipe cancellation and timeout fixtures. Initial LocalLLM catalog/status checks were unavailable, so two native read-only reviews supplied the GS inventory and connection recovery findings. After the gateway recovered, two bounded remote/gpt-6.1-sol reviews checked credential/continuation and panel behavior. The parent verified and fixed the pre-dispatch cancellation window, preserved task/result selection during background completion, and added cancellation inside Settings. A suggested global block on credential changes was not adopted: the shared coordinator already invalidates stale authority, while explicit disconnect remains supported. Workers supplied candidate findings; only the parent changed files and executed checks.

## M5a — compatible GS documents and import feedback

The Rhino host now resolves versioned GS references across producer namespaces for display, cache loading, idle pruning and USD export. It retains producer metadata and native instance transforms. Export-all rejects malformed/unsupported marked references instead of silently producing a partial scene. If two instances refer to identical bytes at different local paths, a failed path no longer suppresses trying a surviving reference; each path is attempted once until explicit reload. Hash verification and existing local-path restrictions remain mandatory.

Settings exposes automatic source units versus explicit 1:1 import. GS import/export reports preparation stages; late progress from an old operation cannot replace a later operation's status. Diagnostics identifies compatible references and local asset directories. Pure reference/cache tests cover independent producer metadata, immutable verified assets and malformed marked-object classification; native display, hidden-object lifetime, Undo and export acceptance remain manual.

Validation: Release build passed with zero warnings/errors; 68 reference/cache, localization and display-status tests passed. Remote Sol review identified the shared-hash alternate-path issue; the parent corrected it and reviewed the host lifecycle. Native host acceptance is pending.

## M5b — server-directed multipart resume

Uploads now honor inclusive missing-part ranges returned by multipart initialization, merge overlaps without expanding an unbounded part list, and expose documented metadata/prefix/filename options in advanced Settings. Single-file requests reject unsupported custom fields explicitly. Completion polling has a cancellable two-minute deadline including HTTP waits; failed/uncertain parts are not blindly resent. A subsequent explicit upload can use server-directed resume, but persistence across tokens is not promised by the official contract.

Upload origin validation also checks port, user info and fragments. Malformed protocol values become handled provider failures. Release build passed with zero warnings/errors; 97 upload/provider/workflow/localization tests passed, including lost replies, malformed values, range deduplication, exact part bytes, credential boundaries and cancellation. Remote Sol design and implementation review contributed the range rules and malformed-response fix; parent verified and executed all changes. No real file was uploaded.

## M5c — Spatial DSL to editable Rhino geometry

Spatial results now place native wall surfaces, door/window outline curves and oriented box Breps in one editable block. Parsing follows the official DSL, metre-based Z-up coordinates and full box dimensions; angles are radians as confirmed by the official SpatialLM implementation. Forward wall references and classes containing spaces/commas are supported; unknown types are skipped with diagnostics. No alignment transform is applied twice, no wall thickness is invented, and automatic boolean openings are not claimed. Source IDs/classes/hash are retained in attributes.

Placement hashes and parses in the background, validates geometry before document mutation, checks document identity/units, uses one Undo, and rolls back its new definition/layer if commit is cancelled or fails. Existing results are located without duplicate insertion. Release build passed with zero warnings/errors; 35 parser/geometry/localization tests passed. Parent corrected the remote draft's invalid sealed inheritance and imprecise reference line numbers; remote implementation review also led to cancellation-aware hashing and unknown-type handling. Rhino-native wall/outline/box and Undo acceptance remains pending.
