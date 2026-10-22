# 智熄的启动器 0.2.7

[下载最新版](https://github.com/ZhixiTiancai/ZX-Launcher/releases/latest) · [发布与自动更新说明](发布更新说明.md)

完整朋友包：`ZX-Launcher-Windows-x64.zip`。解压后运行第一层的“智熄的启动器.exe”，不含个人账号或整合包。
已有旧版可使用 Release 中的 `update.exe`，选择原目录更新，保留整合包、模组、世界和账号。
从 0.2.6 起默认连接本仓库 GitHub Releases 检查更新。0.2.5 用户可在设置保存：
`https://github.com/ZhixiTiancai/ZX-Launcher/releases/latest/download/update.json`。


Windows WPF 中文整合包管理界面，独立实现 Modrinth 整合包导入、客户端文件下载及哈希校验。原生账号管理和可配置的微软设备授权在本界面中完成；通过子进程调用未修改的 Prism Launcher 11.1.1，完成游戏本体与模组加载器安装、Minecraft 启动。

## 构建

在 Windows 10/11 64 位、.NET Framework 4.8 环境中运行 `build/build.ps1`。不需要 Node、npm、Visual Studio 或额外安装 .NET SDK。脚本使用系统自带 C# 编译器，输出图形 EXE 和命令行验证工具。

`build/fetch-deps.ps1` 下载固定版本的原版 Prism、Java 17 / 21 以及对应源码；`build/assemble.ps1` 组装运行目录。

发行压缩包由 `build/package.py` 生成，使用 Python 3 标准库。归档只选择必要文件，不纳入构建缓存、测试账号、测试日志或私人凭证。

## 实现

- `src/PackService.cs`：格式识别、版本约束、ZIP 路径检查、实例生成、并发下载、SHA-512/SHA-1 校验、失败报告与手动补入。
- `src/MetadataCache.cs`：在启动前下载官方组件元数据，递归补全依赖并验证 SHA-256 哈希链，解决 Prism 自身下载 Forge 大索引超时。
- `src/IniConfig.cs`：保留配置区并将全局设置写入 General，修复旧版错写到 UI 区导致反复初始化的问题。
- `src/GameFiles.cs`：按平台规则补全本体、Maven 库、Forge 安装器依赖和资源对象，验证 SHA-1，官方源失败后可回退 BMCLAPI 镜像。
  下载后按原版 Prism 缓存格式登记文件，避免其将预下载文件再次请求下载。
- `src/Program.cs`：WPF 界面事件、任务取消、Prism 命令行调用、Java 路径更新、内存设置。
- `src/App.xaml`：桌面界面。
- `src/SelfTests.cs`：关键路径检查、解压覆盖顺序、校验与损坏检测。

导入不会执行包内脚本。游戏启动后，模组本身会按游戏机制运行。

账号管理使用与随附 Prism 兼容的 accounts.json。原生微软设备授权流程只使用用户为本应用配置的 Client ID，不内置其他启动器的应用编号。账号凭据只保存在本地供游戏引擎使用，不记录到日志；账号修改前的备份使用 Windows DPAPI 加密。

## 验证

运行 `build/PackTools.exe --self-test`。

真实整合包准备：`build/PackTools.exe --prepare <运行目录> <整合包路径>`。

重新验证：`build/PackTools.exe --repair <运行目录> <实例ID>`。

退出码：0 成功；1 操作或配置错误；2 尚有下载失败文件。

此版本已用用户提供的愚者 0.3.1（Minecraft 1.20.1 / Forge 47.4.12）实测全部清单文件下载及哈希校验。0.1.2 已在用户实际 E 盘目录完成正版账号启动，进入整合包主菜单。未打开或创建世界，未进行联机与长时间稳定性测试。
按 Minecraft 官方组件元数据选择随附 Java 17 / 21，保留已显式选择且仍存在的外部 Java。
0.1.4 对齐 Prism 对无版本约束的 Fabric / Quilt 映射依赖的处理方式，以 Minecraft 版本选择映射组件；没有规则的依赖仍会明确报错。
Fabric 旧式 Maven 库从官方仓库读取 SHA-1 和长度后下载、校验，并写入 Prism 缓存。
`build/package-compat-update.py` 制作不含用户数据的 0.1.4 更新包；`build/package-friends.py` 制作不含整合包的完整便携包。

## 许可

本界面和导入器使用 MIT 许可。Prism、Temurin、模组与整合包内容遵循各自原有许可，详见 `THIRD-PARTY.txt`。

0.1.5: src/PackIcons.cs provides per-instance local/HTTPS image icons, bounded decoding,
PNG normalization and atomic saves under data/instance-icons. The default grass icon is
embedded from src/assets/default-grass.png. List and selected-instance headers share the
same frozen ImageSource, and custom icons do not alter instance metadata or game files.
0.1.6 adds PackExporter.cs: complete Modrinth ZIP export with all current files in overrides,
current component dependencies, name/icon preservation, SHA-512 bundled-file verification,
private/runtime data exclusions and atomic publication. ExportTests.cs exercises round-trip
imports, current mod additions/changes/removals, offline restoration, tamper rejection and cancellation.
0.1.7 adds direct per-instance mods folder access and SaveExporter.cs for separate world ZIP backups.
All worlds under saves are retained, including dimensions, player data, datapacks and level.dat_old.
Active session locks and filesystem links are rejected; cancellation retains the previous ZIP.
SaveExportTests.cs verifies backup/restore contents, multiple worlds and cancellation/locking.

0.1.8 adds AccountStore.cs, MicrosoftLogin.cs and AccountWindow.cs for in-launcher
account selection, offline roles, sign-out/removal and native Microsoft device-code login.
MicrosoftLogin requires the operator's own public-client application registration and
Minecraft service access. It never defaults to another launcher's client ID.
Existing Prism accounts are reused without rewriting their tokens; the chosen profile
and offline name are passed explicitly to Prism. The selected account's registered
client ID is handed to the original engine for its existing authorization refresh.
AccountTests.cs exercises the complete HTTP sequence with synthetic responses,
credential-safe errors, polling/backoff, refresh, cancellation, ownership rejection,
atomic storage, encrypted backups and preservation of unrelated account records.
Real new-account sign-in remains pending a configured, service-approved client ID
and the human user's completion of Microsoft authorization in the browser.

0.1.9 adds CurseForge manifest.json pack imports through CurseForgePack.cs.
CurseTools public metadata proxy provides exact project/file IDs, file names, sizes,
SHA-1 digests and download URLs. Files are downloaded from their returned HTTPS URLs.
Resource packs (class 12) and shaders (class 6552) are installed to their proper folders.
Absent automatic download URLs are never fabricated: files remain missing and the
CurseForge webpage is reported for manual download and existing hash-checked restore.
CurseForgeTests.cs covers custom overrides, optional files, cached metadata, ID/hash
validation, loader versions, category routing and restricted-file restore.

0.1.10 adds Explorer file-drop import across the complete left pack library, with
copy-only ZIP/MRPACK validation, hover feedback, sequential batch import, cancellation
and busy/engine-running guards. The existing file-picker entry shares the same pipeline.
PackDropTests.cs verifies native FileDrop payloads and routed WPF preview events from
nested controls, including cues, callback dispatch and source preservation.

0.1.11 adds library/skins/settings navigation, seven semantic color palettes including
a light theme, live Chinese/English interface resources, local background image copying
and persisted appearance settings. Skin management is a placeholder page in this release.
PackRemoval recycles only a validated direct instance folder, rejects filesystem links
and active world locks, and is protected by a name-specific confirmation with No as default.
AppearanceTests exercises persistence, resource switching, page navigation, copied image
restoration, invalid image/path rejection, world locks and recycling synthetic fixtures.

0.1.12 adds SkinStore/SkinPreview/SkinLibraryPage for local PNG collections, modern/slim
and legacy atlas previews, exact-byte export and SHA-256 deduplication. SkinBrowser uses
Microsoft Edge WebView2 to browse NameMC and saves validated downloads into the same
collection, supporting direct texture responses and same-origin browser blob downloads.
Only standard 64x64/64x32 PNGs up to 1 MB are accepted; website content is not executable
through a native host bridge. Skin browsing does not change the active Minecraft skin.
The pinned WebView2 SDK is fetched and verified by build/fetch-webview.ps1. Online
browsing requires the Microsoft Edge WebView2 Runtime; local collections work without it.

## 版本编号规则

按用户指定从旧 0.1.10 起进位为 0.2.0，因此旧 0.1.11 对应 0.2.1，旧 0.1.12 对应 0.2.2。
引入新编号时发布为 0.2.3。补丁位使用 0–9，达到 10 时向次版本进位，例如 0.2.9 的下一版为 0.3.0。
构建和打包使用 Program.cs 的 AssemblyVersion；发布时同步 App.xaml 的版本徽标和文档。

0.2.3 adds per-skin favorite, rename and recycle actions. Favorites sort ahead of other
skins; local names and favorite flags survive restarts and duplicate imports. Missing
Favorite fields in existing records default to false. Deletion confirms the selected name
with No as default, then recycles the PNG and JSON together as one folder. To recover,
restore the .recycle-* folder from Windows Recycle Bin and move its PNG and JSON back
into data/skins, then refresh. Tests cover persistence, ordering, invalid input, duplicate
imports, cancellation rollback and recycling only the selected synthetic fixture.

0.2.4 replaces native pack/skin deletion message boxes with a shared themed confirmation
card: rounded surfaces, selected item name, explicit recycle action, recovery details,
default Cancel, Escape and close actions. It follows all seven launcher palettes and
Chinese/English settings. Existing deletion validation and recycling are unchanged.

0.2.5 adds a signed program-only update pipeline and companion updater. Settings supports
local update ZIPs, an optional HTTPS release feed and startup checks. There is no public
feed configured in this release. The updater has an explicit allow list, SHA-256 file
checks, an embedded RSA public key, bounded extraction and same-root program backups.
It never replaces data, engine, runtime or exports. Failed writes roll back; a root lock
prevents simultaneous installations. The update worker runs from a private application
cache so the installed launcher/updater binaries can both be replaced after exit.

Publisher workflow: preserve .private/update-signing-key.xml outside distributions.
Build with build/build.ps1, then build/package-program-update.ps1. This emits a signed
program ZIP and self-contained update EXE. build/package-friends.py emits the clean
launcher-only full distribution. To activate online checks, rerun the update packaging
script with -BaseUrl https://your-host/release-directory, upload the generated program
ZIP and update.json at that address, then configure the update.json URL in Settings.
HTTPS feed and package signatures are verified against the key embedded in the app;
editing update.json or losing/replacing the signing key breaks existing-client trust.
Never publish the private key or package an in-use launcher directory.

0.2.6 configures the default signed feed at this repository's GitHub Releases latest
asset URL. Release preparation emits an ASCII-named, version-pinned program ZIP and
a signed update.json; publication uploads a complete draft before marking it latest.
See 发布更新说明.md for the publisher workflow. The original 0.2.5 signing key is retained.

0.2.7 fixes CurseForge archived-file handling: an explicitly archived (status 8) pinned
file may be resolved while retaining ID/game/size/SHA-1 checks. Unavailable unknown,
rejected, malware and deleted statuses remain rejected. The provided 390-file pack
was imported into an isolated directory and the archived CDN file's SHA-1 verified.
SkinApplication uploads validated PNGs to the official Minecraft profile service with
the current authorized account, verifies the profile before upload and the returned
active skin afterwards, preserves capes and unknown account fields, and stores one
selected skin ID per account only after success. Unauthorized sessions may renew their
existing authorization; no third-party endpoint receives account tokens. Mock tests
exercise success, failed uploads, account mismatch, renewal, model choice and persistence.
