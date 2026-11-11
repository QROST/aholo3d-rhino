# Realtime cloud scene editing

CuriousArc · qrost. Candidate: 0.2.0-alpha.12; native and live service acceptance pending.

## User flow

Open Render → Open cloud preview, then **Edit cloud scene**. Before starting, add one or more already-ingested cloud mesh or Gaussian asset IDs. The editor does not upload assets, convert a World PLY/SPZ to a cloud mesh ID, or mutate the Rhino document. A prepared Library render can seed the first mesh ID.

Add/edit/remove assets and Rect/Sphere lights in a private draft. Save before startup, or Apply during streaming; Cancel discards all draft changes. Model coordinates use millimetres/Z-up with an explicit one-time glTF conversion where needed, XYZ placement, XYZ rotation, uniform and independent axis scales, plus optional shear. Lights expose XYZ position, linear RGB, relative intensity and physical dimensions; selected Rhino rectangles/points can be captured. World-axis incremental rotation retains position and dimensions; zero leaves captured orientation exact, and new rectangles face local -Z. Environment brightness must be positive. The started preview can remove all assets/lights, but a new preview must start with an asset.

Existing asset payload/type and light type are fixed identities: replace by adding a fresh item and removing the old one. No geometry-count cap is added. Mirror/shear cloud fidelity and advanced light flags require live acceptance; user-facing item labels remain open work.

## Transform and light conventions

The common model scale stays positive. Independent X/Y/Z scale values are finite and nonzero; negative values mirror an axis. Shear uses three values: X from Y, X from Z and Y from Z. Advanced shape values sit in a collapsible section; closing that section does not reset them. Existing serialized models retain unit axis scales, zero X/Y rotations and zero shear.

For column-vector notation the linear transform is `A = Rz * Ry * Rx * H * D`, where `D = diag(Scale*AxisScaleX, Scale*AxisScaleY, Scale*AxisScaleZ)` and `H = [[1, XY, XZ], [0, 1, YZ], [0, 0, 1]]`. Apply `A * B` once, with B the selected asset-unit/axis conversion; translation is the final world-mm offset. Serialization transposes to USD row vectors. Rotation is about the converted asset origin before translation. Finite/invertible numerical validation is retained. The public guide includes nonuniform positive scale and rotated Mesh examples, but gives no explicit guarantee for mirrors/shears or extreme values; the UI identifies this live-service boundary.

Light rotation is an increment from the orientation when the light dialog opened: world X, then Y, then Z (`Rnew = Rz * Ry * Rx * Rold`). Position, physical size and other light settings remain separate. Reopening uses zero increments; it does not decompose or rebuild the captured orientation.

Advanced lighting exposes `inputs:exposure` (negative/fractional stops allowed), `inputs:specular` (finite nonnegative multiplier; no invented 0–1 maximum), `manycore:affectReflections` and Rect-only `manycore:doubleFace`. Effective intensity is `intensity * 2^exposure`; the final float-domain brightness must stay finite. Defaults preserve the earlier 0 exposure, 1 specular, reflection-enabled and one-sided rectangle behavior. Every patch explicitly writes false/zero as needed so an earlier enabled option can be turned off. Sphere lights never receive the rectangular two-sided field. Normalization stays false and light poses stay rigid, with size carried by width/height/radius.

The same validated light writer serves offline render requests, while these new UI controls are in the cloud-preview editor. The offline selection form still captures Rhino lighting with default advanced values. There is no claim of Rhino photometric/lumen parity.

## Authority and failure behavior

Each scene edit carries its base confirmed revision, new revision and complete privately-owned layout. The sidecar retains stable prim identities; deleting a middle item cannot renumber another object. Deleted paths are retired for the lifetime of the preview. One outstanding scene edit acts as a barrier in the same queue as camera edits. Only adjacent camera edits coalesce.

The sidecar commits the candidate after a successful backend response and a still-current credential lease. Duplicate identical requests do not replay the patch; mismatched versions/content, stale base revisions and retired paths fail before queuing. Returned layouts and submitted arrays cannot mutate the retained state. A JPEG arrival is not a scene/camera acknowledgement or proof of appearance.

Stopping, closing the document/window or changing credentials invalidates late confirmation. Uncertain failures close the original session and retain its cleanup obligation; no replacement paid session, automatic edit replay or compensating patch is sent. The provider has no documented atomic multi-object rollback, per-prim readback or JPEG-to-revision association. An edit acknowledged immediately before a connection change may have reached the service; the UI makes no claim to reverse it. Scene drafts bind to the reviewed connection, including while a modal editor is open.

## Contract and syntax evidence

The official RenderCloud JSON was refetched on 2026-11-11 and is byte-identical to the [recorded source and hash](api/README.md). Its embedded OpenUSD guide documents mixed Mesh/GS initialization, model and light additions with `def`, updates with `over`, and deletion at the original typed child/light path. Unsupported reference composition and deleted-path reuse are not invented.

The guide's unquoted `string manycore:operation:type = "del"` inside `customData` fails the actual OpenUSD 26.11 text parser. The writer uses the valid dictionary spelling `string "manycore:operation:type" = "del"`, retaining that literal key. This is different from the nested dictionary produced by `SetCustomDataByKey`; no unverified alternative marker is added. The same quoting rule applies to the documented reflection/two-sided light keys. OpenUSD parses/readbacks the literal keys and target types, but does not itself execute provider deletion or verify how those light flags affect rendering. **Service interpretation and deletion must be accepted live** before claiming this workflow complete.

`fixtures/render-verify.py` parses production-generated initialization/add/edit/delete layers. It composes additions and updates to check both payload paths/types, independent transforms, light positions/dimensions, environment and unchanged camera. For deletions it checks typed target paths and literal custom data only. Standard USD layer composition is an offline verifier, not a feature sent to RenderCloud.

## Manual acceptance

Pending on both macOS and Windows, in English and Chinese:

1. Add a known prepared mesh and GS ID, plus one sphere and two rectangle lights; start explicitly and inspect service usage. Compare units, axes and camera against a known-size fixture. Do not use a World PLY/SPZ as if it were already ingested.
2. Add/edit/remove items before and during streaming. Cancel nested and main dialogs; the prior state must remain. Change model XYZ, three rotations, independent signed scales/shear, light position/linear color/size and environment independently. Verify glTF conversion once and test mirrors/shears separately from positive-scale support. Capture rotated Rhino rectangles, apply world-axis increments, reopen/save at zero and verify direction/position/size. Toggle exposure, specular above 1, reflection and Rect two-sided emission, then reset each to its default/false value; inspect actual cloud appearance. No Rhino geometry should change.
3. Delete a middle light, the first/last light and each model type; remaining objects must retain their location and identity. Confirm deletion visually and on the service. Add a fresh item after deletion. If the literal delete marker is rejected, record the provider result and keep deletion unaccepted; do not silently substitute undocumented syntax or replay.
4. Navigate while an edit waits. Camera updates must preserve the scene barrier. Queued and acknowledged revisions must stay distinct until confirmation; delayed JPEGs must not unlock editing early.
5. Lose a patch reply, close the window/document, stop while a patch waits, and switch API account/region while a modal editor is open. Reject stale drafts/late acknowledgements, close only the original session and retain unresolved cleanup. Do not automatically create another session.
6. Review lists/buttons, resizing/scrolling, keyboard default/cancel actions and both platform dialogs. These native checks are not established by offline .NET tests or SDK compilation.
