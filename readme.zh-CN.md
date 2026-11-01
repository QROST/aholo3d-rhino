# Aholo for Rhino

在 Rhino 8 中生成世界并使用 RenderCloud 云渲染，由 **CuriousArc · qrost** 开发。

[English](readme.md)

**0.1.0-beta.1，本地开发预览版。** 本项目是独立集成，不是 Manycore、Aholo 或 McNeel 官方产品。插件标识、命令、凭据和缓存均独立，不替换 Lux3D for Rhino。

## 当前功能

- 通过文字、单张参考图或两者结合，调用 World API 生成世界。
- 保留任务历史、按已有 World ID 恢复查询，下载原始 PLY/SPZ 及可用的全景图。
- 将 PLY 作为一个彩色 Rhino 点云放入文档，支持指定比例、上轴转换、撤销和定位显示；`AholoImportPly` 可离线使用。
- 上传选中的 Mesh/Brep/Extrusion，以当前透视视口相机提交离线云渲染，下载并打开结果图。
- 可选将 Key 保存至 macOS 钥匙串或 Windows 当前用户 DPAPI；不保存时仅保留在后台进程内存。
- 持久记录提交意图和任务 ID；遇到未知结果时不自动重提，避免重复创建可能扣费的任务。

**世界模型是 3D Gaussian Splatting 数据，不是普通三角网格。** Rhino 内的预览只显示高斯中心点及 RGB/DC 颜色，不包含完整高斯形状、透明度和视角相关效果。原始 PLY/SPZ 保留，可交给完整 3DGS 工具使用。插件不人为设置点数或顶点数上限；实际仍受内存、Rhino/.NET 容器及 GLB 文件格式的 32 位长度字段约束。损坏或暂不支持的数据会报错，不会静默抽稀。

本版尚未实现实时云渲染流、视频/多图重建、完整 Rhino PBR/贴图/灯光或原生高斯渲染。按照当前官方说明，世界生成主要适用于室内场景。

## 环境与验证状态

| 项目 | 状态 |
| --- | --- |
| Rhino | 8.32 及以上，宿主程序集使用 .NET 7 |
| 后台运行时 | 与宿主架构匹配的 .NET 8 Runtime |
| 服务账户 | 有相应权限和额度的国际站 Aholo API Key |
| macOS | 已在 Apple Silicon 完成 Release 构建与离线检查；Rhino 界面/导入/真实云端验收待进行 |
| Windows | 已有跨平台源码及 Windows 凭据实现；Windows 构建、安装执行和 Rhino 宿主验收待进行 |
| Intel Mac | 尚未完成宿主验收 |

普通安装用户不需要 SDK 或 Python。开发构建需要 .NET 8 SDK；打包与包校验脚本还需要 Python 3.9+ 及 Rhino 附带的 Yak CLI。

## 登录、Key 与费用

官方 REST 接口目前要求在 `Authorization` 请求头使用**对应区域的原始 API Key**。Manycore OAuth 登录不能替代 World/RenderCloud 的这个凭据。本首版直接连接国际站 Key，不提供 OAuth 登录按钮；尚未启用中国区域端点。

请按[官方快速开始](https://labs.aholo.ai/api-docs/en/quickstart)在 Aholo 平台创建/管理 Key，不要放进问题报告或源码。连接操作通过查询世界列表验证 Key，不创建世界。World 可用并不代表账户同时拥有渲染权限或足够额度。

生成和渲染可能消耗 Aholo 服务额度。点击提交按钮会启动对应流程，包括参考图或所选几何的上传、渲染前的模型解析。关闭面板或停止本地等待**不会**取消云端任务或退回额度。离线渲染的 `requestId` 只是追踪标识，**不能用来保证重复请求只扣一次费**。

## 使用本地测试包

目前尚未公开上架 Yak。使用 `scripts/build-package.sh` 生成的本地 `.yak`，通过 Rhino 附带的 Yak CLI 安装：`yak install /absolute/path/to/package.yak`，然后手动重启 Rhino。Windows 使用 Rhino 附带的 `Yak.exe` 和包的绝对路径；请使用相应平台的包。

1. 运行 `AholoPanel`。
2. 在连接页输入国际站 API Key，选择是否保存。
3. 在世界页填写描述，可选一张参考图，提交一次后刷新或开启轮询。
4. 成功后下载 PLY，放入当前文档。服务返回上轴信息，但没有为生成世界明确绝对长度单位；默认每个 PLY 单位对应一个 Rhino 文档单位，请按需要调整比例。
5. 云渲染时选中 Mesh/Brep/Extrusion，激活透视视口，选择输出尺寸并提交。保持面板开启和轮询，或手动刷新，使模型解析完成后继续提交渲染。
6. 在渲染历史中下载/打开结果图。

首次可离线运行 `AholoImportPly`，选择 `fixtures/tiny-world.ply`，再按[宿主验收步骤](docs/host-acceptance.md)检查。

出现 `SubmissionUnknown` 时，先核对 Aholo 平台历史，避免直接再提交。已有 World ID 可重新加入历史；若渲染提交的 operation ID 丢失，目前无法自动对账。已保存 ID 的任务可以继续查询。

## 坐标与渲染边界

Rhino 几何按默认网格参数离散，再转换为 glTF 的米/Y 向上坐标。渲染服务读取 USDA 中引用的已上传资产，不直接读取内嵌 USD 顶点数组；插件通过变换恢复渲染端要求的毫米/Z 向上坐标。**快速 GLB 入库是否完整保留这套坐标约定，还需要真实账户验证。** 首次请用已知尺寸的立方体和相机检查尺度、朝向和构图。

相机保留视口水平视场角，使用所选输出图的宽高比。不支持正交相机及偏移视锥时会明确拒绝。仅导出基本漫反射颜色，不导出完整材质图、贴图、场景灯光、块实例或点云；不支持的选中对象会报错。

## 数据与隐私

后台使用专用标准输入/输出管道，没有本地 HTTP 控制端口。只有固定国际 API 域名接收 Key；上传使用单独的短期 OUS 令牌，下载使用不带凭据的 HTTPS，并禁止重定向、检查目标为公网地址。

提交时，提示词、参考图或选中的渲染几何会发送至 Aholo。本地任务记录、输入快照和结果位于系统本地应用数据目录下的 `Aholo-Rhino`，导出目录可能留有中间 GLB。任务 JSON 不保存 Key 和带签名的资产 URL。断开连接会删除已保存 Key，保留本地工作。没有自动遥测。存储保护不防御同一用户权限下的其他进程或管理员。

界面显示最近 100 条本地记录（世界和渲染合计），更早记录仍在磁盘上。上传阶段中断不会自动重启。大型导入/导出需要相应内存和磁盘空间。

## 开发与打包

```sh
dotnet build Aholo.sln -c Release
dotnet test tests/Aholo.Tests/Aholo.Tests.csproj -c Release
./scripts/build-package.sh --platform mac
./scripts/build-package.sh --platform win
```

Windows 可运行 `powershell -File scripts/build-package.ps1`（需要 Python 3）；该脚本尚未在 Windows 上执行验证。

脚本使用新暂存目录构建平台候选包、校验归档并生成 SHA-256 记录，不会自动安装或发布。打上平台标签不代表已经完成该平台实机验收。

[架构与接口依据](docs/architecture.md) · [验证记录](docs/validation.md) · [宿主验收](docs/host-acceptance.md) · [官方接口快照](docs/api/README.md)

## 许可证与署名

Apache-2.0，见 [LICENSE](LICENSE) 与 [NOTICE](NOTICE)。运行时发现、管道通信、安全凭据存储及下载传输参考现有 Lux3D/Tripo 项目。维护者为 **CuriousArc · qrost**。反馈问题时提供版本、平台、任务状态和复现步骤，不包含 Key 或带签名的 URL。
