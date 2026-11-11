# Workspace navigation and creation drafts

CuriousArc · qrost. Implemented after alpha.25; native Mac/Windows acceptance is pending.

| Command | Destination |
| --- | --- |
| `AholoPanel` | Open the document's panel, preserving its last page |
| `AholoWorld` | Create, using that document's most recent World operation |
| `AholoModel` | Create, using its most recent Lux3D operation |
| `AholoTasks` | Library |
| `AholoRender` | Render |

These commands navigate; they do not submit or resubmit generation/rendering. The shared session may continue its already authorized background work. New requests still require the explicit creation/render action. Existing command names and plugin identities remain unchanged.

The request is recorded before opening the native panel. A ready panel or the subsequent shown/Idle event consumes it once, only in its original active document. Newer commands or manual navigation supersede older requests. Closing/switching documents or a 30-second opening timeout removes a pending request. A generation result that arrives after a newer navigation stays in history without forcing Library into view. A superseded queued mesh import stops before preparing a candidate; run the import again when wanted.

Creation drafts and the last page are retained in process memory per open document, including after panel recreation. Each operation keeps its own name, raw text, selected local paths, URL text, service enum values, default/yes/no choices, empty selections, matrix cell text/enabled state and advanced-options state. Draft capture does not parse numbers, validate URLs, read source files, upload or submit. Incomplete values such as `1e-` remain editable. Validation still happens at explicit submission.

This is not disk persistence: closing the document or Rhino discards the workspace. It does not retain credentials, automatic placement authority, prepared native mesh candidates or running operations. Render options, Settings values, Library query/selection and section expansion are not part of this new recreation snapshot. Earlier panels cannot write their snapshot over a newer panel's ownership. Full native lifecycle behavior still requires the checks below.

Library keeps task/result actions visible, moves local-file exchange, task recovery and GS diagnostics into collapsed sections, and keeps placement/candidate controls in an expanded section. Loading or previewing a mesh opens its placement section. The original action handlers and cancellation rules are reused.

## Evidence and native acceptance

Portable tests cover immutable raw draft copies, empty/unvalidated values, independent document workspaces, remembered operation families, newest-request precedence, wrong-document refusal, manual navigation invalidation, old panel ownership and cleanup. The compiled plugin is inspected as metadata to check command names/GUIDs without loading RhinoCommon. These checks do not execute Eto controls or native panel events.

On both supported hosts, in English and Chinese:

1. Open each command with the panel initially closed, then already visible. Switch quickly between World/Model/Tasks/Render; only the newest destination should remain. Verify no new remote task is created.
2. Enter a name with whitespace, a multiline prompt, an unfinished number, chosen file paths, tri-state choices and an unchecked-all output list. For Spatial, edit all matrix cells while the transform is disabled. Close/reopen the panel; values and the last page must return exactly. Submitting an invalid draft must still fail validation.
3. Use two documents with different drafts and pages. Switch, hide, reopen and recreate panels; no input should cross documents. If multiple panel instances are available, older instances must not overwrite the current owner's saved draft. Close a document and create/open another; no old draft or pending navigation may return.
4. During an authorized creation/recovery, navigate to Render or another creation operation. Its eventual task remains in history without taking over the page. Issue a mesh import and then a newer navigation before Idle; the old import must not open Library later.
5. Collapse/expand Library sections at narrow and wide panel sizes. Check keyboard navigation, visible primary actions and the placement section opening for mesh candidates. Ensure headings, buttons and translated labels do not clip.
6. Close a document or quit during pending navigation. Verify no stale Idle callbacks, late control updates or retained document workspace after reopening. This does not establish wider native shutdown acceptance.

No native Rhino execution, real service submission, installation or publication is implied by the offline checks.
