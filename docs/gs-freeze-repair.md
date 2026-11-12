# GS freeze investigation and beta.3 repair

Maintainer: CuriousArc · qrost. Date: 2026-11-01.

## Observed failure

A user-generated local PLY contains **212,864 Gaussian splats** in **14,475,168 bytes**, with degree-0 SH colors. The user confirmed eventual import: Wireframe displayed the intended block proxy, while the experimental GS mode appeared white and navigation was unusably slow. This is a rendering failure; successful download/import does not establish display acceptance.

The affected host was Rhino 8.36 (8.36.26296.17002), ARM64, macOS 27.0 (26A5425a). Read-only process sampling found about 82 GiB RSS. A virtual-memory summary reported a 71.1 GiB physical footprint and 327.8 GiB peak, plus 415,703 graphics regions. The later three-second sample reported a 72.9 GiB footprint with the same peak. These measurements are different memory metrics/timestamps, not interchangeable estimates of asset size.

The main-thread sample included document saving → `Write3dmFile` → `GetPreviewBitmap` → `DrawToDib` → `DrawShadedMeshes` → Metal buffer/material allocation. Thus saving the preview also exercised the expensive path. The original implementation allocated one native mesh and material per visible Gaussian on the UI thread, rebuilt them after camera changes, and issued one draw per Gaussian. This explains the resource growth and UI starvation; it does not prove a specific Rhino driver leak. The sampled Metal symbols establish this host's backend. No application process was terminated by the diagnostic tools.

Beta.2 had no persistent GS-specific log. Evidence came from OS process sampling and source review. The repair introduces a bounded asynchronous `gs-viewport.log` for future diagnosis.

## Repair

- Cache source covariance and opacity once; retain all original Gaussian/SH data.
- Project and globally sort across instances on one cancellable background worker. Drop stale computations when camera or instances change.
- Build batches of up to 2,048 splats, share one material and fixed 1,040 × 1,040 opacity atlas, and transfer/dispose meshes in short UI slices. There is no total input count cap or LOD decimation.
- Preserve vertex RGB using the Rhino 8 face-index drawing overload and keep texture alpha separate. Source color values were confirmed nonconstant. The old renderer used individual material colors, so the vertex-color overload alone does not establish the old all-white failure's exact cause. Native backend color/alpha remains a manual acceptance item. See [McNeel's explanation of the face-index overload](https://discourse.mcneel.com/t/drawmeshfalsecolors-but-with-shading/204585/8).
- Show the proxy during preparation; publish only a complete frame matching the current camera. Suppress GS rendering during document save, printing, capture and non-model/offscreen views.
- Log stage timings, cancellations, counts and errors without prompts, Keys or URLs.

The atlas quantizes display opacity to 256 levels, with maximum rounding error 0.5/255 before texture sampling; source opacity stays floating point. Byte RGB/HDR clipping and native color management remain existing display limitations.

## Verification boundary

An offline run on the actual complete PLY (a synthetic overview camera, no Rhino API) measured parse 173 ms, preparation 132 ms, projection 62 ms, sorting 46 ms and quad reconstruction 76 ms. All 212,864 splats were visible, corresponding to 104 batches. Total managed allocation across parsing and preparation was about 98 MiB. These are a single cold CPU observation, **not native draw times or navigation FPS**. Reproduce with:

```sh
dotnet run --project tools/Aholo3D.Reference -c Release -- --benchmark /absolute/path/to/world.ply
```

The source model and raw OS sample remain local and are not included in the repository/package. Automated equivalence/cancellation checks and release build status are listed in [validation](validation.md). Repaired color, transparency, ordering, navigation, memory plateau and save/reopen still require the [native host test](gs-host-acceptance.md). The updated package takes effect only after a manual Rhino restart; it cannot replace assemblies already loaded by beta.2.
