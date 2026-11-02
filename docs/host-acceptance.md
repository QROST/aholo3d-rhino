# Manual host acceptance

Maintainer: CuriousArc · qrost. Version: 0.1.0.

The user confirmed the local Mac beta.5 startup/GS retest passed on 2026-11-02 and approved version 0.1.0. The checklist below remains the full validation scope; that confirmation does not mark every item or platform passed. The user operates Rhino manually; no computer-control automation is used.

> For the new native GS experiment, follow [GS host acceptance](gs-host-acceptance.md). Point-cloud checks below remain explicit diagnostic tests, not the World default.

## Offline / no service credits

1. Install the matching local Yak package; restart Rhino; run `AholoPanel`.
2. In Rhino plug-in properties verify publisher **CuriousArc**, description and independent plug-in GUID. The numeric assembly version is `0.1.0.0`; the package version is `0.1.0`.
3. Run `AholoImportPly` and select `fixtures/tiny-world.ply`. Expect a colored point cloud, automatic zoom and one Undo step. Redo, save, reopen and check colors/positions again.
4. Import into another document/unit system, use a different explicit scale and confirm its interpretation. Close/change the document while a larger file is parsing: the plugin must not place into the wrong document.
5. Verify narrow panel layout, keyboard input, file pickers and closing/reopening on macOS. Repeat separately on Windows and Intel Mac.
6. Verify connection/disconnect messages with an invalid Key without exposing it. Test remembered Key only with user-owned credentials; no screenshots of Keys.

## Real World service / may consume credits

1. Connect an international Key. Verify that connection itself creates no generation.
2. Submit a small indoor prompt once; confirm the persisted ID appears immediately and polling eventually reports a terminal status.
3. Download PLY, SPZ and panorama where available. Import PLY and confirm preview versus the full official 3DGS viewer is described accurately.
4. Repeat with one local reference image; verify upload and prompt semantics.
5. Close/reopen the panel while a known-ID job runs; continue refreshing without creating another job. Add an existing World ID.
6. For a deliberately interrupted create request, inspect official account history before retrying. Do not repeatedly submit to test recovery.

## Real RenderCloud service / may consume credits

1. Select a known-size colored cube, plane and curved Brep in a millimetre document. Use a perspective camera with identifiable left/right/up directions.
2. Submit at a small output size, follow mesh processing then render, download and open the image.
3. Verify orientation, physical scale, camera framing, basic colors and geometry. Specifically confirm whether fast mesh ingestion preserves glTF metres/Y-up or normalizes it; update the conversion only from observed evidence.
4. Repeat with a metre document representing the same physical scene; output scale/framing should match.
5. Test switching cameras after capture, multiple supported selected objects and unsupported block/point-cloud selection. No unrelated geometry should be uploaded.
6. Confirm orthographic and shifted cameras are rejected before upload. Document textures/lights are not expected to match this first version.
7. Test quota/permission errors and known-ID continuation without repeatedly submitting chargeable tasks.

Record platform, Rhino/runtime versions, package SHA-256, actual outcome and sanitized task IDs in a new validation entry. Do not mark Windows or cloud integration accepted based only on compilation/mocks.
