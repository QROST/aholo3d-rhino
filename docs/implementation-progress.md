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

## M5d — cloud history and original-task recovery

Library now browses paginated World/Lux3D/Spatial history and restores existing World, Lux3D, Spatial or render IDs. Recovery queries only; it never resubmits an uncertain create or grants render-continuation authority. Lux recovery requires choosing the original tool because the public history response omits its operation type. Identity matching covers the complete local journal by connection/region/service/remote ID, retaining downloads and refusing conflicting operation/schema records. Cloud rows omit signed URLs.

World LOD metadata can now be downloaded and retained as an opaque artifact. No undocumented tile schema or progressive-loading contract is inferred. The modal uses the parent cancellation lifetime; failed page loads keep the last successful page, and changing pages/filters clears the old selected ID. Nested operation scopes do not hold a gate across work; late successful restoration only notifies the shared Library.

Release build passed with zero warnings/errors; 71 cloud recovery/journal/application/workflow/localization tests passed. Remote Sol implementation review identified stale selection and failed-page cursor problems, both corrected by the parent. The earlier remote recovery design task timed out, was status-checked and was not retried. Native modal and service acceptance remain pending.

## M5e — mesh candidates and captured placement

Library now previews up to three independent static GLB candidates, including local files, through cached native shaded meshes. Placement uses the current or picked construction plane, point, positive document-unit height and degree rotation; edits update a display transform without repeatedly importing the file. Preview does not change the document. Acceptance keeps the native material/import audit and one Block/Undo. Visible-layer creation and current-layer restoration now belong to that same transaction, including rollback. Closed/cleared previews reject late results and dispose their resources.

Text/image-to-mesh creation can capture an automatic placement intent at the original click. It is volatile and bound to the document, session, units, connection epoch and explicit setting. Closing the panel, turning the option off, pausing the task or changing connection revokes it; history recovery never recreates authority. Native import rechecks cancellation/intent after host calls that may process UI events. Original result keys retain the existing import identity; deleted/undone placements are not reinserted. `AholoPlaceTask` can recover and place an original static GLB by the local Library GUID without generating again. Explicit offline placement of verified cached files remains supported.

Release build and focused placement, snapshot, import-trace and localization tests pass. Remote Sol reviewed lifecycle/account boundaries; parent verified the actual target fields and serialized UI path, retained intended offline use, corrected layer restoration and teardown, and added cancellation checks inside the commit. Native candidate colors, point picking, viewport/document switching and Undo/Redo remain manual acceptance gates.

## M5f — separate Gaussian PLY from native mesh formats

Downloaded PLY is now classified by its declared properties/topology. Complete GS stays on the splat renderer; ordinary polygon mesh PLY enters the same independent native preflight, candidate preview, Block and Undo pipeline as GLB. Point-only, partial-GS and mixed/ambiguous files fail explicitly rather than becoming point clouds. ASCII and both binary byte orders are checked through every face index, finite property and final payload byte, without a geometry-count limit or a triangle-only restriction.

Static STL also uses native mesh preflight and audit. Local loading and generated-result placement preserve the original filename suffix, source bytes and hashes. Snapshots are rehashed before and after native preflight, and again in the existing durable-commit flow; output containing any non-mesh native object is rejected. OBJ/FBX archives, 3MF, general mesh USD and animated GLB remain outside direct placement support.

Remote Sol proposed format discrimination and reviewed the implementation; parent retained polygon support, checked all native consumers and strengthened preflight mutation detection. Release and focused format/placement/localization tests pass. Native PLY/STL importer, material/color, large-file cancellation and Undo acceptance still require Rhino on both platforms.

## M5g — bounded realtime cloud preview and recoverable closure

Render opens a separate modeless JPEG preview for an existing ingested mesh/GS ID, bound to its original document, units, perspective viewport and API connection. An independent current-user named pipe authenticates every request with a child-specific nonce, carries length-bounded JSON and raw JPEG bytes, and remains responsive while ordinary uploads use the serialized RPC channel. Four persistent listeners avoid a verified Unix reconnect gap. Latest-frame storage is bounded; image dimensions are checked before native decoding. Camera changes coalesce; local acknowledgements never claim association with received frames.

Creation intent is saved before the paid request. Session identifiers and unresolved cleanup survive restart; start-after-stop tombstones, original-account admission and file leases prevent stale starts or cleanup races. Cleanup is bounded, never secretly retried; a lost create response is unknown and blocks another creation until explicit external resolution. That user confirmation is not server verification. API changes attempt closure before replacing credentials, and Rhino exit cancels blocked ordinary requests so cleanup can run. A stale API request no longer clears a newer session-only key.

Remote Sol reviewed control architecture, manager races/tests and UI lifecycle. Parent implemented and verified the fixes. Release builds with zero warnings/errors; 722 tests pass, including actual local named-pipe concurrency/authentication, parent exit, journal recovery, camera coalescing, wrong epoch, delayed start and failed cleanup. No paid/live RenderCloud test or native/Windows visual acceptance was performed. Public JPEG lacks depth/alpha/camera-frame IDs; World PLY/SPZ ingestion and native mixed-depth rendering remain external contract gates.

## M5h — optional account OAuth without changing API authority

Settings now exposes international Manycore account login, explicit refresh and local sign-out. Official public resource/authorization metadata was fetched read-only on 2026-11-10 and still advertises S256, dynamic public-client registration and get_user_info. No real authorization or private credential was used. The implementation reuses reviewed Lux3D PKCE logic while isolating it from API Key epochs, task ownership, automatic placement and rendering. Tokens remain process-local; remembered account login and China-region parity remain open. Local sign-out does not claim provider-side revocation or verified user-profile access.

Short start/status/cancel RPCs keep browser waiting in a background account task. Generation checks discard late login/refresh results after sign-out; closure cancels only its own pending attempt. Callback parsing rejects duplicate/malformed/state-mismatched requests, bounds headers/time, tolerates unrelated requests and retains an accepted code if the browser closes its confirmation connection. Serial loopback handling remains vulnerable to delays from hostile local connections; the overall attempt is time-bounded, without claiming protection against same-user processes. Errors returned to the UI are fixed and contain no token/response details.

Remote Sol supplied integration and manager candidates and reviewed the implementation. Parent corrected a cancelled-but-successful return that could leave a pending label, kept completed sessions on dialog closure, and verified the full callback code, transport and tests. Mock protocol, real local callback, credential isolation, refresh/logout races and bilingual resource tests pass; native browser/account acceptance remains pending.

## M5i — PBR constants and captured scene lights

Untextured GLB export now retains optional floating-point PBR base color, metallic, roughness and alpha coverage, with BLEND/OPAQUE selected explicitly. Legacy constructor/default output remains compatible. Rhino PBR input colors follow the document's native Color4f.ApplyGamma preprocessing once, then enter the writer as linear. Complete supported material constants inherit through parent blocks; an unassigned parent does not override a child's display-color fallback. Material/geometry validation fails before replacing the existing file.

The Render page can explicitly include selected world rectangular/point lights and lights in selected blocks. Rectangles convert Rhino's corner and edge vectors to a centered, right-handed cloud-mm/Z-up pose, preserve mirrored emission via inverse-transpose normals, bake dimensions and reject shear. Points become spheres with a user-visible pre-block-scale millimetre radius; nonuniform sphere scale is rejected. Relative intensity is not calibrated photometry. Unsupported selected types fail before upload. The camera/light arrays are copied before validation and persisted with the original render intent, so upload waits and restart/explicit continuation cannot substitute current scene lights.

Remote Sol reviewed the representation and found mutable snapshot and parent-color fallback issues; the parent fixed both and added core geometry and workflow regressions. Existing mesh normals are not exported by this GLB writer, so the review's concern about the reflected native normal array does not affect the present file payload; normal/shading fidelity remains a further improvement. Source material networks, textures, per-face materials, refraction and realtime light editing are not claimed. Native/paid-service visual acceptance remains pending.

## M6 candidate checkpoint — alpha.3

The versioned source compiles cleanly and all 784 tests pass. Deterministic GS assets pass. A separate installed OpenUSD parser verified the generated camera and Rect/Sphere scene, including reflected emission and dimensions. Default English/selected Chinese also reaches the temporary browser callback confirmation. Local mac/win package receipts remain separate from native/paid acceptance and public release. No automatic installation, push, publication or real service submission occurred.

This implementation batch dispatched 20 bounded third-party tasks to the configured preferred `remote/gpt-6.1-sol`: 18 completed and two timed out, were status-checked and were not retried. The parent applied and verified every change; worker responses were candidates, with incompatible or unsupported proposals rejected. One native read-only helper checked local SDK/contracts because remote workers cannot access those files or run tools. No credentials, private task data or full conversation were sent.


## Delivery refinement — normals and embedded bitmaps

The exporter now preserves smooth/custom normals using the complete inverse-transpose before geometry unit/basis conversion, with separate face winding correction for mirrored blocks. Optional GLB NORMAL/TEXCOORD_0 attributes are validated. Direct local sRGB mesh UV PNG/JPEG snapshots, sampler settings and the PBR use-alpha flag are embedded; image bytes are hash-deduplicated. Unsupported mappings/profiles/alpha combinations fail before upload, with an explicit bitmap-export switch for factor-only output. Files are copied in background and checked for changes; source paths never enter the GLB. No geometry-count quota was added.

Three remote Sol candidates covered fidelity design, realtime-control design and texture implementation review. The parent rejected an incorrect basis-reflection claim using a triangle/normal test, fixed the confirmed PNG palette and color-metadata findings, and ran all decisive checks. One native helper performed read-only fixed-SDK investigation. The Release build is clean and 802 tests pass. Official Khronos glTF Validator 2.0.0-dev.3.10 reports zero errors/warnings/info/hints on a production-generated fixture with smooth normals, UVs, deduplicated RGBA PNG and RGB JPEG. Image validation is structural, not a complete JPEG decoder. This does not establish native or live cloud appearance; manual checks are recorded separately. The alpha.3 packages predate these source changes and must not be presented as containing them.


## Delivery refinement — ordered realtime scene controls

Cloud preview has a separate Scene settings dialog for world-mm position, uniform scale, Z rotation, positive absolute environment brightness and the intensity of selected rectangle/point lights captured at startup. Light poses/dimensions remain that snapshot; asset and light-slot additions/deletions are not exposed by this UI. Camera and scene changes share an ordered, bounded worker; only adjacent pending camera edits coalesce. Scene barriers retain their own accepted/acknowledged revisions. The UI commits settings only after server acknowledgement and rejects late nonterminal packets after closure. Unknown updates close the original session without automatic replay.

Remote Sol reviewed the controls and identified premature UI commit; it was fixed. Findings caused by omitted excerpt bodies or already-present shared validation were checked and rejected. The parent added queue ordering, duplicate revision, lost response, wrong epoch, real named-pipe and coordinate/snapshot regressions. The full suite passed 810 tests; after the camera-format repair, the affected realtime/geometry subset passed 124 tests. Release compilation is clean.

Independent OpenUSD parsing discovered that the earlier camera patch's same-line nested `over` statements were invalid USDA. The formatter now separates nodes correctly. A committed production fixture generator and `fixtures/render-verify.py` parse and compose actual scene/camera/GS patches, checking stable types, model millimetres, light/environment and independent camera updates. This check passes with the pinned OpenUSD runtime. No real paid service, native Rhino UI, Windows execution, installation or publication was used as evidence.


### Core 3MF placement

Added local and downloaded core 3MF results to candidate preview and native Block placement, retaining source SHA-256 and the existing import/Undo receipt. OPC references and model extension boundaries are checked before Rhino. UTF-8 package paths have positive regression coverage. Extended material/production/vendor 3MF remains an explicit boundary; no geometry-count limit was added. The 853-test offline suite and both staged payloads pass. See `mesh-formats.md` for native acceptance and remaining format gaps.


## Candidate checkpoint — alpha.4

The post-alpha.3 fidelity, realtime scene and core 3MF commits are consolidated as `0.2.0-alpha.4`. Source validation at `a85b04e`: 853 offline tests passed, clean Release compilation, Khronos GLB validation and actual OpenUSD patch composition. Both platform payloads include the OPC dependency, license and native acceptance sample. The product parity table continues to list open implementation and contract gates; this checkpoint does not mark the plugin complete or publish/install it.


## Ordinary mesh USD export after alpha.4

Added `AholoExportMesh` and a bilingual local panel action, separate from GS exchange and cloud rendering. Selected model-space Mesh/Brep/Extrusion/nested blocks are captured into private tessellated snapshots, with double-precision rebasing, supported normals/materials/UV bitmaps and no GLB intermediate. Local export permits orthographic and readonly documents; page-space roots fail explicitly to prevent unit confusion. USDZ is self-contained; plain USD keeps a stable adjacent image bundle, with atomic root replacement and old bundles preserved.

Remote Sol returned export-design and two implementation reviews; the import-design task timed out, was status-checked and not retried. Primary checks rejected incorrect claims about missing normal/material validation and old-asset deletion, confirmed the image contract, and improved error reporting. A native read-only SDK review identified double-precision-before-transform and page/model-space unit risks; both were fixed. Ordinary mesh import and combined GS/mesh export remain actionable work. The existing alpha.4 archives predate this source change.


## Local ordinary mesh USD import after alpha.4

Added strict static mesh import to the local candidate loader, preserving source units/transforms, split normal/UV seams, supported Preview Surface factors and direct sRGB images. OpenUSD reads private copies of the root/dependency closure; exact source hashes remain distinct from the derived 3DM integrity hash. Independently owned geometry/3DM work runs off UI, with embedded image byte readback. Native preparation/commit remains on UI and revalidates captured document settings. Unsupported content fails explicitly; hierarchy/culling presentation limits are reported.

Two remote Sol implementation reviews completed. The first confirmed the before/after-hash race, fixed with private source copies, and prompted preservation of culling metadata. Its claim that OpenUSD drops the render-context prefix from GetBaseName was disproved with the pinned runtime; the complete-attribute-name check is nevertheless explicit. The second identified stale color settings, now rechecked; its extreme determinant example is already rejected by the shared inverse validation, with a new regression proving that boundary. A proposed two-task draft batch never reached inference because the automatic permission check timed out; gateway status was checked and it was not retried. A native read-only helper checked fixed SDK APIs, unit rules and background ownership. Native File3dm worker/texture persistence and both-platform visual acceptance remain pending.


## Candidate checkpoint — alpha.5

Consolidates local mesh USD export (`7315d45`) and static local mesh USD import (`d29a8de`) into `0.2.0-alpha.5`. The package/assembly/command identities remain compatible. Source runtime verification is 878 passing offline tests and actual OpenUSD exchange checks; the current parity table and native acceptance gates still apply. Local archives are prepared separately from the clean metadata commit and are not installed or published automatically.


## Task-linked mesh USD and placement identity

Task preview/direct/automatic placement now use the common native-or-USD preparation route. Selection pins task/region/account/remote identity, operation, output key/format and known source hash across recovery. Existing/removed receipts are resolved before any download, and preview uses the same identity as direct placement. Cached results remain usable offline; known outputs avoid a redundant refresh. Revision/intent guards prevent late candidates or automatic placement after closure/revocation. Preparation, commit and prepared-document disposal are explicitly dispatched to Rhino UI.

Remote Sol supplied routing and placement-mode reviews, test candidates and a final integration review. Candidate test drafts contained wrong check counts and an assumed constructor; primary tests use the actual implementation and verify the corrected cases. Hash checks remain in immutable source preflight. Final review prompted cancellation validation before receipt framing and explicit UI dispatch after candidate preparation. Native host behavior remains a separate gate.


## Local mesh command and explicit placement modes

`AholoImportMesh` opens the per-document Library, then prepares a reviewable candidate after the command/Undo cycle finishes. It supports interactive and scripted file selection, reports a busy panel, binds the pending request to the original active document and clears it on AholoCancel, document closure or shutdown. Three placement modes separate height fitting, original-size CPlane placement and resolved source scene coordinates. Planar geometry works in the latter two; disabled fields are not parsed. The existing default remains height fitting. Candidate acceptance now forwards cancellation plus panel revision/visibility through native commit checkpoints.

Remote Sol reviewed the mode plan and final command/mathematics. Its extreme-coordinate example exposed an overflowing intermediate height difference despite a valid final ratio; the computation and regression now cover it. Fixed SDK inspection established the per-document panel lookup signatures; native command/Idle/event timing remains pending acceptance.


## Candidate checkpoint — alpha.6

Consolidates task-linked USD placement and result identity (`0b6d474`) with local mesh import/placement modes (`8f5963c`) as `0.2.0-alpha.6`. Stable plugin/assembly/Yak identities remain unchanged. The runtime source passes 921 offline tests and Release compilation; local archive receipts separately bind clean metadata source and actual payload bytes. Native/service acceptance remains pending, and this candidate is not automatically installed or published.


## Remembered OAuth accounts after alpha.6

Optional account login now supports explicit OS-protected persistence, local-only restoration, refresh-token rotation and cross-instance sign-out. A dedicated coordinator keeps credential envelopes separate from the API Key and writes only non-secret epoch/ready flags to disk. Pending operations and ambiguous refresh failures cannot revive older sessions; shutdown preserves completed remembered credentials. Account UI remains bilingual, defaults opt-in off, and distinguishes sign-out from closing/canceling an attempt.

Remote Sol supplied design and two implementation/integration reviews. The parent adapted the proposed refresh-failure behavior to fail closed for ambiguous rotating tokens. A suggested pending-cancel deletion was rejected as conflating dialog closure with explicit sign-out; the API now separates these actions and deterministic tests cover both. A claimed busy-poll race was disproved by the existing timer guard; duplicate-attempt ownership was additionally clarified. Actual separate-process fixtures verify lock ordering and stale refresh rejection using synthetic storage. Native OS-store/browser acceptance remains pending.


## Candidate checkpoint — alpha.7

Consolidates remembered OAuth sessions (`6a28504`) and the independently verified China contract inventory as `0.2.0-alpha.7`. The runtime still uses international endpoints, keeps OAuth separate from API Key access and retains stable plugin/package identities. Runtime evidence is 946 passing offline tests plus actual separate-process synthetic storage checks; local package receipts separately record clean source and archive hashes. Native/live acceptance remains pending.


## Regional integration — 2026-11-11 UTC

Implemented International/China routing, separate saved API Key/OAuth sessions, explicit Settings selection, task recovery and original-region realtime cleanup. Global credential admission retains its lock path and migrates to schema 2; unmarked old records remain International. Failed mutations reconcile retained connections with generation guards. Account cancellation bypasses foreground uploads through authenticated private IPC, including cancel-before-begin tombstones and a second dispatch check after startup.

Four remote/gpt-6.1-sol tasks supplied architecture, storage review, UI integration review and final cancellation review. The parent verified and repaired the escaped-token envelope boundary, connection recovery, stale selectors and startup cancellation window. Final local evidence: 982 passing offline tests, both synthetic local-loopback PKCE flows, actual private-pipe cancellation under a held foreground gate, and passing separate-process OAuth storage fixtures. Native UI/OS-store, Windows execution and actual regional services remain pending.


## Candidate checkpoint — alpha.8

Packages the regional integration (`097b2b7`) as `0.2.0-alpha.8`, retaining stable assembly/package identities and English-default/Chinese-localized UI. OAuth remains optional and does not grant REST API Key access. The new API credential schema requires closing older plugin instances before upgrading. Exact archive provenance is recorded in the local packaging receipts; native/live acceptance and the broader parity backlog remain open.


## Combined scene export after alpha.8

Implemented `AholoExportScene` and a bilingual Library action. Mesh and GS parts share one selection snapshot, including repeated GS nested in ordinary blocks; parent transforms and source SH frames remain explicit. Standalone mesh/GS routes reuse their existing authors. A common publication transaction preserves old output and old texture bundles, and the new package bridge authors a single self-contained stage with one mesh unit/axis conversion. Lazy parent-material capture fixes unrelated bitmap failures for GS or independently materialed child meshes.

One remote/gpt-6.1-sol design review recommended factoring authors into a caller-owned stage rather than temporary-layer composition. Native read-only reviews checked host traversal/units, found the parent-material bug, and reviewed its repair. The primary implemented all changes and ran actual validation. Remote daily quota was nearly exhausted after that bounded task; it was not increased or bypassed. Mixed scene import, native nested-GS viewing and host/live acceptance remain separate open work.


## Candidate checkpoint — alpha.9

Packages unified static scene export (`7c0717d`) as `0.2.0-alpha.9`. The installed command is `AholoExportScene`; existing strict GS/mesh routes and stable plugin/package identities are retained. Runtime evidence is 988 passing offline tests and 24 actual OpenUSD scene combinations, with original mesh/GS exchange regressions passing. Local archive receipts bind the clean source revision and all packaged bytes. Mixed import, native nested-GS viewport rendering, Mac/Windows and live-service acceptance remain pending. This is a local candidate, not installation or publication.


## Combined scene import after alpha.9

Added `AholoImportScene` and the bilingual Library action. One frozen dependency snapshot and one OpenUSD Stage decode/validate both typed payloads; a complete-scene manifest gates admission. Source coordinates/units are preserved. Mesh snapshot preparation and GS cache preparation precede one native commit: a mesh block plus top-level GS siblings, with shared scene identity and one Undo. Content signatures improve rollback checks; pure-GS completion now checks last cancellation and Undo completion. GS-only imports now reject mixed geometry explicitly instead of warning and omitting meshes.

Independent local read-only reviews confirmed the one-snapshot parser and shared transaction design, and prompted the material-content/Undo fixes. Remote catalog/status were checked; only 135 daily output tokens remained, so no useful remote inference was attempted and no quota was changed. The parent implemented and verified the code. A small packaged USDA scene supplies repeatable offline manual acceptance. Native renderer/Undo/material persistence, Windows execution and real cloud-service acceptance remain open.


## Candidate checkpoint — alpha.10

Packages unified static scene import (`fab5a61`) as `0.2.0-alpha.10`. `AholoImportScene` and its Library action accept supported GS and mesh parts from one frozen USD dependency snapshot, preserve source physical placement and commit through one native Undo transaction. The package includes a small two-Gaussian/2-metre-box USDA for manual acceptance. Stable plugin/assembly/Yak identities remain unchanged. Evidence is 1003 offline tests, 24 public-reader round trips, 19 GS format checks and strict malformed-scene rejection; native display, rollback, Undo/Redo, resource persistence and Mac/Windows/live-service acceptance remain pending. Local package receipts bind the clean source and exact bytes; this checkpoint does not install or publish.


## Nested GS viewport occurrences after alpha.10

Ordinary nested blocks now expose GS leaves to loading, resource retention, diagnostics and viewport preparation. Immutable worker snapshots carry exact root-to-leaf occurrence paths and complete parent transforms; viewport bounds and frame identity use the same world affine. Repeated/coincident leaves remain separate placements while sharing immutable assets. Existing top-level task lookup and export behavior stay explicit. Native whole-root proxy suppression is limited to top-level GS; nested bounds remain to preserve ordinary mesh siblings.

Two independent local read-only reviews found and drove fixes for premature inverse validation under cancelling parent/child scales, and exponential expansion of GS-free shared ordinary block graphs. Final traversal snapshots reachable definitions, isolates cyclic components, computes GS reachability to a fixed point, then expands only acyclic productive occurrences with a branch-local guard. The whole damaged component keeps its native proxy rather than interpreting ambiguous cyclic geometry. Cycle isolation uses the complete structural graph independently of visibility, so visible placements always have a matching resource-discovery path. Idle loading, reload and document diagnostics visit each reachable reference once, without expanding repeated or hidden copies. It is iterative, with no fixed nesting/count cap. Remote quota was still insufficient for useful work; no new remote inference or budget change occurred. The primary implemented repairs and ran the checks. Native visual, layer/BlockEdit, Undo/Redo and resource-lifetime acceptance remains pending.


## Candidate checkpoint — alpha.11

Packages nested GS occurrence display (`058e1d8`) alongside the alpha.10 unified USD scene import as `0.2.0-alpha.11`. Runtime evidence is 1024 passing offline tests, including the 21 occurrence/visibility/numerical/graph cases, plus the previously verified actual OpenUSD import/export checks. Stable plugin/assembly/Yak identities remain unchanged. Exact local package receipts bind clean source and payload hashes. Nested proxies remain visible; native Mac/Windows appearance, block/layer lifecycle, Undo/Redo and live services remain pending. No installation or publication is performed by this checkpoint.


## Multi-object realtime scene editor after alpha.11

Added privately-owned stable asset/light layouts, mixed mesh/GS initialization and typed add/edit/delete patches. A bilingual modal editor manages multiple prepared IDs and lights, model placement, environment and light position/color/dimensions without touching the Rhino document. Legacy fixed scenes retain their original IPC path. New edits bind to confirmed base revisions and account identity; stale/duplicate/different edits cannot overwrite or replay previous intent. Camera updates retain the shared barrier queue. Confirmation holds the current credential lease; late backend replies after stop or connection changes cannot commit a new scene.

Two independent local read-only reviews checked the queue/epoch and UI/SDK boundaries. The primary implemented the changes and verification. The actual OpenUSD parser rejected the public guide's unquoted colon-bearing delete key, so the writer emits the same literal dictionary key with valid quoting; provider interpretation remains a live gate. Whole-suite verification also exposed a duplicate Chinese label initialization failure; direct translations now tolerate shared wording and ambiguous exact reverse labels remain unchanged. Remote status left only 135 daily output tokens, insufficient for useful inference; no remote call or budget change occurred for this increment. Advanced transforms/light controls, ingestion contracts, native compositing and native/live acceptance remain open.


## Candidate checkpoint — alpha.12

Packages multi-object realtime scene editing (`c65dc8f`) as `0.2.0-alpha.12`. A bilingual draft editor manages prepared mesh/GS assets and lights with stable paths and confirmed scene revisions. Runtime evidence is 1048 passing offline tests and actual OpenUSD parsing/composition, including provider-delete marker syntax checks without claiming live deletion. The shared-translation initialization regression is fixed. Stable assembly/plugin/Yak identities remain unchanged. Local package receipts bind clean source and exact bytes; real service behavior, native Mac/Windows interaction and the broader parity backlog remain open. No installation or publication is performed by this checkpoint.


## Advanced realtime transforms and lights after alpha.12

Added model XYZ rotation, independent signed axis scales and upper-triangular shear while preserving the original constructor and old JSON defaults. Placement applies one asset-coordinate conversion, then shape/rotation and separate world translation. New advanced controls are collapsible. Light world-axis incremental rotation preserves captured position/dimensions and exact orientation for zero turns. Exposure, specular contribution, reflection influence and Rect-only two-sided emission are validated and serialized explicitly, including reset-to-false/zero patches. The common writer also supports these fields for offline requests; the offline selection form still uses captured defaults.

Two local read-only reviewers checked the official contract, matrix order, independent hand-calculated samples, fixed Eto API and property preservation. The primary implemented and verified the changes. Remote catalog/status still showed only 135 available daily output tokens; no inference or limit change occurred. OpenUSD verifies complete matrices and literal metadata readback, while mirrors/shears, advanced flag interpretation and native UI behavior remain explicit live/host gates. The service does not document a specular maximum of 1, so no such range was invented.


## Candidate checkpoint — alpha.13

Packages advanced cloud transforms and light controls (`0b29816`) as `0.2.0-alpha.13`. The editor adds XYZ rotation, signed axis scale/shear, world-axis light rotation, exposure, specular, reflection and Rect two-sided options while preserving old defaults. Evidence is 1063 passing offline tests and actual OpenUSD model/light composition and reset checks. Stable assembly/plugin/Yak identities remain unchanged. Native UI, real service mirror/shear/flag interpretation and the broader delivery backlog remain open. Exact local receipts bind clean source and package bytes; no installation or publication is performed.


## USD n-gons and material subsets after alpha.13

Extended both standalone and mixed-scene USD import with simple planar n-gon triangulation and valid materialBind face subsets. Exact orientation fallback, simple-boundary checks and deterministic ear clipping preserve source corners/winding; compact offsets keep the existing dense triangle/quad path lean. Material ownership is explicit, inherited bindings use OpenUSD resolution, and original face/corner indexing survives material splits. Source Mesh and actual part/subset paths remain distinct through native metadata. A small self-contained colored sample is included for manual native acceptance.

Independent read-only reviews checked algorithm, binding/provenance and tests. They prompted shared-vertex corner-attribute, translated-instance, competing material-purpose, strict small-area and actual prior-payload atomicity checks. The primary implemented the changes and ran verification. Remote daily allowance was insufficient for meaningful output; no inference or quota increase occurred. Triangles/quads retain their prior route. Subdivision/holes, malformed/nonplanar n-gons, editable hierarchy, general shader graphs, absent-normal smoothness across material parts and native Mac/Windows appearance remain explicit boundaries.


## Candidate checkpoint — alpha.14

Packages simple planar USD polygons and material face subsets (`95942f2`) as `0.2.0-alpha.14`, with a small self-contained colored native acceptance sample. Evidence is 1064 passing offline tests, four-format public-reader topology checks, 17 strict polygon/material rejection cases and existing 24 mixed-scene round trips. Stable assembly/plugin/Yak identities remain unchanged. Triangles/quads retain the native route; subdivision, general shaders and native Mac/Windows appearance/Undo remain open. Exact local receipts bind clean source and package bytes. No installation or publication is performed.


## OBJ input and geometry foundations after alpha.14

Added private OBJ/ZIP source capture, a static polygon syntax model and a host-independent .NET triangulator. Source positions remain double precision; vertex/UV/normal references, face state, repeated-object ordinals and smoothing groups remain available for the later material/scene compiler. ZIP dependencies are read through a confined virtual path table and private numbered copies. No user-facing route has been opened yet, so unimplemented material/normal handling cannot accidentally admit a partial result. The development sequence and evidence are recorded in [OBJ implementation](obj-import-development.md).

Two local read-only reviews found the repeated-object identity requirement and the orientation fast-path boundary for subnormal products; both were implemented and verified. The primary wrote files and ran the checks. Remote catalog/status left only 135 daily output tokens; no inference or limit change occurred. MTL conversion, smoothing-normal synthesis, coordinate choices, shared snapshot wording and task/UI wiring remain required before OBJ placement can be claimed. No new package, installation, publication or live request is part of this foundation commit.
