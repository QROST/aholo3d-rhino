# Using Aholo3D for Rhino

## Create

Choose a World, Lux3D or Spatial operation, enter its inputs and review the options. World generation accepts text and/or one reference image. Reconstruction accepts at least 20 images or supported video. Cloud operations use your selected region's API Key and may consume service credits.

The form checks schema types and service ranges while you edit. Incomplete text remains editable, but Generate is disabled until the form is valid. Local files are checked again before upload. A service face-count setting does not limit later Rhino imports.

Each click creates one task. After an uncertain reply, use history or the original service ID to recover the task; do not repeatedly generate.

## Library

Select a task and an output. The primary button offers the next available action:

| Result state | Main action |
| --- | --- |
| Available remotely | Download result |
| Cached supported geometry | Place in document |
| Cached image | Open image |
| Cached unsupported direct-import format | Open result folder |
| Recoverable render preparation | Continue render, with a credits notice |
| Queryable task | Refresh task |

**More result actions** contains alternate actions, including mesh preview. Downloading a file does not place geometry. **Open result folder** reveals the exact local asset.

Mesh candidates support fit-height on the construction plane, original size on the construction plane, or preserved scene coordinates. Acceptance creates a Rhino block with Undo. Repeated placement checks the existing task/output receipt before creating another object. Unsupported or animated files are retained for external use.

Search waits briefly while you type. **Load history** forces a disk refresh. Refresh and download address the original task; they do not generate another model. Pause polling stops local queries. Cancel stops local waiting/preparation and does not cancel a cloud job already submitted.

## Gaussian display

Import a complete GS PLY, SPZ or SPLAT, then switch to **Aholo3D GS (Experimental)**. A standard display mode shows a proxy box. Large scenes may show the box while a changed view is being prepared. Keep the original/cache assets available when reopening saved 3DMs.

Local file exchange supports separate GS and mesh routes, plus a combined scene USD route. Use the combined route for mixed contents. See [formats](formats.md).

## Render

Select supported Rhino geometry and use a perspective view for offline cloud rendering. The plugin uploads a snapshot of the supported geometry, materials, optional lights and camera. Surfaces are tessellated. The result is an image.

Live cloud preview is a separate window using existing ingested asset IDs. Its scene editor manages supported assets, transforms and lights. It is not a native Rhino depth-composited viewport, and arbitrary World GS ingestion is not guaranteed.

## Settings and drafts

Choose the region and API Key in Settings. Optional account sign-in is separate from API access.

For a saved Rhino document, safe import/render preferences and the last page/operation are remembered locally. Enable **Remember creation drafts for this document** to also keep input text and options across restarts. File selections, URLs and automatic placement are excluded. **Forget saved draft and stop remembering** removes the persisted draft and disables remembering.

Untitled documents keep drafts only in memory. Save As uses another document identity. If two Rhino sessions change the same workspace, a revision conflict preserves the existing saved state; reopen the document before saving again.

## Commands

| Command | Purpose |
| --- | --- |
| `Aholo3DPanel` | Open the panel |
| `Aholo3DWorld` / `Aholo3DModel` | Open the corresponding creation workspace |
| `Aholo3DTasks` / `Aholo3DRender` | Open Library or Render |
| `Aholo3DImportGs` / `Aholo3DExportGs` | Local Gaussian exchange |
| `Aholo3DImportMesh` / `Aholo3DExportMesh` | Local static mesh exchange |
| `Aholo3DImportScene` / `Aholo3DExportScene` | Combined static USD scene exchange |
| `Aholo3DGsProbe` / `Aholo3DGsDiagnostics` | Display probe and diagnostics |
| `Aholo3DCancel` / `Aholo3DCancelOperation` | Cancel local document work or one operation |

Navigation commands do not submit paid tasks. Earlier `Aholo*` spellings remain compatible. `Aholo3DImportPly` is an explicit point-center diagnostic, separate from GS import.
