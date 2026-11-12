# Aholo3D for Rhino

在 Rhino 8 中生成世界与模型、查看高斯场景，并使用 Aholo3D 云渲染。

**CuriousArc · qrost** · [English](readme.md) · [文档目录](docs/index.md)

这是采用 Apache-2.0 许可证的独立插件，并非 Aholo3D、Manycore 或 McNeel 官方产品。

## 主要功能

- **世界生成：**使用文字或参考图生成世界，也可通过支持的照片或视频重建。
- **模型创作：**接入 Lux3D 的网格、图像、材质迁移、拆件、格式转换、绑定和动画文件生成。
- **结果管理：**浏览本地与云端历史，下载原始结果，预览静态网格并以支持撤销的 Rhino 块放入文档。
- **高斯显示：**导入 PLY、SPZ、SPLAT，通过实验性原生 GS 模式查看。
- **场景交换：**导入导出受支持的静态网格、高斯及混合 USD/USDZ 场景。
- **云渲染：**提交受支持的 Rhino 几何进行渲染，或打开独立的实时云预览窗口。

界面分为创建、资源库与渲染三个页面。账户、区域、导入与存储选项统一放在设置中。默认英文；Rhino 使用中文时显示简体中文，无法读取 Rhino 语言时才参考系统语言。

## 环境要求

| 组件 | 要求 |
| --- | --- |
| Rhino | Rhino 8 系列，8.32 或更新版本 |
| 系统 | macOS 或 Windows |
| 后台服务 | .NET 8 运行时 |
| 云端功能 | 所选区域的 Aholo3D API Key、对应服务权限和可用积分 |
| 本地 USD/USDZ | 可选的 OpenUSD 运行时，可在设置中查看安装方法 |

**当前为预览版本：**核心流程已有本地自动化检查和先前的 macOS 测试；最新界面及 Windows 原生流程仍需验收。详见[测试说明](docs/testing.md)。

## 安装

1. 如果此前安装了 **`aholo-rhino`**，先在 Rhino 包管理器中卸载旧包并重启。两个包共享插件标识，不能同时加载；请保留插件的数据目录。
2. 通过 `_PackageManager` 安装已发布的 **`aholo3d-rhino`**，必要时开启预发布版本。安装文件将放在 [Releases](https://github.com/QROST/aholo3d-rhino/releases)；如果尚无可用文件，请参考[源码构建](docs/development.md)。
3. 重启 Rhino，运行 `Aholo3DPanel`。

本地 Yak 文件安装和升级方法见[安装说明](docs/installation.md)。

## 第一次生成

1. 打开**设置**，选择国际或中国区域，验证 API Key。
2. 在**创建**页选择操作并填写输入；参数错误会在提交前提示。
3. 点击生成一次，然后在**资源库**跟踪任务。
4. 使用结果主按钮下载、放入几何或打开图片，其他操作位于“更多结果操作”。

**OAuth 登录是可选功能，不能替代 API Key。** 登录成功并不代表已获得生成权限或积分。云服务可能消耗积分；本地高斯显示和受支持的本地网格工具不需要云账户。

需要重启后恢复输入文字与选项时，请先保存 Rhino 文档，再在设置中开启**记住此文档的创建草稿**。网址和文件选择不会恢复。详见[数据与隐私](docs/privacy.md)。

## 功能边界

- GS 显示仍属实验功能；尚不支持云图像与 Rhino 几何的原生深度合成、Raytraced 集成、打印/布局捕获，以及任意透明对象交错。
- USD/USDZ 仅支持规定的静态内容，不是通用 USD 编辑器。能查看网格 USDZ 的软件不一定能查看高斯 USDZ。
- 绑定和动画结果可以下载，但不支持在 Rhino 内播放动画。
- 服务生成上限与 Rhino 导入是两回事；插件不人为限制总顶点数、面数或高斯数量。
- 提交后若无法确认服务是否接收，不会自动重新生成。请恢复原任务，避免重复扣费。

详见[格式支持](docs/formats.md)、[API 覆盖](docs/api-capabilities.md)和[常见问题](docs/troubleshooting.md)。

## 反馈与贡献

请通过 [GitHub Issues](https://github.com/QROST/aholo3d-rhino/issues)提交可复现问题，附上插件版本、Rhino 版本、操作系统及不含隐私的小样例。请勿上传 API Key、令牌或私有项目。

[参与贡献](CONTRIBUTING.md) · [安全反馈](SECURITY.md) · [Apache-2.0 许可证](LICENSE) · [第三方声明](NOTICE)
