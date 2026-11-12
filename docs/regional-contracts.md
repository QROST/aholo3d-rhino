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

## Implemented runtime and remaining acceptance

Settings now offers explicit International/China selection. Aholo3DRegions controls gateway/prefix, issuer/resource and verified OUS host; language and credential text never infer a region. API identities, saved credentials, tasks/recovery and realtime cleanup retain region. OAuth has independent regional slots and validated envelopes. Unmarked old data remains International.

The shared credential lock/path is preserved, with schema 2 preventing older builds from restoring China as International. Do not mix old/new sidecars. Switching stops local previews, invalidates continuation authority and restores only the target saved Key; other-region saved credentials survive. Admitted requests may finish in their captured region, and unresolved cleanup keeps its original regional identity.

Offline tests cover every service family’s fixed gateway, both OUS hosts and query-based multipart initialization, opposite-host rejection, legacy migration, slot isolation, stale validation/epoch rejection, task/cleanup ownership, OAuth issuer separation and two complete local-loopback PKCE paths with synthetic HTTP. Real private-pipe cancellation is exercised while the foreground semaphore is held. No live account, OS credential-store or paid service acceptance is claimed.

Native acceptance must check two Settings windows, switching/restart, Keychain/DPAPI, regional browser login/refresh/sign-out, uploads, generation/reconstruction, render ingestion and streaming. Public contracts still do not establish cloud depth/alpha/frame-camera composition, arbitrary World PLY/SPZ ingestion or progressive World LOD layout; those gates remain separate.
