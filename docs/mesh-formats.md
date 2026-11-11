# Mesh placement formats

Maintainer: CuriousArc · qrost.

| Format | Current import boundary |
| --- | --- |
| GLB | Static embedded geometry/materials supported by Rhino. Skins, animation and unsupported required glTF extensions fail before document mutation. |
| PLY | Ordinary polygon mesh with complete finite positions and valid face indices; ASCII and either binary byte order. Complete Gaussian fields use the separate GS importer. |
| STL | Original bytes passed to Rhino's native importer and audited as mesh geometry. |
| 3MF | Core static mesh, core components/build transforms and base materials. Package preflight precedes native Rhino import. Extension-bearing files are rejected explicitly. |
| OBJ/FBX archives, extended 3MF, ordinary mesh USD | Retained downloads; integrated placement remains pending. GS USD/USDZ has its own implemented exchange path; local ordinary mesh USD export is also implemented separately. |

All supported formats use a private suffix-preserving copy and the original SHA-256, headless native import, actual mesh audit/repair, candidate preview and one Block/Undo commit. Original downloads are retained. Repeated acceptance uses the existing import receipt instead of creating another object. There is no artificial vertex/face ceiling. Native parsing and memory use still depend on Rhino and the machine.

## Core 3MF boundary

The [3MF Core specification](https://github.com/3MFConsortium/spec_core/blob/master/3MF%20Core%20Specification.md) defines the package/model relationship and self-contained resources. Rhino 8 lists [3MF among its native file formats](https://docs.mcneel.com/rhino/8/help/en-us/fileio/_index_of_import_export_file_types.htm). These references establish the format route, not native acceptance of this plugin's full workflow.

The preflight indexes ZIP parts without extracting them and uses Microsoft's `System.IO.Packaging` 8.0.1 OPC URI resolver. It checks duplicate/ambiguous names, content types, the primary model relationship, all relationship parts (including unreachable ones), missing/external resources and all core model parts. XML disables DTDs and external resolution, with cancellation during reads. Package-absolute and valid parent-relative references remain supported. Escaped separators/unreserved characters, nested percent escapes, filesystem paths and references above the package root are rejected. For cross-platform portable naming, literal/escaped colons are also rejected even inside an otherwise legal OPC part segment; rename such parts when exporting.

Core base colors and ordinary components are passed unchanged to Rhino. Used foreign element/attribute content and required extensions—including material/textures, production, slicing and vendor extensions—are rejected rather than silently dropped. An unused namespace declaration is harmless. This is deliberately a core-only compatibility boundary. It does not claim full ZIP/CRC/XSD validation or sandbox an untrusted native importer; .NET and Rhino still decode the archive and geometry. Native materials, component transforms and Undo need the manual checks below.

## Native acceptance, still pending

On Mac and Windows, load `fixtures/mesh-core.3mf` (regenerate/check with `fixtures/create-core-3mf.py`). It contains blue/orange tetrahedra with a mirrored core component and centimetre units: before placement, expected mm bounds are X 0–30, Y 20–30, Z 0–10, with 8 triangles total. Compare native direct import with the plugin's candidate geometry before placement; confirm color and relative transform, then choose a reference height/CPlane/point/rotation. Confirm one resulting Block, one Undo/Redo, source unchanged, repeated acceptance does not duplicate and save/reopen retains geometry. Include mirrored components and multiple core objects.

Test an extended material/production 3MF separately: the plugin must explain the unsupported boundary and leave the document unchanged. Missing references, external targets, malformed XML and a canceled preflight must also leave the original document and source intact. Native import itself runs on Rhino's UI thread and cannot currently be interrupted safely in the middle of its native parser; cancellation before and after it must not commit unwanted objects.

Package checks must include `System.IO.Packaging.dll` in both the Rhino payload and sidecar, with its MIT license. A Mac build of the Windows package is not a Windows native pass.
