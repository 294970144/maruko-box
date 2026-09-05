# 小丸工具箱 (MarukoBox)

> 本项目是基于经典 [**小丸工具箱**](https://maruko.appinn.me/) 的理念重做现代化版本：保留「简单易用的压制工具箱」这一核心体验，用 WinUI 3 全面重构，并加入 GPU 硬件加速与内置 ffmpeg。

基于 [**ffmpeg**](https://ffmpeg.org/) 的 Windows 桌面多媒体处理工具箱，使用 **WinUI 3** + **.NET 10** 构建，走 **unpackaged（免 MSIX / 免商店）** 部署路线，安装无需管理员权限、无需代码签名。

## 功能

- **视频**：转码、压制（调用 ffmpeg，可配合 GPU 硬件加速）
- **音频**：提取、转码、轨道处理
- **图片**：从视频抽帧、格式转换
- **字幕**：提取、嵌入、字幕格式转换
- **封装 (Mux)**：音视频流重新封装
- **内置 ffmpeg**：安装包捆绑 [jellyfin-ffmpeg](https://github.com/jellyfin/jellyfin-ffmpeg) 便携版，开箱即用，无需自行下载
- **检查更新**：设置页一键检查/升级内置 ffmpeg，支持国内镜像站与 GitHub 双渠道
- **主题设置**：跟随系统 / 浅色模式 / 深色模式

## 技术栈

| 项 | 说明 |
|---|---|
| UI | WinUI 3 (Windows App SDK) |
| 运行时 | .NET 10 (net10.0-windows) |
| 部署 | unpackaged，`WindowsPackageType=None`，自包含 |
| 安装器 | Inno Setup 6 |
| 核心引擎 | ffmpeg（内置捆绑 jellyfin-ffmpeg 便携版） |

## 仓库结构

```
maruko-box/
├── MarukoBox/          主程序源码 (WinUI3)
├── Installer-Inno/     Inno Setup 参数化脚本三件套（一键构建）
├── Harness/            开发调试脚手架（服务层冒烟测试，含无 UI 环境用的 App 桩）
├── third_party/        [gitignore] 内置 ffmpeg 便携版 zip 放这里（构建时自动捆绑）
└── dist/               [gitignore] 构建产物（安装包 exe），可一键重建
```

> `dist/` 不入库（安装包是二进制大文件，走 GitHub Release 分发）。安装包可用下方构建脚本从源码重新生成。

## 环境要求（仅构建时需要）

- Windows 10 / 11 (x64)
- [.NET 10 SDK](https://dotnet.microsoft.com/download)
- [Inno Setup 6](https://jrsoftware.org/isinfo.php)（需要 `ISCC.exe`）
- PowerShell 7（`pwsh`）

## 构建安装包

提供**一键构建脚本**，中间产物默认落到 `%TEMP%` 并在成功后自动清理。

```powershell
pwsh -File Installer-Inno\build-installer.ps1
# 产物：dist\MarukoBoxSetup-Inno_1.0.0.exe
```

流程：`dotnet publish` 主程序（自包含 unpackaged） → 解压内置 ffmpeg 进 payload → `ISCC.exe` 编译 `.iss` 生成安装包（含中文向导、开始菜单快捷方式、卸载注册）。

内置 ffmpeg 源（优先级：`-FfmpegZip` 参数 > 自动发现 `third_party\jellyfin-ffmpeg*portable_win64.zip` > 不捆绑）：

```powershell
# 放置 zip 后直接构建即可；仓库不入库二进制
# jellyfin-ffmpeg_7.1.1-5-portable_win64.zip  ->  third_party/
pwsh -File Installer-Inno\build-installer.ps1 -KeepPayload
```

## 安装与卸载

```text
MarukoBoxSetup-Inno_1.0.0.exe /VERYSILENT /SUPPRESSMSGBOXES /NORESTART   安装
unins000.exe /VERYSILENT /SUPPRESSMSGBOXES /NORESTART                     卸载
```

安装位置为当前用户目录（`%LOCALAPPDATA%\Programs\MarukoBox`），**无需 UAC**。

## 关于 ffmpeg 依赖

安装包默认**内置 jellyfin-ffmpeg 便携版**（解压至 `{app}\ffmpeg\`），开箱即用。程序按以下优先级解析生效路径：

1. **内置**：安装目录下 `ffmpeg\ffmpeg.exe`（默认，可经「设置 → 检查更新」升级）
2. **手动配置**：设置页手动指定的路径
3. **PATH**：系统环境变量中的 ffmpeg

若构建时未捆绑 zip，安装包则不含内置 ffmpeg，需自行提供（放入程序目录或加入 `PATH`）。

## 许可证

见 [LICENSE](./LICENSE)（如需开源请补充对应许可证文件）。
