# 第三方组件与许可证说明（THIRD-PARTY NOTICES）

MarukoBox 2026 自身以 [GPL-3.0 许可证](./LICENSE) 发布（强 copyleft）。本程序在构建/分发时
捆绑或依赖以下第三方组件，各自的许可证义务如下。

## 内置 ffmpeg（jellyfin-ffmpeg）

- **组件**：[jellyfin-ffmpeg](https://github.com/jellyfin/jellyfin-ffmpeg) 便携版
- **许可证**：**GPL v2.1 及更高版本 / LGPL**（ffmpeg 本体为 LGPL，部分可选组件为 GPL）。
  具体随架构而异：

  | 架构 | 使用的构建变体 | 许可倾向 |
  |---|---|---|
  | x64 | `portable_win64`（无 `-gpl` 后缀） | LGPL 为主，含 GPL 可选组件 |
  | ARM64 | `portable_winarm64-clang-gpl` | **GPL**（上游自 7.1.1-6 起 Windows ARM64 仅提供该变体） |

- **说明**：安装包默认将 jellyfin-ffmpeg 解压至 `{app}\ffmpeg\`，随程序内置分发。
  其源代码由上游仓库维护并提供；如启用 GPL 部分（如 `--enable-gpl` 相关滤镜/编码器），
  须遵守 GPL 的对应源码提供义务，该义务由 jellyfin-ffmpeg 项目承担。
  主程序本身即 GPL-3.0，故 ARM64 构建改用 GPL 变体在整体分发上不存在许可冲突。
- **上游源码**：<https://github.com/jellyfin/jellyfin-ffmpeg>

## 运行时与框架依赖

| 组件 | 许可证 | 说明 |
|---|---|---|
| .NET 10 / Windows App SDK 2.5.1 | MIT | 运行时与 WinUI 3 框架 |
| CommunityToolkit.Mvvm 8.4.2 | MIT | MVVM 工具库 |
| Inno Setup 6 | 自有免费许可 | 安装包构建工具 |

## 合规性提示

- 主程序（MarukoBox 2026）以 GPL-3.0 授权：你可自由使用、修改、再分发，但分发（含修改版）
  须以相同 GPL-3.0 条款提供完整对应源码。
- 内置 ffmpeg 同为 GPL 组件，与主程序共同构成 GPL 分发整体；其源码获取义务由
  jellyfin-ffmpeg 上游仓库满足。
- 若你自行替换内置 ffmpeg 为其他构建，请在分发前确认其许可证条款兼容性。
