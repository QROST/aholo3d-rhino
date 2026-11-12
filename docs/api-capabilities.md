# API coverage

The implementation follows five pinned public service specifications, with 30 HTTP routes and 13 creation forms. This describes implementation coverage, not a guarantee of account permissions or support for every returned artifact.

| Service | Routes | Implemented workflows |
| --- | ---: | --- |
| World | 4 | Generation, reconstruction, detail, history and original-output downloads |
| Lux3D | 12 | Ten creation operations, query and history |
| RenderCloud | 6 | Mesh ingestion, offline jobs, realtime streams, updates and closure |
| Asset / OUS | 5 | Upload authorization, single/multipart upload and status |
| SpatialUnderstanding | 3 | Creation, detail, history and DSL placement |

Lux3D creation covers text-to-3D, image-to-3D, multimodal image generation, four-view images, material transfer, part splitting, format export, articulation/animation, rigging and retargeting.

## Account and region

International and China use separate endpoints, credentials and task identities. The API Key is required for service calls; OAuth account login does not replace it. Validating one service does not establish access to every other service.

Creation persists submission intent before sending. An uncertain response never causes automatic paid resubmission. History/query/download preserve original task IDs. Render continuation after recovery requires an explicit action.

## Boundaries

- World LOD metadata is retained; progressive LOD streaming is not implemented.
- Spatial results are surfaces, outlines and boxes, not complete structural reconstruction.
- RenderCloud has no documented general history-list endpoint in the pinned contract.
- Live preview uses JPEG images, without native depth/alpha compositing or reliable camera-frame association.
- Arbitrary World PLY/SPZ ingestion into cloud rendering is not established by the public ingestion contract.
- Rigging and animation outputs are downloadable; native animation playback is not implemented.
- Service geometry settings do not impose an additional import limit.

The versioned [specifications and source hashes](https://github.com/QROST/aholo3d-rhino/tree/main/docs/api) remain reference material for maintainers. [Official documentation](https://labs.aholo.ai/api-docs/en/intro) and account permissions may change independently of a plugin release.
