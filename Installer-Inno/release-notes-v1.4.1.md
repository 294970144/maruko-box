# v1.4.1 发布说明

> 热修版：集中修复 1.4.0 审查中发现的质量四档失效（P0）及多项安全/稳定性问题。

## 修复清单（按优先级）

### P0（阻断级）
- **B1 质量四档在 GPU 路径完全失效**：「普通」级恒定质量四档（低/中/高/非常高）此前
  只写了 CQP 数值 `Quality`，但 GPU 分支仅在 `RateControl=="cqp"` 时才读取它，而默认值
  是 `vbr`，导致四档输出体积完全相同（选「非常高」与「低」无差别）。
  修复：质量档切换时由 `QualityPresets.Apply` 同步把 `RateControl` 切到 `cqp`、
  `CpuMode` 切到 `crf`，并默认 `RateControl="cqp"`。`QualityPresets` 抽成独立模型，
  由 Harness 直接断言四档输出不同。

### P1（高）
- **B3 错误信息被覆盖**：`StartAsync` 的 `finally` 无条件把状态改成「全部完成/编码结束」，
  用户点开后只能看到「失败」却永远看不到失败原因。改为仅在无错误项时覆盖。
- **B2 「编码完成后」真正生效**：此前该下拉只记录不执行（欺骗性 UI）。现实现
  `none / 退出程序 / 关机 / 休眠` 分派；关机/休眠在执行前弹出可取消的确认窗口，且仅当
  全部编码成功时才触发。
- **E1 关于页版本号硬编码**：原为 `0.1.0 (Preview)`，改为读取程序集版本，杜绝漂移。
- **S1 更新 exe 落盘位置**：安装包下载从全局可写的 `%TEMP%` 改为
  `%LOCALAPPDATA%\MarukoBox\Updates\`（仅当前用户可写），消除 TOCTOU 替换窗口；
  下载后校验文件非空。

### P2（中/低）
- **S2 版本号白名单**：GitHub tag 用于拼文件名前做字符白名单校验，杜绝路径穿越。
- **S3 自定义 ffmpeg 参数注入**：命令自定义模式禁用换行/`|`/`&`/`;`/反引号，并要求引号成对。
- **S4 日志脱敏与轮转**：日志从 `%USERPROFILE%\maruko_crash.log` 迁到
  `%LOCALAPPDATA%\MarukoBox\logs\`，命令行路径脱敏为 `<path>`，单文件 5MB 轮转、保留 3 份。
- **B4 更新中断自愈**：备份目录清理移出主 try，避免「已装好却报错」。
- **B5 探测取消时杀进程**：`ProbeStreams/ProbeInfo` 取消时终止 ffmpeg 子进程，避免挂起残留。
- **B6/B7 NVENC 门槛与版本解析**：区分「无 N 卡」与「驱动未知」；版本号解析保留构建号。
- **O1 GPU 检测并发+缓存**：5 个串行进程改为并发，结果按 ffmpeg 路径缓存，启动更快。
- **O2 去掉 `where.exe` 子进程**：改为纯托管遍历 PATH。
- **C1~C6 代码质量**：解析逻辑去重、死字段清理、会话快照整体序列化（避免漏记参数）、
  补 `CpuTune` 下拉、注释校对、Harness 加 B1 参数构建断言。
- **E2/E3 文档与许可证**：README 更新；主程序许可证由 MIT 改为 **GPL-3.0**（强 copyleft），
  同步更新 `LICENSE` 与 `THIRD-PARTY-NOTICES.md`（内置 jellyfin-ffmpeg 为 GPL），安装向导内展示许可证。

## 构建信息
- 版本 1.4.1，文件版本（FileVersion）1.4.1.0。
- 安装包：`MarukoBoxSetup-Inno_1.4.1.exe`（当前用户安装，无需 UAC）。
