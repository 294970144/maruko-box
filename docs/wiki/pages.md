# 功能页面（Pages + ViewModels）

每个功能页由 `Pages/*.xaml`（界面）+ `Pages/*.xaml.cs`（代码后置，仅做导航 / 拖拽调宽 / 事件转发）+ `ViewModels/*.cs`（业务逻辑 + 双向绑定）组成。代码后置刻意保持单薄，逻辑全部下放 ViewModel。

## 功能清单

| 功能 | Page | ViewModel | 说明 |
|------|------|-----------|------|
| 视频编码 | `VideoPage` | `VideoViewModel` | 批量队列、编码参数、质量四档、实时进度、取消；「编码完成后」关机 / 休眠 / 退出；Auto 按硬件解析编码器并自适应面板 |
| 裁剪 | `TrimPage` | `TrimViewModel` | 快速（拷贝码流）/ 精确（重编码），起点缩略图；参数顺序严格（`-ss` 前置于 `-i`，时长用 `-t`） |
| 抽取 | `ExtractPage` | `ExtractViewModel` | 按流无损抽取（`-c copy`），后缀按编码选容器；含 `IsBusy` 并发守卫（v1.10.1 补齐） |
| 音频 | `AudioPage` | `AudioViewModel` | 转码 aac / opus / flac / mp3，声道 / 采样率；输出目录回退到源目录（v1.10.1） |
| 封装 | `MuxPage` | `MuxViewModel` | 多输入无损合并（`-c copy`），MP4 加 faststart；队列去重大小写不敏感 |
| 图片 | `ImagePage` | `ImageViewModel` | png / jpg / webp 互转；数值统一 `NumberBox`（v1.10.0），缩放留空 = 不缩放 |
| 字幕 | `SubtitlePage` | `SubtitleViewModel` | 嵌入 + srt / ass / webvtt 互转；输出目录回退（v1.10.1） |
| 工具 | `ToolsPage` | `ToolsViewModel` | 媒体信息查看（`ProbeInfoAsync`） |
| 设置 | `SettingsPage` | `SettingsViewModel` | 普通 / 高级 / 专家三模式、脏状态红点、ffmpeg 路径校验、GPU 设备引导、检查更新 / 检查依赖、专家专列 ffmpeg 版本强装 |
| 关于 | `AboutPage` | — | 版本 / 许可信息；`HorizontalAlignment="Left"` 修正（v1.10.3） |
| 占位 | `PlaceholderPage` | — | 未启用功能占位页 |

## 跨页面共享模式

- **批量队列**：视频 / 音频 / 封装等支持多文件入队，统一 `EncodeItem` / `MuxInput` 模型承载。
- **实时进度**：ffmpeg `-progress` 输出经 `FfmpegService` → `App.RunOnUiThread` 封送回 UI（见 architecture）。
- **取消**：各 VM 维护 `IsBusy` / 取消令牌，防止并发重入（抽取页曾缺守卫，v1.10.1 修复）。
- **「编码完成后」动作**：仅 Video 支持关机 / 休眠 / 退出。
- **Auto 编码器**：`EncoderType` 按检测结果解析 NVENC / AMF / QSV / CPU，自适应显示参数面板。
- **危险操作确认**：清空队列 / 清空轨道先弹确认框（v1.10.0 统一规范）。
- **按钮权重规范（v1.10.0）**：分析 / 检测 / 查看类为描边按钮，执行类为 Accent 实心（修正了字幕 / 工具 / 抽取页反置问题）。
- **输出目录兜底**：目录不存在时回退到源文件目录（`EnsureOutputDir`，v1.10.1）。

## 设置页三模式

`SettingsViewModel` 分普通 / 高级 / 专家三档，专家模式额外暴露 ffmpeg 版本强装等底层操作。脏状态在左侧导航与设置项旁以红点提示；「更新源」切换即改即存、从脏追踪中移除（v1.9.0）。保存采用「载入 → 改字段 → 写穿」，修复了 v1.10.1 中 `new AppConfig{…}` 只赋 9/11 字段导致 `AutoCheckUpdates` / `NavPaneExpandedWidth` 被静默重置的缺陷。

## 相关文件

- 任务执行核心：[services.md](services.md) 的 `FfmpegService`
- 模型定义：[models.md](models.md)
- 界面统一规范与历史坑：[design-decisions.md](design-decisions.md)
