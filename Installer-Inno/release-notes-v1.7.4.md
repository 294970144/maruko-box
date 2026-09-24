# MarukoBox 1.7.4

> 1.7.4 是 1.7.3 的小幅 UI 改进版本（PATCH），建议所有 1.7.x 用户升级。

## 改进

- **裁剪页播放控制改为图标**：播放/暂停、跳到起点、跳到终点由文字按钮改为 `SymbolIcon` 图标 + 悬停提示，界面更紧凑。
- **起止预览帧画面居中且放大**：「起点帧 / 终点帧」画面改为水平居中对齐，缩略图高度由 150 提升至 170，侧位显示更饱满。
- **空格键固定为播放/暂停**：在裁剪页按空格键固定触发播放/暂停，已规避输入框/按钮焦点与长按自动重复导致的误触发。

## 说明

- 用户数据（`config.json` / `session.json`）位于 `%LOCALAPPDATA%\MarukoBox`，安装/覆盖不受影响。
- 覆盖安装会自动静默卸载旧版并清掉已移除的旧文件。

## 下载

- GitHub：https://github.com/294970144/maruko-box/releases/tag/v1.7.4
- Gitee：https://gitee.com/zhang-lin701442/maruko-box/releases/tag/v1.7.4
