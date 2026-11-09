# Integrated alpha acceptance

CuriousArc · qrost. Candidate: 0.2.0-alpha.1. Native and service acceptance: pending.

## Offline Rhino checks

Run separately on Apple Silicon Mac and Windows Rhino 8.32+, in English and Chinese. The user operates Rhino; automated source tests do not mark these passed.

1. Start `AholoPanel`. Verify Create / Library / Render and Settings at narrow panel widths. Choose all 13 operations; required controls and advanced options must remain usable. Enter a draft, switch operation, switch back: name, prompt, files and options survive.
2. Read local history without connecting. Search/filter/page; restore-selection tests must select the requested task, not the first unrelated entry. Existing old journals remain readable. Open a downloaded file's directory and compare the displayed location.
3. Start a long local import, close the panel and reopen. Verify one shared operation and accessible cancellation. Repeat via `AholoImportGs` / `AholoExportGs` and `AholoCancel`. Close the target document: no work may mutate a different document.
4. Place a downloaded static GLB: visible native Block at origin, intended height, materials retained, one Undo/Redo cycle. Repeat the click and reopen the panel: no duplicate. Delete/Undo the result: no automatic re-insertion. Save/reopen 3DM and repeat identity check. Test another unit system, wrong active document and units changed during preparation.
5. Use GS probe and a known PLY/SPLAT/SPZ/USD scene. Test SPZ World placement twice, Undo/Redo and export to USDZ; verify all parts, opacity, SH colors and source axis. Verify source count, color, clipping and navigation as in the GS checklist. Unsupported PLY/animated GLB must fail with the original file retained.
6. While busy, open Settings and attempt connection changes: key entry must not disappear silently. Settings status must update after a connection operation. Inspect numeric plug-in version 0.2.0.0 and publisher CuriousArc.

## Authorized service checks

These require user-selected input data and paid-service authorization. They were not run by the development agent.

- Validate a Lux3D-only Key via Lux3D history. Confirm other service permissions are handled separately; OAuth alone does not enable API calls.
- Text→GLB, one/many-image→GLB and World generation/reconstruction: submit once; check exact remote ID and original-task polling, typed result slots, downloads, then supported placement.
- Material transfer/format export: request partial formats and confirm NOT_REQUESTED slots do not shift result identities. Four-view images retain all four views. Rig/retarget failures remain explicit; downloaded animation files are not claimed as playable in Rhino.
- Spatial: choose local aligned indoor PLY; confirm Asset upload precedes creation, result is a DSL file, and no automatic Rhino walls are claimed.
- Render a generated mesh Block plus Brep and a nested/reflected instance. Compare transforms, physical scale, base colors and camera to direct geometry. User geometry must not be exploded or changed. GS selections must fail before upload rather than render bounding boxes.
- Pause local polling, reset the connection, restart Rhino and recover MeshReady. Query/download must not submit a render; explicit Continue is required after authority is lost. Never interrupt/resubmit paid creations repeatedly for testing; verify official account records first.

Record platform, Rhino/runtime versions, source commit, package hash, observed result and sanitized identifiers. Do not record keys, signed URLs or private prompts. Realtime native mixed-depth cloud rendering is outside this alpha's acceptance claim.
