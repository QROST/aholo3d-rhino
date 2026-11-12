# Aholo3D naming and upgrade compatibility

Maintainer: CuriousArc · qrost. Updated: 2026-11-11. Candidate: 0.2.0-alpha.32.

The source repository is [QROST/aholo3d-rhino](https://github.com/QROST/aholo3d-rhino). Product titles, the Rhino panel tab, GS display mode, new object/layer names, solution, source directories, assemblies, generated files and new Yak packages use Aholo3D. Build with `dotnet build Aholo3D.sln -c Release`; run `dotnet test Aholo3D.sln -c Release --no-build`.

## Installation

The new package is **`aholo3d-rhino`**, containing `Aholo3D.Rhino.rhp`. The previous `aholo-rhino` release remains unlisted. Renaming the package does not transfer Yak ownership or provide automatic upgrades; no public publication is implied by this candidate.

Close Rhino before the first new-name installation. `scripts/install-local.py` verifies the archive, moves the old package directory intact into `packages/aholo3d-rename-backups/<unique-id>/aholo-rhino` outside the `packages/8.0` scan, installs the new package and verifies every payload byte. If installation fails, a newly created partial version is removed and the old directory is restored. The receipt records the backup. Never install both names together: they share the same plug-in GUID. Keep backups until manual acceptance.

## Data and credentials

New installations use `Aholo3D-Rhino` under the platform's local application-data directory. For an existing `Aholo-Rhino` directory, the installer creates a new-name directory link to the same physical data when the OS permits it. It never copies or moves task journals, locks, assets, saved document references or the USD virtual environment. Both path spellings therefore reach the same files. Windows may disallow unprivileged directory links; the runtime then retains the legacy directory as the explicit compatibility fallback. Independent current and legacy directories are treated as a conflict, never silently merged or hidden.

Existing task records and 3DM files may still contain old absolute paths; these remain valid and are not rewritten. `AHOLO3D_*` environment overrides take priority, with `AHOLO_*` supported as fallback. An explicit state-directory override remains authoritative. The USD runtime uses its own override or the shared default data root.

Credential service `ai.aholo.world.rhino`, the Windows `Aholo Rhino` credential directory and its DPAPI entropy remain unchanged to preserve decryptability and account isolation. Credentials are never copied into the repository or migration receipt. Real `aholo.ai` API/OAuth domains and official API snapshots retain their original spellings.

## Rhino identity and commands

Plug-in, panel and canonical command GUIDs remain stable. New commands use `Aholo3D*`, including `Aholo3DPanel`, `Aholo3DWorld`, `Aholo3DModel`, `Aholo3DTasks` and `Aholo3DRender`. All 18 previous `Aholo*` command spellings remain registered compatibility commands that call exactly the same implementation. No user alias is overwritten and no nested command macro is launched.

The plugin's saved `Aholo GS (Experimental)` display mode is renamed in place to `Aholo3D GS (Experimental)`, retaining the mode GUID used by views. Only the saved mode with that exact legacy name is eligible; unrelated display modes are not renamed. Native readback must confirm the new name. Stored `Aholo.GS`, mesh, task and spatial metadata keys remain stable so old blocks, import identity, Undo/Redo and task deduplication continue to resolve.

Historical package bytes, validation records, benchmark receipts, official snapshots and user-authored filenames are evidence, not current branding templates; they are not rewritten.

## 中文摘要

标题、源码路径、程序集、临时文件、新对象名称和新包统一为 Aholo3D；新包名为 `aholo3d-rhino`，旧包不会自动升级到此名称。先关闭 Rhino，再由安装脚本备份旧包并安装；失败时恢复旧包。

旧数据目录保留，用新名称的目录链接访问同一份内容；系统不允许建链接时回退旧目录。新旧目录若是两份独立数据，会明确报冲突，不自动合并。旧命令、文档中的元数据键、已有文件路径、凭据加密标识及真实官方域名继续兼容。原生 Mac/Windows 加载、标题、旧文档和显示模式仍需手动验收。
