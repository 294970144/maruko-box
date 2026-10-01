# 辅助层

`MarukoBox/Helpers/*` —— 与 UI 交互、文件系统、命名相关的独立工具类，被各 ViewModel 复用。

## 文件与命名

| 文件 | 职责 |
|------|------|
| `OutputNaming.cs` | 10 种输出命名规则；批处理防同名互覆 `DedupeBatch` |
| `OutputPathHelper.cs` | 输出目录解析与兜底（不存在时回退源文件目录） |
| `PickerHelper.cs` | 文件 / 文件夹选择器封装 |
| `FileDropHelper.cs` | 拖放（Drag & Drop）接收文件 |

## 系统交互与确认

| 文件 | 职责 |
|------|------|
| `ShellHelper.cs` | 资源管理器定位文件 / 打开所在目录 |
| `UiConfirm.cs` | 危险操作二次确认（清空队列 / 清空轨道等） |

## 裁剪专用

| 文件 | 职责 |
|------|------|
| `TrimTimeline.xaml` / `TrimTimeline.xaml.cs` | 裁剪时间轴控件，提供起点缩略图与区间选择 |

## 设计约束

- `OutputNaming.DedupeBatch` 解决批量输出同名互覆：对生成的文件名做去重计数，避免后写覆盖先写。
- 输出目录兜底（`OutputPathHelper.EnsureOutputDir`）在 v1.10.1 接入 Audio / Video / Subtitle 三页，修复目录不存在时无兜底的问题。
- `UiConfirm` 与 v1.10.0「危险操作确认」界面规范一致：清空类操作先确认再执行。

## 相关文件

- 命名规则被 `FfmpegService` 任务产出使用，见 [services.md](services.md)。
- 裁剪时间轴服务于 `TrimPage`，见 [pages.md](pages.md)。
