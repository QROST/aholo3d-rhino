# ADR 0001 · 原生 Gaussian Splatting 视口与实时云渲染

日期：2026-11-01。作者：CuriousArc · qrost。状态：方案已制定，按下述门槛逐步实施。

## 目标和不可偷换的概念

在 Rhino 原生建模窗口中显示真正的 3D Gaussian Splatting（GS），保留其外观；与 Mesh、NURBS/Brep 的显示几何及点云发生可验证的遮挡。用户可在原生显示模式菜单中选择 Aholo 模式。世界资产不默认转换成 Rhino PointCloud；已有点云导入仅作为显式诊断工具保留。

“在窗口内看到图片”“能够读取 PLY”“存在一个显示模式名称”均不等于完成该目标。必须分别验证图像数学、深度关系、原生对象生命周期、帧同步和实际性能。

## 已核实的事实

- [Krill 作者说明](https://discourse.mcneel.com/t/krill-3d-gaussian-splats-in-rhino-8-closed-beta/222338)：GS 由自己的视口渲染器绘制，Rhino 块实例承担文档对象身份，支持原生几何深度关系；公开测试范围为 Windows。其展示证明此类插件可行，但没有公开可直接复用的实现或 Mac 兼容性保证。
- Rhino 8.32 的 [DisplayConduit](https://developer.rhino3d.com/guides/rhinocommon/display-conduits/) 与 [DisplayPipeline](https://developer.rhino3d.com/api/rhinocommon/type/rhino.display.displaypipeline/) 提供视口绘制、深度测试/写入状态栈及透明材质绘制。`PostDrawObjects` 和 `DrawForeground` 的深度状态不同，不能随意互换。
- [Material](https://developer.rhino3d.com/api/rhinocommon/type/rhino.docobjects.material/) 支持 `DisableLighting`、`AlphaTransparency`、纹理和材质透明度。可以据此构造公开 API 的小场景原型；纹理 alpha 组合、绘制排序和后端差异仍需实机验收。
- [RealtimeDisplayMode](https://developer.rhino3d.com/en/guides/rhinocommon/render-engine-integration-interactive-viewport/) 提供注册自定义原生实时视口模式的入口；旧教程的 Windows 说明不能自动外推到 Mac。8.32 中具体接口是否存在以本机 SDK 为准。
- 当前 [Aholo RenderCloud 官方规范](https://labs.aholo.ai/api-docs/specifications/services/rendercloud/openapi.en.json) 与仓库快照一致：实时流返回 JPEG；未公开 depth/alpha/AOV、帧号、时间戳或相机版本关联字段。完整场景允许 Mesh 和 `ParticleField3DGaussianSplat`，未列出 Points。
- World 返回 worldId 和 PLY/SPZ 等 URL；云 GS 引用需要独立 meshId。公开文档没有保证 World→meshId 桥接或 PLY/SPZ 入库支持。不得拿 worldId、任务号或 sourceReferId 冒充 meshId。

## 从像素形成过程推导架构

一个 Gaussian 至少包含中心 μ、三个尺度、旋转、不透明度和视角相关 SH 颜色。按公开格式保留 log-scale、logit-opacity、四元数及 SH 原值；显示时才激活/投影。不能把协方差或透明度丢弃后再称为 GS。

空间协方差 Σ = R diag(s²) Rᵀ。先用相机基变换，再用投影 Jacobian 得到屏幕二维协方差，加明确的像素滤波项。每个像素使用椭圆二次型计算 Gaussian alpha，按深度顺序合成颜色。建立独立 CPU 参考栅格器作为后续 GPU 后端的数值对照，不把参考实现的性能当成最终性能。

对于已绘制的不透明 Rhino 几何，GS 每个片元应与同一相机/尺寸的原生深度比较；GS 自身 alpha 合成不能通过简单写入中心深度把后方全部遮死。标准 GS 的中心排序/平面深度仍是近似，尤其近裁面附近和相交透明层。透明 Rhino 对象与多层 GS 的任意交错需要统一透明队列或更强的多层合成，不能由一张 RGBA 加单层深度彻底解决。

本地和云端因此共用场景/相机语义，但不强求共用同一种输出合成方式：

- **本地 Aholo GS**：GS 渲染器与 Rhino 原生深度协同，原生对象继续由 Rhino 绘制。先验证不透明对象，再处理透明混合、选择高亮、裁切、布局细节等情况。
- **Aholo Cloud**：当前协议下优先整场景渲染；把允许的 Rhino 对象转为云可读的显示网格，原始 NURBS/Brep 仍在文档。点云、选择工具和网格线叠加必须有独立能力判断，不能静默丢弃。
- 如果云服务以后提供对齐的深度/透明度/帧版本，再评估本地-云混合。没有这些能力时，不开放声称通用正确遮挡的 RGB 覆盖模式。

## 模块和数据边界

1. `Aholo.Core`：完整 Gaussian 资产/PLY 解析；独立相机、变换、SH、协方差投影、参考合成；遮挡能力/帧一致性规则。
2. `Aholo.Rhino`：文档代理对象、持久引用、变换/可见性/撤销；显示模式注册；原生视口/深度适配；渲染资源按文档/视口隔离。
3. `Aholo.Provider`：实时会话 REST、WebSocket 握手和图片帧、串行场景更新、显式关闭与错误处理。流 token 不进入日志、文档或持久任务记录。
4. 后续 GPU 后端：以 CPU 对照为验收依据，分别处理 Windows/OpenGL 与 Mac 实际显示技术。不可在未核实的 Metal 上直接调用 OpenGL，也不可粗暴改全局渲染状态。

实例变换必须同时作用于位置和协方差；SH 的方向要变回源资产坐标。正交相机的视线方向与透视相机不同。多视图各自排序，不共享错误的“当前相机”结果。

## 阶段、提交与验收门槛

| 阶段 | 可交付物 | 通过条件 |
| --- | --- | --- |
| M0 方案与依据 | 本 ADR、研究证据、能力边界 | 不把未公开能力当成既定事实；先提交 |
| M1 完整数据与参考数学 | 严格 GS PLY、SH 0–3、协方差/投影、CPU RGBA 合成、合成 fixture | 单位/旋转/各向异性/视角颜色/透明叠加/不透明深度屏蔽数值测试通过 |
| M2 原生视口实验 | 独立显示模式、真实 Gaussian 椭圆 quad/alpha 材质原型、文档代理、离线验收场景 | 编译及离线检查；随后人工在 Mac/Win 验证平滑 alpha、深度、变换、隐藏、Undo、保存重开。未实机前只标实验 |
| M3 云实时协议基础 | REST/WSS、分片 JPEG、会话/更新/关闭状态机、假传输测试 | 无密钥串漏；不重复创建；相机 patch 串行且末值送达；错误/关闭可恢复 |
| M4 本地 GPU 与场景能力 | 批量排序/绘制、源数据无损、可取消加载/LOD、裁切和透明处理 | 实机通过 M2，GPU 图像与 CPU 对照在定义误差内；性能以设备/场景实测报告，不虚报大场景能力 |
| M5 原生云视口 | 整场景同步、GS 入库桥接、原生模式生命周期、陈旧帧提示 | 真实账户证明 GS meshId 及单位轴向；点云/工具叠加规则明确；帧同步局限不误导用户 |
| M6 发行验收 | Mac/Win 安装包、双语说明、故障恢复和性能记录 | 各平台实机与真实服务分别通过；之前不宣称生产就绪 |

M1/M2 与 M3 可按独立文件范围并行。M4 不绕过 M2 实机门槛；M5 不绕过服务能力/入库门槛。每次提交保持可构建，记录已验事实和未验事项。首轮先做 M0–M3 的本地可验证部分，得到用户可手动测试的原型；不能据此把 M4–M6 勾成完成。

## 数值与实机测试矩阵

- 各向同性/各向异性、四元数符号与归一化、旋转后 covariance、透视/正交、视口外中心但椭圆入屏、near/far 和非有限输入。
- DC 与 SH 1–3；对象旋转后视角颜色；HDR 不提前量化成 RGB byte。
- 前红半透明/后蓝半透明，加位于前方、中间、后方的不透明绿色平面；CPU 图像给出明确预期。
- 实机同一模型用 Mesh、Brep/NURBS、点云表示，分别位于 GS 前/后；先验证普通不透明显示，再独立验证透明、选中、高亮和裁切。
- 文档切换、视图尺寸/相机变化、Undo/Redo、复制/旋转/缩放、隐藏图层、重开文件及资产丢失提示。
- 云会话错误/超时、分片/超大消息、乱序更新、关闭时取消；没有公开帧关联时严禁假定图片属于最新相机。

## 源码与验证约束

按[公开论文](https://arxiv.org/abs/2308.04079)的数学和文件格式独立实现；不复制 Graphdeco 研究用途许可的 CUDA/训练源码到 Apache-2.0 项目。[官方 Aholo Viewer](https://github.com/manycoretech/aholo-viewer) 可作为另一个公开参考，但 Web viewer 不是 Rhino 原生深度集成的现成方案。

本轮不使用 Computer Use 操作 Rhino，不读取/复用用户凭据，不自动进行付费生成或云渲染，不发布 Yak。人工宿主与真实账户验证必须记录实际结果，不能以 mock 或编译代替。


## 首轮执行记录（0.1.0-beta.2）

- M0 已先提交方案；M1 已落地完整 PLY / SH / covariance / projection / CPU reference，41 个新数学/解析用例与 29 个原几何用例通过；参考图中心值符合闭式结果。
- M2 已实现原生 Shaded 派生模式、外部 PLY 引用块、双精度 quad、全局深度排序、透明材质、资源取消/回收、故障包围盒与离线试验命令。编译通过，但 Mac/Windows 的 alpha 与实际深度遮挡仍未验收。字节材质会预先截断 HDR，不能宣称浮点图像等价。
- M3 已实现 REST/WSS、分片消息、串行 patch、初始化/关闭保护和显式清理重试，67 个用例通过；没有接入云视口，没有真实账户调用。
- 集成 Release 构建零警告/错误，完整离线套件 179 项通过。M4–M6 仍按原门槛继续，不将 mock/编译当成宿主或服务实测。验收步骤见 [GS host acceptance](../gs-host-acceptance.md)。
