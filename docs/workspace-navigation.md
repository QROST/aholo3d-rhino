# Workspace navigation and creation drafts

CuriousArc · qrost. Current behavior: alpha.33; native Mac/Windows acceptance is pending.

| Command | Destination |
| --- | --- |
| `Aholo3DPanel` | Open the document's panel, preserving its last page |
| `Aholo3DWorld` | Create, using that document's most recent World operation |
| `Aholo3DModel` | Create, using its most recent Lux3D operation |
| `Aholo3DTasks` | Library |
| `Aholo3DRender` | Render |

These commands navigate; they do not submit or resubmit generation/rendering. The shared session may continue its already authorized background work. New requests still require the explicit creation/render action. Existing command names and plugin identities remain unchanged.

The request is recorded before opening the native panel. A ready panel or the subsequent shown/Idle event consumes it once, only in its original active document. Newer commands or manual navigation supersede older requests. Closing/switching documents or a 30-second opening timeout removes a pending request. A generation result that arrives after a newer navigation stays in history without forcing Library into view. A superseded queued mesh import stops before preparing a candidate; run the import again when wanted.

Concurrent operations have individual navigation authority. Only the latest initiated reveal may select its result automatically, and a later user navigation or panel ownership transfer overrides it. Failed duplicate admission does not take away an already running operation's reveal authority. Passive downloads, exports and readiness checks do not compete for this authority. [Operation coordination and manual tests](operation-lifecycle.md).

Creation drafts and the last page are retained in process memory per open document, including after panel recreation. Each operation keeps its own name, raw text, selected local paths, URL text, service enum values, default/yes/no choices, empty selections, matrix cell text/enabled state and advanced-options state. Draft capture does not parse numbers, validate URLs, read source files, upload or submit. Incomplete values such as `1e-` remain editable. Pure schema validation now also runs after edits; final capture repeats that same validator before explicit submission. Raw incomplete text remains unchanged.

Since alpha.33, saved documents retain their last page/operation and safe preferences in `ui/workspaces/<document-path-hash>.json` under the plugin data directory. **Settings → Tasks and storage → Remember creation drafts for this document** opts into persisting per-operation text/options. **Forget saved draft and stop remembering** removes persisted draft fields and disables future draft writes; the current live form stays editable. URLs and file selections are excluded, including signed asset URLs. Credentials, upload overrides, placement authority and native mesh candidates are never included. The default remains no draft persistence.

Preferences cover GS scale/up axis/source units, render resolution, texture/light inclusion, point-light radius (mm) and relative light multiplier. Document-unit mesh height, auto-placement, session-global polling, Library filters/selection and arbitrary section expansion are not persisted. Untitled documents remain isolated in memory until saved. Save As creates a separate bucket without overwriting an existing bucket. Moving/renaming a 3DM outside Rhino changes its lookup identity. Paths use normalized full path spelling (case folded on Windows), not an embedded document GUID; aliases with different spelling may have separate state.

Writes follow a 750 ms edit delay and flush on hide, owner transfer, close and shutdown. Per-file locks plus revision checks reject stale Rhino sessions; original files survive failed writes, unsupported versions and corrupt JSON. A 2 MiB bound applies only to UI JSON, never geometry. Correcting an oversized draft can retry; revision conflicts require reopening the document. Earlier panel owners cannot overwrite newer owners. Native lifecycle acceptance remains pending.

Library exposes one fixed primary result button: download an uncached result, place cached supported geometry, open a cached image, open an unsupported-format folder, continue an explicitly recovered render, or refresh a queryable task. Offline mesh placement receipt lookup remains available. Secondary actions live in **More result actions**, and original invocation/paid-consent paths are reused. The primary caption retains the credit notice for render continuation. Mesh candidate controls open on preview/load.

Search uses a 250 ms edit delay and cancels superseded reads. A read-only index caches only persisted SUCCEEDED/FAILED/CANCELED records. Active/recovery-sensitive/error records always go through authoritative journal reads; returned pages deep-copy nested arrays and extension fields. Directory enumeration notices additions/deletions, metadata detects ordinary atomic replacements, and selected-task reveal rereads its identity. **Load history** forces a complete refresh. A same-size external rewrite with an identical timestamp can remain stale until that refresh; direct task mutations never trust the index.


## Evidence and native acceptance

Portable tests cover immutable raw draft copies, empty/unvalidated values, independent document workspaces, remembered operation families, newest-request precedence, wrong-document refusal, manual navigation invalidation, old panel ownership and cleanup. The compiled plugin is inspected as metadata to check command names/GUIDs without loading RhinoCommon. These checks do not execute Eto controls or native panel events.

On both supported hosts, in English and Chinese:

1. Open each command with the panel initially closed, then already visible. Switch quickly between World/Model/Tasks/Render; only the newest destination should remain. Verify no new remote task is created.
2. Enter a name with whitespace, a multiline prompt, an unfinished number, chosen file paths, tri-state choices and an unchecked-all output list. For Spatial, edit all matrix cells while the transform is disabled. Close/reopen the panel; values and the last page must return exactly. Submitting an invalid draft must still fail validation.
3. Use two documents with different drafts and pages. Switch, hide, reopen and recreate panels; no input should cross documents. If multiple panel instances are available, older instances must not overwrite the current owner's saved draft. Close a document and create/open another; no draft may cross identities. Reopening the same saved document restores only its explicitly remembered fields. Pending navigation must not return.
4. During an authorized creation/recovery, navigate to Render or another creation operation. Its eventual task remains in history without taking over the page. Issue a mesh import and then a newer navigation before Idle; the old import must not open Library later.
5. Collapse/expand Library sections at narrow and wide panel sizes. Check keyboard navigation, visible primary actions and the placement section opening for mesh candidates. Ensure headings, buttons and translated labels do not clip.
6. Close a document or quit during pending navigation. Verify no stale Idle callbacks, late control updates or retained document workspace after reopening. This does not establish wider native shutdown acceptance.

No native Rhino execution, real service submission, installation or publication is implied by the offline checks.


## Narrow layouts and action feedback (after alpha.29)

Settings groups Connection/account, Tasks/storage, Import/placement and Uploads/USD tools. Close stays outside the scrolling body. Import/upload defaults are applied on close; account actions and polling changes keep their existing immediate behavior. Account login, cloud history and OBJ options share the same scrolling body/footer pattern. Main action groups keep a stable vertical order; only short title/file/pagination toolbars stay horizontal. Matrix fields share four scaled columns and are disabled until alignment is enabled. Library candidate controls are initially collapsed and open when loading/previewing a candidate.

Display captions are shorter, with the original descriptions in tooltips. Internal operation identity, cancellation/reveal authority and paid-action notices remain intact. Empty-ID restore, Lux restore without the original tool, empty-key connection and empty candidate acceptance are disabled. Poll pause/resume controls reflect task state; cloud history cancellation is enabled only while work is active. Opening cloud preview without a selected task remains supported, because its scene editor accepts existing asset IDs manually.

Manual acceptance (both languages, Mac then Windows):

1. At approximately 320, 460 and 700 logical pixels, inspect Create/Library/Render and all four dialogs. Expand/collapse Settings groups and advance to long option/filename/task messages. The close footer remains visible; text/controls remain reachable. Below the 280-pixel content floor, native horizontal scrolling is intentional, not silent clipping.
2. Resize across the previous 640-pixel threshold: action orientation stays stable. Repeatedly open/close while resize callbacks are queued; no exceptions or resize loop. Expand content until a vertical scrollbar appears and disappears.
3. Use Tab/Shift-Tab/Space and OBJ Enter/Escape; ensure matrix cells retain all values when disabled/re-enabled. Check long English and Chinese labels and high-DPI scaling.
4. With no account/task/result/candidate, confirm irrelevant actions are disabled. Enter/clear a World ID and a key, choose/clear a Lux tool, pause/resume an active original task, load/remove/clear candidates. No extra submission or automatic retry occurs.
5. A paid button still visibly says it uses credits. Connection changes keep the existing authority behavior; test operation completion and cancellation while Settings is open.

This is source/static evidence until the user performs the native checks. The scrolling helper uses a coalesced, disposal-guarded UI callback and the visible viewport width instead of a guessed 24-pixel reserve. Its APIs were checked against the installed SDK; upstream [Scrollable contract](https://github.com/picoe/Eto/blob/develop/src/Eto/Forms/Controls/Scrollable.cs) is supporting documentation, not proof that every installed platform handler behaves identically.


## Wrapped-label height correction (alpha.31)

The alpha.30 Mac screenshot exposed oversized vertical label regions. Wrapped labels now leave preferred width unset, use explicit left/top alignment and share one helper; their parent stacks do not override label alignment. Scroll content does not expand vertically to fill unused space. Viewport width fitting remains responsible for the actual wrapping width. Avoid using a one-pixel preferred label width as a shrink hint: the Mac handler can use it to calculate text height.

After restarting with alpha.31, check Settings first: Service region, its selector and Apply button should form compact successive rows, and connection status should not occupy a large blank region. Then inspect Create, Library and Render at narrow/wide widths and with long English/Chinese text. Increase window height: spare space should remain outside the compact form rows. Confirm vertical scrolling and the fixed Close footer still work. Native confirmation is pending; compilation alone does not establish this result.

## Alpha.33 manual checks

1. At 320, 460 and 700 logical pixels, inspect Library with uncached/cached GS, mesh, image and archive outputs. Its primary button stays reachable; secondary controls appear once in More. Test offline mesh receipt lookup and a recovered render; no click other than explicit paid creation/continuation may submit work.
2. Enter/clear a prompt; enter incomplete, nonfinite, fractional and out-of-range face counts. Check inline errors, service range hints, advanced fields, Chinese/English and Generate enablement. Choose/clear files and URLs; switch operations. All 13 forms must remain usable.
3. Type rapidly in a large history, change filters/pages, and select another task while an older read is pending. Latest selection wins. Update a task externally, then Load history. Existing refresh/download/placement must keep original task identity.
4. Save two test 3DMs. Opt into draft remembering for only one; edit its prompt, unfinished number, matrix and choices. Restart manually. Only opted-in text/options return; URLs/files and auto-placement do not. Check scale and render preferences; mesh height starts in current document units.
5. Forget a saved draft, restart and confirm it stays forgotten. Try an untitled document, first Save, Save As and two Rhino processes writing the same saved document. A revision conflict must preserve the first committed snapshot and show a recoverable message. Do not damage real workspace files for corruption tests.

Synthetic 2,000-record timing on Apple Silicon (.NET 8): cached median 5.28 ms versus forced-read median 69.00 ms over 15 iterations; cold scan 166.92 ms. This measures journal reads only, not native UI latency or live tasks. [Raw measurements](benchmarks/history-index-2026-11-12-arm64.json).
