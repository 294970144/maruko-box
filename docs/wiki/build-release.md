# 构建与发布

构建脚本与安装包位于 `Installer-Inno/`，发布说明按版本归档。

## 构建与安装包

| 文件 | 职责 |
|------|------|
| `Installer-Inno/build.ps1` | 项目构建（自包含，无需预装 .NET 运行时） |
| `Installer-Inno/build-installer.ps1` | 生成 Inno Setup 安装包 |
| `Installer-Inno/release-notes-*.md` | 各版本发布说明 |

- 安装包为**自包含**构建，当前用户安装、**无需 UAC**、**无需 .NET 运行时**。
- 安装包**未做代码签名**，部分安全软件可能误报（哈希与官方编译产物一致，非被篡改，属已知现象）。
- 覆盖安装自动静默卸载旧版并清理已移除的旧文件。卸载**一律保留**用户数据（`config.json` / `session.json` / logs / Updates 缓存均位于 `%LOCALAPPDATA%\MarukoBox`，在 `{app}` 之外，卸载/覆盖从不触碰），**不再提供「删除个人数据」选项**（原卸载确认框已移除）。

## 发布说明索引

版本演进（发布说明文件）：

`v1.1.0` → `v1.2.0` → `v1.3.0` → `v1.4.0` → `v1.4.1` → `v1.5.0` → `v1.5.1` → `v1.6.0` → `v1.7.0` → `v1.7.1` → `v1.7.2` → `v1.7.4` → `v1.8.0` → `v1.10.3`

> 注：`v1.9.0`–`v1.10.2` 为本地构建、未单独对外发布，其变更随 **v1.10.3** 一并发布（见 `release-notes-v1.10.3.md`）。

### 关键版本亮点

- **v1.8.0**：设置界面重构 + 25 项缺陷修复（含 P0 启动崩溃）。
- **v1.9.0**：导航窗格可拖拽调宽、自动检查更新（15s 延时）、更新源即时保存、下载进度真实化。
- **v1.9.1 / 1.9.2**：拖拽热区方案修正、连续调宽体验取舍。
- **v1.10.0**：功能页界面统一规范（按钮权重 / 参数分组 / 危险确认 / NumberBox 统一）。
- **v1.10.1 / 1.10.2 / 1.10.3**：编码器参数三处 Critical（C1/C2/C3）修复、更新链路多项缺陷修复、CUDA 仅限 NVENC、Harness 基线对照、设置页布局修正。**当前稳定版：v1.10.3（内置 ffmpeg 7.1.1-5）**。

## 发布配置

- `.vscode/`、属性目录 `Properties/PublishProfiles/`：含 win-x64 / arm64 / x86 发布配置。
- `docs/screenshots/`：界面截图；`docs/icon.png`：应用图标。

## 下载源（v1.10.3）

- GitHub：`https://github.com/294970144/maruko-box/releases/tag/v1.10.3`
- Gitee：`https://gitee.com/zhang-lin701442/maruko-box/releases/tag/v1.10.3`

`MarukoBoxSetup-Inno_1.10.3.exe`（约 93.9 MB）· SHA-256：`e5223594ff5008b468a84420039ae18e7810d2dd53ab660445df13a544360754`
