# Architecture and API evidence

Maintainer: CuriousArc · qrost. API review date: 2026-11-01.

## Boundaries

- **Aholo.Rhino**, .NET 7: Eto UI, document identity checks, tessellation, camera capture and point-cloud placement. Only Rhino's UI thread mutates the document.
- **Aholo.Core**, .NET 7: transport contracts/client, atomic local task journal, streaming PLY reader, GLB writer and USDA scene builder. It has no service credentials or Rhino dependency.
- **Aholo.Provider**, .NET 8: HTTP clients, exact service-specific envelopes, upload token separation, OS credential protection. API Keys stay here/the sidecar after entry.
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

Sources: [official introduction](https://labs.aholo.ai/api-docs/en/intro), [quickstart](https://labs.aholo.ai/api-docs/en/quickstart), and [exact per-service snapshots](api/README.md). The RenderCloud OpenUSD guide is embedded in the `RenderCloudOpenUSDGuide` tag of the render specification. No .NET SDK is documented, so this implementation uses those REST schemas.

World generation supports text only, one image, or both. The separate reconstruction endpoint exists but is not exposed in this version. World terminal states include SUCCEEDED, FAILED, CANCELED, TIMEOUT and REJECTED. Render operations carry their own boolean `done`; mesh processing uses numeric state 3 for success and 4 for failure. These formats are deliberately parsed separately.

## Recovery and charging

The journal writes `Submitting`, `MeshSubmitting` or `RenderSubmitting` atomically before a create request. A lost reply or process interruption becomes `SubmissionUnknown`; there is no automatic replay. A successful submit persists the returned ID immediately. Read-only polling may be repeated safely. After mesh processing succeeds, refresh advances that already-approved render flow once; the render intent is saved before the next POST. If the panel is closed at that stage, reopening and refreshing resumes it.

Reference images and GLB input are snapshotted before upload. Keys bind task records through a hash, so switching Keys cannot poll/download another Key's records. Signed URLs are fetched fresh and omitted from the durable journal. File storage and record replacement are local; no journal content is synced by the plugin.

## Geometry conventions

PLY import streams ASCII and binary little/big endian scalar/list data, accepts RGB or SH DC colors and validates the entire body before the document changes. It does not downsample. Other properties remain preserved in the original file; they are not rendered in Rhino. A Y-up file converts to Z-up; absent axis metadata uses the explicit local import convention. World scale is user-selected because absolute units are not specified.

GLB writes triangle meshes with glTF's canonical metres/Y-up convention `(x,z,-y)/1000` from Rhino millimetres/Z-up. USDA applies the inverse conversion using a row-vector matrix. Cameras already use millimetres/Z-up; their translation is in the fourth row. The documented cloud renderer ignores `metersPerUnit`/`upAxis` for conversion and always uses millimetres/Z-up.

**Open integration question:** the public mesh processing contract does not explicitly guarantee that fast GLB ingestion preserves native vertex units/axes. The conversion is internally consistent and fixture-tested, but visual parity and scale must be confirmed with an actual account. This cannot be established by HTTP mocks.

## Deliberate first-version limits

International endpoint only; no OAuth button or China endpoint; no reconstruction UI or realtime stream; basic diffuse mesh colors only; no blocks, custom render meshes, PBR textures or light export. Host/UI and service entitlement validation remain manual acceptance items.
