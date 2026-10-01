# 模型层

`MarukoBox/Models/*` —— 纯数据模型，零框架依赖（无 WinUI / MVVM 引用），因此可被 `Harness` 测试桩直接复用。

## 编码器与参数

| 模型 | 职责 |
|------|------|
| `EncoderType` | 编码器枚举 + 扩展方法：GPU 判定、ffmpeg 名称、Auto 按硬件解析 |
| `EncodeSettings` | 全量编码参数（编码器、码率控制、CRF / CQP、预设、调优、分辨率、帧率等） |
| `EncodeProgress` | 进度快照（已处理时长 / 总时长 / 速度 / 百分比） |
| `EncoderOption` | 单个编码器可选项（用于面板自适应渲染） |
| `OptionEntry` | 键值型选项条目 |

## 质量与预设

| 模型 | 职责 |
|------|------|
| `QualityPresets` | 质量四档（低 30 / 中 26 / 高 22 / 非常高 18），同时落地 CRF 与 CQP 双路径（必须同步切 `RateControl=cqp` & `CpuMode=crf`，否则四档失效，见 design-decisions B1） |
| `AudioPreset` | 音频转码预设（编码器 / 比特率 / 声道 / 采样率） |

## 会话与状态

| 模型 | 职责 |
|------|------|
| `SessionState` | 「保持习惯」会话状态；含旧格式 `Legacy` 字段迁移逻辑 |
| `GpuInfo` | GPU 探测结果（型号 / 驱动版本 / NVENC API 版本 / 可用性） |
| `FfmpegReleaseRow` | 内置 ffmpeg 发布行（版本 / 下载信息），用于更新链路 |

## 媒体与任务

| 模型 | 职责 |
|------|------|
| `MediaFileInfo` | 媒体文件元信息（封装 / 时长 / 大小） |
| `MediaStreamInfo` | 单条流信息（类型 / 编码 / 语言 / 是否默认） |
| `EncodeItem` | 视频编码队列单项 |
| `TrimRequest` | 裁剪请求（起点 / 时长 / 快速 or 精确） |
| `FrameExtractOptions` | 抽帧选项（模式 / 时间 / 间隔 / 缩放） |
| `MuxInput` | 封装合并的单个输入 |

## 复用要点

- 纯数据、无 UI 依赖，是 `Harness` 能在无 WinUI 环境下跑编码 / 更新冒烟的前提（见 [harness.md](harness.md)）。
- 序列化的配置 / 会话模型（`SessionState` 等）与 `ConfigService` 的 JSON 读写对应（见 [services.md](services.md)）。
