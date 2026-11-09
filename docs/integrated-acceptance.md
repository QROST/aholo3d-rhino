# Integrated alpha acceptance

CuriousArc · qrost. Candidate: 0.2.0-alpha.2. Native and service acceptance: pending.

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

## GS stability and recovery additions

- On Mac and Windows, rotate/zoom, stop, resize, and move Rhino between displays with different scaling. Display must settle without endless preparation; a true camera/window change must rebuild.
- Hide all GS, switch to a layout, or use a view with no projected splats. Diagnostics must describe that state rather than report zero-input success. With one valid and one missing asset, the valid scene remains usable and the overlay reports partial readiness.
- With disposable fixture copies, damage the local cache then re-import the original. Verify one corrupt backup and restored full counts. A document whose only recorded source is the corrupt cache must keep bounds and an actionable error. Never damage real model files for this check.

## Shared connection recovery

- Open two Rhino instances (including different task-directory overrides). Change or disconnect the saved Key in one. The other must reject its next authenticated request and clear automatic render continuation, then allow Settings → Restore saved connection. A session-only Key is never loaded into a different process.
- End a disposable background process between queries, restore the connection, and inspect history. The original task must remain; a MeshReady result needs an explicit Continue action.
- Pause/resume during mesh processing, and change connection while continuation is queued. Queued unsent rendering must be cancelled. A request already accepted by the service is not recalled.
- With a pending offline render, close/reopen the panel. Query recovery must work without creating another render; connection errors remain visible in Settings.

## USD runtime diagnostics

In Settings, run Check USD readiness with the private runtime present and absent. Confirm that the displayed installation command belongs to this plugin. The check must not install anything, expose credentials or block the UI; closing Settings during a check must not update disposed controls. Repeat imports/exports after restoring the configured runtime.

## Interaction follow-up

Start a download or refresh for task A, then select task B and one of its results. When A finishes, B and that result must remain selected; completion feedback names A and its output. Creating or explicitly restoring a task should still reveal that task. In Settings, cancel a connection check or USD readiness check without closing the dialog, then verify another operation can start.
