using System.Diagnostics;
using System.Text;
using MarukoBox.Models;

namespace MarukoBox.Services;

/// <summary>
/// GPU 硬件与 ffmpeg 后端能力检测服务。
/// </summary>
public interface IGpuDetectionService
{
    /// <summary>
    /// 检测 ffmpeg 版本、可用编码器、滤镜以及显卡信息。
    /// </summary>
    /// <param name="ffmpegPath">ffmpeg.exe 完整路径。</param>
    /// <param name="ct">取消令牌。</param>
    Task<GpuInfo> DetectAsync(string ffmpegPath, CancellationToken ct = default);
}

/// <inheritdoc cref="IGpuDetectionService"/>
public class GpuDetectionService : IGpuDetectionService
{
    private const int ProcessTimeoutMs = 15000;

    // ---------- v1.4.1：进程内缓存 ----------
    // GPU 能力在一次会话内不会变化，而视频页与设置页的构造函数各调一次 DetectAsync，
    // 每次又是 5 个外部进程——启动阶段最坏要等 ~75 秒。缓存后只跑一轮。
    private static readonly object CacheGate = new();
    private static GpuInfo? _cached;
    private static string _cachedPath = string.Empty;

    /// <summary>
    /// 作废 GPU 能力缓存。内置 ffmpeg 被更新（编码器集合可能变化）后必须调用，
    /// 否则界面会继续显示旧版 ffmpeg 的能力。
    /// </summary>
    public static void Invalidate()
    {
        lock (CacheGate)
        {
            _cached = null;
            _cachedPath = string.Empty;
        }
    }

    private static GpuInfo? TryGetCached(string ffmpegPath)
    {
        lock (CacheGate)
        {
            return _cached is not null
                   && string.Equals(_cachedPath, ffmpegPath, StringComparison.OrdinalIgnoreCase)
                ? _cached
                : null;
        }
    }

    private static void Store(GpuInfo info, string ffmpegPath)
    {
        lock (CacheGate)
        {
            _cached = info;
            _cachedPath = ffmpegPath;
        }
    }

    /// <inheritdoc/>
    public async Task<GpuInfo> DetectAsync(string ffmpegPath, CancellationToken ct = default)
    {
        var path = ffmpegPath ?? string.Empty;

        var cached = TryGetCached(path);
        if (cached is not null)
        {
            return cached;
        }

        var info = new GpuInfo { FfmpegPath = path };

        // ---------- 1. ffmpeg 是否存在 ----------
        if (string.IsNullOrWhiteSpace(path) || !File.Exists(path))
        {
            info.ErrorMessage = $"未找到 ffmpeg.exe：{path}";
            info.DetectionSucceeded = false;
            return info;
        }

        info.FfmpegFound = true;

        try
        {
            // ---------- 2~5. 并发启动 4 个互不依赖的 ffmpeg 查询 ----------
            // v1.4.1：原先串行 await，最坏情况 4 × 15s = 60s；并发后总耗时≈单次调用。
            // RunAsync 内部同步 Start 进程，因此 4 个进程在下面几行内就已同时跑起来。
            var versionTask = RunAsync(path, "-version", ct);
            var encodersTask = RunAsync(path, "-hide_banner -encoders", ct);
            var filtersTask = RunAsync(path, "-hide_banner -filters", ct);
            var hwaccelsTask = RunAsync(path, "-hide_banner -hwaccels", ct);

            info.FfmpegVersion = ParseFfmpegVersion(await versionTask);

            var encoders = await encodersTask;
            var nvencHevcListed = encoders.Contains("hevc_nvenc", StringComparison.OrdinalIgnoreCase);
            var nvencH264Listed = encoders.Contains("h264_nvenc", StringComparison.OrdinalIgnoreCase);
            var amfListed = encoders.Contains("hevc_amf", StringComparison.OrdinalIgnoreCase);
            var qsvListed = encoders.Contains("hevc_qsv", StringComparison.OrdinalIgnoreCase);

            var filters = await filtersTask;
            var cudaScaleListed = filters.Contains("scale_cuda", StringComparison.OrdinalIgnoreCase);

            var hwaccels = await hwaccelsTask;
            var cudaDecodeListed = hwaccels.Contains("cuda", StringComparison.OrdinalIgnoreCase);

            // ---------- 5.5 真实可用性探测（C3 修复）----------
            // `ffmpeg -encoders` 只说明"这个后端被编译进了 ffmpeg"，**不代表本机真能起来**。
            // jellyfin-ffmpeg 便携版无条件编译进 nvenc / amf / qsv 全套，因此旧逻辑在
            // 没有对应显卡的机器上也会把 Has* 全置 true：
            //   → Auto（默认）恒解析成 hevc_nvenc，并在 BuildCoreArguments 追加
            //     -hwaccel cuda，编解码两侧都在**初始化阶段**失败；
            //   → 叠加 C1（回退的 x264 参数本来也是坏的）时非 N 卡用户没有任何可用路径；
            //   → 同时设置页 Summary 会一边显示 GpuName="未检测到"、一边给 Success 绿横幅。
            // 现在对每种后端**真编 5 帧到 null 输出**，只有退出码为 0 才认定可用。
            // 尺寸不能取 64x64：NVENC 有最小帧尺寸限制，实测报
            // "Frame dimensions are less than the minimum supported value"（320x240 正常）。
            var probeNvencHevc = nvencHevcListed ? ProbeEncoderAsync(path, "hevc_nvenc", ct) : Task.FromResult(false);
            var probeNvencH264 = nvencH264Listed ? ProbeEncoderAsync(path, "h264_nvenc", ct) : Task.FromResult(false);
            var probeAmf = amfListed ? ProbeEncoderAsync(path, "hevc_amf", ct) : Task.FromResult(false);
            var probeQsv = qsvListed ? ProbeEncoderAsync(path, "hevc_qsv", ct) : Task.FromResult(false);

            info.HasNvencHevc = nvencHevcListed && await probeNvencHevc;
            info.HasNvencH264 = nvencH264Listed && await probeNvencH264;
            info.HasAmf = amfListed && await probeAmf;
            info.HasQsv = qsvListed && await probeQsv;

            // cuda 解码/缩放是 NVIDIA 专属：探测不通过时即使 -hwaccels 列了 cuda 也不能用，
            // 否则会把系统内存帧喂给 scale_cuda（N9 那一类错误）。
            var nvidiaUsable = info.HasNvencHevc || info.HasNvencH264;
            info.HasCudaDecode = cudaDecodeListed && nvidiaUsable;
            info.HasCudaScale = cudaScaleListed && info.HasCudaDecode;

            // ---------- 6. 显卡型号与驱动（nvidia-smi） ----------
            await DetectNvidiaGpuAsync(info, ct);

            info.DetectionSucceeded = true;

            // 只缓存成功的检测结果，失败不缓存（下一次要真的重试）
            Store(info, path);
        }
        catch (Exception ex)
        {
            info.ErrorMessage = ex.Message;
            info.DetectionSucceeded = false;
        }

        return info;
    }

    /// <summary>
    /// 通过 nvidia-smi 获取显卡型号与驱动版本，并推断 NVENC API 版本；
    /// 同时枚举全部 GPU 设备（index + 型号）供多 GPU 序号上限与设备列表使用。
    /// nvidia-smi 不存在时（非 N 卡）静默跳过。
    /// </summary>
    private static async Task DetectNvidiaGpuAsync(GpuInfo info, CancellationToken ct)
    {
        try
        {
            var output = await RunAsync(
                "nvidia-smi",
                "--query-gpu=index,name,driver_version --format=csv,noheader",
                ct);

            if (string.IsNullOrWhiteSpace(output))
            {
                return;
            }

            // 每行格式: "0, NVIDIA GeForce RTX 4060 Laptop GPU, 610.62"
            // （多卡时多行；nvidia-smi 按 index 升序输出）
            var lines = output.Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
            if (lines.Length == 0)
            {
                return;
            }

            foreach (var line in lines)
            {
                var parts = line.Split(',');
                if (parts.Length >= 3
                    && int.TryParse(parts[0].Trim(), out var index))
                {
                    info.GpuDevices.Add(new GpuDeviceEntry(index, parts[1].Trim()));
                }
            }

            // 首行继续作为「主显卡」信息源（与既有单卡展示逻辑兼容）
            var firstParts = lines[0].Split(',');
            if (firstParts.Length >= 3)
            {
                info.GpuName = firstParts[1].Trim();
                info.DriverVersion = firstParts[2].Trim();
                info.NvencApiVersion = InferNvencApiVersion(info.DriverVersion);
            }
            else if (firstParts.Length == 2)
            {
                info.GpuName = firstParts[1].Trim();
            }
        }
        catch
        {
            // nvidia-smi 不可用（非 NVIDIA 显卡）—— 不是错误，保持默认值
        }
    }

    /// <summary>
    /// 从驱动主版本号推断该驱动支持的 NVENC API 上限。
    /// 依据 NVIDIA Video Codec SDK 各版本官方系统要求（Windows 最低驱动），
    /// 与 ffmpeg nvenc.c 的 nvenc_print_driver_requirement 对照表一致：
    /// SDK 13.1 → 驱动 610；13.0 → 570；12.2 → 560；12.1 → 530；12.0 → 520。
    /// </summary>
    private static string InferNvencApiVersion(string driverVersion)
    {
        if (string.IsNullOrWhiteSpace(driverVersion))
        {
            return "未知";
        }

        var majorPart = driverVersion.Split('.')[0];
        if (!double.TryParse(majorPart, out var major))
        {
            return "未知";
        }

        return major switch
        {
            >= 610 => "13.1",
            >= 570 => "13.0",
            >= 560 => "12.2",
            >= 530 => "12.1",
            >= 520 => "12.0",
            _ => "≤12.0"
        };
    }

    /// <summary>
    /// 从 <c>ffmpeg -version</c> 输出中提取版本号。
    /// 首行形如: "ffmpeg version 8.1.2-full_build-www.gyan.dev Copyright ..."
    /// <para>
    /// v1.4.1 修复：此前用 Split(' ', '-') 同时按空格与连字符切分，
    /// "7.1.1-5-Jellyfin" 会被截成 "7.1.1"，丢掉构建号 -5，
    /// 与内置版本标记 VERSION（写的是完整 "7.1.1-5"）显示不一致，也干扰版本比较。
    /// 现在先按空格取首段，再在连字符后跟纯数字时补回构建号。
    /// </para>
    /// </summary>
    internal static string ParseFfmpegVersion(string output)
    {
        if (string.IsNullOrWhiteSpace(output))
        {
            return "未知";
        }

        var firstLine = output.Split('\n', StringSplitOptions.RemoveEmptyEntries)[0];

        // "ffmpeg version 7.1.1-5-Jellyfin Copyright ..."
        var versionIndex = firstLine.IndexOf("version", StringComparison.OrdinalIgnoreCase);
        if (versionIndex < 0)
        {
            return "未知";
        }

        var afterVersion = firstLine[(versionIndex + 7)..].Trim();
        var token = afterVersion.Split(' ')[0];

        if (string.IsNullOrWhiteSpace(token))
        {
            return "未知";
        }

        // 仅当连字符后是纯数字时才视为构建号（"7.1.1-5-Jellyfin" → "7.1.1-5"）；
        // "8.1.2-full_build-www.gyan.dev" 的次段非数字，保持 "8.1.2" 不变。
        var parts = token.Split('-');
        if (parts.Length >= 2 && parts[1].Length > 0 && parts[1].All(char.IsDigit))
        {
            return parts[0] + "-" + parts[1];
        }

        return parts[0];
    }

    /// <summary>
    /// 【C3 修复】真实可用性探测：用指定编码器实际编 5 帧到 null 输出，
    /// 退出码为 0 才算"本机可用"。任何异常/超时一律按不可用处理（宁可回退 CPU）。
    /// </summary>
    private static async Task<bool> ProbeEncoderAsync(string ffmpegPath, string codec, CancellationToken ct)
    {
        try
        {
            // testsrc 320x240：足够大以避开 NVENC 的最小帧尺寸限制，又足够小以保证探测够快。
            var code = await RunExitCodeAsync(
                ffmpegPath,
                $"-hide_banner -loglevel error -f lavfi -i testsrc=s=320x240:d=1:r=25 -c:v {codec} -frames:v 5 -f null -",
                ct);

            App.LogInfo($"编码器探测 {codec}：退出码 {code}（{(code == 0 ? "可用" : "不可用")}）");
            return code == 0;
        }
        catch (Exception ex)
        {
            App.LogInfo($"编码器探测 {codec} 失败，按不可用处理：{ex.Message}");
            return false;
        }
    }

    /// <summary>启动外部进程，只关心退出码（丢弃输出）。</summary>
    private static async Task<int> RunExitCodeAsync(string fileName, string arguments, CancellationToken ct)
    {
        var psi = new ProcessStartInfo
        {
            FileName = fileName,
            Arguments = arguments,
            UseShellExecute = false,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            CreateNoWindow = true,
            StandardOutputEncoding = Encoding.UTF8,
            StandardErrorEncoding = Encoding.UTF8
        };

        using var process = Process.Start(psi)
            ?? throw new InvalidOperationException($"无法启动进程：{fileName}");

        // 与 RunAsync 同样必须并发抽干两个流，否则缓冲区满会令进程永不退出。
        var outTask = process.StandardOutput.ReadToEndAsync(ct);
        var errTask = process.StandardError.ReadToEndAsync(ct);
        var waitTask = process.WaitForExitAsync(ct);

        var completed = await Task.WhenAny(waitTask, Task.Delay(ProcessTimeoutMs, ct));
        if (completed != waitTask)
        {
            try { process.Kill(entireProcessTree: true); } catch { /* 忽略 */ }
            try { await Task.WhenAll(outTask, errTask); } catch { /* 忽略 */ }
            throw new TimeoutException($"进程执行超时：{fileName} {arguments}");
        }

        try { await Task.WhenAll(outTask, errTask); } catch { /* 取消时忽略 */ }
        return process.ExitCode;
    }

    /// <summary>
    /// 启动外部进程并异步读取标准输出。
    /// </summary>
    private static async Task<string> RunAsync(string fileName, string arguments, CancellationToken ct)
    {
        var psi = new ProcessStartInfo
        {
            FileName = fileName,
            Arguments = arguments,
            UseShellExecute = false,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            CreateNoWindow = true,
            StandardOutputEncoding = Encoding.UTF8,
            StandardErrorEncoding = Encoding.UTF8
        };

        using var process = Process.Start(psi)
            ?? throw new InvalidOperationException($"无法启动进程：{fileName}");

        // v1.4.1 修复：stdout 与 stderr 必须并发读取，否则任一流（默认 4KB）缓冲区
        // 填满后 ffmpeg 会阻塞写、进程永不退出；同时 ffmpeg 的 -version / -i 等文本
        // 输出在 stderr，只读取 stdout 会拿到空串导致版本号始终"未知"。
        // 两路都读、合并返回，保证后续 Contains 判定对任意输出位置都成立。
        var outTask = process.StandardOutput.ReadToEndAsync(ct);
        var errTask = process.StandardError.ReadToEndAsync(ct);
        var waitTask = process.WaitForExitAsync(ct);

        var completed = await Task.WhenAny(waitTask, Task.Delay(ProcessTimeoutMs, ct));
        if (completed != waitTask)
        {
            // 超时 —— 杀掉进程，避免挂起
            try { process.Kill(entireProcessTree: true); } catch { /* 忽略 */ }
            try { await Task.WhenAll(outTask, errTask); } catch { /* 忽略 */ }
            throw new TimeoutException($"进程执行超时：{fileName} {arguments}");
        }

        string outStr = string.Empty;
        string errStr = string.Empty;
        try { outStr = await outTask; } catch { /* 取消/读取失败拿已读部分 */ }
        try { errStr = await errTask; } catch { /* 同上 */ }

        return outStr + "\n" + errStr;
    }
}
