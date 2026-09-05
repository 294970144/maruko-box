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

    /// <inheritdoc/>
    public async Task<GpuInfo> DetectAsync(string ffmpegPath, CancellationToken ct = default)
    {
        var info = new GpuInfo { FfmpegPath = ffmpegPath ?? string.Empty };

        // ---------- 1. ffmpeg 是否存在 ----------
        if (string.IsNullOrWhiteSpace(ffmpegPath) || !File.Exists(ffmpegPath))
        {
            info.ErrorMessage = $"未找到 ffmpeg.exe：{ffmpegPath}";
            info.DetectionSucceeded = false;
            return info;
        }

        info.FfmpegFound = true;

        try
        {
            // ---------- 2. ffmpeg 版本 ----------
            var versionOutput = await RunAsync(ffmpegPath, "-version", ct);
            info.FfmpegVersion = ParseFfmpegVersion(versionOutput);

            // ---------- 3. 编码器可用性 ----------
            var encoders = await RunAsync(ffmpegPath, "-hide_banner -encoders", ct);
            info.HasNvencHevc = encoders.Contains("hevc_nvenc", StringComparison.OrdinalIgnoreCase);
            info.HasNvencH264 = encoders.Contains("h264_nvenc", StringComparison.OrdinalIgnoreCase);
            info.HasAmf = encoders.Contains("hevc_amf", StringComparison.OrdinalIgnoreCase);
            info.HasQsv = encoders.Contains("hevc_qsv", StringComparison.OrdinalIgnoreCase);

            // ---------- 4. scale_cuda 滤镜（全 GPU 缩放） ----------
            var filters = await RunAsync(ffmpegPath, "-hide_banner -filters", ct);
            info.HasCudaScale = filters.Contains("scale_cuda", StringComparison.OrdinalIgnoreCase);

            // ---------- 5. 硬件解码 ----------
            var hwaccels = await RunAsync(ffmpegPath, "-hide_banner -hwaccels", ct);
            info.HasCudaDecode = hwaccels.Contains("cuda", StringComparison.OrdinalIgnoreCase);

            // ---------- 6. 显卡型号与驱动（nvidia-smi） ----------
            await DetectNvidiaGpuAsync(info, ct);

            info.DetectionSucceeded = true;
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
    /// 从驱动主版本号推断 NVENC API 版本。
    /// 依据实测：驱动 610.62 → NVENC API 13.1；驱动 591.86 → NVENC API 13.0。
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
            >= 590 => "13.0",
            >= 550 => "12.2",
            >= 530 => "12.1",
            _ => "≤12.0"
        };
    }

    /// <summary>
    /// 从 <c>ffmpeg -version</c> 输出中提取版本号。
    /// 首行形如: "ffmpeg version 8.1.2-full_build-www.gyan.dev Copyright ..."
    /// </summary>
    private static string ParseFfmpegVersion(string output)
    {
        if (string.IsNullOrWhiteSpace(output))
        {
            return "未知";
        }

        var firstLine = output.Split('\n', StringSplitOptions.RemoveEmptyEntries)[0];

        // "ffmpeg version 8.1.2-full_build-www.gyan.dev ..."
        var versionIndex = firstLine.IndexOf("version", StringComparison.OrdinalIgnoreCase);
        if (versionIndex < 0)
        {
            return "未知";
        }

        var afterVersion = firstLine[(versionIndex + 7)..].Trim();
        var token = afterVersion.Split(' ', '-')[0];

        return string.IsNullOrWhiteSpace(token) ? "未知" : token;
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

        var readTask = process.StandardOutput.ReadToEndAsync(ct);
        var waitTask = process.WaitForExitAsync(ct);

        var completed = await Task.WhenAny(waitTask, Task.Delay(ProcessTimeoutMs, ct));
        if (completed != waitTask)
        {
            // 超时 —— 杀掉进程，避免挂起
            try { process.Kill(entireProcessTree: true); } catch { /* 忽略 */ }
            throw new TimeoutException($"进程执行超时：{fileName} {arguments}");
        }

        return await readTask;
    }
}
