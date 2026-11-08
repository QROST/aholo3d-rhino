# Repository rename and compatibility

Maintainer: CuriousArc · qrost. Updated: 2026-11-08.

The canonical source repository is [QROST/aholo3d-rhino](https://github.com/QROST/aholo3d-rhino), and the checkout directory is `aholo3d-rhino`. This is a source repository rename; the installed product continues to be Aholo for Rhino.

## Updated references

- Git origin uses `https://github.com/QROST/aholo3d-rhino.git`.
- Build repository metadata, the Yak website and Rhino plug-in website point to the new repository.
- Both READMEs, the architecture entry and the development plan identify the new checkout.
- Build, package, install and USD setup scripts already locate their files relative to the script/repository root; they do not require the former source directory.

For an existing checkout, run from its new directory:

```sh
git remote set-url origin https://github.com/QROST/aholo3d-rhino.git
dotnet build Aholo.sln -c Release
dotnet test Aholo.sln -c Release --no-build
```

Restore/build again after moving a checkout. `bin` and `obj` are generated outputs, not references to edit by search-and-replace. IDE workspace entries that explicitly point to a former checkout must be reopened at the new directory.

## Stable installed identities

| Identity | Retained value | Reason |
| --- | --- | --- |
| Yak package | `aholo-rhino` | Existing published package and update channel; a new package name would be a separate migration |
| Plug-in GUID | `D1D17217-1169-41BB-B63A-FEE17DFA777F` | Existing Rhino registration and document compatibility |
| Solution / assemblies | `Aholo.sln`, `Aholo.*`, `Aholo.Rhino.rhp` | Repository directory names do not determine .NET or plug-in identity |
| Commands | Existing `Aholo*` English names and GUIDs | Existing shortcuts and scripts |
| User data / USD runtime | `Aholo-Rhino` application-data directory | Existing journals, source assets, caches, document paths and installed runtime |
| Credential service | `ai.aholo.world.rhino` | Existing protected credentials; no credential migration is needed |
| Environment variables | Existing `AHOLO_*` names | Existing launch and development overrides |
| Persisted GS metadata | Existing attribute/schema keys | Saved documents and placements remain interpretable |

Old names in these roles are intentional. Do not change them just to make a text search empty. The service brand, provider endpoints and third-party attribution also remain unchanged.

## Local receipts and published artifacts

Local `artifacts/latest-package-*.json` and `latest-install.json` may contain absolute archive paths from the former checkout. When relocating these active pointers, first verify that the corresponding file in the new checkout has the recorded SHA-256; preserve name, version, source commit, acceptance status and installed-directory fields. Retain the original receipt as local evidence. This repairs the archive location, not its provenance or acceptance.

Existing published `.yak` bytes and historical logs/receipts are not rewritten. The website metadata change applies to future builds. A newly built package with an already-published version is not a new release: bump the version and complete the normal release checks before any subsequent publication. Repository adaptation alone neither publishes nor reinstalls a package.

## 中文摘要

源码统一使用 `aholo3d-rhino` 和新的 GitHub 地址。Yak 仍搜索 `aholo-rhino`；程序集、命令、插件 GUID、凭据服务、`Aholo-Rhino` 用户数据目录和 `AHOLO_*` 环境变量继续兼容旧安装。不要对这些持久化标识做无差别替换。

改目录后重新 restore/build；脚本根据自身路径定位仓库。活跃回执中的旧源码绝对路径仅在校验归档 hash 后迁移，并保留原回执；既有发布包、历史记录和用户数据不改写。新网站信息随后续版本发行生效。
