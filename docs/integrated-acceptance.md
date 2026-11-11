# Integrated alpha acceptance

CuriousArc · qrost. Candidate: 0.2.0-alpha.16. Native and service acceptance: pending.

## Offline Rhino checks

Run separately on Apple Silicon Mac and Windows Rhino 8.32+, in English and Chinese. The user operates Rhino; automated source tests do not mark these passed.

1. Start `AholoPanel`. Verify Create / Library / Render and Settings at narrow panel widths. Choose all 13 operations; required controls and advanced options must remain usable. Enter a draft, switch operation, switch back: name, prompt, files and options survive.
2. Read local history without connecting. Search/filter/page; restore-selection tests must select the requested task, not the first unrelated entry. Existing old journals remain readable. Open a downloaded file's directory and compare the displayed location.
3. Start a long local import, close the panel and reopen. Verify one shared operation and accessible cancellation. Repeat via `AholoImportGs` / `AholoExportGs` and `AholoCancel`. Close the target document: no work may mutate a different document.
4. Place a downloaded static GLB: visible native Block on the selected construction plane/point, intended height, materials retained, one Undo/Redo cycle. Repeat the click and reopen the panel: no duplicate. Delete/Undo the result: no automatic re-insertion. Save/reopen 3DM and repeat identity check. Test another unit system, wrong active document and units changed during preparation.
5. Use GS probe and a known PLY/SPLAT/SPZ/USD scene. Test SPZ World placement twice, Undo/Redo and export to USDZ; verify all parts, opacity, SH colors and source axis. Verify source count, color, clipping and navigation as in the GS checklist. Unsupported PLY/animated GLB must fail with the original file retained.
6. While busy, open Settings and attempt connection changes: key entry must not disappear silently. Settings status must update after a connection operation. Inspect numeric plug-in version 0.2.0.0 and publisher CuriousArc.

## Authorized service checks

These require user-selected input data and paid-service authorization. They were not run by the development agent.

- Validate a Lux3D-only Key via Lux3D history. Confirm other service permissions are handled separately; OAuth alone does not enable API calls.
- Text→GLB, one/many-image→GLB and World generation/reconstruction: submit once; check exact remote ID and original-task polling, typed result slots, downloads, then supported placement.
- Material transfer/format export: request partial formats and confirm NOT_REQUESTED slots do not shift result identities. Four-view images retain all four views. Rig/retarget failures remain explicit; downloaded animation files are not claimed as playable in Rhino.
- Spatial: choose local aligned indoor PLY; confirm Asset upload precedes creation, result is a DSL file, then place wall surfaces, door/window outlines and oriented box Breps as one editable block. Check units and one Undo; no invented thickness or boolean openings.
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

## Mesh candidates and automatic placement

- Load three local/result static GLBs. Compare candidate colors; adjust height and rotation repeatedly, use current CPlane and pick a point. Acceptance creates only the chosen Block. Clear/hide/close removes preview geometry without removing an accepted model. Clearing during preparation must not restore the candidate later.
- Start from a hidden/locked current layer or an invisible ancestor layer. Accept, Undo and Redo; both the Block and any created visible layer belong to one operation, and the original current layer is restored.
- Generate with automatic placement enabled. While waiting, change the active document, close the target, change units, pause the task, turn the option off or disconnect. No stale automatic placement is allowed. Restoring history and restarting Rhino never grants automatic placement.
- Use `AholoPlaceTask` with the full local task GUID from Library. It may query/download the original supported static mesh; it must never generate. Repeat after success, delete and Undo: no duplicate is inserted. Cached static mesh placement works without a cloud connection.

## Native mesh formats

Compare ordinary ASCII/little-endian/big-endian mesh PLY and STL to Rhino's native importer, including quads/polygons, vertex colors and large files. Preview and acceptance must preserve the supported geometry without a point-cloud conversion. Confirm GS PLY remains a splat scene; point-only, partial-GS and ambiguous files fail with original bytes retained. Check hash mismatch/mutated fixture rejection and one Undo on Mac and Windows.

## Realtime cloud preview

Use only an authorized account and an already-ingested test mesh/GS ID. Opening the window itself must not create a stream; Start clearly indicates service credits. While orbiting the original perspective viewport, the nonmodal window updates JPEGs and receive times. Local geometry is not composited. Compare native-mm/Z-up and glTF-m/Y-up transforms against a known cube; never label local camera acknowledgements as frame IDs.

Exercise Start→immediate Stop/Close, document close/switch, changed units/viewports, account replacement, panel reopen, slow upload while previewing, lost create reply and failed DELETE. Observe that original sessions remain queryable/closable and no unknown creation is replayed. A missing session ID allows only explicit platform-confirmed resolution, clearly distinguished from verified server closure. Close Rhino during a disposable preview and verify the official platform: a crash cannot guarantee cleanup. On Mac/Windows in both languages, inspect window sizing, decoded JPEGs, memory during long playback, listener reconnection and cleanup-list usability. These remain manual/live acceptance checks.

## Optional account login

With no API Key, open Settings → Account sign-in, complete browser login, then confirm generation remains unavailable. Cancel while waiting, close/reopen the dialog, refresh explicitly and sign out. Neither login nor logout may change the existing API Key, task identity or realtime authority. Closing after a completed login retains the process-local session; closing while still pending cancels only that attempt. Restarting/resetting the sidecar requires account login again. Use Mac/Windows and both UI languages; no credentials or full authorization URLs belong in logs/screenshots.

## Material constants and scene lights

Render PBR objects with distinct metallic/roughness/alpha constants, a legacy material and object/layer/parent material inheritance through nested blocks. Check a parent with no assigned material and a child with its own display color. Compare input-color preprocessing on/off and a nondefault gamma; no document material should be mutated. Procedural textures, glass and per-face appearance remain unsupported; direct bitmaps have the separate check below.

Explicitly include selected world rectangle/point lights, including transformed/reflected blocks. Compare corner-to-center position, physical millimetres, rectangle emission direction and width/height against a known cube. Point preview radius is specified before block scaling; relative intensity is not Rhino lumen parity. Unsupported light types, sheared rectangles and nonuniform point scaling must fail before upload. Restart after mesh processing and continue the original render: saved light/camera values must survive later scene edits. Repeat on both platforms; these are manual/live checks, not passed by mock tests.


## Smooth normals and direct base-color bitmaps

Pending on native Mac/Windows and the real rendering service. Use a curved mesh with custom smooth normals and hard edges, nested rotation/nonuniform scale, and a mirrored copy. Lighting should follow preserved normals with outward winding; document meshes/materials must not change. Test a deliberately missing/zero normal separately.

Use an asymmetric four-color local RGB JPEG and RGBA PNG on a mesh with existing UVs and no explicit object/parent mapping. Check vertical orientation, repeat/clamp, nearest/linear, offset/rotation applied once, shared-image reuse, and PBR use-alpha on/off. Move/delete the source only after capture: a completed GLB should remain self-contained; a change during capture should fail. Missing paths, non-Modulate mode, linear/profile-tagged/EXIF images, explicit/WCS mapping, surface-generated UVs and embedded-only 3DM bitmaps must report the boundary before any upload. Disabling bitmap export is an explicit factor-only fallback. Do not call unsupported graphs visually equivalent.


## Realtime scene controls

Pending native/live acceptance: before starting, adjust world-mm XYZ, scale, Z rotation and environment brightness. Optionally capture selected Rect/Point lights; verify no Rhino object is changed. During streaming, edit position/environment/light intensity while navigating. The latest camera must not discard the scene adjustment. Confirm the queued/server-acknowledged counters differ while the server is delayed, and the settings dialog remains unavailable until the outstanding scene change is acknowledged. Force a failed/lost edit reply: preserve cleanup obligations, do not create a replacement session or replay the edit. Restart or switch credentials/documents during an open settings dialog and confirm its late result is rejected. Repeat on Mac and Windows. Follow the expanded [multi-object scene checklist](realtime-scene-editing.md#manual-acceptance) for additions/deletions, independent light positions/dimensions, first/last lights, cancellation and credential changes. Live deletion is not established by OpenUSD syntax checks.


## Core 3MF placement

Native Mac/Windows checks remain pending. Use the packaged `fixtures/mesh-core.3mf` and the expected colors, units and bounds in `mesh-formats.md`. Check mirrored/nested components, candidate placement, one Block, Undo/Redo, original hash, save/reopen and duplicate receipt protection. Use a material/production-extension file as a negative case: reject before document mutation without deleting the downloaded source.


## Local mesh USD export

Run `AholoExportMesh` and the panel action on both platforms. Follow the mesh checklist in `usd-exchange.md`, including mirrored/far-translated blocks, readonly/orthographic capture, page-space rejection, source materials unchanged, alpha/UV appearance, moved USDZ/plain companion bundles and cancellation/closure. This route needs no API Key. Automated USD parsing does not establish Rhino capture or native viewing acceptance.


### Local ordinary USD import (source after alpha.4)

Use the local mesh loader for USD/USDA/USDC/USDZ. Exercise textured triangles/quads, face-varying seams, nested/mirrored instances, Y/Z axes and normal document units; verify chosen height/CPlane placement and one Block/Undo. Change units/tolerance/color preprocessing during loading: preparation or acceptance must reject the stale snapshot. Close the document/cancel during processing. Clear the candidate’s temporary directory, save/reopen and verify embedded textures and source/derived hashes. Unsupported subdivision/nonplanar or self-intersecting n-gons/animation/invalid or overlapping subsets/mixed GS must fail with no document mutation. Record Mac and Windows native results separately; the automated OpenUSD checks do not establish this acceptance.


### Task mesh recovery and USD routing

For the same GLB/3MF/USD result, preview then accept, and invoke direct placement: only the original block is selected, never duplicated. Repeat after clearing the downloaded cache and disconnecting: existing placement/Redo guidance must work without network. For a missing unplaced cache, reconnect to the original account and recover only the selected output; a reordered response must not change the choice. Cancel, switch account, close/hide the panel or change the target document while downloading/converting: no late unwanted placement/candidate. Confirm the source task is not regenerated. Repeat in both native hosts.


### Mesh import command and placement modes

Run `AholoImportMesh` interactively and with a scripted quoted path, including spaces/non-ASCII characters. Confirm Library opens with a candidate only, no document object until acceptance. Repeat with a busy panel, an active command/Undo cycle, document switch/closure and `AholoCancel`; no request may silently land in another document.

Use a zero-height quad and a far-translated, physically sized USD. Original-size/CPlane must preserve scale while moving its base midpoint to the chosen point, with captured tilted plane/rotation. Scene-coordinate mode must preserve prepared world positions and ignore invalid text in disabled height/rotation controls. Fit-height remains the default and reports a clear alternative for planar meshes. Change candidates, clear, accept and reload: the mode is retained. Cancel or close/hide the panel during acceptance's native import: no final block should survive the canceled operation; verify one Undo/Redo on a successful import. These checks require both native platforms.


### Remembered OAuth account (source after alpha.6)

Use the optional account dialog independently of the API Key. With Remember off, restart the background process and verify account sign-out. With Remember on, complete login, restart and open account status: restore without browser/network refresh, showing expiry and refresh availability. Explicitly refresh and confirm the rotated session survives another restart. Confirm the selected checkbox survives status polling and that errors do not reveal tokens.

Open two Rhino instances under the same OS user: explicit sign-out in one must remove the saved account from both on their next status check, including while another login/refresh is in flight. Close a pending login dialog, cancel refresh, deny Keychain access, simulate Windows protected-store failure and retry sign-out. There must be no stale session resurrection or plaintext fallback. API Key connection and task ownership must remain unchanged. Native macOS Keychain prompts, Windows DPAPI behavior, dialog layout, real browser authorization and provider rotation are pending acceptance; synthetic fixture tests do not establish them.


## Regional account and cancellation acceptance

- Select China without a Key and verify China/disconnected persists; UI language must remain independent. With Settings open in two panels, a committed region change updates both selectors. Invalid replacement Key validation restores the retained connection and displays failure.
- Save separate regional Keys and OAuth sessions; switch/restart and verify separation. Forget/sign out one region without removing the other. Repeat in another Rhino instance. Mixed older/newer background versions are unsupported after credential-state migration.
- Retain tasks with colliding remote IDs in both regions. Refresh/download/continue and realtime cleanup require the original region and Key; interrupted cleanup retains its original record.
- During another panel's upload, close a pending account login. Its late token must not publish, and the upload must continue. Repeat while the sidecar starts; closing an already completed login preserves it.
- Verify real regional authorization, Keychain/DPAPI, uploads, service permissions and streaming using explicitly authorized accounts. Offline fixtures do not accept live behavior.


## Combined GS + mesh export

Use `AholoExportScene` and Library → Export selected scene USD with mixed and single-type selections, nested/repeated GS, mirrored/nonuniform blocks and both unit systems. Follow the combined scene checklist in `usd-exchange.md`, especially parent-material overrides, corrupted references, cancellation/document closure, page-space rejection and captured-state placement. Use `AholoImportScene` for the mixed round trip; separate typed importers still reject mixed content. Record macOS/Windows native results separately from the 24-file OpenUSD checks.


## Combined GS + mesh import

Import the packaged `fixtures/mixed-scene.usda` via `AholoImportScene` and the Library action. Expect two red/cyan GS siblings and one blue mesh block, with a 2-metre-high box converted to document units. Check GS display, opaque occlusion, source positions, one Undo/Redo and save/reopen. Repeat with textured external dependencies, native USD instances, mirrors/nonuniform scale and both axes. While a scene loads, cancel, close the original document or change units/gamma; there must be no stale or partial commit. Exercise a failure during the final GS append after mesh native import, including preexisting material-name conflicts; inspect material/resource rollback and any explicit unconfirmed-state warning. Repeat separately on Mac and Windows. Automated parsing and fixed-SDK compilation do not accept native Undo or resource persistence.


## Nested GS viewport references

Follow the [nested-block host checklist](gs-host-acceptance.md#nested-block-display-after-alpha10). Verify full parent transforms, independent repeated/coincident paths, hidden/locked layers, source cache lifetime, ordinary mesh visibility, BlockEdit, Undo/Redo and save/reopen on each platform. Nested proxy bounds intentionally remain; suppressing an ordinary parent block is never an accepted workaround. Source-level graph/projection tests do not accept native callbacks or visual appearance.


## USD polygon and material-subset import

On both native platforms import `fixtures/mesh-subsets.usda` with Original size or Preserve scene coordinates, then Zoom Extents. Expect green triangle, concave blue polygon and red quad as three mesh parts in one accepted Block. The blue source has 6 square metres of area and three triangles; its concavity must remain empty. Check source Mesh/subset user strings, material colors, physical units, one Undo/Redo and save/reopen. Repeat through `AholoImportScene` with a mixed GS scene. Use a separate textured fixture with shared vertices and discontinuous UVs/normals; no texture seam may move when faces split by material. No authored normals means host-generated per-part shading, not guaranteed smoothness across the material boundary. Invalid material partitions and nonplanar/self-crossing n-gons must fail before document mutation. Automated numeric/transport checks do not establish native appearance or texture persistence.


## OBJ and typed OBJ ZIP import

Follow [OBJ native acceptance](obj-import-development.md#reproducible-host-acceptance--pending) on Mac and Windows. The packaged textured sample and two-entry archive cover explicit source units/up-axis, entry choice, material transparency versus image alpha, fixed-source selection, candidate/direct paths, one result receipt, cancellation and persistence. Record native outcomes separately from offline reader tests.


## USD flat shading and planar concave quads

Use the packaged `fixtures/mesh-shading.usda` through `AholoImportMesh`, selecting **Preserve scene coordinates**. It uses metres/Z-up and should contain three parts with six triangles in total: a blue two-face corner with a hard edge, a translated blue corner with explicitly smooth shared-edge normals, and a red concave quad represented by two non-overlapping triangles. The red shape has area 3 square metres; its interior diagonal joins original corners 0 and 2.

On Mac and Windows, compare shaded/rendered views from both sides, check that the two blue corners differ as authored, and inspect the concave boundary for a filled notch or missing triangle. Repeat through `AholoImportScene`; verify physical positions, one Undo/Redo and save/reopen. For mirrored and left-handed files, confirm the geometric direction and authored shading remain consistent. Record source commit/package hash and observations. Automated math/file checks do not establish these native results.
