# Troubleshooting

| Symptom | What to check |
| --- | --- |
| OAuth works but generation is disabled | Add an API Key for the selected region. Account login does not grant API permissions or credits. |
| A task stays at zero progress | Some operations report no useful intermediate percentage. Refresh the original task and check service history; avoid generating again. |
| File downloaded but nothing appears | Select the output and choose **Place in document**. Downloads alone do not change the Rhino document. |
| GS shows only a bounding box | Switch to **Aholo3D GS (Experimental)**, wait for view preparation and check that the cached source exists. Run `Aholo3DGsDiagnostics`. |
| Import rejects a file | Check the supported static format subset. An animated GLB, point-only PLY or unsupported USD scene is not converted silently. |
| USD tools are unavailable | In Settings, run **Check USD readiness** and follow the optional runtime setup command. |
| The background service cannot start | Verify the .NET 8 runtime and reinstall the complete package; retain the `sidecar` directory. |
| Two plugin names appear | Remove the legacy `aholo-rhino` package and restart. Keep application data; see [upgrading](installation.md#upgrading-from-aholo-rhino). |
| History seems stale | Choose **Load history** to force a disk refresh. |
| Draft disappeared after restart | Save the Rhino document and enable remembering. URLs/files are intentionally excluded; untitled documents are memory-only. |
| Workspace changed in another session | Reopen the document. The existing saved state was preserved to avoid overwriting another session. |
| A task's submission status is unknown | Recover using cloud history or the original ID. An automatic resubmission could charge twice. |

## Reporting a problem

Include the package version, Rhino build, OS, command/page, expected result and reproducible steps. A small synthetic model is preferable to a complete project.

For GS issues, include the sanitized output of `Aholo3DGsDiagnostics` and whether the problem changes with display mode or camera movement. Test a copy before altering caches. Do not attach credentials, signed URLs, private prompts or unreviewed diagnostic archives.

[Report an issue](https://github.com/QROST/aholo3d-rhino/issues) · [Security reports](../SECURITY.md)
