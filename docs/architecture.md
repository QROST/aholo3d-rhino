# Architecture and API evidence

Maintainer: CuriousArc · qrost. API review date: 2026-11-08. Complete current endpoint inventory: [API coverage](api-capabilities.md).

Canonical source repository: [https://github.com/QROST/aholo3d-rhino](https://github.com/QROST/aholo3d-rhino); checkout directory: `aholo3d-rhino`. The repository rename does not rename installed package or persisted identifiers; see [compatibility details](repository-rename.md).

## Boundaries

- **Aholo.Rhino**, .NET 7: Eto UI, document identity checks, tessellation, camera capture, native Gaussian block references and an experimental DisplayConduit mode. Only Rhino's UI thread mutates the document.
- **Aholo.Core**, .NET 7: transport contracts/client, atomic local task journal, streaming point/strict Gaussian PLY readers, full Gaussian parameters, independent SH/projection/CPU compositing, GLB writer, cloud USDA scene builder, and static GS USD/USDZ scene import/export through an isolated OpenUSD process. It has no service credentials or Rhino dependency.
- **Aholo.Provider**, .NET 8: HTTP clients, exact service-specific envelopes, upload token separation, OS credential protection and experimental REST/WebSocket realtime session lifecycle. API Keys stay here/the sidecar after entry.
- **Aholo.Sidecar**, .NET 8: private pipe dispatcher, workflow progression, durable submission intent and public-only asset download transport.

Rhino can keep its own .NET runtime while the child process runs on .NET 8. Plug-in GUID: `D1D17217-1169-41BB-B63A-FEE17DFA777F`. Keychain service: `ai.aholo.world.rhino`. Package: `aholo-rhino`. These differ from Lux3D's identifiers.

## Verified service contracts

International base: `https://api.aholo.ai/global/`. `Authorization` contains the raw API Key, not a Bearer token. No authenticated account calls were made while developing this initial candidate.

| Purpose | Method/path after base | Important detail |
| --- | --- | --- |
| Generate world | POST `world/v1/generations` | `prompt`, optional one-image `resources`; returns string `worldId` |
| Query world | GET `world/v1/{worldId}` | Raw detail object; `assets.splats.urls.plyPath/spzPath`; `assets.semanticsMetadata.upAxis` |
| Check connection/list | POST `world/v1/list` | `pageNum:0`, `pageSize:20`; `result` rows |
| Upload token | GET `asset/v1/token` | `ousToken`, `globalDomain`, `blockSize` |
| Upload data | OUS `/ous/api/v2/...` | `ous-token-v2` only; separate upload origin and `c/d` envelopes |
| Mesh processing | POST `rendercloud/v1/mesh-upload-process/task/create` | Uploaded GLB URL, `fastUpload:true`; numeric task ID preserved losslessly |
| Mesh query | GET `rendercloud/v1/mesh-upload-process/task/get?taskid=...` | `success/data`; success outputs contain mesh ID |
| Render submit | POST `rendercloud/v1/jobs` | `requestId` plus Base64(UTF-8(USDA)) |
| Render query | POST `rendercloud/v1/jobs` | Only `operationId`; done/result/error operation envelope |
| Realtime submit/query | POST `rendercloud/v1/streams` | Create: requestId + Base64 USDA + autoEnhanceAI=false; query: operationId only |
| Realtime update | POST `rendercloud/v1/streams/{sessionId}:push` | Ordered Base64 USDA patches, distinct tracing IDs; no automatic replay |
| Realtime close | DELETE `rendercloud/v1/streams/{sessionId}` | Close WebSocket first; retain cleanup obligation on uncertain deletion |

Sources: [official introduction](https://labs.aholo.ai/api-docs/en/intro), [quickstart](https://labs.aholo.ai/api-docs/en/quickstart), and [exact per-service snapshots](api/README.md). The RenderCloud OpenUSD guide is embedded in the `RenderCloudOpenUSDGuide` tag of the render specification. No .NET SDK is documented, so this implementation uses those REST schemas.

World generation supports text only, one image, or both. Reconstruction and the ten Lux3D operations have separate schema-backed forms. World terminal states include SUCCEEDED, FAILED, CANCELED, TIMEOUT and REJECTED. Render operations carry their own boolean `done`; mesh processing uses numeric state 3 for success and 4 for failure. These formats are deliberately parsed separately.

## Recovery and charging

The journal writes `Submitting`, `MeshSubmitting` or `RenderSubmitting` atomically before a create request. A lost reply or process interruption becomes `SubmissionUnknown`; there is no automatic replay. A successful submit persists the returned ID immediately. Read-only polling may be repeated safely. Refresh/download never submit the paid render phase. A shared per-process session may continue the original in-memory render intent once; panel closure preserves it. Pause, restart and transport reset remove that authority. Recovered MeshReady tasks require the separate explicit continuation action.

Reference images and GLB input are snapshotted before upload. Keys bind task records through a hash, so switching Keys cannot poll/download another Key's records. Signed URLs are fetched fresh and omitted from the durable journal. File storage and record replacement are local; no journal content is synced by the plugin.

## Geometry conventions

The shared PLY parser streams ASCII and binary little/big endian scalar/list data and validates the full body before document insertion. `ReadGaussians` requires position, three log-scales, wxyz quaternion, logit-opacity and complete SH degree 0–3 coefficients. No silent point-cloud fallback is allowed. The explicit center-point diagnostic remains separate. Y-up conversion and user-selected scale are applied as instance transforms to both centers and covariance; viewing directions are transformed back to source space for SH.

The native experiment loads at Rhino startup and schedules registration of a Shaded-derived mode on UI idle, because ordinary saved block references do not trigger demand-loading of this plug-in. Startup registration makes no service calls. Each source instance is a Rhino bounding-box block; draw-time GS quads are transient render resources. The PLY snapshot is external and content-addressed, with hash/path in instance attributes. Missing/corrupt assets retain the proxy. Per-view projected Gaussian ellipses are sorted across all visible instances and rendered in `PostDrawObjects` with native depth testing and depth writes disabled, followed by Flush before restoring state. Native material alpha/ordering remains a host acceptance gate. Selection outlines, comprehensive clipping behavior, nested blocks and arbitrary transparent-object interleaving are not production-supported. Basic clipping-plane interaction was reported working on the local Mac. HDR values remain in Core/source, but stock material colors are clipped to byte range for this first experiment.

Beta.3 prepares covariance/opacity once per asset and captures immutable camera/instance snapshots for one cancellable worker. A value-type projection path is checked against the scalar reference. All visible instances share global far-to-near ordering; 2,048-splat packets share one fixed opacity atlas/material. Native resource creation/disposal is sliced on the UI thread. Camera changes invalidate old work and use the proxy until a complete matching frame is ready. Real model-view dimensions and save/print/capture guards exclude thumbnail pipelines. The face-index `DrawMeshShaded` overload preserves per-vertex RGB; native color/texture-alpha combination still requires the manual probe on each backend. Bounded asynchronous logs contain timings/counts without credentials or prompts.

Beta.4 separates normalized projection identity from CPU depth coverage. Equivalent DPI passes reuse the accepted projection; clip-range expansion accumulates a padded union while native clipping remains active. Camera/scene candidates settle for 75 ms before replacing work. Only complete upload publication redraws the view, and real geometry bounds participate in native near/far calculation. Native-view polling is inventory-only except for one confirmed resize recovery. Replacement/rejection logs identify remaining starvation causes; see [display stability](gs-visibility-stability.md).

Beta.5 reuses validated existing modes without unconditional updates, verifies any required update by runtime readback and keeps renderer initialization failures separate from panel loading. Registration starts on idle with bounded UI-timer retries and explicit command retry; it never deletes display modes. Runtime readback does not prove persistence across restarts.

The CPU reference computes premultiplied RGBA, testing each Gaussian contribution against opaque linear depth before composition. It is independent of Rhino and intentionally not a production realtime backend. See [ADR 0001](decisions/0001-native-gaussian-viewport.md) and [GS acceptance](gs-host-acceptance.md).

GLB writes triangle meshes with glTF's canonical metres/Y-up convention `(x,z,-y)/1000` from Rhino millimetres/Z-up. USDA applies the inverse conversion using a row-vector matrix. Cameras already use millimetres/Z-up; their translation is in the fourth row. The documented cloud renderer ignores `metersPerUnit`/`upAxis` for conversion and always uses millimetres/Z-up.

**Open integration question:** the public mesh processing contract does not explicitly guarantee that fast GLB ingestion preserves native vertex units/axes. The conversion is internally consistent and fixture-tested, but visual parity and scale must be confirmed with an actual account. This cannot be established by HTTP mocks.

## Deliberate first-version limits

International endpoint only; no OAuth button or China endpoint; no realtime cloud viewport; basic diffuse cloud-render colors only; no custom render meshes, PBR textures or light export. Nested ordinary blocks are traversed with their placements; GS bounds are explicitly rejected. Host/UI and service entitlement validation remain manual acceptance items.

## Realtime protocol foundation

The Provider library has injected REST/WebSocket transports and serial scene patches. Adjacent pending camera updates may coalesce, but cannot cross a scene edit barrier; final camera state can be flushed. Creation and unknown requests are never automatically replayed. `RealtimeSession` takes ownership only of an already-known ACTIVE operation, waits for the stream initialization status and closes the update queue, WebSocket and REST session. Cleanup uncertainty retains the session ID and an explicit retry obligation. This library is not connected to the panel/sidecar workflow yet.

Stream credentials are ephemeral, omitted from normal JSON/diagnostic formatting and never replace the API Key at the gateway. WSS connections use TLS/443, actual-socket public DNS pinning, no redirects/proxy/cookies and no gateway Key. Status and JPEG message byte limits bound protocol reassembly, not scene geometry. JPEG framing checks are not a full image-decoder validation.

The published protocol exposes no depth, alpha, object IDs or camera/frame association. Frames explicitly report no camera association. Whole-scene cloud rendering also requires a validated World GS→meshId bridge, a supported geometry policy and stale-frame handling; none is inferred from successful mock tests.

## Local GS exchange

Local USD interchange uses the pinned OpenUSD Gaussian schema, separate from cloud RenderCloud USDA submission. The panel captures complete top-level GS block references, current transforms and document units on Rhino’s UI thread. Background work reads immutable hash-verified assets and preserves all instances, SH and affine transforms; export writes atomically. Import writes every part in one Undo transaction. See [the exchange contract](usd-exchange.md). No API Key is needed for this local path.

## Integrated application and workspace

One `AholoSession` per Rhino process owns the pipe, connection state, polling and operation registry. Panels and commands use it; history reads directly from the task store. Task-scoped disk locks serialize writes while unrelated tasks remain independently queryable. Draft forms are kept per operation. The Create / Library / Render workspace keeps credentials, polling and placement defaults in Settings. Library reads are paginated and an explicit restore locates the exact task page.

Generic service submission validates the official request schema and local file mapping before uploading. Every input is snapshotted before the first upload. Submission barriers precede each charged request. Local-only Spatial input guarantees the plugin performed its Asset upload. Result decoders preserve ordered output/action identity, avoid raw server errors in diagnostics, and retain signed URLs only in memory. Downloads publish immutable hash-named files before the journal switches references, preserving old bytes across interrupted re-downloads.

Static GLB placement uses adapted container validation, private snapshots, native headless preflight, limited degenerate-face/normal repair and native import/block wrapping from the sibling Lux3D integration. Independent Aholo mesh markers protect duplicate imports, Undo and recovery. The target document, units and state are rechecked before mutation. Render selection expands ordinary nested blocks without exploding user objects. These new native paths await host acceptance.
