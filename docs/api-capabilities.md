# Aholo public API coverage

Maintainer: CuriousArc · qrost. Official contract verification: 2026-11-10 (current published schemas semantically match the pinned snapshots).

The [official index](https://labs.aholo.ai/api-docs/en/llms.txt) lists five service schemas with 30 HTTP endpoints. Sources and exact download hashes are in [api/README.md](api/README.md). Endpoint coverage is distinct from Rhino workflow availability, optional parameter coverage and live service acceptance.

The provider now has a callable route for every listed endpoint. The new create registry covers World generation/reconstruction, all ten Lux3D operations, and SpatialUnderstanding. Public request fields are checked against embedded official schemas plus cross-field rules. All 13 create operations now have schema-backed Rhino forms, shared journals, query and download paths. The existing offline rendering flow shares the same Library. No paid-service acceptance is claimed.

| Service | Endpoints | Provider coverage | Rhino workflow at this milestone |
| --- | ---: | --- | --- |
| World | 4 | Generate/reconstruct/get/list; cover/scene/timestamps/LOD metadata and paginated filters | Generate/reconstruct forms, cloud history/ID recovery, GS placement and opaque LOD metadata downloads; live reconstruction acceptance pending |
| Lux3D | 12 | Ten create operations; query and filtered paginated list; int64 IDs preserved as strings; ordered output content retained | All ten forms, cloud history/ID recovery, local input upload, typed outputs/downloads and static GLB/mesh PLY/STL/core 3MF placement with candidate preview; animation playback and arbitrary output import are not implemented |
| RenderCloud | 6 | Offline submit/query, mesh processing/create/query, realtime create/query/push/delete; ingestion supports fastUpload selection and all returned mesh IDs | Mesh/Brep/extrusion and nested Block snapshots with PBR constants and optional selected Rect/Sphere light conversion, shared original-intent continuation; modeless JPEG preview follows the original camera for known ingested mesh/GS IDs, with independent control and durable cleanup; live acceptance pending |
| Asset / OUS | 5 | Token plus streamed single/multipart upload and status | Used by all local service inputs; advanced multipart metadata/prefix/name and server-directed missing-part selection implemented |
| SpatialUnderstanding | 3 | Create, identity-checked detail, paginated filtered list; result retained as a DSL download URL | Local indoor PLY upload, create/query, cloud history/ID recovery, DSL download and native wall surfaces/opening outlines/oriented boxes; automatic wall solids/boolean openings are not claimed |

## Public operations

### world

- `POST /global/world/v1/reconstructions` — Create 3DGS world reconstruction job
- `POST /global/world/v1/generations` — Create 3DGS world generation job
- `GET /global/world/v1/{worldId}` — Get world details
- `POST /global/world/v1/list` — Paginated list of my worlds

### lux3d

- `POST /global/lux3d/v1/generate/multimodal-to-image/task/create` — Create Multimodal Single-Image Generation Task
- `POST /global/lux3d/v1/generate/image-to-four-view/task/create` — Create Multimodal Four-View Task
- `POST /global/lux3d/v1/generate/img-to-3d/task/create` — Create Image-to-3D Task
- `POST /global/lux3d/v1/generate/text-to-3d/task/create` — Create Text-to-3D Task
- `POST /global/lux3d/v1/generate/material-transfer/task/create` — Create Material Transfer Task
- `POST /global/lux3d/v1/part-split/task/create` — Create Part-Splitting Task
- `POST /global/lux3d/v1/multi-format-export/task/create` — Create Multi-Format Export Task
- `POST /global/lux3d/v1/articulation-animation/task/create` — Create Model Articulation and Animation Task
- `POST /global/lux3d/v1/animations/rig/task/create` — Create Humanoid Auto-Rigging Task
- `POST /global/lux3d/v1/animations/retarget/task/create` — Create Humanoid Animation Retargeting Task
- `GET /global/lux3d/v1/generate/task/get` — Query Generation Task
- `GET /global/lux3d/v1/generate/task/list` — List Generation Records

### rendercloud

- `POST /global/rendercloud/v1/jobs` — Submit or poll an OpenUSD offline render job
- `POST /global/rendercloud/v1/streams` — Create or poll an OpenUSD realtime render stream
- `POST /global/rendercloud/v1/streams/{sessionId}:push` — Push incremental OpenUSD content to a realtime render stream
- `DELETE /global/rendercloud/v1/streams/{sessionId}` — Close a realtime render stream
- `POST /global/rendercloud/v1/mesh-upload-process/task/create` — Create a 3D parsing and ingestion task
- `GET /global/rendercloud/v1/mesh-upload-process/task/get` — Query 3D parsing and ingestion task status

### asset

- `GET /global/asset/v1/token` — Get OUS upload token
- `POST /ous/api/v2/single/upload` — OUS single-file upload (≤ blockSize)
- `POST /ous/api/v2/block/upload/init` — OUS multipart upload — initialize
- `POST /ous/api/v2/block/upload/part` — OUS multipart upload — upload one part
- `GET /ous/api/v2/upload/status` — OUS — poll upload status

### spatialunderstanding

- `POST /global/spatial-understanding/v1/tasks` — Create spatial understanding task
- `GET /global/spatial-understanding/v1/tasks/{taskId}` — Get spatial understanding task details
- `POST /global/spatial-understanding/v1/tasks/list` — List my spatial understanding tasks

## Contracts that must remain distinct

- API authorization is a raw international API Key. An account OAuth login does not prove generation access or credit balance. Validation queries must not create charged work.
- World IDs, Lux int64 task IDs, render operation IDs, ingestion IDs and spatial task IDs are separate. Unknown create replies remain unknown; they are never automatically replayed.
- Lux outputs are operation-specific: URL, JSON-encoded result list/object, null or `NOT_REQUESTED`. A successful HTTP request or a running task with partial outputs is not a completed model. Task queries do not require undocumented `bizId`.
- Generation's face-count parameter is a service constraint. It is not an import or viewport geometry limit. No post-generation decimation is implied.
- World reconstruction uses at least 20 images, or supported video/INSV resources. World generation supports text and/or one image. Lux supports its own text/image/multi-image modes; validation is not shared blindly.
- SpatialUnderstanding requires an indoor PLY uploaded through Asset. Coordinates are metres, right-handed, Z-up; its matrix uses column vectors. RenderCloud scene matrices use row vectors and millimetres. A valid HTTPS string alone does not prove Asset upload provenance; enforce that in the workflow.
- Spatial result URLs contain UTF-8 Wall/Door/Window/Bbox DSL, not JSON. Lux PLY is not automatically a Gaussian asset. Parse and validate each artifact according to its declared operation.
- RenderCloud documents GS payloads but does not establish arbitrary World PLY/SPZ ingestion support. Realtime JPEG streams do not provide the depth/alpha/camera-frame data required for arbitrary native mixed-scene occlusion. These remain separate acceptance/research gates.
- The offline render docs disagree about requestId deduplication. Do not promise idempotence or retry an uncertain submission based on that field.

Local API tests use mock HTTP. They verify request paths/bodies, validation before network, ID and output preservation, page conventions and no automatic retries. They are not live permission, quota, cost, render-quality or native UI evidence.

## Remaining scope

Library exposes World/Lux3D/Spatial cloud history and recovery by existing World/Lux3D/Spatial/render ID. RenderCloud has no documented history-list endpoint. World LOD metadata is retained without assuming an unpublished progressive-loading schema. Durable upload recovery across tokens is not guaranteed by the public contract; cloud GS ingestion, native realtime compositing and full structural wall reconstruction are not implemented. Settings can validate through World, Lux3D or Spatial history; this never proves all other service entitlements. Full render texture/material-network export, animation playback, undocumented output formats and Windows native execution require separate work.

Downloaded files use immutable content-addressed names. Missing remote outputs retain downloaded local references. Rig/animation GLB files can be uploaded/downloaded, but static placement rejects unsupported skins, animations or required glTF extensions before touching the document. This is a format-support boundary, not a geometry-count cap. Generation/articulation URLs without a recognized format extension remain explicit unsupported results. Archives are retained as files and are not extracted automatically.

## Product parity at alpha.5

The 30 public HTTP routes and 13 create forms are implemented; that is not a claim that every reference-plugin behavior or returned artifact is fully supported in Rhino.

| Area | Implemented in this source candidate | Remaining before a complete parity claim |
| --- | --- | --- |
| Lux3D workflows | Ten public creates, typed outputs, paged cloud history, original-ID recovery, three mesh candidates, CPlane/point placement and volatile original-generation placement intent | Animation playback; OBJ/FBX archives, extended 3MF and task-linked ordinary mesh USD placement; native material and Undo acceptance |
| Local mesh exchange | Local static USD/USDA/USDC/USDZ mesh import to candidates plus supported tessellated export, normals, PBR/direct UV bitmaps and double origins | Task-linked USD placement, polygons/subsets/subdivision, combined GS/mesh export, editable hierarchy and full material/texture mapping support; native/viewer acceptance |
| GSRhino capabilities | PLY/SPLAT/SPZ v1–4, local static GS USD/USDZ, compatible saved GS references, full SH0–3 data and experimental native display | Windows/new-host regression acceptance; transparent interleaving, nested GS instances, capture/layout/print/Raytraced support; further large-scene performance work |
| Accounts | International API Key validation/storage plus separate optional PKCE OAuth login/refresh | Remembered OAuth sessions and China-region parity; authenticated account/browser acceptance |
| World / Spatial | Generation/reconstruction, history/recovery, PLY/SPZ/panorama/LOD downloads, editable spatial DSL block | World LOD streaming schema; full structural solids/opening reconstruction; live reconstruction/DSL acceptance |
| Offline rendering | Mesh/Brep/extrusion and ordinary nested blocks; PBR constants, smooth/custom vertex normals and direct Mesh UV bitmaps; selected rectangular/point lights; original camera/light snapshot and explicit recovered continuation | Procedural/explicit texture mapping, surface-meshing UVs, native embedded-image persistence acceptance, per-face/complex materials, glass transmission, calibrated light intensity, real ingestion/camera/render acceptance |
| Realtime rendering | Existing ingested mesh/GS ID, modeless latest-JPEG preview, original camera updates, model placement/environment/captured-light intensity controls, independent IPC and durable cleanup | Native mixed-depth viewport compositing; World GS-to-cloud ingestion contract; asset/light-slot additions and deletions in the UI; live session acceptance |
| Release | Cross-platform compilation and local checks, bilingual interface/docs, package checks | Native Mac and Windows interaction tests, real authorized service acceptance, final publication decision |

Public contract gates are distinct from ordinary implementation work: JPEG currently has no depth/alpha/camera-frame linkage; arbitrary World PLY/SPZ ingestion and progressive LOD metadata need provider-confirmed contracts. Texture/material export, format import, patch controls and account persistence remain actionable development backlog. None is silently marked complete by endpoint counts or mock tests.
