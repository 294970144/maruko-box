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
| 安装器 | 两条可选路线：Native AOT 自解压 / Inno Setup 6 |
| 核心引擎 | ffmpeg（运行时依赖，见下文） |

## 仓库结构

```
maruko-box/
├── MarukoBox/          主程序源码 (WinUI3)
├── MarukoBoxSetup/     AOT 自解压安装器源码 + 一键构建脚本
├── Installer-Inno/     Inno Setup 参数化脚本三件套
├── Harness/            安装/卸载冒烟测试辅助工具
└── dist/               [gitignore] 构建产物（安装包 exe），可一键重建
```

> `dist/` 不入库（单文件超过 GitHub 100MB 限制）。安装包通过下方构建脚本从源码重新生成。

## 环境要求（仅构建时需要）

- Windows 10 / 11 (x64)
- [.NET 10 SDK](https://dotnet.microsoft.com/download)
- **Visual Studio「使用 C++ 的桌面开发」工作负载**（AOT 路线需要 `link.exe`；纯 Inno 路线可省略）
- [Inno Setup 6](https://jrsoftware.org/isinfo.php)（Inno 路线需要 `ISCC.exe`）
- PowerShell 7（`pwsh`）
- Python 3（AOT 路线 `append_payload.py`，仅用标准库）

## 构建安装包

两条路线均提供**一键构建脚本**，中间产物默认落到 `%TEMP%` 并在成功后自动清理。

### 路线 A：Native AOT 自解压安装器（约 105 MB）

```powershell
pwsh -File MarukoBoxSetup\build-installer.ps1
# 产物：dist\MarukoBoxSetup_1.0.0.exe
```

流程：`dotnet publish` 主程序 → 压缩为 payload.zip → Native AOT 编译安装器 stub → 将 payload 追加到 stub 末尾。安装时 stub 自解压到当前用户程序目录。

### 路线 B：Inno Setup 安装包（约 70 MB）

```powershell
pwsh -File Installer-Inno\build-installer.ps1
# 产物：dist\MarukoBoxSetup-Inno_1.0.0.exe
```

流程：`dotnet publish` 主程序 → `ISCC.exe` 编译 `.iss` 生成安装包（含中文向导、开始菜单快捷方式、卸载注册）。

## 安装与卸载

### AOT 路线

```text
MarukoBoxSetup_1.0.0.exe /silent           安装（静默）
MarukoBoxSetup_1.0.0.exe /uninstall /silent 卸载（静默，三清：目录+快捷方式+注册表）
MarukoBoxSetup_1.0.0.exe /?                 查看帮助
```

### Inno 路线

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
