# 测试 / 冒烟（Harness）

`Harness/` 提供一套脱离 WinUI UI 的端到端冒烟测试，让服务层与 ffmpeg 真实交互，从根上验证参数构建正确性。

## 结构

| 文件 | 职责 |
|------|------|
| `Harness/AppStub.cs` | 替代 WinUI `App` 的测试桩（仅日志），使服务层在无 UI 下可实例化运行 |
| `Harness/Program.cs` | 冒烟主程序：编码冒烟 + 更新链路冒烟 + 更新中断自愈冒烟 + 裁剪冒烟 |

## 隔离机制

- 受 `MARUKO_HARNESS` 编译符号隔离，避免把 UI 类型（如 `App`、`MainWindow`）编进测试桩。
- `AppStub` 用纯日志桩替换 `App` 的 UI 行为，使 `AppServices` / `FfmpegService` / `UpdateService` 可直接驱动。

## 冒烟策略：基线对照法（v1.10.1+）

原 `Harness` 用关键字白名单判 SKIP，曾在唯一能测 AMF 的机器上给出「假绿」（AMF+cuda 报 `Function not implemented` 被记成 SKIP）。改为**基线对照判别式**：

- 全参过 = **PASS**
- 全参挂 + 基线过 = **FAIL**（真实缺陷，如 C2/C3 类参数错误）
- 两者都挂 = **SKIP**（环境不支持，非代码缺陷）

`Program.cs` 对 5 种编码器各真编 2 帧到 null，以基线对照判 PASS / FAIL / SKIP，堵住「只断言字符串形状、不与真实后端验证」的系统性问题。

## 相关文件

- 被冒烟的服务：`FfmpegService` / `UpdateService` / `ConfigService`，见 [services.md](services.md)
- 真实缺陷登记：[design-decisions.md](design-decisions.md)
