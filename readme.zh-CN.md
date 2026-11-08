# Aholo for Rhino

在 Rhino 8 中生成世界并使用 RenderCloud 云渲染，由 **CuriousArc · qrost** 开发。

[English](readme.md) · [源码仓库](https://github.com/QROST/aholo3d-rhino)

仓库和源码目录现为 **`aholo3d-rhino`**。已发布的 Yak 包继续使用 **`aholo-rhino`**，保持原有用户的更新渠道；插件标识、命令、凭据、任务/缓存及可选 USD 运行时路径保持兼容。详见[仓库改名与兼容说明](docs/repository-rename.md)。

**0.1.1-beta.2，macOS 预览版，支持自动中英界面选择及 GS USD/USDZ 双向交换。** 之前 0.1.0 的生成与显示流程已在本机 Apple Silicon Mac 由用户测试通过；新增导入导出界面及双语原生布局仍待手动验收。 本项目是独立集成，不是 Manycore、Aholo 或 McNeel 官方产品。插件标识、命令、凭据和缓存均独立，不替换 Lux3D for Rhino。

## 界面语言

默认及兜底语言为英文。插件启动时优先使用有效的 Rhino 界面语言；中文 Rhino（含简体、繁体等中文地区设置）显示简体中文，其他 Rhino 语言显示英文。仅在 Rhino 无法提供有效语言时，才读取系统界面语言。修改 Rhino 语言后请重启。

面板、命令提示、GS 诊断和已知任务错误遵循同一规则；用户提示词、名称、路径、任务 ID 和保存的状态码保持原样。未知外部或技术错误保留原文，不猜测翻译。语言选择与内嵌资源已有自动化测试，双语原生界面布局仍需手动在 Rhino 验收。

## 当前功能

- 通过文字、单张参考图或两者结合，调用 World API 生成世界。
- 保留任务历史、按已有 World ID 恢复查询，下载原始 PLY/SPZ 及可用的全景图。
- 将完整 3DGS PLY 放为原生块实例，在 **Aholo GS (Experimental)** 模式绘制各向异性高斯椭圆、不透明度和 SH 0–3 视角颜色。原文件与完整参数保留；已实现比例、轴向、变换和撤销逻辑，详细文档生命周期测试仍待验收。
- `AholoGsProbe` 创建离线视口试验，`AholoImportGs` 导入本地 GS，`AholoGsDiagnostics` 输出状态。`AholoImportPly` 仅作为显式中心点诊断保留。
- 上传选中的 Mesh/Brep/Extrusion，以当前透视视口相机提交离线云渲染，下载并打开结果图。
- 可选将 Key 保存至 macOS 钥匙串或 Windows 当前用户 DPAPI；不保存时仅保留在后台进程内存。
- 持久记录提交意图和任务 ID；遇到未知结果时不自动重提，避免重复创建可能扣费的任务。

**世界模型是 3D Gaussian Splatting 数据，不是普通三角网格。** World 默认放置方式现为 GS 实验渲染。缺少完整高斯字段的 PLY 会明确报错，不会悄悄转成点云。原始 PLY/SPZ 保留；首版渲染器解码 PLY，尚未解码 SPZ。

这是**实验渲染器**。beta.2 在真实世界模型上出现严重内存膨胀和导航卡顿；beta.3 已替换逐高斯创建原生资源的路径。可取消的后台任务负责投影与全局深度排序；界面分小段提交网格，每批最多 2,048 个高斯，共用透明度图集和材质。批大小是传输单位，不是几何上限，保留所有可见高斯。相机变化后的准备期间显示包围盒，完整帧准备好后恢复 GS；保存 3dm 时不再为缩略图渲染 GS。见[卡顿修复](docs/gs-freeze-repair.md)及[显示稳定性修复](docs/gs-visibility-stability.md)。

用户已在本机 Apple Silicon Mac 确认红/青色测试高斯、世界彩色显示、导航改善及基础裁切交互。beta.4/beta.5 修复显示稳定性与启动错误后，用户于 2026-11-02 确认复测通过，版本定为 0.1.0。完整透明度/遮挡/排序和 Windows 表现仍待验收。透明 Rhino 对象任意交错、嵌套块、布局细节、打印/视图捕获、立体视图与 Raytraced 集成尚不支持。显示 RGB 为 8 位且会在混合前截断 HDR；透明度使用 256 级图集，量化误差最多 0.5/255，不改变源数据。CPU 参考保留浮点数据，不能将两者说成像素完全一致。用户已观察到 Mac 基础裁切平面交互；多平面与边缘行为仍属实验范围。

实时云渲染 REST/WebSocket 及经过模拟测试的会话清理已形成协议基础，**尚未开放实时云视口**。公开流只有 JPEG，没有深度、alpha 或可关联相机的帧号；World GS 入库到云 meshId 的桥接也需验证。整场景云渲染按能力门槛继续推进。本版本不包含视频/多图重建和完整 Rhino PBR/贴图/灯光。官方当前说明世界生成主要适用于室内场景。

详见[阶段方案与依据](docs/decisions/0001-native-gaussian-viewport.md)及 [GS 验收步骤](docs/gs-host-acceptance.md)。

插件先检查并复用已有 GS 模式，在界面空闲时初始化；GS 初始化失败也不阻断主面板。若仍未就绪，可运行 `AholoGsMode` 在本机重试。0.1.0 保留已经测试的 beta.5 实现；0.1.1-beta.1 在该基础上新增本地 GS 交换，保留云任务流程；0.1.1-beta.2 新增自动界面语言选择。

## 环境与验证状态

| 项目 | 状态 |
| --- | --- |
| Rhino | 8.32 及以上，宿主程序集使用 .NET 7 |
| 后台运行时 | 与宿主架构匹配的 .NET 8 Runtime |
| 服务账户 | 有相应权限和额度的国际站 Aholo API Key |
| macOS | Apple Silicon：世界生成/导入、GS 颜色/导航/基础裁切已确认；beta.5 启动与显示复测通过，详细矩阵仍待验收 |
| Windows | 已有跨平台源码及 Windows 凭据实现；Windows 构建、安装执行和 Rhino 宿主验收待进行 |
| Intel Mac | 尚未完成宿主验收 |

普通安装用户不需要 SDK；USD 导入/导出还需下文的可选独立 Python/OpenUSD 运行时。开发构建需要 .NET 8 SDK；打包与包校验脚本还需要 Python 3.9+ 及 Rhino 附带的 Yak CLI。

## 登录、Key 与费用

官方 REST 接口目前要求在 `Authorization` 请求头使用**对应区域的原始 API Key**。Manycore OAuth 登录不能替代 World/RenderCloud 的这个凭据。本首版直接连接国际站 Key，不提供 OAuth 登录按钮；尚未启用中国区域端点。

请按[官方快速开始](https://labs.aholo.ai/api-docs/en/quickstart)在 Aholo 平台创建/管理 Key，不要放进问题报告或源码。连接操作通过查询世界列表验证 Key，不创建世界。World 可用并不代表账户同时拥有渲染权限或足够额度。

生成和渲染可能消耗 Aholo 服务额度。点击提交按钮会启动对应流程，包括参考图或所选几何的上传、渲染前的模型解析。关闭面板或停止本地等待**不会**取消云端任务或退回额度。离线渲染的 `requestId` 只是追踪标识，**不能用来保证重复请求只扣一次费**。

## 安装

本预览包面向 macOS；Windows 发布仍待原生验收。安装已发布的预览版时，在 Rhino PackageManager 中启用预发行包并搜索 `aholo-rhino`。也可使用 `scripts/build-package.sh` 生成的本地 `.yak`，通过 Rhino 附带的 Yak CLI 安装：`yak install /absolute/path/to/package.yak`。安装或更新后请手动重启 Rhino，勿将 macOS 包用于 Windows。

1. 运行 `AholoPanel`。
2. 在连接页输入国际站 API Key，选择是否保存。
3. 在世界页填写描述，可选一张参考图，提交一次后刷新或开启轮询。
4. 成功后下载 PLY，通过 GS 实验模式放入当前文档。服务返回上轴信息，但没有为生成世界明确绝对长度单位；默认每个 PLY 单位对应一个 Rhino 文档单位，请按需要调整比例。
5. 云渲染时选中 Mesh/Brep/Extrusion，激活透视视口，选择输出尺寸并提交。保持面板开启和轮询，或手动刷新，使模型解析完成后继续提交渲染。
6. 在渲染历史中下载/打开结果图。

首次 GS 测试请使用空文档，运行 `AholoGsProbe`。它离线创建合成高斯、Brep 和 Mesh，不连接 Aholo，两次 Undo 可移除测试内容。`AholoImportGs` 可加载 `fixtures/gaussian-anisotropic.ply` 或 `fixtures/gaussian-sh3.ply`，`AholoGsMode` 可启用/重载模式。[GS 验收步骤](docs/gs-host-acceptance.md)列出预期与限制。`fixtures/tiny-world.ply` 仅供 `AholoImportPly` 中心点诊断使用。

出现 `SubmissionUnknown` 时，先核对 Aholo 平台历史，避免直接再提交。已有 World ID 可重新加入历史；若渲染提交的 operation ID 丢失，目前无法自动对账。已保存 ID 的任务可以继续查询。

## 坐标与渲染边界

Rhino 几何按默认网格参数离散，再转换为 glTF 的米/Y 向上坐标。渲染服务读取 USDA 中引用的已上传资产，不直接读取内嵌 USD 顶点数组；插件通过变换恢复渲染端要求的毫米/Z 向上坐标。**快速 GLB 入库是否完整保留这套坐标约定，还需要真实账户验证。** 首次请用已知尺寸的立方体和相机检查尺度、朝向和构图。

相机保留视口水平视场角，使用所选输出图的宽高比。不支持正交相机及偏移视锥时会明确拒绝。仅导出基本漫反射颜色，不导出完整材质图、贴图、场景灯光、块实例或点云；不支持的选中对象会报错。

## 本地 GS USD / USDZ 导入导出

本地交换不需要 API Key，不调用网络服务。`AholoImportGs` 和面板本地导入支持完整 Gaussian PLY，以及官方 OpenUSD 静态 GS `.usd`、`.usda`、`.usdc`、`.usdz` 场景，保留多部分、完整变换、单位及向上轴；原云任务的 PLY 放置流程保留。

选中顶层 GS 块后运行 **`AholoExportGs`**，或点击面板导出。面板可明确选择选中的 GS 或全部顶层 GS；全部导出将隐藏/锁定的 GS 也作为可见资产写入。空选择及混选非 GS 对象会报错，不自动扩大导出范围。`.usdz` 是自包含便携包，`.usd`/`.usdc` 为二进制，`.usda` 为文本。保留全部高斯、SH0–3、透明度与完整对象变换，使用当前 Rhino 单位和 Z 向上。官方几何字段为 float32；双精度变换和局部原点重定位减少大坐标损失，但不能宣称任意 double 值逐位无损。导出前请设定标准长度单位。普通网格 USDZ、任意厂商 GS 结构、动画及 Rhino 嵌套块不在本次 GS 交换范围内。

USD 功能需用 Python 3.11–3.14 安装一次独立运行时：

```sh
python3.12 scripts/setup-usd.py
python3.12 scripts/setup-usd.py --check
```

Windows 可用 `py -3.12`。脚本把固定的 `usd-core==26.11` 安装到 macOS 的 `~/Library/Application Support/Aholo-Rhino/usd-runtime` 或 Windows 的 `%LOCALAPPDATA%\Aholo-Rhino\usd-runtime`，不修改系统 Python。支持 `--wheel-dir 目录` 离线安装，或在启动 Rhino 前设置 `AHOLO_USD_PYTHON` 指向已有环境；安装之后导入导出完全在本机完成。详见 [USD 交换范围及验收](docs/usd-exchange.md)。

## 数据与隐私

后台使用专用标准输入/输出管道，没有本地 HTTP 控制端口。只有固定国际 API 域名接收 Key；上传使用单独的短期 OUS 令牌，下载使用不带凭据的 HTTPS，并禁止重定向、检查目标为公网地址。

提交时，提示词、参考图或选中的渲染几何会发送至 Aholo。本地任务记录、输入快照和结果位于系统本地应用数据目录下的 `Aholo-Rhino`，导出目录可能留有中间 GLB。GS 导入会建立以 SHA-256 命名的 PLY 快照；Rhino 文档只保存哈希和本地路径，不嵌入全部高斯数据，转移 3dm 时需同时转移匹配的 PLY/缓存。资源缺失或变化时保留可见包围盒。因此文档可能包含本地文件路径。任务 JSON 不保存 Key 和带签名的资产 URL。断开连接会删除已保存 Key，保留本地工作。没有自动遥测。存储保护不防御同一用户权限下的其他进程或管理员。

界面显示最近 100 条本地记录（世界和渲染合计），更早记录仍在磁盘上。上传阶段中断不会自动重启。大型导入/导出需要相应内存和磁盘空间。

GS 诊断日志写入本机 `Aholo-Rhino/logs/gs-viewport.log`，记录准备耗时、批次数和经过清理的错误；约 1 MiB 轮换并保留一份旧日志。`AholoGsDiagnostics` 会显示实际路径。日志不记录提示词、Key 或签名链接。

## 开发与打包

```sh
dotnet build Aholo.sln -c Release
dotnet test tests/Aholo.Tests/Aholo.Tests.csproj -c Release
python3 scripts/create-gs-assets.py --check
dotnet run --project tools/Aholo.Reference -c Release -- --output artifacts/gs-reference
./scripts/build-package.sh --platform mac
./scripts/build-package.sh --platform win
```

Windows 可运行 `powershell -File scripts/build-package.ps1`（需要 Python 3）；该脚本尚未在 Windows 上执行验证。

脚本使用新暂存目录构建平台候选包、校验归档并生成 SHA-256 记录，不会自动安装或发布。打上平台标签不代表已经完成该平台实机验收。

[架构与接口依据](docs/architecture.md) · [验证记录](docs/validation.md) · [宿主验收](docs/host-acceptance.md) · [官方接口快照](docs/api/README.md)

## 许可证与署名

Apache-2.0，见 [LICENSE](LICENSE) 与 [NOTICE](NOTICE)。运行时发现、管道通信、安全凭据存储及下载传输参考现有 Lux3D/Tripo 项目。维护者为 **CuriousArc · qrost**。反馈问题时提供版本、平台、任务状态和复现步骤，不包含 Key 或带签名的 URL。
