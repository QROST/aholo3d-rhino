# Aholo public API coverage

Maintainer: CuriousArc · qrost. Official contract verification: 2026-11-08.

The [official index](https://labs.aholo.ai/api-docs/en/llms.txt) lists five service schemas with 30 HTTP endpoints. Sources and exact download hashes are in [api/README.md](api/README.md). Endpoint coverage is distinct from Rhino workflow availability, optional parameter coverage and live service acceptance.

The provider now has a callable route for every listed endpoint. The new create registry covers World generation/reconstruction, all ten Lux3D operations, and SpatialUnderstanding. Public request fields are checked against embedded official schemas plus cross-field rules. Existing World/RenderCloud workflows are being unified; the other new provider operations are not yet connected to the Rhino panel. No paid-service acceptance is claimed.

| Service | Endpoints | Provider coverage | Rhino workflow at this milestone |
| --- | ---: | --- | --- |
| World | 4 | Generate/reconstruct/get/list; cover/scene/timestamps/LOD metadata and paginated filters | Generate, original-task recovery, download and GS placement; reconstruction pending |
| Lux3D | 12 | Ten create operations; query and filtered paginated list; int64 IDs preserved as strings; ordered output content retained | Pending integrated task/asset/mesh workflow |
| RenderCloud | 6 | Offline submit/query, mesh processing/create/query, realtime create/query/push/delete; ingestion supports fastUpload selection and all returned mesh IDs | Selected mesh/Brep/extrusion offline render; Block snapshots and shared continuation pending; realtime host preview pending |
| Asset / OUS | 5 | Token plus streamed single/multipart upload and status | Used by existing input upload; custom metadata/prefix/name and resumed missing-part selection not exposed |
| SpatialUnderstanding | 3 | Create, identity-checked detail, paginated filtered list; result retained as a DSL download URL | Pending upload/DSL asset workflow; no native wall reconstruction claim |

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
