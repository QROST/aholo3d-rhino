# Data and privacy

## What is sent

Cloud generation sends the prompt and selected inputs required by that operation. Cloud rendering sends a prepared snapshot of supported selected geometry, materials, optional lights and the camera. Live cloud preview sends the configured asset references, scene edits and camera updates.

Cloud processing is performed by the selected Aholo3D regional service. Review the provider's terms and data practices before submitting private material. Local viewing and file conversion do not require cloud submission.

## Credentials

API Keys and OAuth account sessions have different roles. OAuth login does not replace the API Key used by generation and rendering.

When requested, credentials are protected through macOS Keychain or Windows current-user DPAPI. Otherwise they remain in process memory. International and China credentials are separate. Disconnect/sign-out controls clear the relevant saved local credentials; they do not cancel remote tasks.

Never put credentials in prompts, issue reports, screenshots or source files.

## Local storage

| System | Default data directory |
| --- | --- |
| macOS | `~/Library/Application Support/Aholo3D-Rhino` |
| Windows | `%LOCALAPPDATA%\Aholo3D-Rhino` |

An existing legacy directory may continue to be used for compatibility. Explicit configured data-directory overrides take precedence.

Tasks, original inputs, downloaded assets, derived caches and diagnostics are local. Saved 3DMs can refer to external GS assets; deleting the cache may break display. Use Library's **Open result folder** to locate a specific output.

Task journals omit temporary signed download URLs. They still contain task names, service IDs and local asset references. Treat them as project data.

## Remembered workspace

Safe preferences are stored under `ui/workspaces`. Draft text/options are saved only when **Remember creation drafts for this document** is enabled. These local files are access-restricted on supported platforms but are not a credential vault or an encrypted project store.

URLs, file selections, upload overrides, credentials and automatic placement are excluded. Disabling remembering or choosing Forget removes the persisted draft fields. A saved document's normalized path determines its workspace identity; moving or renaming the document may create a new workspace.

No workspace synchronization is implemented. When reporting a problem, share only the minimum sanitized diagnostic information; inspect files before attaching them.
