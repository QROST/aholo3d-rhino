# GS display stability investigation

Maintainer: CuriousArc · qrost. Candidate: 0.1.0-beta.5 (includes beta.4 visibility changes). Date: 2026-11-01.

## Evidence

The user confirmed beta.3 red/teal probe display, colored world appearance and substantially faster navigation on the local Apple Silicon Mac. Basic clipping-plane interaction was also observed. Some viewpoints nevertheless showed no stable GS image, sometimes recovering after a wait.

The local GS log establishes repeated invalidation, not a failed download or corrupt PLY: in the 11:26 UTC minute there were **234 preparation starts, 233 prepared results and zero completed frames**. Each preparation still processed the complete 212,864-splat world in roughly 110–150 ms. Upload often reached only 16 or 32 of 104 batches before a newer generation replaced it. Later one generation completed all 104 batches. No preparation/draw exception was recorded in this sample.

Beta.3 logged starts/completions but not the changed camera component. Therefore the sample cannot retrospectively identify a particular pose/frustum/DPI value as the sole trigger. Source review found a feedback path: intermediate upload progress called `Redraw`, which could enter `PreDrawObjects` again with a different Retina pass size or automatically adjusted near/far values. Exact equality of the entire camera then discarded the upload and started again. Periodic native-view size changes could request additional redraws.

Rhino dynamically adjusts near/far planes to the visible scene. A display conduit should include its custom geometry bounds in scene calculations. See [McNeel's clipping-plane explanation](https://developer.rhino3d.com/guides/cpp/adjusting-clipping-planes-from-conduits/) and [CalculateBoundingBox](https://developer.rhino3d.com/api/rhinocommon/rhino.display.displayconduit/calculateboundingbox).

## Repair design

- Compare normalized projection separately from depth coverage. Equivalent logical/physical DPI passes reuse the same prepared world-space quads. Tiny arithmetic noise in derived focal/principal-point values does not restart preparation; actual camera position, rotation, zoom or aspect changes do.
- Reuse an existing frame only when its CPU depth interval covers the current interval. If the interval expands, rebuild with their union and a tiny outward relative margin (1e-9), retaining the same projection sampling. The margin prevents successive floating-point noise from repeatedly extending coverage by one ULP. This converges under alternating clip planes without permanently losing previously culled Gaussians. Native depth testing and clipping use Rhino's current pipeline.
- Remove intermediate progress redraws and polling-driven redraw feedback. Keep deterministic instance ordering and include transformed GS bounds in the conduit.
- Debounce actual camera/scene changes for 75 ms, and retire the previous frame only when the candidate is accepted. A resize rejected before native-view inventory catches up gets one redraw after independent size confirmation; routine size polling never invalidates frames.
- Record replacement reasons and viewport/camera differences so any remaining restart loop can be diagnosed directly.

No model is resubmitted or regenerated, and no input-count cap, point-cloud conversion or LOD reduction is introduced. A true camera change still prepares a new full frame and can temporarily show its bounding-box placeholder. This patch targets the failure to settle after the view stops; it is not a dedicated GPU billboard renderer.

## Acceptance

The beta.4 offline suite passed 221 tests. Twelve new regression cases cover alternating DPI passes, clip-range contraction/expansion, simulated upload completion and real pose/projection changes. The 256-observation simulation completes all 104 batches after one depth expansion; it does not execute native Rhino callbacks. The Release solution builds without warnings or errors. Host retest remains required at the reported angles. The previous probe/world-color result is user-reported host evidence, not an automated screenshot comparison. Basic clipping observed in one scene does not establish all clipping-plane combinations or exact Gaussian-volume clipping.

## Restart failure and beta.5 repair

The user reported that restarting into beta.4 failed before the plug-in finished loading, with “无法保存 Aholo GS 显示模式。” The exception came from an unconditional `UpdateDisplayMode` call in `PlugIn.OnLoad`. Read-only inspection found the existing Aholo GS mode and matching saved ID in local Rhino settings. The native API returned false; this does not establish whether the cause was startup readiness, an unchanged update or another native failure. Offline tests and package-byte checks did not exercise this startup path.

Beta.5 first verifies the existing mode's required flags and reuses it without updating the mode definition when already correct. If a change is required, it performs one update and rereads the manager's runtime state regardless of the boolean result. This confirms runtime usability, not disk persistence. Missing saved IDs recover by exact mode name before copying Shaded; new IDs are saved before subsequent configuration so retries reuse them. Other modes are not deleted or changed.

The panel loads independently. Renderer initialization starts on UI idle, with a UI timer for subsequent attempts (at most three total); `AholoGsMode` can retry explicitly. Failed partial initialization releases resources, and diagnostics retain the initialization stage. No cloud requests or model regeneration are involved.

The full offline suite passes 244 tests, including 23 registration-policy cases. Native startup timing and resource cleanup are compile-checked, not fault-injected in a running Rhino host.

Acceptance requires a successful Rhino restart, `AholoPanel` and original-world display, followed by another restart to verify stable mode identity and persistence. The beta.4 view-stability retest was blocked by the startup error and remains pending in beta.5.
