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

## Review and execution boundaries

The user authorized this development batch's bounded third-party analysis, code drafts, test design and reviews using necessary non-sensitive source/documentation/diffs. Credentials, task data, personal paths and full conversation are excluded. The parent applies changes and executes validation. At most two workers and 4096 output tokens per task; uncertain outcomes are inspected, not retried automatically.

The user controls Rhino for visual/native acceptance. No desktop control, real charged service submissions, push or publication is implied by local implementation. No changes to the independent GSRhino project are required.
