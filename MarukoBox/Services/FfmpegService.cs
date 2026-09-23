using System.Diagnostics;
using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;
using MarukoBox.Models;

namespace MarukoBox.Services;

/// <summary>
/// ffmpeg 编码服务：构建参数、启动进程、解析实时进度。
/// 视频编码 / 轨道抽取 / 音频转码 三种任务共用同一套进程执行与进度解析逻辑。
/// </summary>
public interface IFfmpegService
{
    /// <summary>
    /// 构建 ffmpeg 视频编码命令行参数（不含 ffmpeg.exe 本身）。
    /// </summary>
    string BuildArguments(EncodeSettings settings, EncoderType resolvedEncoder, GpuInfo gpuInfo);

    /// <summary>
    /// 执行视频编码任务，并通过 <paramref name="progress"/> 上报实时进度。
    /// </summary>
    /// <returns>成功返回 true；被取消或失败返回 false。</returns>
    Task<bool> EncodeAsync(
        EncodeSettings settings,
        GpuInfo gpuInfo,
        IProgress<EncodeProgress> progress,
        CancellationToken ct = default);

    /// <summary>
    /// 解析源文件中的媒体流列表（用于「抽取」页选择轨道）。
    /// </summary>
    Task<List<MediaStreamInfo>> ProbeStreamsAsync(
        string ffmpegPath,
        string filePath,
        CancellationToken ct = default);

    /// <summary>
    /// 将选中的流无损抽取（-c copy）到输出目录，按编码自动选择容器后缀。
    /// </summary>
    /// <returns>(是否成功, 已完成数, 总数)。</returns>
    Task<(bool success, int done, int total)> ExtractStreamsAsync(
        string ffmpegPath,
        string source,
        string outputDir,
        IEnumerable<MediaStreamInfo> streams,
        IProgress<EncodeProgress> progress,
        CancellationToken ct = default);

    /// <summary>
    /// 将单个音频文件转码为目标格式。
    /// </summary>
    Task<bool> TranscodeAudioAsync(
        string ffmpegPath,
        string inputPath,
        string outputPath,
        AudioPreset preset,
        IProgress<EncodeProgress> progress,
        CancellationToken ct = default);

    /// <summary>
    /// 将多个输入（视频 / 音频 / 字幕文件）无损合并（-c copy）到指定容器。
    /// </summary>
    Task<bool> RemuxAsync(
        string ffmpegPath,
        IReadOnlyList<string> inputs,
        string container,
        string outputPath,
        IProgress<EncodeProgress> progress,
        CancellationToken ct = default);

    /// <summary>
    /// 从视频抽取帧（单张截图 / 定时序列帧，可选缩放）。
    /// </summary>
    Task<bool> ExtractFramesAsync(
        string ffmpegPath,
        string inputVideo,
        string outputDir,
        FrameExtractOptions options,
        IProgress<EncodeProgress> progress,
        CancellationToken ct = default);

    /// <summary>
    /// 将图片转码为目标格式（png / jpg / webp）。
    /// </summary>
    Task<bool> ConvertImageAsync(
        string ffmpegPath,
        string inputImage,
        string outputPath,
        string format,
        IProgress<EncodeProgress> progress,
        CancellationToken ct = default);

    /// <summary>
    /// 解析媒体文件的概要信息（总时长 + 流列表），用于「工具」页媒体信息查看。
    /// </summary>
    Task<MediaFileInfo> ProbeInfoAsync(
        string ffmpegPath,
        string filePath,
        CancellationToken ct = default);

    /// <summary>
    /// 将外部字幕嵌入视频（视频复制 + 字幕流合并到新容器）。
    /// </summary>
    Task<bool> EmbedSubtitleAsync(
        string ffmpegPath,
        string videoPath,
        string subtitlePath,
        string outputVideo,
        IProgress<EncodeProgress> progress,
        CancellationToken ct = default);

    /// <summary>
    /// 字幕格式转换（srt ↔ ass ↔ webvtt，按输出扩展名决定编码器）。
    /// </summary>
    Task<bool> ConvertSubtitleAsync(
        string ffmpegPath,
        string inputSub,
        string outputSub,
        IProgress<EncodeProgress> progress,
        CancellationToken ct = default);

    /// <summary>
    /// 构建裁剪命令行参数（不含 ffmpeg.exe 本身）。
    /// 单独暴露出来是为了能被冒烟测试直接断言——裁剪的坑几乎全在参数顺序上
    /// （-ss 的位置、-t 与 -to 的语义），不跑一遍真实文件很难发现。
    /// </summary>
    string BuildTrimArguments(TrimRequest request, GpuInfo gpuInfo);

    /// <summary>
    /// 按指定区间裁剪视频。
    /// <see cref="TrimMode.Copy"/> 直接拷贝码流（秒级完成、画质无损，切点对齐关键帧）；
    /// <see cref="TrimMode.ReEncode"/> 重新编码视频轨（切点精确到帧，耗时与片段长度相关）。
    /// </summary>
    /// <param name="gpuInfo">本机硬件能力，用于解析 <see cref="EncoderType.Auto"/>。</param>
    /// <returns>成功返回 true；被取消或失败返回 false。</returns>
    Task<bool> TrimAsync(
        string ffmpegPath,
        TrimRequest request,
        GpuInfo gpuInfo,
        IProgress<EncodeProgress> progress,
        CancellationToken ct = default);

    /// <summary>
    /// 抽取指定时刻的一帧，保存为图片（用于裁剪页的起点 / 终点缩略图）。
    /// </summary>
    /// <returns>成功返回 true（文件已落盘）。</returns>
    Task<bool> GrabFrameAsync(
        string ffmpegPath,
        string inputVideo,
        double seconds,
        string outputImagePath,
        int maxWidth = 320,
        CancellationToken ct = default);
}

/// <inheritdoc cref="IFfmpegService"/>
public class FfmpegService : IFfmpegService
{
    /// <inheritdoc/>
    public string BuildArguments(EncodeSettings settings, EncoderType resolvedEncoder, GpuInfo gpuInfo)
    {
        var core = BuildCoreArguments(settings, resolvedEncoder, gpuInfo);
        return core + $"-y \"{settings.OutputPath}\"";
    }

    /// <summary>
    /// 构建视频编码的核心参数（输入 / 滤镜 / 编码器 / 音轨），不含末尾的
    /// <c>-y "输出"</c>。供单遍编码与 2-Pass 编码（需拆成两遍）复用。
    /// </summary>
    private static string BuildCoreArguments(EncodeSettings settings, EncoderType resolvedEncoder, GpuInfo gpuInfo)
    {
        var sb = new StringBuilder();
        var isGpu = resolvedEncoder.IsGpuEncoder();

        // ---------- 输入（GPU 路径启用 CUDA 硬件解码） ----------
        if (isGpu && gpuInfo.HasCudaDecode)
        {
            sb.Append("-hwaccel cuda -hwaccel_output_format cuda ");
        }

        sb.Append($"-i \"{settings.InputPath}\" ");

        // ---------- 帧数限制 ----------
        if (settings.FrameCount > 0)
        {
            sb.Append($"-frames:v {settings.FrameCount} ");
        }

        // ---------- 视频滤镜 ----------
        // 注意：ffmpeg 只接受最后一个 -vf，多个 -vf 会互相覆盖。
        // 因此所有滤镜必须合并为一条用逗号分隔的滤镜链。
        var filters = new List<string>();

        if (settings.StartFrame > 0)
        {
            filters.Add($"select=gte(n\\,{settings.StartFrame})");
        }

        var scaleFilter = BuildVideoFilter(settings, resolvedEncoder, gpuInfo);
        if (!string.IsNullOrEmpty(scaleFilter))
        {
            filters.Add(scaleFilter);
        }

        if (filters.Count > 0)
        {
            sb.Append($"-vf \"{string.Join(",", filters)}\" ");
        }

        // ---------- 视频编码器 ----------
        sb.Append($"-c:v {resolvedEncoder.ToFfmpegCodec()} ");

        if (isGpu)
        {
            sb.Append(BuildGpuEncoderArgs(settings));
        }
        else
        {
            sb.Append(BuildCpuEncoderArgs(settings));
        }

        // ---------- 多 GPU 选择 ----------
        // 【B1 修复】-gpu 只有 NVENC 认。AMF (AMD) / QSV (Intel) 收到这个参数会直接
        // 报 "Option not found" 让整次编码失败，而设置页的设备号输入框对所有用户可见，
        // 非 N 卡用户一旦填了 >0 就每次必挂。
        // QSV 的多设备选择另有机制且各 ffmpeg 版本参数名不一致，未实测前宁可不加。
        if (settings.GpuDevice > 0 &&
            resolvedEncoder is EncoderType.NvencHevc or EncoderType.NvencH264)
        {
            sb.Append($"-gpu {settings.GpuDevice} ");
        }

        // ---------- 音轨 / 字幕 ----------
        sb.Append(BuildAudioArgs(settings));

        // 注意：-progress pipe:1 -nostats 由 RunFfmpegCoreAsync 统一追加，
        // 避免各任务重复添加或遗漏。

        return sb.ToString();
    }

    /// <summary>
    /// 构建视频滤镜（缩放）。GPU 路径用 scale_cuda，CPU 路径用 scale。
    /// </summary>
    private static string BuildVideoFilter(EncodeSettings settings, EncoderType resolved, GpuInfo gpuInfo)
    {
        if (settings.KeepOriginalResolution)
        {
            return string.Empty;
        }

        var w = settings.Width;
        var h = settings.Height;

        if (w <= 0 || h <= 0)
        {
            return string.Empty;
        }

        // GPU 路径且支持 scale_cuda → 全 GPU 缩放（interp_algo=4 即 lanczos）
        if (resolved.IsGpuEncoder() && gpuInfo.HasCudaScale)
        {
            return $"scale_cuda={w}:{h}:interp_algo=4";
        }

        // CPU 缩放，同样使用 lanczos
        return $"scale={w}:{h}:flags=lanczos";
    }

    /// <summary>
    /// 构建 GPU 编码器（NVENC / AMF / QSV）参数。
    /// </summary>
    private static string BuildGpuEncoderArgs(EncodeSettings s)
    {
        var sb = new StringBuilder();

        // ---- 码率控制 ----
        // VBV 上限（maxrate）必须 ≥ 目标码率，否则会把平均码率也压到 maxrate 以下，
        // 表现为「设置高码率却不生效」。这里按目标码率自动抬高 maxrate / bufsize 下限。
        var maxRate = Math.Max(s.MaxBitrateKbps, s.BitrateKbps);
        var bufSize = Math.Max(s.BufferSizeKbps, maxRate * 2);
        var vbv = $"-maxrate {maxRate}k -bufsize {bufSize}k ";

        switch (s.RateControl)
        {
            case "cqp":
                sb.Append($"-rc constqp -qp {s.Quality} ");
                break;

            case "cbr":
                sb.Append($"-rc cbr -b:v {s.BitrateKbps}k ");
                sb.Append(vbv);
                break;

            case "2pass":
                // VBR + 多遍（全分辨率）双遍编码
                sb.Append($"-rc vbr -b:v {s.BitrateKbps}k ");
                sb.Append(vbv);
                if (s.Multipass)
                {
                    sb.Append("-multipass 2 ");   // 2 = full-res two-pass
                }
                break;

            default: // vbr
                sb.Append($"-rc vbr -b:v {s.BitrateKbps}k ");
                sb.Append(vbv);
                break;
        }

        // ---- 质量调优 ----
        sb.Append($"-preset p{s.GpuPreset} ");
        sb.Append($"-tune {s.GpuTune} ");
        sb.Append($"-profile:v {s.Profile} ");

        if (s.SpatialAq)
        {
            sb.Append($"-spatial-aq 1 -aq-strength {s.AqStrength} ");
        }

        sb.Append($"-rc-lookahead {s.RcLookahead} ");
        sb.Append($"-bf {s.BFrames} ");
        sb.Append($"-refs {s.RefFrames} ");

        if (s.ForcedIdr)
        {
            sb.Append("-forced-idr 1 ");
        }

        return sb.ToString();
    }

    /// <summary>
    /// 构建 CPU 编码器（x264 / x265）参数。
    /// </summary>
    private static string BuildCpuEncoderArgs(EncodeSettings s)
    {
        // 命令自定义模式：用户直接给出视频编码器参数，跳过内置预设，完全交给用户控制。
        if (s.CpuMode == "custom")
        {
            var custom = (s.CustomArgs ?? string.Empty).Trim();

            // v1.4.1 安全加固（S3）：自定义参数原样拼进命令行。虽然
            // UseShellExecute=false 且走 ProcessStartInfo.Arguments 不会过 shell，
            // 但仍应挡掉会破坏命令行结构 / 便于夹带其它命令的字符：
            // 换行可注入新行、| & ; ` 是典型命令分隔符。
            // 注：不禁用引号——-vf "scale=..." 这类合法 ffmpeg 写法需要引号，
            // 改为要求引号成对出现（奇数个引号视为截断风险）。
            if (custom.Length > 0)
            {
                if (Regex.IsMatch(custom, @"[\r\n|&;`]") || custom.Count(c => c == '"') % 2 != 0)
                {
                    throw new ArgumentException(
                        "自定义参数不能包含换行、|、&、; 或反引号；引号必须成对出现。");
                }
            }

            return custom.Length > 0 ? custom + " " : string.Empty;
        }

        var sb = new StringBuilder();

        switch (s.CpuMode)
        {
            case "crf":
                sb.Append($"-crf {s.Crf} ");
                break;

            case "2pass":
                sb.Append($"-b:v {s.BitrateKbps}k ");
                break;
        }

        sb.Append($"-preset {s.CpuPreset} ");
        sb.Append($"-tune {s.CpuTune} ");
        sb.Append($"-aq-mode {s.AqMode} ");
        sb.Append($"-aq-strength {s.CpuAqStrength.ToString(CultureInfo.InvariantCulture)} ");
        sb.Append($"-psy-rd {s.PsyRd} ");
        sb.Append($"-keyint {s.KeyInt} -min-keyint {s.MinKeyInt} ");

        return sb.ToString();
    }

    /// <summary>
    /// 构建音轨与字幕参数（视频编码任务内的音轨处理）。
    /// </summary>
    private static string BuildAudioArgs(EncodeSettings s)
    {
        var audio = s.AudioMode switch
        {
            "aac128" => "-c:a aac -b:a 128k ",
            "aac192" => "-c:a aac -b:a 192k ",
            "opus128" => "-c:a libopus -b:a 128k ",
            "mute" => "-an ",
            _ => "-c:a copy "      // 默认复制，最快且无损
        };

        var subtitle = s.SubtitleMode switch
        {
            "drop" => "-sn ",
            _ => "-c:s copy "
        };

        return audio + subtitle;
    }

    /// <inheritdoc/>
    public async Task<bool> EncodeAsync(
        EncodeSettings settings,
        GpuInfo gpuInfo,
        IProgress<EncodeProgress> progress,
        CancellationToken ct = default)
    {
        var resolved = settings.Encoder.Resolve(gpuInfo);
        if (string.IsNullOrWhiteSpace(gpuInfo.FfmpegPath))
        {
            throw new InvalidOperationException(
                "未配置 ffmpeg 路径，请在「设置」页指定 ffmpeg.exe 的位置。");
        }

        // CPU 的 2-Pass 需要两遍：第一遍分析（输出到 null），第二遍正式编码。
        if (!resolved.IsGpuEncoder() && settings.CpuMode == "2pass")
        {
            return await EncodeTwoPassAsync(settings, resolved, gpuInfo, progress, ct);
        }

        var arguments = BuildArguments(settings, resolved, gpuInfo);
        return await RunFfmpegCoreAsync(
            gpuInfo.FfmpegPath, arguments, Path.GetFileName(settings.InputPath), progress, ct);
    }

    /// <summary>
    /// CPU 编码器的真实 2-Pass：第一遍只做统计分析（输出到 null），第二遍按统计结果编码。
    /// 两遍共用同一份日志文件，进度分别上报。
    /// </summary>
    private async Task<bool> EncodeTwoPassAsync(
        EncodeSettings settings,
        EncoderType resolved,
        GpuInfo gpuInfo,
        IProgress<EncodeProgress> progress,
        CancellationToken ct)
    {
        var core = BuildCoreArguments(settings, resolved, gpuInfo);
        var log = Path.Combine(Path.GetTempPath(), $"maruko_2pass_{Guid.NewGuid():N}.log");

        try
        {
            // 第一遍：分析，输出丢弃到 NUL（Windows 空设备），避免占用 stdout 干扰进度管道。
            var pass1 = core +
                        $"-pass 1 -passlogfile \"{log}\" -an -f null NUL";
            var ok1 = await RunFfmpegCoreAsync(
                gpuInfo.FfmpegPath, pass1, Path.GetFileName(settings.InputPath), progress, ct);
            if (!ok1 || ct.IsCancellationRequested)
            {
                return false;
            }

            // 第二遍：正式编码
            var pass2 = core +
                        $"-pass 2 -passlogfile \"{log}\" -progress pipe:1 -nostats -y \"{settings.OutputPath}\"";
            return await RunFfmpegCoreAsync(
                gpuInfo.FfmpegPath, pass2, Path.GetFileName(settings.InputPath), progress, ct);
        }
        finally
        {
            try { File.Delete(log); } catch { /* 忽略清理失败 */ }
        }
    }

    /// <summary>
    /// ffmpeg 进程执行核心：启动进程、解析实时进度、支持取消。
    /// 视频编码 / 轨道抽取 / 音频转码 共用此方法，保证进度解析、取消与异常防护一致。
    /// <para>
    /// 注意：进度回调（<paramref name="progress"/>）可能被后台线程触发，
    /// 调用方负责把回调内容封送到 UI 线程（见 <see cref="App.RunOnUiThread"/>）。
    /// </para>
    /// </summary>
    private async Task<bool> RunFfmpegCoreAsync(
        string ffmpegPath,
        string arguments,
        string currentFile,
        IProgress<EncodeProgress> progress,
        CancellationToken ct)
    {
        // 守卫：ffmpeg 路径缺失会令 process.Start() 抛 Win32Exception，提前给出明确信息。
        if (string.IsNullOrWhiteSpace(ffmpegPath))
        {
            throw new InvalidOperationException(
                "未配置 ffmpeg 路径，请在「设置」页指定 ffmpeg.exe 的位置。");
        }

        // 统一追加结构化进度输出，供进度解析使用。
        if (!arguments.Contains("-progress", StringComparison.Ordinal))
        {
            arguments = arguments.TrimEnd() + " -progress pipe:1 -nostats";
        }

        // 诊断：记录本次实际执行的命令，便于复现与排错。
        // v1.4.1（S4）：命令行里含用户完整文件路径（可能暴露姓名、项目名），
        // 这里把路径脱敏成 <path> 后再落盘，仅保留参数结构以便排错。
        App.LogInfo($"FfmpegPath = {ffmpegPath}");
        App.LogInfo($"CMD = ffmpeg {RedactPaths(arguments)}");

        var psi = new ProcessStartInfo
        {
            FileName = ffmpegPath,
            Arguments = arguments,
            UseShellExecute = false,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            CreateNoWindow = true,
            StandardOutputEncoding = Encoding.UTF8,
            StandardErrorEncoding = Encoding.UTF8
        };

        using var process = new Process { StartInfo = psi };

        var current = new EncodeProgress
        {
            CurrentFile = currentFile,
            StatusMessage = "正在编码…"
        };

        // v1.4.1：总时长由 stderr 线程写入、stdout 线程读取。原先是 TimeSpan 局部变量，
        // 读写无内存屏障，理论上存在新值不可见的窗口（表现为进度百分比偶发停滞）。
        // 改用 long + Volatile 读写，读取时再转 TimeSpan。
        long durationTicks = 0;

        // GPU 编码器（NVENC 等）在 -progress 输出里 fps 恒为 0.00（ffmpeg 自身行为，
        // 与解析无关）。这里记录墙钟耗时，供 ParseProgressLine 在 fps<=0 时用
        // 「已编码帧数 ÷ 已耗时」兜底，避免进度面板 FPS 一直显示 0。
        var wallClock = Stopwatch.StartNew();

        // ---------- stderr: 日志 + 总时长 ----------
        // 此回调运行在线程池线程，任何未捕获异常都应就地吞掉，避免终止进程。
        process.ErrorDataReceived += (_, e) =>
        {
            try
            {
                if (string.IsNullOrEmpty(e.Data))
                {
                    return;
                }

                var line = e.Data;

                if (Volatile.Read(ref durationTicks) == 0)
                {
                    var dur = ParseDuration(line);
                    if (dur.HasValue)
                    {
                        Volatile.Write(ref durationTicks, dur.Value.Ticks);
                    }
                }

                progress.Report(current);
            }
            catch (Exception ex)
            {
                App.LogCrash(ex, "FfmpegService.ErrorDataReceived");
            }
        };

        if (!process.Start())
        {
            current.HasError = true;
            current.ErrorMessage = "无法启动 ffmpeg 进程";
            current.IsCompleted = true;
            progress.Report(current);
            return false;
        }

        // 取消时终止进程树
        await using var registration = ct.Register(() =>
        {
            try { process.Kill(entireProcessTree: true); } catch { /* 忽略 */ }
        });

        process.BeginErrorReadLine();

        // ---------- stdout: 结构化进度 ----------
        // 注意：此任务运行在线程池线程，异常必须自行捕获，否则会终止进程（闪退）。
        var stdoutTask = Task.Run(async () =>
        {
            try
            {
                while (!ct.IsCancellationRequested)
                {
                    var line = await process.StandardOutput.ReadLineAsync(ct);
                    if (line is null)
                    {
                        break;
                    }

                    if (string.IsNullOrWhiteSpace(line))
                    {
                        continue;
                    }

                    ParseProgressLine(
                        line,
                        current,
                        new TimeSpan(Volatile.Read(ref durationTicks)),
                        wallClock.Elapsed);
                    progress.Report(current);
                }
            }
            catch (OperationCanceledException)
            {
                // 正常取消，无需处理
            }
            catch (Exception ex)
            {
                App.LogCrash(ex, "FfmpegService.stdoutTask");
            }
        }, ct);

        await process.WaitForExitAsync(ct);

        try { await stdoutTask; }
        catch (OperationCanceledException) { /* 正常取消 */ }

        if (ct.IsCancellationRequested)
        {
            current.StatusMessage = "已取消";
            current.IsCompleted = true;
            progress.Report(current);
            return false;
        }

        current.IsCompleted = true;

        if (process.ExitCode != 0)
        {
            current.HasError = true;
            current.ErrorMessage = $"ffmpeg 退出码 {process.ExitCode}";
            current.StatusMessage = "编码失败";
            progress.Report(current);
            return false;
        }

        current.Percent = 100;
        current.StatusMessage = "已完成";
        progress.Report(current);
        return true;
    }

    /// <inheritdoc/>
    public async Task<List<MediaStreamInfo>> ProbeStreamsAsync(
        string ffmpegPath,
        string filePath,
        CancellationToken ct = default)
    {
        var result = new List<MediaStreamInfo>();

        if (string.IsNullOrWhiteSpace(ffmpegPath) || !File.Exists(filePath))
        {
            return result;
        }

        var psi = new ProcessStartInfo
        {
            FileName = ffmpegPath,
            Arguments = $"-i \"{filePath}\"",
            UseShellExecute = false,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            CreateNoWindow = true,
            StandardOutputEncoding = Encoding.UTF8,
            StandardErrorEncoding = Encoding.UTF8
        };

        using var process = new Process { StartInfo = psi };
        var stderr = new StringBuilder();

        process.ErrorDataReceived += (_, e) =>
        {
            if (!string.IsNullOrEmpty(e.Data))
            {
                stderr.AppendLine(e.Data);
            }
        };

        if (!process.Start())
        {
            return result;
        }

        // v1.4.1（B5）：取消时必须终止 ffmpeg 子进程。此前只 catch 了
        // OperationCanceledException 就继续往下解析，若探测的是网络路径或损坏文件
        // 导致 ffmpeg 挂起，进程会永久残留。
        await using var registration = ct.Register(() =>
        {
            try { process.Kill(entireProcessTree: true); } catch { /* 忽略 */ }
        });

        process.BeginErrorReadLine();

        // ffmpeg -i 未指定输出文件会以非零退出，这里仅读取 stderr 的流信息即可。
        try
        {
            await process.WaitForExitAsync(ct);
        }
        catch (OperationCanceledException)
        {
            // 探测被取消，返回已解析的部分结果
        }

        // v1.4.1（C1）：解析逻辑曾与 ProbeInfoAsync 逐行重复约 40 行，
        // 现在统一到 ParseStreams，两处调用方各自决定如何装载结果。
        return ParseStreams(stderr.ToString());
    }

    /// <inheritdoc/>
    public async Task<(bool success, int done, int total)> ExtractStreamsAsync(
        string ffmpegPath,
        string source,
        string outputDir,
        IEnumerable<MediaStreamInfo> streams,
        IProgress<EncodeProgress> progress,
        CancellationToken ct = default)
    {
        var list = streams.ToList();
        var total = list.Count;
        var done = 0;

        var agg = new EncodeProgress
        {
            CurrentFile = Path.GetFileName(source),
            StatusMessage = "准备抽取…"
        };
        progress.Report(agg);

        foreach (var s in list)
        {
            if (ct.IsCancellationRequested)
            {
                break;
            }

            var ext = GetStreamExtension(s);
            var outName = $"{Path.GetFileNameWithoutExtension(source)}_s{s.Index}{ext}";
            var outPath = Path.Combine(outputDir, outName);
            var args = $"-i \"{source}\" -map 0:{s.Index} -c copy \"{outPath}\" -y";

            agg.StatusMessage = $"抽取流 {s.Index}（{s.TypeLabel}） {done + 1}/{total}";
            progress.Report(agg);

            // 子进度：把每个流的进度合并进聚合进度。
            var child = new Progress<EncodeProgress>(p =>
            {
                agg.Percent = total > 0 ? done * 100.0 / total + p.Percent / total : p.Percent;
                agg.Speed = p.Speed;
                agg.Fps = p.Fps;
                agg.BitrateKbps = p.BitrateKbps;
                agg.Processed = p.Processed;
                agg.Remaining = p.Remaining;
                progress.Report(agg);
            });

            var ok = await RunFfmpegCoreAsync(ffmpegPath, args, outName, child, ct);

            if (!ok && !ct.IsCancellationRequested)
            {
                agg.HasError = true;
                agg.ErrorMessage = $"流 {s.Index} 抽取失败";
                agg.StatusMessage = "抽取失败";
                progress.Report(agg);
                return (false, done, total);
            }

            done++;
            agg.Percent = done * 100.0 / total;
            agg.StatusMessage = $"已完成 {done}/{total}";
            progress.Report(agg);
        }

        agg.IsCompleted = true;
        if (ct.IsCancellationRequested)
        {
            agg.StatusMessage = "已取消";
        }
        else
        {
            agg.Percent = 100;
            agg.StatusMessage = $"全部完成（{done}/{total}）";
        }
        progress.Report(agg);

        return (!ct.IsCancellationRequested, done, total);
    }

    /// <inheritdoc/>
    public async Task<bool> TranscodeAudioAsync(
        string ffmpegPath,
        string inputPath,
        string outputPath,
        AudioPreset preset,
        IProgress<EncodeProgress> progress,
        CancellationToken ct = default)
    {
        var arguments = BuildAudioArguments(inputPath, outputPath, preset);
        return await RunFfmpegCoreAsync(
            ffmpegPath, arguments, Path.GetFileName(inputPath), progress, ct);
    }

    /// <inheritdoc/>
    public async Task<bool> RemuxAsync(
        string ffmpegPath,
        IReadOnlyList<string> inputs,
        string container,
        string outputPath,
        IProgress<EncodeProgress> progress,
        CancellationToken ct = default)
    {
        if (inputs == null || inputs.Count == 0)
        {
            throw new InvalidOperationException("封装操作至少需要一个输入文件。");
        }

        var sb = new StringBuilder();
        foreach (var inp in inputs)
        {
            sb.Append($"-i \"{inp}\" ");
        }

        // 映射每个输入的全部流：多输入时 -map N 映射第 N 个输入的所有流。
        // 典型场景是「1 个视频 + N 个音轨 + N 个字幕」，全部无损拷贝。
        for (var i = 0; i < inputs.Count; i++)
        {
            sb.Append($"-map {i} ");
        }

        sb.Append("-c copy ");

        // MP4 容器下加 faststart 便于网络渐进播放；
        // 字幕流（如 ASS）在 MP4 中 copy 可能不被支持，封装时建议选 MKV。
        if (string.Equals(container, "mp4", StringComparison.OrdinalIgnoreCase))
        {
            sb.Append("-movflags +faststart ");
        }

        sb.Append($"-y \"{outputPath}\"");

        return await RunFfmpegCoreAsync(
            ffmpegPath, sb.ToString(), Path.GetFileName(outputPath), progress, ct);
    }

    /// <inheritdoc/>
    public async Task<bool> ExtractFramesAsync(
        string ffmpegPath,
        string inputVideo,
        string outputDir,
        FrameExtractOptions options,
        IProgress<EncodeProgress> progress,
        CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(inputVideo) || !File.Exists(inputVideo))
        {
            throw new InvalidOperationException("源视频不存在，请重新选择。");
        }

        Directory.CreateDirectory(outputDir);

        var ext = string.Equals(options.Format, "jpg", StringComparison.OrdinalIgnoreCase) ? "jpg" : "png";
        var scale = (options.ScaleWidth > 0 && options.ScaleHeight > 0)
            ? $"scale={options.ScaleWidth}:{options.ScaleHeight}"
            : string.Empty;

        var sb = new StringBuilder();

        if (options.Mode == FrameMode.Single)
        {
            var outFile = Path.Combine(outputDir, $"frame_{options.TimeSeconds:0.###}s.{ext}");
            sb.Append($"-ss {options.TimeSeconds:0.###} -i \"{inputVideo}\" ");
            if (!string.IsNullOrEmpty(scale))
            {
                sb.Append($"-vf {scale} ");
            }

            sb.Append($"-frames:v 1 -q:v 2 \"{outFile}\"");
        }
        else
        {
            var interval = options.IntervalSeconds > 0 ? options.IntervalSeconds : 1;
            var outPattern = Path.Combine(outputDir, $"frame_%04d.{ext}");
            sb.Append($"-i \"{inputVideo}\" ");
            // 序列帧用 fps 滤镜；若需缩放则合并到同一条滤镜链（ffmpeg 只接受最后一个 -vf）。
            sb.Append(string.IsNullOrEmpty(scale)
                ? $"-vf fps=1/{interval} "
                : $"-vf fps=1/{interval},{scale} ");
            sb.Append($"\"{outPattern}\"");
        }

        return await RunFfmpegCoreAsync(
            ffmpegPath, sb.ToString(), Path.GetFileName(inputVideo), progress, ct);
    }

    /// <inheritdoc/>
    public async Task<bool> ConvertImageAsync(
        string ffmpegPath,
        string inputImage,
        string outputPath,
        string format,
        IProgress<EncodeProgress> progress,
        CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(inputImage) || !File.Exists(inputImage))
        {
            throw new InvalidOperationException("源图片不存在，请重新选择。");
        }

        var sb = new StringBuilder($"-i \"{inputImage}\" ");
        if (string.Equals(format, "jpg", StringComparison.OrdinalIgnoreCase))
        {
            sb.Append("-q:v 2 ");
        }

        sb.Append($"-y \"{outputPath}\"");

        return await RunFfmpegCoreAsync(
            ffmpegPath, sb.ToString(), Path.GetFileName(inputImage), progress, ct);
    }

    /// <inheritdoc/>
    public async Task<MediaFileInfo> ProbeInfoAsync(
        string ffmpegPath,
        string filePath,
        CancellationToken ct = default)
    {
        var info = new MediaFileInfo();

        if (string.IsNullOrWhiteSpace(ffmpegPath) || !File.Exists(filePath))
        {
            return info;
        }

        var psi = new ProcessStartInfo
        {
            FileName = ffmpegPath,
            Arguments = $"-i \"{filePath}\"",
            UseShellExecute = false,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            CreateNoWindow = true,
            StandardOutputEncoding = Encoding.UTF8,
            StandardErrorEncoding = Encoding.UTF8
        };

        using var process = new Process { StartInfo = psi };
        var stderr = new StringBuilder();

        process.ErrorDataReceived += (_, e) =>
        {
            if (!string.IsNullOrEmpty(e.Data))
            {
                stderr.AppendLine(e.Data);
            }
        };

        if (!process.Start())
        {
            return info;
        }

        // v1.4.1（B5）：同 ProbeStreamsAsync，取消时终止子进程。
        await using var registration = ct.Register(() =>
        {
            try { process.Kill(entireProcessTree: true); } catch { /* 忽略 */ }
        });

        process.BeginErrorReadLine();

        try
        {
            await process.WaitForExitAsync(ct);
        }
        catch (OperationCanceledException)
        {
            // 探测被取消，返回已解析的部分结果
        }

        var text = stderr.ToString();

        // 总时长
        foreach (var line in text.Split('\n'))
        {
            var d = ParseDuration(line);
            if (d.HasValue)
            {
                info.Duration = d.Value;
                break;
            }
        }

        // 流列表（与 ProbeStreamsAsync 共用同一套解析规则）
        foreach (var stream in ParseStreams(text))
        {
            info.Streams.Add(stream);
        }

        return info;
    }

    /// <summary>
    /// 从 <c>ffmpeg -i</c> 的 stderr 中解析出媒体流列表。
    /// 匹配形如 <c>Stream #0:1(chi): Audio: aac (LC) ...</c> 的行。
    /// </summary>
    private static readonly Regex StreamLineRegex = new(
        @"Stream #\d+:(\d+)(?:\[[^\]]*\])?(?:\(([^)]*)\))?:\s*(\w+):\s*(.*)",
        RegexOptions.Compiled);

    private static List<MediaStreamInfo> ParseStreams(string ffmpegStderr)
    {
        var result = new List<MediaStreamInfo>();
        if (string.IsNullOrEmpty(ffmpegStderr))
        {
            return result;
        }

        foreach (var line in ffmpegStderr.Split('\n'))
        {
            var m = StreamLineRegex.Match(line);
            if (!m.Success)
            {
                continue;
            }

            var idx = int.Parse(m.Groups[1].Value, CultureInfo.InvariantCulture);
            var lang = m.Groups[2].Value.Trim();
            var typeStr = m.Groups[3].Value.ToLowerInvariant();
            var detail = m.Groups[4].Value.Trim();

            var type = typeStr switch
            {
                "video" => StreamType.Video,
                "audio" => StreamType.Audio,
                "subtitle" => StreamType.Subtitle,
                "data" => StreamType.Data,
                "attachment" => StreamType.Attachment,
                _ => StreamType.Unknown
            };

            var codec = detail.Split(' ', ',')[0].Trim();

            result.Add(new MediaStreamInfo
            {
                Index = idx,
                Type = type,
                Codec = codec,
                Language = lang,
                Detail = detail
            });
        }

        return result;
    }

    /// <inheritdoc/>
    public async Task<bool> EmbedSubtitleAsync(
        string ffmpegPath,
        string videoPath,
        string subtitlePath,
        string outputVideo,
        IProgress<EncodeProgress> progress,
        CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(videoPath) || !File.Exists(videoPath))
        {
            throw new InvalidOperationException("源视频不存在，请重新选择。");
        }

        if (string.IsNullOrWhiteSpace(subtitlePath) || !File.Exists(subtitlePath))
        {
            throw new InvalidOperationException("字幕文件不存在，请重新选择。");
        }

        // 视频与字幕合并：复制视频全部流 + 字幕流；字幕按输出容器由 ffmpeg 自动选编码器。
        var sb = new StringBuilder(
            $"-i \"{videoPath}\" -i \"{subtitlePath}\" -map 0 -map 1 -c copy -y \"{outputVideo}\"");

        return await RunFfmpegCoreAsync(
            ffmpegPath, sb.ToString(), Path.GetFileName(outputVideo), progress, ct);
    }

    /// <inheritdoc/>
    public async Task<bool> ConvertSubtitleAsync(
        string ffmpegPath,
        string inputSub,
        string outputSub,
        IProgress<EncodeProgress> progress,
        CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(inputSub) || !File.Exists(inputSub))
        {
            throw new InvalidOperationException("源字幕不存在，请重新选择。");
        }

        // ffmpeg 按输出扩展名（srt / ass / vtt）自动选择字幕编码器。
        var sb = new StringBuilder($"-i \"{inputSub}\" -y \"{outputSub}\"");

        return await RunFfmpegCoreAsync(
            ffmpegPath, sb.ToString(), Path.GetFileName(outputSub), progress, ct);
    }

    /// <summary>
    /// 根据流类型与编码决定抽取后的文件后缀，尽量选兼容容器。
    /// </summary>
    private static string GetStreamExtension(MediaStreamInfo s)
    {
        if (s.Type == StreamType.Subtitle)
        {
            return s.Codec.ToLowerInvariant() switch
            {
                "ass" or "ssa" => ".ass",
                "srt" or "subrip" or "mov_text" => ".srt",
                "pgs" or "hdmv_pgs_subtitle" => ".sup",
                "dvd_subtitle" => ".sub",
                _ => ".srt"
            };
        }

        if (s.Type == StreamType.Audio)
        {
            return s.Codec.ToLowerInvariant() switch
            {
                "aac" => ".m4a",
                "opus" => ".opus",
                "mp3" or "mp3float" => ".mp3",
                "flac" => ".flac",
                "dts" => ".dts",
                "eac3" => ".eac3",
                "ac3" => ".ac3",
                "truehd" => ".thd",
                "pcm_s16le" or "pcm_s24le" or "pcm_s32le" => ".wav",
                _ => ".mka"
            };
        }

        if (s.Type == StreamType.Video)
        {
            return s.Codec.ToLowerInvariant() switch
            {
                "h264" => ".mp4",
                "hevc" => ".mp4",
                "vp9" => ".mkv",
                "av1" => ".mkv",
                "mpeg4" => ".m4v",
                _ => ".mkv"
            };
        }

        return ".bin";
    }

    /// <summary>
    /// 根据音频预设构造 ffmpeg 参数。
    /// </summary>
    private static string BuildAudioArguments(string input, string output, AudioPreset p)
    {
        var sb = new StringBuilder($"-i \"{input}\" ");

        switch (p.Codec)
        {
            case "aac":
                sb.Append("-c:a aac ");
                if (p.BitrateKbps > 0) sb.Append($"-b:a {p.BitrateKbps}k ");
                break;

            case "opus":
                sb.Append("-c:a libopus ");
                if (p.BitrateKbps > 0) sb.Append($"-b:a {p.BitrateKbps}k ");
                break;

            case "flac":
                sb.Append("-c:a flac ");
                break;

            case "mp3":
                sb.Append("-c:a libmp3lame ");
                if (p.BitrateKbps > 0) sb.Append($"-b:a {p.BitrateKbps}k ");
                break;

            default:
                sb.Append("-c:a copy ");
                break;
        }

        if (p.Channels > 0) sb.Append($"-ac {p.Channels} ");
        if (p.SampleRate > 0) sb.Append($"-ar {p.SampleRate} ");

        sb.Append($"-y \"{output}\"");
        return sb.ToString();
    }

    /// <summary>
    /// 解析 <c>-progress</c> 输出的一行（形如 "out_time_us=12345678"）。
    /// </summary>
    private static void ParseProgressLine(
        string line,
        EncodeProgress current,
        TimeSpan totalDuration,
        TimeSpan elapsed = default)
    {
        var idx = line.IndexOf('=');
        if (idx <= 0)
        {
            return;
        }

        var key = line[..idx].Trim();
        var value = line[(idx + 1)..].Trim();

        switch (key)
        {
            case "frame":
                if (long.TryParse(value, out var frame)) current.CurrentFrame = frame;
                break;

            case "fps":
                if (double.TryParse(value, CultureInfo.InvariantCulture, out var fps)) current.Fps = fps;
                break;

            case "bitrate":
                // 形如 "3800.5kbits/s"
                var num = value.Replace("kbits/s", string.Empty, StringComparison.OrdinalIgnoreCase).Trim();
                if (double.TryParse(num, CultureInfo.InvariantCulture, out var br)) current.BitrateKbps = br;
                break;

            case "out_time_us":
            case "out_time_ms":
                // 注意：ffmpeg 的 out_time_ms 键名虽含 "ms"，实际输出的值是微秒
                // （ffmpeg 历史遗留问题，与 out_time_us 相同）。若按毫秒解析，
                // Processed 会被放大 1000 倍，进度百分比瞬间爆表并永远卡在 100。
                if (long.TryParse(value, out var us))
                {
                    current.Processed = TimeSpan.FromMicroseconds(us);
                    UpdatePercent(current, totalDuration);
                }
                break;

            case "speed":
                // 形如 "8.76x"
                var sp = value.TrimEnd('x', 'X');
                if (double.TryParse(sp, CultureInfo.InvariantCulture, out var speed)) current.Speed = speed;
                break;

            case "progress":
                current.StatusMessage = value == "end" ? "正在收尾…" : "正在编码…";

                // 一个进度块到此结束。GPU 编码器不回报 fps（恒 0），
                // 用「已编码帧数 ÷ 已耗时」兜底，让 FPS 显示有实际意义。
                if (current.Fps <= 0 && current.CurrentFrame > 0 && elapsed.TotalSeconds > 0.5)
                {
                    current.Fps = current.CurrentFrame / elapsed.TotalSeconds;
                }
                break;
        }
    }

    /// <summary>
    /// 依据已处理时长与总时长更新进度百分比与剩余时间。
    /// </summary>
    private static void UpdatePercent(EncodeProgress current, TimeSpan totalDuration)
    {
        if (totalDuration <= TimeSpan.Zero)
        {
            return;
        }

        current.Percent = Math.Clamp(
            current.Processed.TotalMilliseconds / totalDuration.TotalMilliseconds * 100, 0, 100);

        var remain = totalDuration - current.Processed;
        current.Remaining = remain > TimeSpan.Zero ? remain : TimeSpan.Zero;
    }

    /// <inheritdoc/>
    public string BuildTrimArguments(TrimRequest request, GpuInfo gpuInfo)
    {
        if (string.IsNullOrWhiteSpace(request.OutputPath))
        {
            throw new InvalidOperationException("未指定输出路径。");
        }

        var duration = request.Duration;
        if (duration <= TimeSpan.Zero)
        {
            throw new InvalidOperationException("裁剪区间无效：结束时间必须晚于开始时间。");
        }

        var sb = new StringBuilder();

        // -ss 放在 -i 之前：先按索引粗定位、再由解码器精修。
        // 重编码模式下 ffmpeg 会从最近关键帧开始解码并丢弃起点之前的帧，因此起点是帧级精确的；
        // 拷贝模式下无法在 GOP 中间切断，只能从最近关键帧开始（表现为片段可能略微提前）。
        sb.Append($"-ss {request.Start.TotalSeconds:0.###} ");
        sb.Append($"-i \"{request.InputPath}\" ");

        // 用 -t（时长）而不是 -to（绝对时刻）：-ss 前置后输出时间轴从 0 重新起算，
        // 此时 -to 会被当作"从起点起的偏移"，写出来容易相差一个 start。
        sb.Append($"-t {duration.TotalSeconds:0.###} ");

        if (request.Mode == TrimMode.Copy)
        {
            // 全轨无损拷贝：画面、音轨、字幕、章节原样保留。
            //
            // 不要加 -avoid_negative_ts：实测（ffmpeg 8.1.2，10s 测试片取 1.5s~6.5s）
            // 加了它之后 -t 会被忽略，5 秒的请求产出 6.59 秒；
            // 不加时输出 5.067 秒、首帧与源 1.5s 处完全一致（md5 相同），起点是精确的。
            // 反直觉但可复现：这个选项会连带关闭拷贝模式下的时间戳截断。
            sb.Append("-map 0 -c copy ");
        }
        else
        {
            var enc = request.Encoder == EncoderType.Auto ? gpuInfo.RecommendedEncoder : request.Encoder;
            var quality = Math.Clamp(request.Quality, 0, 51);

            // NVENC 用 -cq（配合默认 VBR），CPU 编码器用 -crf；
            // AMF / QSV 的量化参数命名各家不一致，这里只用它们的预设档位，避免参数不被识别。
            var qualityArgs = enc switch
            {
                EncoderType.NvencHevc or EncoderType.NvencH264 => $"-preset p5 -cq {quality}",
                EncoderType.AmfHevc or EncoderType.QsvHevc => "-quality balanced",
                _ => $"-preset medium -crf {quality}"
            };

            // 只保留画面与音轨：字幕 / data 轨在重编码后常与容器不兼容（例如 srt 进 MP4），
            // 与其让它静默失败，不如在 UI 上把取舍说明白（见 TrimPage 的「精确剪切」提示）。
            sb.Append($"-map 0:v:0 -map 0:a? ");
            sb.Append($"-c:v {enc.ToFfmpegCodec()} {qualityArgs} -c:a copy ");
        }

        if (string.Equals(Path.GetExtension(request.OutputPath), ".mp4", StringComparison.OrdinalIgnoreCase))
        {
            sb.Append("-movflags +faststart ");
        }

        sb.Append($"-y \"{request.OutputPath}\"");

        return sb.ToString();
    }

    /// <inheritdoc/>
    public async Task<bool> TrimAsync(
        string ffmpegPath,
        TrimRequest request,
        GpuInfo gpuInfo,
        IProgress<EncodeProgress> progress,
        CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(request.InputPath) || !File.Exists(request.InputPath))
        {
            throw new InvalidOperationException("源视频不存在，请重新选择。");
        }

        return await RunFfmpegCoreAsync(
            ffmpegPath,
            BuildTrimArguments(request, gpuInfo),
            Path.GetFileName(request.OutputPath),
            progress,
            ct);
    }

    /// <inheritdoc/>
    public async Task<bool> GrabFrameAsync(
        string ffmpegPath,
        string inputVideo,
        double seconds,
        string outputImagePath,
        int maxWidth = 320,
        CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(inputVideo) || !File.Exists(inputVideo))
        {
            return false;
        }

        var dir = Path.GetDirectoryName(outputImagePath);
        if (!string.IsNullOrEmpty(dir))
        {
            Directory.CreateDirectory(dir);
        }

        // -2 表示按偶数对齐（部分编码器要求宽高为偶数）；-1 在奇分辨率下会直接报错。
        var scale = maxWidth > 0 ? $"-vf scale={maxWidth}:-2 " : string.Empty;
        var args = $"-ss {Math.Max(0, seconds):0.###} -i \"{inputVideo}\" " +
                   $"{scale}-frames:v 1 -q:v 3 -y \"{outputImagePath}\"";

        // 抽帧是一次性短任务，进度无意义，用空接收者满足参数契约。
        return await RunFfmpegCoreAsync(
            ffmpegPath, args, Path.GetFileName(inputVideo), new Progress<EncodeProgress>(), ct);
    }

    /// <summary>
    /// 把命令行中的文件系统路径替换为 <c>&lt;path&gt;</c>，避免用户目录结构落入日志。
    /// 只处理「被引号包裹且含 / 或 \」的片段——这样 <c>-vf "scale=1280:720"</c>
    /// 这类滤镜参数不会被误伤（它不含路径分隔符）。
    /// </summary>
    private static readonly Regex PathTokenRegex = new("\"[^\"]*[\\\\/][^\"]*\"", RegexOptions.Compiled);

    private static string RedactPaths(string arguments) =>
        PathTokenRegex.Replace(arguments, "\"<path>\"");

    /// <summary>
    /// 从 ffmpeg 输出行中解析总时长（"Duration: 00:02:14.38"）。
    /// </summary>
    private static TimeSpan? ParseDuration(string line)
    {
        const string marker = "Duration:";
        var idx = line.IndexOf(marker, StringComparison.OrdinalIgnoreCase);
        if (idx < 0)
        {
            return null;
        }

        var after = line[(idx + marker.Length)..].Trim();
        var timePart = after.Split(',')[0].Trim();

        return TimeSpan.TryParse(timePart, CultureInfo.InvariantCulture, out var ts) ? ts : null;
    }
}
