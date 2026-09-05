# 小丸工具箱 (MarukoBox)

基于 [**ffmpeg**](https://ffmpeg.org/) 的 Windows 桌面多媒体处理工具箱，使用 **WinUI 3** + **.NET 10** 构建，走 **unpackaged（免 MSIX / 免商店）** 部署路线，安装无需管理员权限、无需代码签名。

## 功能

- **视频**：转码、压制（调用 ffmpeg，可配合 GPU 硬件加速）
- **音频**：提取、转码、轨道处理
- **图片**：从视频抽帧、格式转换
- **字幕**：提取、嵌入、字幕格式转换
- **封装 (Mux)**：音视频流重新封装

## 技术栈

| 项 | 说明 |
|---|---|
| UI | WinUI 3 (Windows App SDK) |
| 运行时 | .NET 10 (net10.0-windows) |
| 部署 | unpackaged，`WindowsPackageType=None`，自包含 |
| 安装器 | Inno Setup 6 |
| 核心引擎 | ffmpeg（运行时依赖，见下文） |

## 仓库结构

```
maruko-box/
├── MarukoBox/          主程序源码 (WinUI3)
├── Installer-Inno/     Inno Setup 参数化脚本三件套（一键构建）
├── Harness/            开发调试脚手架（服务层冒烟测试，含无 UI 环境用的 App 桩）
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
# 产物：dist\MarukoBoxSetup-Inno_1.0.0.exe（约 70 MB）
```

流程：`dotnet publish` 主程序（自包含 unpackaged） → `ISCC.exe` 编译 `.iss` 生成安装包（含中文向导、开始菜单快捷方式、卸载注册）。

## 安装与卸载

```text
MarukoBoxSetup-Inno_1.0.0.exe /VERYSILENT /SUPPRESSMSGBOXES /NORESTART   安装
unins000.exe /VERYSILENT /SUPPRESSMSGBOXES /NORESTART                     卸载
```

安装位置为当前用户目录（`%LOCALAPPDATA%\Programs\MarukoBox`），**无需 UAC**。

## 关于 ffmpeg 依赖

本工具箱本身不内置 ffmpeg 二进制。安装器在部署时会检测 ffmpeg 是否存在；
缺失时给出提示。使用前请确保：

- ffmpeg / ffprobe 位于程序所在目录，或
- 已加入系统 `PATH`

## 许可证

见 [LICENSE](./LICENSE)（如需开源请补充对应许可证文件）。
