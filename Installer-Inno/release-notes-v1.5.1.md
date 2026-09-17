# MarukoBox v1.5.1

> 修复编码进度显示异常

## 修复
- **编码进度恒定 100%**：ffmpeg `-progress` 输出的 `out_time_ms` 实际单位是**微秒**（与 `out_time_us` 同值，ffmpeg 历史遗留问题）。此前按毫秒解析，已处理时长被放大 1000 倍，百分比第一帧即被钳制在 100% 并再也不动。现统一按微秒解析，进度正常爬升。
- **GPU 编码时 FPS 恒为 0**：NVENC 等硬件编码器不在 `-progress` 中回报 fps（ffmpeg 行为）。现以「已编码帧数 ÷ 已耗时」兜底，FPS 显示恢复正常。

## 下载
`MarukoBoxSetup-Inno_1.5.1.exe` · 当前用户安装（无需 UAC）· 内置 ffmpeg 7.1.1-5
