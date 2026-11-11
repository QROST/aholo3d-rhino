# Local operations and command feedback

CuriousArc · qrost. Implemented after alpha.23; native acceptance remains pending.

Local work has an immutable operation ID, a document/task scope and a terminal state. A scope cannot start duplicate work. Cancelling a document snapshots the original operations: a later operation that reuses the same scope is not cancelled accidentally. An exact-ID cancellation is also available to application code. Cancellation requests do not retroactively change a successful commit into a cancelled result.

`AholoImportGs`, `AholoExportGs`, `AholoImportPly`, `AholoGsProbe`, `AholoPlaceTask`, `AholoImportScene`, `AholoExportScene` and `AholoExportMesh` print the same local operation ID at start and completion/cancellation/failure. A successful command return means the background operation was accepted, not that the model or file has finished. Follow its final message. The current document's panel footer shows command-side active work as well as panel work. `AholoImportMesh` still opens an interactive Library candidate; it is not a headless import command.

`AholoCancel` and the panel cancel button stop local waiting/preparation for the current document. They do not cancel cloud jobs, undo already committed objects or delete downloaded originals. Closing a document requests the same local cancellation. Task placement is registered before waiting for Rhino Idle; a cancelled queued placement removes its event handler without starting the placement. The diagnostic PLY import and GS probe now participate in this registry. A probe cancelled between its GS import and native occlusion geometry keeps the GS already committed; use Undo to remove it.

GS/PLY import and GS USD export accept paths from the Rhino command line in Scripted mode. Scripted OBJ/OBJ-ZIP task placement refuses to guess units, up axis or archive entry; use interactive task placement or Library to choose them. Other document/format validation remains unchanged. USD progress messages use the panel operation generation, so queued messages from an earlier operation cannot overwrite current work.

Pausing a task or revoking its connection authority stops the affected local query/continuation without recording a spurious connection failure. Unexpected transport cancellation/timeouts are failures, not user cancellation. Explicit choice dismissal and invalidated placement intent are distinguished from transport cancellation. No cancelled or unknown cloud submission is replayed automatically.

Session shutdown rejects new registered work, requests cancellation, waits for its cleanup and the polling gate, then disposes the shared client. Disposal is idempotent. A failing cancellation callback or cleanup step does not skip the remaining cleanup; accumulated shutdown errors are reported after cleanup. It is cooperative: a worker that ignores cancellation can delay shutdown. There is no blocking wait on the Rhino UI thread and no forced process termination.

## Evidence and manual acceptance

Portable regression tests cover scope replacement during cancellation, exact IDs, queued signal cancellation/unsubscription, duplicate signals, notification reentry, successful commit preservation, cleanup ordering, callback/client disposal failures, task pause, connection changes and unexpected timeouts. They do not run Rhino or prove host shutdown/event/layout behavior.

On both supported hosts, manually check:

1. Start a large GS/PLY import or USD export with the panel visible. The command's start/final IDs must match and the footer must show active work. Cancel from the command and from the panel; no new geometry should appear after cancellation unless it was already committed.
2. Queue `AholoPlaceTask`, then cancel before Idle or close its document. It must never start later or write into a different document. A normal placement must still respect units, document identity and Undo.
3. Run the GS/PLY import and GS export commands in Scripted mode with quoted paths containing spaces. No file dialog should appear. A scripted OBJ task must fail with the explicit interactive-choice instruction; an interactive OBJ choice cancelled by the user must say cancelled, not failed.
4. Cancel a probe before and after GS insertion. Verify the message and applicable Undo records. Do not treat cancellation as automatic rollback.
5. Pause an in-flight query; switch/reset a connection during a queued render continuation. No paid continuation should start after authority is revoked and no normal cancellation should become a refresh failure. Real cloud verification requires the agreed authorized test scope.
6. Close a panel/document, and quit Rhino during preparation. No late UI updates or lingering queued imports. Restart and verify retained task/cache data. Check narrow English/Chinese panel layouts and final command history.

These native checks have not been executed for this candidate. No package installation, cloud submission or publication is implied by local test results.
