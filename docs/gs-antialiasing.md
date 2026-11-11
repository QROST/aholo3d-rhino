# Per-instance Gaussian antialiasing

CuriousArc · qrost. Native appearance acceptance remains pending.

GS parts with an antialiased source flag now use view-dependent opacity compensation. This includes the existing SPZ flag, compatible USD `gaussianSplatting:antialiased` metadata and saved block-reference metadata. Unmarked PLY/SPLAT assets keep classic mode. The flag belongs to the actual GS leaf occurrence: an ordinary parent block does not override it, and two references to one asset may use different modes. The frame identity includes the flag, so a committed attribute change invalidates the previous display frame.

For projected covariance C and the existing pixel-filter variance ε, the displayed opacity is multiplied by `sqrt(det(C) / det(C + εI))`. This preserves the continuous integrated opacity of an individual filtered Gaussian. It follows the [gsplat antialiased rasterization definition](https://docs.gsplat.studio/versions/1.5.3/apis/rasterization.html); the native viewer uses its existing ε = 0.3 on the accepted sampling grid. It does not infer a producer's unspecified training/filter settings. Camera, instance transform and pixel sampling all affect the factor. DPI-equivalent callbacks are normalized to the existing grid before projection.

Only projected opacity is adjusted. Source logits, scales, SH coefficients, prepared base opacity and cached PLY bytes remain unchanged. Export keeps the source flag and original opacity; repeated drawing/import/export does not accumulate compensation. The native opacity atlas still has 256 levels, so sufficiently small corrected values may quantize to transparent. RGB/HDR, depth ordering, transparent-object interleaving and host capture/layout limitations remain as documented.

## Numeric and performance boundary

The raw area calculation balances diagonal exponents and uses a compensated difference of products. This avoids losing representable opacity when an unscaled determinant underflows, including subnormal inputs, without an arbitrary minimum opacity.

Projection uses the principal-axis Gram factor instead of expanding the 3D covariance before applying the camera Jacobian. This fixes a reproduced near-singular but invertible shear case that could create a negative raw variance from rounding. It is the same covariance mathematically; floating-point evaluation order differs in both modes. Prepared data now stores nine axis entries instead of six covariance entries: an additional 24 bytes per Gaussian. Source covariance overflow remains explicitly rejected. Projection and sorting retain zero per-Gaussian managed allocation and no count cap; native speed still requires measurement.

## Reproduce offline evidence

Build Release and run `python3 fixtures/gaussian-antialias-verify.py`. This standard-library script checks 35 high-precision Decimal determinant cases and 24 independent affine projection/integral cases against both production projection paths. Cases include nonuniform reflected/sheared transforms, perspective/orthographic cameras, multiple focal lengths/depths, almost singular covariance and extreme binary exponents. It is a CI gate on both target platforms; local execution does not prove the remote Windows run passed.

The .NET suite covers original/source preservation, shared assets with opposite modes, cache identity, source-prefix isolation, sampling-grid equivalence, all SH0–3 degrees and 212,864-splat allocation checks in both modes. The paired USDA fixture is generated and read back by `Aholo.Reference --antialias-scene-fixture fixtures/gaussian-antialiasing.usda` using the configured OpenUSD interpreter. Its two parts retain identical source attributes and opacity 0.5 with opposing mode flags.

## Manual host acceptance (pending)

1. Import packaged `fixtures/gaussian-antialiasing.usda` using `AholoImportGs`. It uses metres/Z-up and contains two columns of four cyan Gaussians: classic on the left (X = −1.25 m), antialiased on the right (X = +1.25 m). Each column runs from Y = −1.2 to +1.2 m with increasing source scale 0.02/0.04/0.08/0.16 m; original opacity is 0.5 throughout.
2. In Top view, zoom out until footprints approach pixel size. Right-side opacity should reduce as the filter dominates; zooming in should make both columns converge. Repeat in perspective, orbit and resize. No batch/frame disappearance or stale alpha is expected. Very small values can still disappear because of atlas quantization.
3. Duplicate both parts, put them in an ordinary nested block and apply reflected/nonuniform transforms. Both source modes must follow their own leaf objects while sharing asset data. Hide/unhide and Undo/Redo. A committed change of the leaf's existing `Aholo.GS.Antialiased` value followed by redraw must take effect without moving the camera; unrelated parent/prefix values must not override it.
4. Save/reopen, then export/re-import USD/USDZ. Both flags and original opacity must remain, and there must be no second compensation baked into the source. Repeat with a marked SPZ and an existing compatible saved reference. Check fractional-DPI monitor changes without inventing pixel-identical renderer parity.
5. Record source commit/package hash, platform, Rhino/runtime, display backend and observations separately. These cases remain unexecuted in native Mac/Windows Rhino; no cloud render is required.
