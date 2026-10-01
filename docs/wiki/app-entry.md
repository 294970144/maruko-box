# 应用入口

应用入口由两个文件承担：全局生命周期与异常处理在 `App.xaml.cs`，导航外壳与窗口交互在 `MainWindow.xaml.cs`。

## 1. `App.xaml.cs`

`MarukoBox/App.xaml.cs`

- **全局异常兜底**：捕获未处理异常并落盘崩溃日志，避免无提示退出。
- **崩溃日志轮转**：`%LOCALAPPDATA%\MarukoBox\logs\marukobox.log`，5MB 上限、保留 3 代。
- **主题应用**：根据配置应用浅色 / 深色主题。
- **启动初始化**：构造 `AppServices` 单例，触发 `ConfigService.Load()` 解析 ffmpeg 路径。
- **静默检查更新**：启动后延时 **15s** 检测一次（错开启动高峰），可在设置页开关；仅提示不自动下载。
- **统一退出**：提供 `App.Shutdown()`，unpackaged 下替代行为未定义的 `Application.Exit()`（见 design-decisions N7）。
- **UI 线程封送**：`App.RunOnUiThread` 将后台 ffmpeg 回调封送回 UI 线程，避免 `0xc000027b` 闪退。
- **日志**：`App.LogCrash` / `App.LogInfo`；路径脱敏为 `<path>`。

## 2. `MainWindow.xaml.cs`

`MarukoBox/MainWindow.xaml.cs`

- **导航外壳**：基于 `NavigationView` 承载 10 个功能页，代码后置只做导航跳转与事件转发，业务逻辑全部下放到 ViewModel。
- **导航窗格拖拽调宽（v1.9.0–1.9.2）**：鼠标移到左侧导航栏右缘可拖动调宽，默认 320px、范围 160–480px，宽度记忆到配置文件且不受「保持习惯」开关影响（布局偏好属窗口外观，与会话无关）。
  - v1.9.1 修正：初版透明热区首次悬停后失效，改为**根网格热区方案**（按坐标判定热区、位置零维护）。
  - v1.9.2 取舍：放弃「拖窄自动收成纯图标」，改为始终带文字的连续调宽，避免离散跳变突兀。
- **「保持习惯」会话保存**：`SaveSessionIfEnabled` 在退出 / 导航时按需把会话状态写入 `session.json`。

## 启动 → 导航数据流

```
App.xaml.cs
  ├ 异常兜底 / 日志轮转 / 主题
  ├ 构造 AppServices（GpuDetection/Ffmpeg/Config/Update）
  ├ 解析 ffmpeg 路径
  ├ 15s 后静默检查更新
  └ 启动 MainWindow
MainWindow.xaml.cs
  ├ NavigationView 加载默认页
  ├ 导航窗格拖拽调宽（根网格热区）
  └ SaveSessionIfEnabled（会话保存）
```

## 相关文件

- 服务层入口：[services.md](services.md)
- 各功能页：[pages.md](pages.md)
- 会话 / 配置持久化：见 [services.md](services.md) 的 `ConfigService`
