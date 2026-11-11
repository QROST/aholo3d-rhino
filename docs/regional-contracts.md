# Regional contract audit

CuriousArc · qrost. Verified 2026-11-11 UTC. Public read-only fetches only; no credentials, authorization browser, uploaded assets, charged work or live account acceptance.

## Official coverage

The [China public index](https://labs.aholo3d.cn/api-docs/llms.txt) exposes World (4 paths), Lux3D (12), SpatialUnderstanding (3), RenderCloud (6) and Asset/OUS (5). Exact source URLs and hashes are committed in [the snapshot inventory](api/china/sources.json). After removing `/global` from international paths and excluding prose/examples/SDK operation names/server declarations, all five `paths + components` trees match the repository's international snapshots. `scripts/check-region-contracts.py` reproduces this structural comparison offline. This is evidence for shared request/response types, not equal entitlement or successful execution.

| Setting | International | China |
| --- | --- | --- |
| API gateway and path prefix | `https://api.aholo.ai/global/` | `https://api.aholo3d.cn/` |
| Documented OUS upload host | `https://ous-sg.kujiale.com` | `https://ous-cos.kujiale.com` |
| OAuth resource | `https://api.aholo.ai` | `https://api.aholo3d.cn` |
| OAuth issuer | `https://www.aholo.ai` | `https://www.aholo3d.cn` |

OUS schemas describe these hosts as common production values and return `globalDomain` in the upload token response. Runtime routing must still validate that returned host; this audit does not establish that every future OUS host has the same name.

The [China protected resource](https://api.aholo3d.cn/.well-known/oauth-protected-resource) and [authorization server](https://www.aholo3d.cn/.well-known/oauth-authorization-server) advertise dynamic public-client registration, authorization code plus refresh token, S256 and `none` client authentication. The disclosed account scope is `get_user_info`. REST service schemas still require a regional raw API Key. There is no verified token-to-Key exchange or userinfo endpoint; OAuth sign-in is not generation authority.

## Required implementation

The current runtime remains international-only. Region parity is actionable implementation work, not a missing-contract blocker:

- Add one explicit region configuration mapping gateway/prefix, OAuth issuer/resource and allowed OUS host. Never infer it from UI language or credential text.
- Bind region to API Key identity, secure-store slots, coordination epochs, account sessions, tasks/history recovery and realtime cleanup records. Existing records without a region retain the international meaning.
- Switch region through connection-change cleanup, canceling stale operations and retaining original task/operation identities. A task from another region must never be queried with the current region's key.
- Preserve the current query-based multipart upload initialization. The sibling Lux3D plugin's older form-based version is not the authority for the newly verified schemas.
- Test both regional request routes, authority isolation, migration and switching offline. Then separately validate native account dialogs, actual regional permissions, uploads, generation, render ingestion and realtime streams.

The public contracts still do not establish native cloud depth/alpha/frame-camera composition, arbitrary World PLY/SPZ cloud ingestion or progressive World LOD layout. Those gates remain separate from regional routing.
