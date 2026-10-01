# 设计决策 / 历史坑

本页汇总 MarukoBox 演进中踩过的关键坑与对应决策。编号用于在代码评审 / 提交中快速引用。多数已在 v1.10.x 修复。

## 编码器参数（Critical 类）

| ID | 现象 | 根因 | 对策 |
|----|------|------|------|
| **C1** | x264/x265 重编码 100% 失败 | 误写 x264 命令行名 `-keyint` / `-min-keyint`（ffmpeg 不认） | 改为 ffmpeg 参数 `-g` / `-keyint_min`（v1.10.1） |
| **C2** | AMF 报 `Error setting option rc to value vbr` | NVENC/AMF/QSV 共用 NVENC 私有参数（`-rc`/`-preset`） | 按 `EncoderType` 分家：NVENC 保留、AMF 用 `-rc cqp\|cbr\|vbr_peak`+`-quality`+`-usage transcoding`、QSV 用 VBV+`-preset` 映射（v1.10.1） |
| **C3** | 能力检测「假可用」 | 只读 `ffmpeg -encoders` 文本（jellyfin 便携版无条件编进全套，无显卡也列 true） | 对每种编码器**真编 5 帧到 null、退出码 0 才算可用**，探针尺寸须 ≥ 320×240（v1.10.1，见 GpuDetectionService） |

## 硬件加速

| ID | 现象 | 根因 | 对策 |
|----|------|------|------|
| **N9 / CUDA** | AMF/QSV 配 `-hwaccel_output_format cuda` + `scale_cuda` 直接 exit 127 | CUDA 显存帧只 NVENC 能消费 | **CUDA 三件套仅限 NVENC**；纯解码侧 `-hwaccel cuda` 对 AMF 无害保留（v1.10.2） |
| **B1'** | `-gpu` 在非 NVENC 报错 | `-gpu` 仅 NVENC 认 | 参数构建时按类型条件追加 |

## 质量与配置

| ID | 现象 | 根因 | 对策 |
|----|------|------|------|
| **B1** | 质量四档（低/中/高/非常高）不生效 | 仅切了单侧控制 | 必须同步切 `RateControl=cqp` & `CpuMode=crf`（CRF 与 CQP 双路径落地，见 QualityPresets） |
| **M2** | 用户自定义 ffmpeg 路径被覆盖 | `ConfigService.Load()` 把解析结果回写 `config.FfmpegPath` | 新增 `[JsonIgnore] ResolvedFfmpegPath` 隔离，7 处 VM 与 4 处设置项改用解析值（v1.10.1） |
| **S-配置重置** | 保存设置静默重置 `AutoCheckUpdates` / `NavPaneExpandedWidth` | `SaveAsync` 用 `new AppConfig{…}` 只赋 9/11 字段 | 改为「载入 → 改字段 → 写穿」（v1.10.1） |

## 更新链路安全

| ID | 现象 / 要求 | 对策 |
|----|------------|------|
| **S1 / S2 / S5 / N8** | 下载投毒 / 完整性 | 下载落 `%LOCALAPPDATA%` 防 TOCTOU；版本号白名单正则；SHA-256 校验，缺失来源显式警告 |
| **S-TOCTOU** | 校验→解压间存在 TOCTOU 窗口 | 临时解压目录从 `%TEMP%` 挪到 `UpdatesDirectory`（同威胁模型，v1.10.2） |
| **S-HttpClient** | ffmpeg zip 被缓冲上限截断 | 元数据走 60s+10MB 专用 client；大文件下载保留无上限 client |
| **NVENC 驱动门槛** | 驱动过低静默失败 | 8.x 需 NVIDIA 驱动 ≥ 610，低于显式提示 |

## UI 与交互

| ID | 现象 | 根因 | 对策 |
|----|------|------|------|
| **N7** | unpackaged 下退出行为未定义 | `Application.Exit()` 在 unpackaged 行为不确定 | 统一用 `App.Shutdown()` |
| **0xc000027b** | 后台 ffmpeg 回调触发闪退 | 跨线程访问 UI | 所有回调经 `App.RunOnUiThread` 封送（见 architecture） |
| **S-并发** | 抽取页并发触发 | 缺 `IsBusy` 守卫 | 补齐守卫（其余页本有，v1.10.1） |
| **S-输出目录** | 目录不存在无兜底 | 部分页面缺回退 | 新增 `EnsureOutputDir` 回退源目录，Audio/Video/Subtitle 接入（v1.10.1） |
| **S-布局** | 设置页内容靠右留大片空白 | 根 `StackPanel` 只设 `MaxWidth` 未设 `HorizontalAlignment`，Stretch 被截断后落位靠右 | 去 `MaxWidth` 铺满视口；`AboutPage` 补 `HorizontalAlignment="Left"`（v1.10.3） |
| **S-格式化** | 时间 / 数值格式化问题 | 缺 `InvariantCulture`、单帧缺 `-y`、队列去重大小写敏感 | 补 `InvariantCulture`、单帧补 `-y`、去重改大小写不敏感（v1.10.1） |

## 系统通知（unpackaged）

| ID | 现象 | 根因 | 对策 |
|----|------|------|------|
| **N-Toast** | 通知永远不弹 | unpackaged 无包身份（AUMID），要求开始菜单存在一条指向当前 exe、且设置了 `System.AppUserModel.ID` 的快捷方式 | `NotificationService.Initialize()` 注册前经 `ShortcutHelper.EnsureShortcut()` 创建 / 修复该快捷方式（文件名刻意与 Inno 的「MarukoBox 2026.lnk」一致，避免重复入口）；`Package.appxmanifest` 的 Identity 在 unpackaged 运行时不生效，不可依赖 |
| **N-Toast-2** | 快捷方式已就绪，但 `Register()` 仍抛 `COMException: 无法遍历该路径，因为它包含不受信任的装入点`（0x800701C0） | WinAppSDK 的 `Register()` 内部枚举开始菜单/资源时会遍历到每个 Windows 用户都有的系统标准 junction `%LOCALAPPDATA%\Application Data`（指向 `%LOCALAPPDATA%` 自身），被判定为不受信任装入点而抛错。与安装路径无关（已用 `fsutil reparsepoint query` 确认 `D:\Programs\MarukoBox` 是真实目录，非装入点）。升级到 2.5.1 后仍偶发 | 不再把 0x800701C0 视为致命错误。与 self-contained unpackaged 下 `0x8007007E`（缺 `Microsoft.WindowsAppRuntime.Insights.Resource.dll`）类似，按 GitHub issue #6774 经验，`Show()` 通常仍可显示 Toast。因此捕获 0x800701C0/0x8007007E 后均把 `_degradedSendAllowed` 置为 true，允许 `IsAvailable` 为真并继续尝试发送通知（v1.10.5） |
| **N-Toast-3** | 显式重载 `Register(displayName, iconUri)` 抛 `文件名、目录名或卷标语法不正确`（0x8007007B） | 在 WinAppSDK 2.4.0 上，显式重载不接受 `file:///` 指向 `Assets\StoreLogo.png` 的 URI；该重载本身在此版本有兼容性问题 | 放弃显式重载，改用无参 `Register()` + WinAppSDK 2.5.1；AUMID 快捷方式继续保留作为应用身份兜底 |

**要点**：`ShortcutHelper` 移植自 Windows App SDK 官方 unpackaged 通知示例——经 `IShellLinkW` + `IPropertyStore` 把 `System.AppUserModel.ID`（键 `{9F4C2855-9F79-4B39-A8D0-E1D42DE1D5F3}`, pid 5）写入 `.lnk` 的 extra-data 块，再 `IPersistFile.Save`。该值在 `.lnk` 上走特殊存储，`IPropertyStore.GetValue` 读不到（返回 `VT_EMPTY`），但 Windows 通知子系统按 AUMID 查找快捷方式时能正确取到——验证应直接检查 `.lnk` 字节中是否落盘该字符串，而非 `GetValue`。

## 导航栏拖拽重排（v1.10.6）

| ID | 现象 / 要求 | 根因 / 决策 | 对策 |
| --- | --- | --- | --- |
| **N16** | 导航栏功能页顺序固定，用户无法按习惯调整 | 原 `MenuItems` 全静态写死在 XAML，顺序不可变；**NavigationView 的菜单区内部是 ItemsRepeater，没有内置重排与让位动画**——自绘拖拽（v1.10.6 前两版：先「幽灵卡跟随鼠标」、再「占槽实时换位」）都卡在同一处：换位无动画、跳格生硬，观感远不及 Edge | 改走**官方 drag-reorder**：整个侧栏（8 个功能页 + 设置/关于）统一放进 `NavigationView.PaneCustomContent` 的 `ListView`，启用 `CanDragItems` + `AllowDrop` + `CanReorderItems` 三件套，由系统提供「被拖项跟随鼠标」的拖动视觉、插入指示线与平滑让位动画——一次性解决「无动画 / 与鼠标脱节 / 长按迟钝」三项（改为按下即拖）。硬性前提：数据源必须 `ObservableCollection`（`List` 不会自动更新顺序）、`ItemsPanel` 必须实现 `IInsertionPanel`（默认面板满足，换自定义面板会显示禁止图标）。设置/关于标 `IsFixed=true`：`DragItemsStarting` 里 `Cancel` 禁拖，拖拽完成后 `NormalizeFixedItemsToEnd` 强制归位末尾。`NavItemTemplateSelector` 给固定项套带分组分隔线与徽标位的模板。徽标改为经 `NavItemModel` 的 `UpdateBadgeVisibility`/`RestartBadgeVisibility` 绑定（窗口加载前调用会暂存、构建后套用） |
| **N16-4** | 拖拽时「原地还留了一个」，拖动卡位移过去与残留卡片重叠 | ①**只替换容器 ControlTemplate 会丢失一切内置拖拽视觉**：WinUI 的 `ListViewItem` 由 `ListViewItemPresenter` 呈现，选中/悬停/**拖起视觉与让位动画**都在它内部；我们此前用自定义 `Grid`+`ContentPresenter` 复刻 `NavigationViewItem` 观感，等于把 presenter 换掉，于是源项呆在原地不动、邻居瞬移 → 双卡重叠。②`ListView` 内置重排本身也会把源项继续渲染在列表里（作被拖占位），与系统 drag visual 天然构成「两张卡」 | ①项容器改**轻量样式**（Lightweight styling）：只设 `MinHeight`/`Padding`/`Margin`/`CornerRadius`/对齐等 Setter，**绝不替换 ControlTemplate**——顺带发现 WinUI 3 默认 `ListViewItem` 自带左缘选中竖条（社区有「如何关掉它」的提问），观感本就与 `NavigationViewItem` 一致，自定义模板纯属多余。②`DragItemsStarting` 里把源容器 `Opacity=0` 隐藏（**不是 `Visibility=Collapsed`**：保留占位高度，空槽才能随插入位置移动，即「经过时让出空位」）；`DragItemsCompleted` 无条件恢复（含取消拖拽路径），并全量兜底复位一遍以防容器被虚拟化复用后留下永久透明项 |
| **N16-2** | 与根网格「窗格拖拽调宽」热区冲突 | 两者都吃指针事件 | Grip 在窗格内部、远离右缘 6px 热区；Grip `PointerPressed` 置 `e.Handled=true` 阻止冒泡到根网格，根网格只在 `IsNearPaneEdge` 时响应，互不抢占 |
| **N16-3** | 顺序需「保持习惯」门控 | 顺序属用户习惯，应随「保持习惯」开关 | 仅当 `AppConfig.RememberLastSession` 开启才在 `PersistNavOrder` 写 session；关闭时本次会话可调、退出不记，下次启动回落默认。设置/关于（`IsFixed=true`）固定不参与重排且不写入顺序。设置页「导航顺序自定义」卡片提供「恢复默认导航顺序」按钮（`ResetNavOrderCommand` → `MainWindow.ResetNavOrderToDefault`） |

## 安全与脱敏

- **路径脱敏**：日志中路径统一替换为 `<path>`。
- **命令注入**：自定义参数拦截注入字符（`;` / `&` / `|` 等）。
- **不自动下载更新**：仅提示，符合既有安全立场（见 UpdateService）。

> 以上 ID 与 `release-notes-v1.10.3.md` 的 C1/C2/C3、CUDA 三件套、Harness 假绿等条目一一对应。
