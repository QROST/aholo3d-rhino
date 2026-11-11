# Realtime cloud scene editing

CuriousArc · qrost. Local implementation after alpha.11; native and live service acceptance pending.

## User flow

Open Render → Open cloud preview, then **Edit cloud scene**. Before starting, add one or more already-ingested cloud mesh or Gaussian asset IDs. The editor does not upload assets, convert a World PLY/SPZ to a cloud mesh ID, or mutate the Rhino document. A prepared Library render can seed the first mesh ID.

Add/edit/remove assets and Rect/Sphere lights in a private draft. Save before startup, or Apply during streaming; Cancel discards all draft changes. Model coordinates use millimetres/Z-up with an explicit one-time glTF conversion where needed, XYZ placement, uniform scale and Z rotation. Lights expose XYZ position, linear RGB, relative intensity and physical dimensions; selected Rhino rectangles/points can be captured. Their orientation is preserved, and new rectangles face local -Z. Environment brightness must be positive. The started preview can remove all assets/lights, but a new preview must start with an asset.

Existing asset payload/type and light type are fixed identities: replace by adding a fresh item and removing the old one. No geometry-count cap is added. Advanced affine/light controls and model labels are still open work.

## Authority and failure behavior

Each scene edit carries its base confirmed revision, new revision and complete privately-owned layout. The sidecar retains stable prim identities; deleting a middle item cannot renumber another object. Deleted paths are retired for the lifetime of the preview. One outstanding scene edit acts as a barrier in the same queue as camera edits. Only adjacent camera edits coalesce.

The sidecar commits the candidate after a successful backend response and a still-current credential lease. Duplicate identical requests do not replay the patch; mismatched versions/content, stale base revisions and retired paths fail before queuing. Returned layouts and submitted arrays cannot mutate the retained state. A JPEG arrival is not a scene/camera acknowledgement or proof of appearance.

Stopping, closing the document/window or changing credentials invalidates late confirmation. Uncertain failures close the original session and retain its cleanup obligation; no replacement paid session, automatic edit replay or compensating patch is sent. The provider has no documented atomic multi-object rollback, per-prim readback or JPEG-to-revision association. An edit acknowledged immediately before a connection change may have reached the service; the UI makes no claim to reverse it. Scene drafts bind to the reviewed connection, including while a modal editor is open.

## Contract and syntax evidence

The official RenderCloud JSON was refetched on 2026-11-11 and is byte-identical to the [recorded source and hash](api/README.md). Its embedded OpenUSD guide documents mixed Mesh/GS initialization, model and light additions with `def`, updates with `over`, and deletion at the original typed child/light path. Unsupported reference composition and deleted-path reuse are not invented.

The guide's unquoted `string manycore:operation:type = "del"` inside `customData` fails the actual OpenUSD 26.11 text parser. The writer uses the valid dictionary spelling `string "manycore:operation:type" = "del"`, retaining that literal key. This is different from the nested dictionary produced by `SetCustomDataByKey`; no unverified alternative marker is added. OpenUSD parses/readbacks the literal key and target type, but does not itself execute provider deletion. **Service interpretation and deletion must be accepted live** before claiming this workflow complete.

`fixtures/render-verify.py` parses production-generated initialization/add/edit/delete layers. It composes additions and updates to check both payload paths/types, independent transforms, light positions/dimensions, environment and unchanged camera. For deletions it checks typed target paths and literal custom data only. Standard USD layer composition is an offline verifier, not a feature sent to RenderCloud.

## Manual acceptance

Pending on both macOS and Windows, in English and Chinese:

1. Add a known prepared mesh and GS ID, plus one sphere and two rectangle lights; start explicitly and inspect service usage. Compare units, axes and camera against a known-size fixture. Do not use a World PLY/SPZ as if it were already ingested.
2. Add/edit/remove items before and during streaming. Cancel nested and main dialogs; the prior state must remain. Change model XYZ/scale/Z angle, light position/linear color/size and environment independently. Capture rotated Rhino rectangles and verify direction. No Rhino geometry should change.
3. Delete a middle light, the first/last light and each model type; remaining objects must retain their location and identity. Confirm deletion visually and on the service. Add a fresh item after deletion. If the literal delete marker is rejected, record the provider result and keep deletion unaccepted; do not silently substitute undocumented syntax or replay.
4. Navigate while an edit waits. Camera updates must preserve the scene barrier. Queued and acknowledged revisions must stay distinct until confirmation; delayed JPEGs must not unlock editing early.
5. Lose a patch reply, close the window/document, stop while a patch waits, and switch API account/region while a modal editor is open. Reject stale drafts/late acknowledgements, close only the original session and retain unresolved cleanup. Do not automatically create another session.
6. Review lists/buttons, resizing/scrolling, keyboard default/cancel actions and both platform dialogs. These native checks are not established by offline .NET tests or SDK compilation.
