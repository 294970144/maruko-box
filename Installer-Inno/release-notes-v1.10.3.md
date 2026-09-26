# MarukoBox v1.10.3

> 距离上次公开发布版 **v1.8.0**，本次累计带来：可拖拽调整的导航窗格、自动检查更新、功能页界面统一规范（按钮权重 / 参数分组 / 危险确认 / NumberBox 统一），以及一批编码器与更新链路的实质性缺陷修复。所有改动均为非破坏性，配置结构保持兼容。建议所有 1.8.x 用户升级。

> 说明：v1.9.0 → v1.10.2 均为本地构建、未单独对外发布，其变更随本次 **v1.10.3** 一同发布（v1.9.0 / v1.10.0 为 MINOR 级功能，v1.9.1–v1.10.3 为 PATCH 级修复）。

## 新增

### 导航窗格宽度可拖拽调整（v1.9.0 / 1.9.1 / 1.9.2）
- 将鼠标移到左侧导航栏右缘即可拖动调宽，宽度实时生效，默认 320px、范围 160–480px。
- 宽度**记忆到配置文件**，下次启动自动恢复，且**不受「保持习惯」开关影响**（布局偏好属窗口外观，与会话无关）。
- 拖拽手感修正（v1.9.1）：初版透明热区在首次悬停后整体失效，已改为根网格热区方案，按坐标判定热区、位置零维护。
- 体验取舍（v1.9.2）：放弃「拖窄自动收成纯图标」，改为始终带文字的连续调宽，避免离散跳变突兀。

### 自动检查更新（v1.9.0 / 1.9.3）
- 启动后延时 15s 检测一次（错开启动高峰），可在设置页开关。
- 检测到新版时，设置项文字旁与左侧导航栏各亮一个提示徽标；不自动下载，符合既有安全立场。
- 设置页将「更新源」与「自动检查更新」合并为**同行两列**，右对齐对齐按钮列。

### 更新源即时保存（v1.9.0）
- 切换更新源（GitHub / 国内镜像）即改即存写穿配置，从「保持习惯」脏追踪中移除——它对红点 / 保存按钮无意义。

### 功能页界面统一规范（v1.10.0，P0 批次）
- **按钮权重规范**：分析 / 检测 / 查看类操作改为描边按钮，执行类操作为 Accent 实心。修正了此前字幕页、工具页、抽取页权重反置的问题。
- **参数内分组**：音频转码参数拆「编码器选择 / 音频参数（比特率·声道·采样率）」；图片抽帧拆「抽帧模式 / 时间参数 / 缩放参数」；视频 GPU/CPU 面板子组间补弱分隔线（可见性与相邻组一致）。
- **危险操作确认**：清空队列 / 清空轨道改为先弹确认框再执行，避免误触清空。
- **ImagePage 数值统一 NumberBox**：时间 / 间隔 / 缩放由文本框改为 NumberBox（缩放留空 = 不缩放），边界与步进规范化。

## 修复

### 编码器参数构建三处 Critical（v1.10.1）
- **[C1] x264 / x265 关键帧参数拼错**：原写 `-keyint` / `-min-keyint`（这是 x264 命令行名），ffmpeg 侧不认，导致硬件无关的重编码 **100% 失败**；已改为 ffmpeg 参数 `-g` / `-keyint_min`。
- **[C2] GPU 编码器参数混用**：NVENC / AMF / QSV 原先共用 NVENC 私有参数（`-rc` / `-preset` 等），AMF 直接报 `Error setting option rc to value vbr`。已按编码器分流：NVENC 保留原参、AMF 用 `-rc cqp|cbr|vbr_peak` + `-quality` + `-usage transcoding`、QSV 用 VBV + `-preset` 映射。
- **[C3] 能力检测从不实到真实**：原先只读 `ffmpeg -encoders` 文本（jellyfin 便携版无条件编进全套，无显卡也列 true），现改为对每种编码器**真编 5 帧到 null、退出码 0 才算可用**（探针尺寸须 ≥ 320×240，否则 NVENC 拒收）。

### 更新链路缺陷（v1.10.1 / 1.10.2）
- **保存即静默重置配置**：`SettingsViewModel.SaveAsync` 曾用 `new AppConfig{…}` 只赋 9/11 字段，导致 `AutoCheckUpdates` / `NavPaneExpandedWidth` 被悄悄重置回默认；已改为「载入 → 改字段 → 写穿」。
- **ffmpeg 路径解析回写**：`ConfigService.Load()` 把解析结果误写回 `config.FfmpegPath`，新增 `[JsonIgnore] ResolvedFfmpegPath` 隔离，7 处 VM 与 4 处设置项改用解析值。
- **抽取页并发触发**：`ExtractViewModel` 两个入口缺 `IsBusy` 守卫（其余页本有），已补齐。
- **输出目录无回退**：输出目录不存在时部分页面无兜底；新增 `EnsureOutputDir` 回退到源文件目录，Audio / Video / Subtitle 三页接入。
- **格式化与队列**：多处时间 / 数值格式化补 `InvariantCulture`、单帧抽帧补 `-y`、音频 / 封装队列去重改大小写不敏感。
- **CUDA 三件套只限 NVENC（v1.10.2）**：上一轮修好的 AMF / QSV 又被 `-hwaccel_output_format cuda` 与 `scale_cuda` 打死（AMF 拿不到 CUDA 显存帧，exit 127）；现限定仅 NVENC 使用，纯解码侧 `-hwaccel cuda` 对 AMF 无害保留。
- **Harness 假绿（v1.10.2）**：原 SKIP 判据用关键字白名单，AMF+cuda 报 `Function not implemented` 被记成 SKIP，在唯一能测 AMF 的机器上给出假绿；改为「基线对照」判别式（全参过 = PASS、全参挂 + 基线过 = FAIL、两者都挂 = SKIP）。

### 设置页布局（v1.10.3）
- 设置页内容整体靠右、左侧留大片空白：根 `StackPanel` 只设 `MaxWidth` 未设 `HorizontalAlignment`，WinUI 中 Stretch 被宽度上限截断后落位靠右而非居中。已去掉 `MaxWidth` 铺满视口（与视频 / 音频等功能页一致）；`AboutPage` 同款写法补 `HorizontalAlignment="Left"`。

### 更新临时文件落点（v1.10.2）
- `UpdateService` 的临时解压目录从 `%TEMP%` 挪到 `UpdatesDirectory`（`%LOCALAPPDATA%\MarukoBox\Updates`），与安装包同一威胁模型，消除「校验 → 解压」之间的 TOCTOU 窗口。

## 优化

- **下载进度真实化（v1.9.0）**：原先进度条全程流动动画、数值永不显示；现改为真实字节进度「已下载 x / y MB（n%）」，仅当大小未知时回退流动动画。
- **精确文件大小（v1.9.0）**：GitHub 取资产 size，国内镜像源用 HEAD 探测 `Content-Length`；更新弹窗显示真实大小（探测失败才回退旧文案）。
- **ffmpeg 失败原因落盘（v1.10.1）**：失败时把 stderr 尾部 40 行与真实错误消息写入日志与界面，不再只有「退出码 N」。

## 更新机制与工程加固

- **Harness 端到端断言（v1.10.1）**：新增「真跑 ffmpeg」测试段，对 5 种编码器各编 2 帧到 null，以基线对照判 PASS / FAIL / SKIP，从根上堵住「只断言字符串形状、不与真实后端验证」的系统性问题。
- **元数据与下载 HttpClient 拆分**：元数据（JSON / 镜像索引 / 校验文件）走 60s 超时 + 10MB 上限的专用 client，大文件下载保留无上限 client，避免大文件被缓冲上限截断（ffmpeg zip 曾因此被杀）。

## 说明

- 用户数据（`config.json` / `session.json`）位于 `%LOCALAPPDATA%\MarukoBox`，安装 / 覆盖不受影响。
- 覆盖安装会自动静默卸载旧版并清掉已移除的旧文件；卸载默认保留个人数据，可选一并清除。
- 安装包为**自包含**构建，无需预先安装 .NET 运行时；当前用户安装，**无需 UAC**。
- 安装包**未做代码签名**，部分安全软件可能误报，属已知现象（哈希与官方编译产物一致，非被篡改）。

## 下载

- **GitHub**：https://github.com/294970144/maruko-box/releases/tag/v1.10.3
- **Gitee**：https://gitee.com/zhang-lin701442/maruko-box/releases/tag/v1.10.3

`MarukoBoxSetup-Inno_1.10.3.exe`（约 93.9 MB）· SHA-256：`e5223594ff5008b468a84420039ae18e7810d2dd53ab660445df13a544360754` · 内置 ffmpeg 7.1.1-5
