# 服务层（核心）

服务层是 MarukoBox 的业务心脏，全部为静态 / 单例风格，由 `AppServices` 持有。详见 [architecture.md](architecture.md)。

文件位置：`MarukoBox/Services/*`、`MarukoBox/AppServices.cs`

## 1. `AppServices`（服务定位器）

`MarukoBox/AppServices.cs`

- 静态单例 `AppServices.Instance`，在 `App.xaml.cs` 启动阶段构造。
- 持有 `GpuDetection`、`Ffmpeg`、`Config`、`Update` 四个服务实例。
- 各 ViewModel 直接引用具体类型，无接口抽象、无第三方 DI。

## 2. `FfmpegService`（编码 / 任务执行）

`MarukoBox/Services/FfmpegService.cs`

职责：编码器参数构建、ffmpeg 进程执行、实时进度解析、失败原因提取，覆盖全部媒体任务。

- **编码器参数分家构建（关键）**：NVENC / AMF / QSV 的 `-rc` / `-preset` / `-tune` 私有参数不同，必须按 `EncoderType` 分别构建，避免误用私有 AVOption（见 C2 / C3）。`-gpu` 仅 NVENC 认。
- **进度解析**：读取 `-progress` 输出，解析 `out_time_ms` / `total_duration_ms` / `speed` 等，经 `App.RunOnUiThread` 封送回 UI（见 architecture 线程约束）。
- **失败原因**：进程非零退出时提取 stderr 尾部 40 行与真实错误消息，写入日志与界面（v1.10.1+）。
- **任务类型**：编码、抽取（`-c copy`）、转码、封装合并、抽帧、字幕嵌入 / 互转、裁剪（快速拷贝码流 / 精确重编码）。

## 3. `UpdateService`（软件 + 内置 ffmpeg 更新）

`MarukoBox/Services/UpdateService.cs`

- **双更新源**：GitHub（官方）与国内镜像（Gitee / 兰州大学），切换即写穿配置。
- **更新对象**：①软件自身安装包；②内置 ffmpeg（jellyfin-ffmpeg，含镜像索引解析）。
- **NVENC 驱动门槛**：8.x 需 NVIDIA 驱动 ≥ 610，低于则显式提示不静默失败。
- **安全校验**：版本号白名单正则；下载 SHA-256 校验，缺失来源显式警告；临时解压目录落在 `UpdatesDirectory`（非 `%TEMP%`）消除 TOCTOU 窗口（v1.10.2+）。
- **原子替换 + 中断自愈**：整目录替换，更新过程被中断可由 `RecoverBundledBackup` 自愈。
- **HttpClient 拆分**：元数据（JSON / 索引 / 校验）走 60s 超时 + 10MB 上限专用 client；大文件下载保留无上限 client，避免 ffmpeg zip 被缓冲上限截断。
- **版本号工具**：`GetAppVersionStatic()` 取程序集版本；`CompareVersions` / `NormalizeTag` 处理 `v1.10.3` 等 tag。

## 4. `GpuDetectionService`（能力探测）

`MarukoBox/Services/GpuDetectionService.cs`

- 并发探测 ffmpeg 版本 / 编码器 / 滤镜 / hwaccel 列表。
- **真实可用性（v1.10.1+）**：不只读 `ffmpeg -encoders` 文本（jellyfin 便携版无条件编进全套，无显卡也列 true），而是对每种编码器**真编 5 帧到 null、退出码 0 才算可用**。探针尺寸须 ≥ 320×240，否则 NVENC 拒收。
- 进程内缓存，避免重复探测开销。

## 5. `ConfigService`（持久化）

`MarukoBox/Services/ConfigService.cs`

- 读写 `config.json` 与 `session.json`。
- **ffmpeg 路径解析顺序**：内置便携版 → 配置项 → 系统 PATH；解析结果经 `[JsonIgnore] ResolvedFfmpegPath` 隔离，**不回写** `config.FfmpegPath`（v1.10.1 修复，避免覆盖用户自定义路径）。
- **旧格式自动迁移**：识别并迁移历史配置字段。
- **内置备份自愈**：`RecoverBundledBackup` 在目录损坏 / 缺失时从内置副本恢复。

## 6. `NotificationService`（系统通知）

`MarukoBox/Services/NotificationService.cs`（unpackaged 通知通道）

- 封装 Windows App SDK 的 `AppNotificationManager`，落地「任务完成」类通知（视频 / 音频批量编码全部成功时触发）。
- **注册前提（关键）**：unpackaged 应用无包身份，注册前必须存在带 `System.AppUserModel.ID` 的开始菜单快捷方式——由 `Helpers/ShortcutHelper.cs` 在 `Initialize()` 注册前创建 / 修复（详见 [design-decisions.md](design-decisions.md) N-Toast）。
- **健壮性**：`Initialize()` / `Uninitialize()` 全程 try/catch，注册失败仅降级为「本轮不弹通知」，绝不阻断启动；`_registered` 守卫保证注销幂等，`Shutdown()` 与 `ProcessExit` 双保险。
- **交互**：点击通知经 `NotificationInvoked` → `MainWindow.BringToForeground()` 把窗口提到前台（含最小化恢复）。
- **Harness 隔离**：`#if !MARUKO_HARNESS` 下为真实实现，Harness 下为空实现（不引用 WindowsAppSDK / `App`）。

## 设计要点交叉引用

- 编码器参数分家、CUDA 仅 NVENC、质量四档双路径等，见 [design-decisions.md](design-decisions.md)。
- 服务层由 `Harness` 在无 UI 下直接驱动做冒烟测试，见 [harness.md](harness.md)。
