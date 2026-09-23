using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Globalization;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using MarukoBox.Helpers;
using MarukoBox.Models;
using MarukoBox.Services;

namespace MarukoBox.ViewModels;

/// <summary>
/// 裁剪页 ViewModel：选文件 → 探测时长 → 在时间轴上定区间 → 剪切。
///
/// 一个刻意的取舍：<b>端点变化不自动抽帧</b>。
/// 拖动手柄时每移动一像素就跑一次 ffmpeg 抽帧会把界面拖垮，
/// 因此缩略图只在"松手"（页面调用 <see cref="RefreshThumbnailsAsync"/>）或按钮微调后刷新。
/// </summary>
public partial class TrimViewModel : ObservableObject
{
    private readonly IFfmpegService _ffmpeg = AppServices.Ffmpeg;
    private readonly IConfigService _config = AppServices.Config;
    private readonly IGpuDetectionService _gpuDetection = AppServices.GpuDetection;

    private CancellationTokenSource? _cts;
    private CancellationTokenSource? _thumbCts;
    private string? _namingRule;

    /// <summary>上一轮生成的缩略图路径，用于在新图就位后删掉旧图（M2）。</summary>
    private string? _lastStartThumb;
    private string? _lastEndThumb;

    private string NamingRule =>
        _namingRule ??= OutputNaming.Normalize(_config.Load().OutputFileNameRule);

    private string FfmpegPath => _config.Load().FfmpegPath;

    // ---------- 输入 ----------

    [ObservableProperty]
    public partial string InputPath { get; set; } = string.Empty;

    /// <summary>是否已有可裁剪的视频。</summary>
    [ObservableProperty]
    public partial bool HasVideo { get; set; }

    /// <summary>预览控件无法解码该文件（系统解码器缺失）时为真，此时降级为"仅时间轴"。</summary>
    [ObservableProperty]
    public partial bool PreviewFailed { get; set; }

    // ---------- 时间轴状态 ----------

    [ObservableProperty]
    public partial TimeSpan Duration { get; set; }

    [ObservableProperty]
    public partial TimeSpan Start { get; set; }

    [ObservableProperty]
    public partial TimeSpan End { get; set; }

    [ObservableProperty]
    public partial TimeSpan Position { get; set; }

    // ---------- 端点缩略图 ----------

    [ObservableProperty]
    public partial string? StartThumbPath { get; set; }

    [ObservableProperty]
    public partial string? EndThumbPath { get; set; }

    // ---------- 输出与执行 ----------

    /// <summary>
    /// 快速剪切（默认开）。实测两种模式的起点都是帧级精确的，
    /// 快速模式的代价只有"尾部可能多带极少量画面"和"不能改画质"，
    /// 对绝大多数"剪一刀"场景来说不值得为此等一次重编码。
    /// </summary>
    [ObservableProperty]
    public partial bool FastMode { get; set; } = true;

    [ObservableProperty]
    public partial int Quality { get; set; } = 20;

    [ObservableProperty]
    public partial EncoderType SelectedEncoder { get; set; } = EncoderType.Auto;

    [ObservableProperty]
    public partial string OutputPath { get; set; } = string.Empty;

    [ObservableProperty]
    public partial bool IsBusy { get; set; }

    [ObservableProperty]
    public partial string StatusText { get; set; } = "拖入视频，或点「浏览…」选择文件";

    [ObservableProperty]
    public partial EncodeProgress Progress { get; set; } = new();

    /// <summary>可选编码器（只列能稳定工作的；AMF / QSV 的参数命名各家不一，交给「自动」处理）。</summary>
    public ObservableCollection<OptionEntry> EncoderOptions { get; } = new()
    {
        new() { Value = EncoderType.Auto.ToString(), Name = "自动（优先 GPU）" },
        new() { Value = EncoderType.NvencH264.ToString(), Name = "H.264 · NVENC（显卡）" },
        new() { Value = EncoderType.NvencHevc.ToString(), Name = "H.265 · NVENC（显卡）" },
        new() { Value = EncoderType.X264.ToString(), Name = "H.264 · CPU（兼容性最好）" }
    };

    /// <summary>编码器下拉用的选中值（字符串形式，便于 XAML SelectedValue 绑定）。</summary>
    [ObservableProperty]
    public partial string SelectedEncoderValue { get; set; } = EncoderType.Auto.ToString();

    // ---------- 派生文本 ----------

    public string StartText => TrimTimeline.FormatClock(Start);

    public string EndText => TrimTimeline.FormatClock(End);

    public string PositionText => TrimTimeline.FormatClock(Position);

    public string DurationText => TrimTimeline.FormatClock(Duration);

    /// <summary>选中的片段长度。</summary>
    public string SelectionText => TrimTimeline.FormatClock(SelectedDuration);

    public TimeSpan SelectedDuration => End > Start ? End - Start : TimeSpan.Zero;

    /// <summary>起止时间的一句话摘要，例如「00:01:02.500 → 00:01:35.000（32.500s）」。</summary>
    public string RangeSummary => HasVideo
        ? $"{StartText} → {EndText}（{SelectionText}）"
        : "—";

    /// <summary>快速模式的说明文字（切点可能对齐关键帧）。</summary>
    public string ModeHint =>
        FastMode
            ? "快速剪切：直接拷贝码流，秒级完成、画质零损失，所有轨道（画面 / 音轨 / 字幕）原样保留。"
              + "实测起点精确到帧，尾部可能多带极少量画面（合成测试片约 0.07 秒）。"
            : "精确剪切：重新编码画面，起止都精确到帧，音轨仍为原样拷贝；耗时取决于片段长度与编码器。"
              + "注意：精确模式只保留画面与音轨，字幕 / 附件轨会被丢弃。";

    // ---------- 属性变更联动 ----------

    partial void OnStartChanged(TimeSpan value)
    {
        OnPropertyChanged(nameof(StartText));
        OnPropertyChanged(nameof(SelectionText));
        OnPropertyChanged(nameof(RangeSummary));
    }

    partial void OnEndChanged(TimeSpan value)
    {
        OnPropertyChanged(nameof(EndText));
        OnPropertyChanged(nameof(SelectionText));
        OnPropertyChanged(nameof(RangeSummary));
    }

    partial void OnPositionChanged(TimeSpan value) => OnPropertyChanged(nameof(PositionText));

    partial void OnDurationChanged(TimeSpan value) => OnPropertyChanged(nameof(DurationText));

    partial void OnFastModeChanged(bool value)
    {
        OnPropertyChanged(nameof(ModeHint));
        RefreshOutputPath();
    }

    partial void OnSelectedEncoderValueChanged(string value)
    {
        if (Enum.TryParse<EncoderType>(value, out var parsed))
        {
            SelectedEncoder = parsed;
        }

        RefreshOutputPath();
    }

    partial void OnInputPathChanged(string value) => OnPropertyChanged(nameof(RangeSummary));

    partial void OnHasVideoChanged(bool value) => OnPropertyChanged(nameof(RangeSummary));

    // ---------- 载入视频 ----------

    /// <summary>
    /// 载入视频：探测时长、重置区间、生成默认输出路径、抽首尾帧缩略图。
    /// </summary>
    /// <returns>时长解析成功返回 true（时长为 0 时时间轴不可用）。</returns>
    public async Task<bool> LoadAsync(string path)
    {
        if (string.IsNullOrWhiteSpace(path) || !File.Exists(path))
        {
            StatusText = "文件不存在，请检查路径";
            return false;
        }

        InputPath = path;
        HasVideo = true;
        PreviewFailed = false;
        StatusText = "正在解析视频…";

        var info = await _ffmpeg.ProbeInfoAsync(FfmpegPath, path);
        var duration = info.Duration;

        if (duration <= TimeSpan.Zero)
        {
            Duration = TimeSpan.Zero;
            HasVideo = false;
            StatusText = "无法解析视频时长，请确认文件可正常播放";
            return false;
        }

        Duration = duration;
        Start = TimeSpan.Zero;
        End = duration;
        Position = TimeSpan.Zero;

        RefreshOutputPath();

        await RefreshThumbnailsAsync();

        StatusText = $"已载入：{Path.GetFileName(path)}（总长 {DurationText}）";
        return true;
    }

    // ---------- 端点操作 ----------

    [RelayCommand]
    private void SetStartToPosition()
    {
        if (!HasVideo)
        {
            return;
        }

        var v = Position;
        Start = v > End - TimeSpan.FromSeconds(0.1) ? End - TimeSpan.FromSeconds(0.1) : v;
        if (Start < TimeSpan.Zero)
        {
            Start = TimeSpan.Zero;
        }

        _ = RefreshThumbnailsAsync();
    }

    [RelayCommand]
    private void SetEndToPosition()
    {
        if (!HasVideo)
        {
            return;
        }

        var v = Position;
        End = v < Start + TimeSpan.FromSeconds(0.1) ? Start + TimeSpan.FromSeconds(0.1) : v;
        if (End > Duration)
        {
            End = Duration;
        }

        _ = RefreshThumbnailsAsync();
    }

    /// <summary>按秒数微调端点。参数形如 "-0.1"、"1"（字符串来自 XAML CommandParameter）。</summary>
    [RelayCommand]
    private void NudgeStart(string? deltaText) => Nudge(deltaText, isStart: true);

    /// <summary>按秒数微调终点。</summary>
    [RelayCommand]
    private void NudgeEnd(string? deltaText) => Nudge(deltaText, isStart: false);

    private void Nudge(string? deltaText, bool isStart)
    {
        if (!HasVideo || !double.TryParse(deltaText, NumberStyles.Float, CultureInfo.InvariantCulture, out var delta))
        {
            return;
        }

        const double gap = 0.1;

        if (isStart)
        {
            var v = Start.TotalSeconds + delta;
            Start = TimeSpan.FromSeconds(Math.Clamp(v, 0, Math.Max(0, End.TotalSeconds - gap)));
        }
        else
        {
            var v = End.TotalSeconds + delta;
            End = TimeSpan.FromSeconds(Math.Clamp(v, Math.Min(Duration.TotalSeconds, Start.TotalSeconds + gap), Duration.TotalSeconds));
        }

        _ = RefreshThumbnailsAsync();
    }

    /// <summary>恢复为整段（起点 0、终点为总时长）。</summary>
    [RelayCommand]
    private void ResetRange()
    {
        if (!HasVideo)
        {
            return;
        }

        Start = TimeSpan.Zero;
        End = Duration;
        _ = RefreshThumbnailsAsync();
    }

    /// <summary>
    /// 重新抽取起点 / 终点缩略图。拖动过程中不要调用（每次约 100~300ms），
    /// 只在松手或按钮微调后调用。
    /// </summary>
    public async Task RefreshThumbnailsAsync()
    {
        if (!HasVideo || string.IsNullOrWhiteSpace(InputPath))
        {
            return;
        }

        _thumbCts?.Cancel();
        _thumbCts?.Dispose();
        _thumbCts = new CancellationTokenSource();
        var ct = _thumbCts.Token;

        var dir = Path.Combine(Path.GetTempPath(), "MarukoBox", "trim-thumbs");
        Directory.CreateDirectory(dir);

        // 【M2】顺手清掉一天前的历史缩略图。每次微调都会产生两张新图，
        // 不清的话用上几十次就在 %TEMP% 里堆下几百个文件。
        PurgeOldThumbs(dir);

        // 终点取 End 前 50ms：正好落在 End 上时常因关键帧/容器边界抽不出画面。
        var startSec = Start.TotalSeconds;
        var endSec = Math.Max(0, End.TotalSeconds - 0.05);

        var startFile = Path.Combine(dir, $"start_{Guid.NewGuid():N}.jpg");
        var endFile = Path.Combine(dir, $"end_{Guid.NewGuid():N}.jpg");

        try
        {
            var okStart = await _ffmpeg.GrabFrameAsync(FfmpegPath, InputPath, startSec, startFile, 320, ct);
            var okEnd = await _ffmpeg.GrabFrameAsync(FfmpegPath, InputPath, endSec, endFile, 320, ct);

            if (ct.IsCancellationRequested)
            {
                return;
            }

            // 换文件名而不是换同一个文件：Image 控件会按 URI 缓存，同名文件更新后画面不刷新。
            StartThumbPath = okStart ? startFile : null;
            EndThumbPath = okEnd ? endFile : null;

            // 【M2】既然每次都用新文件名，上一轮的旧文件就必须自己删掉，
            // 否则「换名绕缓存」的做法会变成永久性垃圾堆积。
            DeleteThumb(_lastStartThumb, startFile);
            DeleteThumb(_lastEndThumb, endFile);
            _lastStartThumb = okStart ? startFile : null;
            _lastEndThumb = okEnd ? endFile : null;
        }
        catch (OperationCanceledException)
        {
            // 被后续拖动取代，忽略
        }
        catch (Exception ex)
        {
            App.LogCrash(ex, "TrimViewModel.RefreshThumbnailsAsync");
        }
    }

    // ---------- 输出路径 ----------

    /// <summary>按命名规则重新生成输出路径（用户手动指定过则以用户为准）。</summary>
    private void RefreshOutputPath()
    {
        if (string.IsNullOrWhiteSpace(InputPath))
        {
            return;
        }

        var ext = Path.GetExtension(InputPath);
        if (string.IsNullOrEmpty(ext))
        {
            ext = ".mp4";
        }

        var codec = FastMode
            ? "copy"
            : Enum.TryParse<EncoderType>(SelectedEncoderValue, out var e) ? e.ToFfmpegCodec() : "auto";

        var path = OutputNaming.BuildOutputPath(
            NamingRule, InputPath, null,
            new OutputNamingContext(InputPath, "trim", codec, string.Empty, ext));

        // 「原名」规则会生成与源文件同名的输出，直接覆盖源文件是不可接受的
        if (string.Equals(path, InputPath, StringComparison.OrdinalIgnoreCase))
        {
            var dir = Path.GetDirectoryName(InputPath) ?? string.Empty;
            var name = Path.GetFileNameWithoutExtension(InputPath);
            path = Path.Combine(dir, $"{name}_trim{ext}");
        }

        OutputPath = path;
    }

    // ---------- 执行裁剪 ----------

    [RelayCommand]
    private async Task StartAsync()
    {
        if (!HasVideo || IsBusy)
        {
            return;
        }

        if (SelectedDuration <= TimeSpan.Zero)
        {
            StatusText = "裁剪区间无效：结束时间必须晚于开始时间";
            return;
        }

        if (string.IsNullOrWhiteSpace(OutputPath))
        {
            RefreshOutputPath();
        }

        var dirOut = Path.GetDirectoryName(OutputPath);
        if (!string.IsNullOrEmpty(dirOut) && !Directory.Exists(dirOut))
        {
            StatusText = "输出目录不存在，请重新选择输出路径";
            return;
        }

        IsBusy = true;
        _cts = new CancellationTokenSource();
        StatusText = FastMode ? "快速剪切中…" : "精确剪切中（重新编码画面）…";

        try
        {
            var gpu = await _gpuDetection.DetectAsync(FfmpegPath, _cts.Token);

            var request = new TrimRequest
            {
                InputPath = InputPath,
                OutputPath = OutputPath,
                Start = Start,
                End = End,
                Mode = FastMode ? TrimMode.Copy : TrimMode.ReEncode,
                Encoder = SelectedEncoder,
                Quality = Quality
            };

            var prog = new Progress<EncodeProgress>(p =>
            {
                App.RunOnUiThread(() =>
                {
                    Progress.Percent = p.Percent;
                    Progress.Speed = p.Speed;
                    Progress.Fps = p.Fps;
                    Progress.BitrateKbps = p.BitrateKbps;
                    Progress.Processed = p.Processed;
                    Progress.Remaining = p.Remaining;
                    Progress.StatusMessage = p.StatusMessage;
                    Progress.IsCompleted = p.IsCompleted;
                    Progress.HasError = p.HasError;
                    Progress.ErrorMessage = p.ErrorMessage;
                    Progress.CurrentFile = p.CurrentFile;
                });
            });

            var ok = await _ffmpeg.TrimAsync(FfmpegPath, request, gpu, prog, _cts.Token);
            StatusText = ok ? $"裁剪完成 → {OutputPath}" : "裁剪失败，详见日志";
        }
        catch (OperationCanceledException)
        {
            StatusText = "已取消";
        }
        catch (Exception ex)
        {
            App.LogCrash(ex, "TrimViewModel.StartAsync");
            StatusText = $"出错：{ex.Message}";
        }
        finally
        {
            IsBusy = false;
            _cts?.Dispose();
            _cts = null;
        }
    }

    [RelayCommand]
    private void Cancel() => _cts?.Cancel();

    /// <summary>删除上一轮的缩略图（keep 是本轮正在用的，不能删）。</summary>
    private static void DeleteThumb(string? old, string? keep)
    {
        if (string.IsNullOrEmpty(old) || string.Equals(old, keep, StringComparison.OrdinalIgnoreCase))
        {
            return;
        }

        try
        {
            if (File.Exists(old))
            {
                File.Delete(old);
            }
        }
        catch
        {
            // 删不掉（被占用 / 权限）不值得打断裁剪流程
        }
    }

    /// <summary>清掉目录下一天前的文件：这些都是本进程产生的临时缩略图。</summary>
    private static void PurgeOldThumbs(string dir)
    {
        try
        {
            var cutoff = DateTime.Now.AddDays(-1);
            foreach (var f in Directory.EnumerateFiles(dir, "*.jpg"))
            {
                if (File.GetLastWriteTime(f) < cutoff)
                {
                    File.Delete(f);
                }
            }
        }
        catch
        {
            // 清理失败不影响主流程
        }
    }

    /// <summary>供页面在"系统解码器放不出来"时告知 VM，切换为无预览模式。</summary>
    public void MarkPreviewFailed()
    {
        PreviewFailed = true;
        StatusText = "预览画面不可用（系统解码器不支持该文件），仍可使用时间轴与缩略图进行裁剪";
    }
}
