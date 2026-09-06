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
            info.HasNvencHevc = encoders.Contains("hevc_nvenc", StringComparison.OrdinalIgnoreCase);
            info.HasNvencH264 = encoders.Contains("h264_nvenc", StringComparison.OrdinalIgnoreCase);
            info.HasAmf = encoders.Contains("hevc_amf", StringComparison.OrdinalIgnoreCase);
            info.HasQsv = encoders.Contains("hevc_qsv", StringComparison.OrdinalIgnoreCase);

            var filters = await filtersTask;
            info.HasCudaScale = filters.Contains("scale_cuda", StringComparison.OrdinalIgnoreCase);

            var hwaccels = await hwaccelsTask;
            info.HasCudaDecode = hwaccels.Contains("cuda", StringComparison.OrdinalIgnoreCase);

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
    /// 通过 nvidia-smi 获取显卡型号与驱动版本，并推断 NVENC API 版本。
    /// nvidia-smi 不存在时（非 N 卡）静默跳过。
    /// </summary>
    private static async Task DetectNvidiaGpuAsync(GpuInfo info, CancellationToken ct)
    {
        try
        {
            var output = await RunAsync(
                "nvidia-smi",
                "--query-gpu=name,driver_version --format=csv,noheader",
                ct);

            if (string.IsNullOrWhiteSpace(output))
            {
                return;
            }

            // 输出格式: "NVIDIA GeForce RTX 4060 Laptop GPU, 610.62"
            var firstLine = output.Split('\n', StringSplitOptions.RemoveEmptyEntries)[0].Trim();
            var parts = firstLine.Split(',');

            if (parts.Length >= 2)
            {
                info.GpuName = parts[0].Trim();
                info.DriverVersion = parts[1].Trim();
                info.NvencApiVersion = InferNvencApiVersion(info.DriverVersion);
            }
            else if (parts.Length == 1)
            {
                info.GpuName = parts[0].Trim();
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
