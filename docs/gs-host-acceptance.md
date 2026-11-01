# GS viewport acceptance / 原生 GS 视口验收

Maintainer: CuriousArc · qrost. Candidate: 0.1.0-beta.3. Status: **host tests pending**.

This document separates offline mathematical evidence from real Rhino rendering. A successful build, a mode name or visible proxy box is not a visual acceptance result. No account or API Key is needed for the local tests below. / 本文严格区分数学验证与 Rhino 实机表现；显示模式名称和包围盒不代表渲染通过。以下本地测试无需账户或 Key。

## First manual test / 首次手动测试

1. Install the matching local Yak candidate, restart Rhino manually and create an empty document. Run `AholoGsProbe`. / 安装对应平台包，手动重启 Rhino，新建空文档，运行 `AholoGsProbe`。
2. The active modeling viewport should use **Aholo GS (Experimental)**. Expect two layers of soft, anisotropic red/teal ellipses, one opaque blue Brep and one opaque ochre mesh. Expect smooth alpha edges, not opaque rectangles or point markers. / 应看到两层红色/青色柔和椭圆及两个不透明遮挡物，不应是硬矩形或中心点。
3. Orbit the view. A foreground solid must hide the splats behind it; splats in front must blend over the solid. There must be no ghost rectangle writing depth or full-screen image overlay. / 旋转视图，前方实体应遮住后方高斯；前方高斯应透明叠加于后方实体，不得出现写入深度的矩形边界或整屏图片覆盖。
4. Run `AholoGsDiagnostics` and record version, Rhino version, OS/architecture, mode, reported OpenGL/non-OpenGL backend, count and error. “Non-OpenGL” does not establish Metal. / 记录诊断信息，不能根据 Non-OpenGL 自动认定 Metal。
5. Two Undo operations remove the opaque test geometry then the GS block; Redo should restore both. The probe deliberately uses two separately named undo records. / 两次 Undo 分别撤销遮挡几何和 GS；Redo 应恢复。

Start with the colored probe, then test the existing generated world; no regeneration is needed. Beta.2 failed with a 212,864-splat scene. Beta.3 batches every visible splat, prepares in the background and skips saved-file thumbnail rendering. First verify color/alpha, then orbit/zoom continuously, stop and wait for the GS frame, and save/reopen. During preparation a bounding box is expected; it must not freeze navigation. Record `AholoGsDiagnostics` and its log. / 先测彩色小样，再复用原世界；无需重新生成。检查颜色/透明度、连续导航、静止后恢复与保存重开。准备期间的框是预期行为，但界面不应卡死。记录诊断和日志。

## Appearance and depth matrix / 外观与深度矩阵

| Check | Expected / 预期 | Current result |
| --- | --- | --- |
| Texture alpha × opacity | Smooth radial falloff, partial center transparency, no square corners / 柔和衰减、中心半透明、无方角 | Pending on Mac and Windows |
| Multiple GS layers | Far-to-near native draw matches the intended transparent layer order / 前后层混合顺序正确 | Pending |
| Mesh occluder | Before / between / behind GS layers behave differently / 前、中、后三种深度关系正确 | Pending |
| Brep / NURBS occluder | Native shaded render meshes participate in the same depth buffer / 原生曲面显示网格参与深度 | Pending |
| Point cloud occluder | Only actual drawn opaque points occlude, not an inferred continuous surface / 只由真实绘制的点片元遮挡 | Pending; add a small native point cloud manually |
| Projection | Perspective and orthographic views retain shape and camera alignment / 透视和正交均对齐 | Pending |
| SH color | Import `fixtures/gaussian-sh3.ply`; color changes with viewing direction and follows object rotation / 视角颜色随观察方向和对象旋转变化 | Pending |
| Large coordinates | Move a small GS far from the origin; no float-vertex collapse / 大坐标下不塌陷 | Double vertices enabled; host precision pending |
| Equal-depth layers | Repeat draws/camera changes without random tie-order changes / 同深度层次稳定 | Deterministic tie rule implemented; host ordering pending |

The CPU oracle is an independently implemented numerical reference, not an image captured from Rhino. Run:

```sh
dotnet run --project tools/Aholo.Reference -c Release -- --output artifacts/gs-reference
```

It emits four PNGs and `reference.json`. At the center, front red alpha=.5 and back blue alpha=.5 over black give RGB (.5,0,.25). With an opaque green plane in front, between, or behind, the expected RGB values are (0,1,0), (.5,.5,0), (.5,.25,.25). PNG output maps linear RGB directly to 8-bit without gamma correction. Rhino may apply different color management. / 中心闭式值用于核对数学，宿主颜色管理须另行验证。

The host's stock byte-color material clips HDR before blending, while the oracle retains it. This version must not be accepted as an HDR-equivalent renderer. / 原生材质预先截断 HDR，不能宣称与浮点参考完全等价。

## Document lifecycle / 文档生命周期

- Move, Rotate, uniform/nonuniform Scale, mirror, duplicate, hide/show, lock/unlock and hide the containing layer. Shape/covariance and SH direction must follow the instance; hidden objects must stop drawing. / 测试变换、复制、隐藏、锁定和图层可见性。
- Switch between two documents and multiple viewports. Each view must use its own camera, sorting and visibility; closing a document/view must release its render resources. / 多文档、多视口应独立，关闭后释放资源。
- Restart Rhino and reopen the 3dm without first running an Aholo command; the startup conduit should restore the saved GS mode. If Rhino has overridden plug-in loading, run `AholoGsMode` and record the exception. Save/reopen the 3dm. Source PLY is stored outside the 3dm under `Aholo-Rhino/assets/gaussians`, addressed by SHA-256. The file is not embedded. / 保存重开须恢复，外部 PLY 并未嵌入文档。
- Test missing-resource behavior using a disposable copy of the cache, then restore it and run `AholoGsMode`. A missing or changed asset should retain a visible placeholder plus diagnostics, never silently use center points. / 测试资源缺失后包围盒和提示，再恢复资源并重载。
- Import a malformed/truncated file and the RGB-only `fixtures/tiny-world.ply` with `AholoImportGs`. It should fail without placing an incomplete GS. / 损坏或缺高斯字段应拒绝，不生成半成品。
- Changing the target document, units or active command during asynchronous loading must prevent insertion into the wrong context. / 异步期间文档、单位或命令状态变化不得误放。

## Explicitly outside this candidate / 当前不验收为已支持

Arbitrary transparent Rhino/GS interleaving; clipping planes; nested GS blocks; selection-outline fidelity and editing grips; layout details; printed/captured/stereo output; Raytraced/Cycles integration; dedicated GPU shaders, HDR parity, chunked streaming/LOD and million-splat speed. A native dropdown option exists for the experiment; it is a Shaded-derived conduit mode, not a replacement path tracer. / 透明对象交错、裁切、嵌套块、选择轮廓、打印/捕获/立体、Raytraced、专用 GPU、HDR、分块 LOD 与百万级速度均需后续阶段。

## Realtime cloud gate / 实时云视口门槛

The REST/WebSocket/session library has offline protocol tests. A mode named “Aholo Cloud” is intentionally not registered until it can render a coherent scene. Before exposing it:

1. Validate with a real account that World PLY/SPZ can become a RenderCloud GS mesh ID; a world ID is not a mesh ID.
2. Verify the documented stream status schema, JPEG decode, camera convention, sequential patches and explicit server cleanup with actual service behavior.
3. Implement whole-scene ownership and supported geometry rules. Public OpenUSD supports Mesh and Gaussian Splat primitives; Points is not listed. Do not silently omit point clouds.
4. Establish how stale frames are handled. The public JPEG protocol has no frame ID/camera version, so the client cannot truthfully tag an image as belonging to the latest camera. RGB alone cannot produce depth-correct local/cloud compositing.

真实 GS 入库、协议与相机、场景类型和陈旧帧处理均通过后，再进入原生云视口。当前编译/mock 通过不代替这些条件。

Record each actual host/service result with version, device and scene before changing a Pending entry. / 只有实际测试结果才能将 Pending 改为通过。
