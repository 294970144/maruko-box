# MarukoBox 项目 Wiki

> **当前版本**：v1.10.3（内置 ffmpeg 7.1.1-5）· 最后维护：2026-10-01
> 本 Wiki 由通读核心源码自动生成，覆盖入口、服务层、页面 / ViewModel、模型、辅助类、测试桩与构建发布全链路。
> 机器可读索引见仓库根的 `aoci.txt` / `aoci.meta.txt`（规则见 `AGENTS.md`）。

**MarukoBox** 是一个基于 FFmpeg 的 Windows 桌面媒体处理工具，技术栈为 .NET + WinUI 3 / Windows App SDK（C#，CommunityToolkit.Mvvm，自研轻量服务定位器，无第三方 DI）。功能包含视频编码、裁剪、轨道抽取、音频转码、封装合并、图片转码、字幕、媒体信息查看。内置 jellyfin-ffmpeg 便携版，支持 GitHub / 国内镜像（Gitee / 兰州大学）双更新源。

## 快速导航

| 模块 | 子页面 | 关键文件 |
|------|--------|----------|
| 架构总览 | [wiki/architecture.md](wiki/architecture.md) | 分层模型、服务定位器、UI 线程封送、日志 |
| 应用入口 | [wiki/app-entry.md](wiki/app-entry.md) | `MarukoBox/App.xaml.cs` · `MarukoBox/MainWindow.xaml.cs` |
| 服务层（核心） | [wiki/services.md](wiki/services.md) | `FfmpegService` · `UpdateService` · `GpuDetectionService` · `ConfigService` · `AppServices` |
| 功能页面 | [wiki/pages.md](wiki/pages.md) | 10 个 Page + 9 个 ViewModel |
| 模型层 | [wiki/models.md](wiki/models.md) | `EncodeSettings` · `QualityPresets` 等 16 个纯数据模型 |
| 辅助层 | [wiki/helpers.md](wiki/helpers.md) | `OutputNaming` · `ShellHelper` 等 8 个 |
| 测试 / 冒烟 | [wiki/harness.md](wiki/harness.md) | `Harness/AppStub.cs` · `Harness/Program.cs` |
| 构建与发布 | [wiki/build-release.md](wiki/build-release.md) | Inno 安装包 · 发布说明索引 |
| 设计决策 / 历史坑 | [wiki/design-decisions.md](wiki/design-decisions.md) | C1/C2/C3 · S1/S2 · N 系列缺陷与对策 |

## 状态与数据位置

| 数据 | 路径 |
|------|------|
| 用户配置 | `%LOCALAPPDATA%\MarukoBox\config.json` |
| 会话（保持习惯） | `%LOCALAPPDATA%\MarukoBox\session.json` |
| 崩溃 / 运行日志 | `%LOCALAPPDATA%\MarukoBox\logs\marukobox.log`（5MB 轮转，保留 3 代） |
| 内置 ffmpeg | `{exe目录}\ffmpeg\ffmpeg.exe` + `ffmpeg\VERSION` |
| 更新包 / 临时解压 | `%LOCALAPPDATA%\MarukoBox\Updates\` |

## 仓库认知层

- `AGENTS.md`：面向 AI 的仓库说明（含 AOCI 索引约定、安全立场）。
- `aoci.txt` / `aoci.meta.txt` / `aoci.code.txt`：机器可读索引。
- `THIRD-PARTY-NOTICES.md`、`LICENSE`：第三方声明与许可。
- `maruko-box.slnx`：解决方案（新版 `.slnx` 格式）。

## 阅读建议

首次接手代码，建议顺序：`architecture.md` → `app-entry.md` → `services.md`（重点 `FfmpegService` 与 `UpdateService`）→ `pages.md`，遇到具体坑再查 `design-decisions.md`。
