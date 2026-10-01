# 架构总览

MarukoBox 采用分层 + 轻量服务定位器的结构，UI 层（Page / ViewModel）依赖服务层，服务层通过 `AppServices` 静态单例相互发现，模型层为纯数据、零框架依赖、可被测试桩 `Harness` 复用。

## 分层模型

```
App.xaml.cs / MainWindow.xaml.cs        ← 应用入口 + 导航外壳（NavigationView）
   └─ AppServices (静态服务定位器)
        ├─ GpuDetectionService          硬件 / NVENC / AMF / QSV 能力探测
        ├─ FfmpegService                编码 / 抽取 / 转码 / 封装 / 抽帧 / 字幕 / 裁剪
        ├─ ConfigService                config.json / session.json 持久化
        └─ UpdateService                软件自身 + 内置 ffmpeg 更新
   └─ Pages/*.xaml(.cs)                 各功能页（代码后置仅做导航 / 拖拽调宽 / 事件转发）
        └─ ViewModels/*.cs              业务逻辑 + 双向绑定（CommunityToolkit.Mvvm）
             └─ Models/*.cs             纯数据模型（无框架依赖）
   └─ Helpers/*.cs                      文件选择 / 拖放 / 命名 / Shell / 确认 / 裁剪时间轴
```

核心约束与模式：

- **UI 线程封送**：ffmpeg 在后台进程运行，进度回调必须经由 `App.RunOnUiThread` 封送回 UI 线程，否则触发 `0xc000027b` 线程模型异常导致闪退。所有 `FfmpegService` 的进度 / 完成回调都走此通道。
- **统一退出**：unpackaged 应用下 `Application.Exit()` 行为未定义，统一用 `App.Shutdown()` 退出（见 `design-decisions.md` N7）。
- **统一日志**：`App.LogCrash` / `App.LogInfo` 落盘到 `%LOCALAPPDATA%\MarukoBox\logs\marukobox.log`；日志中路径统一替换为 `<path>`，自定义参数拦截命令注入字符。
- **配置 / 会话分离**：普通配置走 `config.json`；「保持习惯」会话状态走 `session.json`，受独立开关控制，互不影响。

## 服务定位器 `AppServices`

`MarukoBox/AppServices.cs` 是静态单例容器，启动时由 `App.xaml.cs` 构造并持有四个服务实例。各 ViewModel 通过 `AppServices.Instance.*` 访问，避免引入第三方 DI 容器。服务间也不通过接口耦合，直接引用具体类型。

## 启动数据流

```
App.xaml.cs
  ├─ 全局异常兜底 + 崩溃日志轮转
  ├─ 应用主题
  ├─ 构造 AppServices（GpuDetection / Ffmpeg / Config / Update）
  ├─ ConfigService.Load() 解析 ffmpeg 路径
  ├─ 启动 15s 延时静默检查更新（可在设置关闭）
  └─ 启动 MainWindow → NavigationView 默认页
MainWindow.xaml.cs
  ├─ 导航菜单（含「保持习惯」会话恢复 SaveSessionIfEnabled）
  ├─ 导航窗格拖拽调宽（根网格热区方案，v1.9.1+）
  └─ 功能页导航项改为 NavItemModel 集合驱动，支持 Grip 拖拽重排（v1.10.6；顺序存 session.json，跟随「保持习惯」）
```

## 模块清单（与子页面映射）

| 层 | 入口 | 详情 |
|----|------|------|
| 入口 | `App.xaml.cs` / `MainWindow.xaml.cs` | [app-entry.md](app-entry.md) |
| 服务 | 4 个 Service + AppServices | [services.md](services.md) |
| 页面 | 10 Page + 9 ViewModel | [pages.md](pages.md) |
| 模型 | 16 个模型类 | [models.md](models.md) |
| 辅助 | 8 个 Helper | [helpers.md](helpers.md) |
| 测试 | Harness 桩 + 冒烟 | [harness.md](harness.md) |
| 发布 | Inno 安装包 + 发布说明 | [build-release.md](build-release.md) |
