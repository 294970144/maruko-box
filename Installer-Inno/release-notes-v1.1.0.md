# 小丸工具箱 2026 v1.1.0

基于小丸工具箱理念重做的现代化版本，本次更新实现开箱即用：

## 新增

- **内置 ffmpeg**：安装包捆绑 jellyfin-ffmpeg 7.1.1 便携版（`{app}\ffmpeg\`），安装后无需自行下载配置 ffmpeg，开箱即用
- **检查更新**：设置页新增「检查更新」按钮，一键检测并升级内置 ffmpeg（下载带进度显示，更新后自动重新检测硬件能力）
- **检查依赖**：设置页新增「检查依赖」按钮，输出 ffmpeg / ffprobe / 内置版本 / GPU 编码能力的体检报告
- **更新渠道**：设置页新增「更新渠道」下拉，可选国内镜像站（mirror.lzu.edu.cn）或 GitHub Releases
- **主题设置**：新增跟随系统 / 浅色模式 / 深色模式（切换后重启应用生效）

## 变更

- ffmpeg 路径解析优先级调整为：内置 > 手动指定路径 > 系统环境变量 PATH
- 安装包体积 72 MB → 97 MB（增量来自内置的 ffmpeg）
- README 说明本项目为小丸工具箱重做版

## 安装

零 UAC、当前用户安装：

```
MarukoBoxSetup-Inno_1.1.0.exe /VERYSILENT /SUPPRESSMSGBOXES /NORESTART
```

安装位置：`%LOCALAPPDATA%\Programs\MarukoBox`，卸载用同目录 `unins000.exe`。
