using System.Collections.ObjectModel;
using System.Diagnostics;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using MarukoBox.Helpers;
using MarukoBox.Models;
using MarukoBox.Services;

namespace MarukoBox.ViewModels;

/// <summary>
/// 视频页 ViewModel：管理批量队列、编码参数、实时进度与取消。
/// 采用 partial property 语法以符合 WinUI 3 AOT 规范。
/// </summary>
public partial class VideoViewModel : ObservableObject
{
    private readonly IConfigService _config = AppServices.Config;
    private readonly IGpuDetectionService _gpu = AppServices.GpuDetection;
    private CancellationTokenSource? _cts;

    /// <summary>编码参数（与控件双向绑定，编码时直接读取）。</summary>
    public EncodeSettings Settings { get; } = new();

    /// <summary>批量队列。</summary>
    public ObservableCollection<EncodeItem> Queue { get; } = new();

    /// <summary>当前用户级别（控制界面控件显示范围；重启生效）。</summary>
    public UserLevel UserLevel { get; } = UserLevels.Parse(AppServices.Config.Load().UserLevel);

    /// <summary>
    /// 编码器下拉选项：默认（小白）级别只保留 自动/NVENC/x264 三项，
    /// 高手及以上显示全部（含 AMF/QSV/NVENC H.264/x265）。构造函数中初始化。
    /// </summary>
    public ObservableCollection<EncoderOption> EncoderOptions { get; }

    public ObservableCollection<OptionEntry> RateControlOptions { get; } = new()
    {
        new() { Value = "vbr", Name = "VBR（动态码率）" },
        new() { Value = "cbr", Name = "CBR（恒定码率）" },
        new() { Value = "cqp", Name = "CQP（恒定质量）" },
        new() { Value = "2pass", Name = "2-Pass VBR（质量优先）" }
    };

    public ObservableCollection<OptionEntry> CpuModeOptions { get; } = new()
    {
        new() { Value = "crf", Name = "CRF（恒定质量）" },
        new() { Value = "2pass", Name = "2-Pass（指定码率）" },
        new() { Value = "custom", Name = "命令自定义（手动参数）" }
    };

    /// <summary>
    /// 「普通」级专属的恒定质量四档。值映射到 CRF（CPU）/ CQP（GPU），
    /// 数值越低质量越高：低=30、中=26、高=22、非常高=18。
    /// </summary>
    public ObservableCollection<OptionEntry> QualityPresetOptions { get; } = new()
    {
        new() { Value = "low",      Name = "低（体积最小）" },
        new() { Value = "medium",   Name = "中" },
        new() { Value = "high",     Name = "高（推荐）" },
        new() { Value = "veryhigh", Name = "非常高（体积最大）" }
    };

    /// <summary>质量档 → CRF/CQP 数值映射。</summary>
    private static int QualityPresetToValue(string preset) => preset switch
    {
        "low" => 30,
        "high" => 22,
        "veryhigh" => 18,
        _ => 26
    };

    public ObservableCollection<OptionEntry> ContainerOptions { get; } = new()
    {
        new() { Value = "mp4", Name = "MP4" },
        new() { Value = "mkv", Name = "MKV" },
        new() { Value = "mov", Name = "MOV" },
        new() { Value = "m4v", Name = "M4V" }
    };

    public ObservableCollection<OptionEntry> AudioOptions { get; } = new()
    {
        new() { Value = "copy", Name = "复制（不重编码）" },
        new() { Value = "aac128", Name = "AAC 128k" },
        new() { Value = "aac192", Name = "AAC 192k" },
        new() { Value = "opus128", Name = "Opus 128k" },
        new() { Value = "mute", Name = "静音" }
    };

    public ObservableCollection<OptionEntry> SubtitleOptions { get; } = new()
    {
        new() { Value = "copy", Name = "复制" },
        new() { Value = "drop", Name = "丢弃" }
    };

    public ObservableCollection<OptionEntry> ProfileOptions { get; } = new()
    {
        new() { Value = "main", Name = "Main" },
        new() { Value = "main10", Name = "Main10" },
        new() { Value = "rext", Name = "Rext" }
    };

    public ObservableCollection<string> GpuTuneOptions { get; } = new() { "hq", "ll", "ull", "lossless" };

    public ObservableCollection<string> CpuPresetOptions { get; } = new()
    {
        "ultrafast", "superfast", "veryfast", "faster", "fast",
        "medium", "slow", "slower", "veryslow", "placebo"
    };

    public ObservableCollection<OptionEntry> AfterOptions { get; } = new()
    {
        new() { Value = "none", Name = "无操作" },
        new() { Value = "shutdown", Name = "关机" },
        new() { Value = "hibernate", Name = "休眠" },
        new() { Value = "exit", Name = "退出程序" }
    };

    [ObservableProperty]
    public partial EncoderOption? SelectedEncoderOption { get; set; }

    [ObservableProperty]
    public partial string SelectedRateControl { get; set; } = "2pass";

    [ObservableProperty]
    public partial string SelectedCpuMode { get; set; } = "crf";

    /// <summary>「普通」级质量档（low/medium/high/veryhigh）；变更即写入 CRF 与 CQP 两条路径。</summary>
    [ObservableProperty]
    public partial string SelectedQualityPreset { get; set; } = "high";

    /// <summary>命令自定义模式的原始 ffmpeg 视频参数。</summary>
    [ObservableProperty]
    public partial string CustomArgs { get; set; } = string.Empty;

    [ObservableProperty]
    public partial string SelectedContainer { get; set; } = "mp4";

    /// <summary>输出文件夹。留空则输出到源文件同目录（当前路径）。</summary>
    [ObservableProperty]
    public partial string OutputDir { get; set; } = string.Empty;

    [ObservableProperty]
    public partial string SelectedAudio { get; set; } = "copy";

    [ObservableProperty]
    public partial string SelectedSubtitle { get; set; } = "copy";

    [ObservableProperty]
    public partial string SelectedProfile { get; set; } = "main";

    [ObservableProperty]
    public partial string SelectedGpuTune { get; set; } = "hq";

    [ObservableProperty]
    public partial string SelectedCpuPreset { get; set; } = "medium";

    [ObservableProperty]
    public partial string SelectedAfter { get; set; } = "none";

    [ObservableProperty]
    public partial GpuInfo GpuInfo { get; set; } = new();

    [ObservableProperty]
    public partial EncodeProgress Progress { get; set; } = new();

    [ObservableProperty]
    public partial bool IsEncoding { get; set; }

    [ObservableProperty]
    public partial bool IsDetecting { get; set; }

    [ObservableProperty]
    public partial int GpuDevice { get; set; }

    [ObservableProperty]
    public partial string StatusText { get; set; } = "就绪";

    /// <summary>是否保持原始分辨率；与 Settings.KeepOriginalResolution 保持同步，确保 ToggleSwitch 能触发 UI 刷新。</summary>
    [ObservableProperty]
    public partial bool KeepOriginalResolution { get; set; }

    // ---------- 派生可见性（驱动自适应参数面板） ----------

    /// <summary>
    /// 计算「实际会用到」的编码器是否为 GPU。
    /// 关键点：Auto 会按硬件检测结果解析为具体编码器（有 GPU 时解析为 NVENC），
    /// 面板必须跟随「解析后」的编码器，否则会出现「界面显示 CPU/CRF，实际却用 GPU 编码」的错位，
    /// 导致 CRF 等 CPU 参数被静默忽略（见 bug #2）。
    /// </summary>
    private bool EffectiveIsGpu()
    {
        var t = SelectedEncoderOption?.Type ?? EncoderType.Auto;
        return t == EncoderType.Auto ? this.GpuInfo.HasAnyGpuEncoder : t.IsGpuEncoder();
    }

    public bool ShowGpuPanel => EffectiveIsGpu();
    public bool ShowCpuPanel => !EffectiveIsGpu();
    public bool ShowGpuBitrate => ShowGpuPanel && SelectedRateControl != "cqp";
    public bool ShowGpuCqp => ShowGpuPanel && SelectedRateControl == "cqp";
    public bool ShowCpuBitrate => ShowCpuPanel && SelectedCpuMode != "crf";
    public bool ShowCpuCrf => ShowCpuPanel && SelectedCpuMode == "crf";
    public bool ShowCpuCustom => ShowCpuPanel && SelectedCpuMode == "custom";

    public VideoViewModel()
    {
        var all = new[]
        {
            new EncoderOption { Type = EncoderType.Auto, Name = "自动检测（推荐）" },
            new EncoderOption { Type = EncoderType.NvencHevc, Name = "NVIDIA NVENC (HEVC)" },
            new EncoderOption { Type = EncoderType.NvencH264, Name = "NVIDIA NVENC (H.264)" },
            new EncoderOption { Type = EncoderType.AmfHevc, Name = "AMD AMF (HEVC)" },
            new EncoderOption { Type = EncoderType.QsvHevc, Name = "Intel QSV (HEVC)" },
            new EncoderOption { Type = EncoderType.X264, Name = "x264 (CPU)" },
            new EncoderOption { Type = EncoderType.X265, Name = "x265 (CPU)" }
        };
        EncoderOptions = new ObservableCollection<EncoderOption>(
            UserLevel == UserLevel.Default
                ? all.Where(o => o.Type is EncoderType.Auto or EncoderType.NvencHevc or EncoderType.X264)
                : all);

        var config = _config.Load();
        GpuDevice = config.GpuDevice;

        SelectedEncoderOption = EncoderOptions.FirstOrDefault(o => o.Type.ToString() == config.DefaultEncoder)
                                ?? EncoderOptions[0];

        SyncToSettings();
        KeepOriginalResolution = Settings.KeepOriginalResolution;

        // 全局默认输出目录作为「输出文件夹」的初始值；用户清空即回退到源文件同目录。
        OutputDir = config.OutputDirectory;

        // 保持习惯：恢复上次会话的编码参数（覆盖上方默认值）
        if (config.RememberLastSession)
        {
            RestoreSession(ConfigService.LoadSession());
        }

        _ = DetectAsync();
    }

    /// <summary>把当前参数快照写入 session.json（由 MainWindow 关闭流程调用）。</summary>
    public void SaveSession() => ConfigService.SaveSession(CaptureSession());

    // ---------- 保持习惯：会话快照 ----------

    /// <summary>把当前视频页全部参数捕获为可持久化快照。</summary>
    public SessionState CaptureSession() => new()
    {
        Encoder = (SelectedEncoderOption?.Type ?? EncoderType.Auto).ToString(),
        RateControl = SelectedRateControl,
        CpuMode = SelectedCpuMode,
        QualityPreset = SelectedQualityPreset,
        Crf = Settings.Crf,
        Quality = Settings.Quality,
        BitrateKbps = Settings.BitrateKbps,
        MaxBitrateKbps = Settings.MaxBitrateKbps,
        BufferSizeKbps = Settings.BufferSizeKbps,
        GpuPreset = Settings.GpuPreset,
        GpuTune = SelectedGpuTune,
        Profile = SelectedProfile,
        CpuPreset = SelectedCpuPreset,
        CustomArgs = CustomArgs,
        KeepOriginalResolution = KeepOriginalResolution,
        Width = Settings.Width,
        Height = Settings.Height,
        Container = SelectedContainer,
        AudioMode = SelectedAudio,
        SubtitleMode = SelectedSubtitle,
        OutputDir = OutputDir,
        AfterCompletion = SelectedAfter
    };

    /// <summary>恢复会话快照；null（无快照/解析失败）时保持默认值不动。</summary>
    private void RestoreSession(SessionState? s)
    {
        if (s is null)
        {
            return;
        }

        if (Enum.TryParse<EncoderType>(s.Encoder, out var enc))
        {
            var opt = EncoderOptions.FirstOrDefault(o => o.Type == enc);
            if (opt is not null)
            {
                SelectedEncoderOption = opt;
            }
        }
        SelectedRateControl = s.RateControl;
        SelectedCpuMode = s.CpuMode;
        SelectedQualityPreset = s.QualityPreset;
        Settings.Crf = s.Crf;
        Settings.Quality = s.Quality;
        Settings.BitrateKbps = s.BitrateKbps;
        Settings.MaxBitrateKbps = s.MaxBitrateKbps;
        Settings.BufferSizeKbps = s.BufferSizeKbps;
        Settings.GpuPreset = s.GpuPreset;
        SelectedGpuTune = s.GpuTune;
        SelectedProfile = s.Profile;
        SelectedCpuPreset = s.CpuPreset;
        CustomArgs = s.CustomArgs;
        KeepOriginalResolution = s.KeepOriginalResolution;
        Settings.Width = s.Width;
        Settings.Height = s.Height;
        SelectedContainer = s.Container;
        SelectedAudio = s.AudioMode;
        SelectedSubtitle = s.SubtitleMode;
        OutputDir = s.OutputDir;
        SelectedAfter = s.AfterCompletion;
    }

    // ---------- 选择器变更：同步到 Settings 并刷新可见性 ----------

    partial void OnSelectedEncoderOptionChanged(EncoderOption? value)
    {
        Settings.Encoder = value?.Type ?? EncoderType.Auto;
        RaiseVisibility();
    }

    partial void OnSelectedRateControlChanged(string value)
    {
        Settings.RateControl = value;
        RaiseVisibility();
    }

    partial void OnSelectedCpuModeChanged(string value)
    {
        Settings.CpuMode = value;
        RaiseVisibility();
    }

    partial void OnSelectedQualityPresetChanged(string value)
    {
        // 同时写 CRF（CPU 路径）与 CQP Quality（GPU 路径），编码器解析到哪条都用同一档
        Settings.Crf = QualityPresetToValue(value);
        Settings.Quality = QualityPresetToValue(value);
    }

    partial void OnGpuInfoChanged(GpuInfo value)
    {
        // 硬件检测完成后，Auto 的解析结果可能从 CPU 变为 GPU，需刷新面板可见性。
        RaiseVisibility();
    }

    partial void OnCustomArgsChanged(string value) => Settings.CustomArgs = value;

    partial void OnSelectedContainerChanged(string value) => Settings.Container = value;
    partial void OnSelectedAudioChanged(string value) => Settings.AudioMode = value;
    partial void OnSelectedSubtitleChanged(string value) => Settings.SubtitleMode = value;
    partial void OnSelectedProfileChanged(string value) => Settings.Profile = value;
    partial void OnSelectedGpuTuneChanged(string value) => Settings.GpuTune = value;
    partial void OnSelectedCpuPresetChanged(string value) => Settings.CpuPreset = value;
    partial void OnSelectedAfterChanged(string value) => Settings.AfterCompletion = value;
    partial void OnGpuDeviceChanged(int value) => Settings.GpuDevice = value;
    partial void OnKeepOriginalResolutionChanged(bool value) => Settings.KeepOriginalResolution = value;

    private void RaiseVisibility()
    {
        OnPropertyChanged(nameof(ShowGpuPanel));
        OnPropertyChanged(nameof(ShowCpuPanel));
        OnPropertyChanged(nameof(ShowGpuBitrate));
        OnPropertyChanged(nameof(ShowGpuCqp));
        OnPropertyChanged(nameof(ShowCpuBitrate));
        OnPropertyChanged(nameof(ShowCpuCrf));
        OnPropertyChanged(nameof(ShowCpuCustom));
    }

    private void SyncToSettings()
    {
        Settings.Encoder = SelectedEncoderOption?.Type ?? EncoderType.Auto;
        Settings.RateControl = SelectedRateControl;
        Settings.CpuMode = SelectedCpuMode;
        Settings.CustomArgs = CustomArgs;
        Settings.Container = SelectedContainer;
        Settings.AudioMode = SelectedAudio;
        Settings.SubtitleMode = SelectedSubtitle;
        Settings.Profile = SelectedProfile;
        Settings.GpuTune = SelectedGpuTune;
        Settings.CpuPreset = SelectedCpuPreset;
        Settings.AfterCompletion = SelectedAfter;
        Settings.GpuDevice = GpuDevice;
    }

    /// <summary>检测硬件能力（供参数构建与编码器解析使用）。</summary>
    [RelayCommand]
    private async Task DetectAsync()
    {
        var ffmpegPath = _config.Load().FfmpegPath;
        if (string.IsNullOrWhiteSpace(ffmpegPath))
        {
            StatusText = "未设置 ffmpeg 路径，请在设置页配置";
            return;
        }

        IsDetecting = true;
        try
        {
            GpuInfo = await _gpu.DetectAsync(ffmpegPath);
            StatusText = GpuInfo.HasAnyGpuEncoder
                ? $"GPU 编码器可用：{GpuInfo.GpuName}"
                : "未检测到 GPU 编码器，将使用 CPU 编码";
        }
        catch (Exception ex)
        {
            StatusText = $"检测异常：{ex.Message}";
        }
        finally
        {
            IsDetecting = false;
        }
    }

    /// <summary>添加文件到队列（去重）。</summary>
    public void AddFiles(IEnumerable<string> paths)
    {
        foreach (var p in paths)
        {
            if (Queue.Any(i => i.InputPath == p))
            {
                continue;
            }

            Queue.Add(new EncodeItem { InputPath = p, StatusText = "等待中" });
        }

        if (Queue.Count > 0 && StatusText == "就绪")
        {
            StatusText = $"已加入 {Queue.Count} 个文件";
        }
    }

    /// <summary>从队列移除一项。</summary>
    [RelayCommand]
    private void RemoveItem(EncodeItem? item)
    {
        if (item is not null)
        {
            Queue.Remove(item);
        }
    }

    /// <summary>清空队列。</summary>
    [RelayCommand]
    private void ClearQueue()
    {
        if (!IsEncoding)
        {
            Queue.Clear();
            StatusText = "就绪";
        }
    }

    /// <summary>计算输出路径（用户指定目录优先，否则源文件同目录，文件名加 _encoded 后缀）。</summary>
    private string ComputeOutputPath(string inputPath)
    {
        var dir = OutputPathHelper.ResolveDir(Path.GetDirectoryName(inputPath) ?? ".", OutputDir);
        var name = Path.GetFileNameWithoutExtension(inputPath) + "_encoded." + SelectedContainer;
        return Path.Combine(dir, name);
    }

    /// <summary>通过文件夹选择器设定输出文件夹。</summary>
    [RelayCommand]
    private async Task BrowseOutputAsync()
    {
        var dir = await PickerHelper.PickFolderAsync();
        if (dir is not null)
        {
            OutputDir = dir;
        }
    }

    /// <summary>开始批量编码。</summary>
    [RelayCommand]
    private async Task StartAsync()
    {
        if (Queue.Count == 0 || IsEncoding)
        {
            return;
        }

        SyncToSettings();
        _cts = new CancellationTokenSource();
        IsEncoding = true;
        StatusText = "开始编码…";

        try
        {
            foreach (var item in Queue.ToList())
            {
                if (_cts.IsCancellationRequested)
                {
                    break;
                }

                item.IsEncoding = true;
                item.IsDone = false;
                item.HasError = false;
                item.StatusText = "编码中";
                item.Percent = 0;

                Settings.InputPath = item.InputPath;
                Settings.OutputPath = ComputeOutputPath(item.InputPath);

                // 注意：ffmpeg 的进度回调来自后台线程（stderr 读取线程 / stdoutTask 线程池线程）。
                // 必须经由 App.RunOnUiThread 封送回 UI 线程，否则跨线程更新可视化树会触发
                // 0xc000027b 致命快速失败（闪退）。
                // 同时，这里改为改写 Progress 对象的各个属性，而非整体替换引用——
                // Progress 是 [ObservableProperty]，整体赋值同一引用不会触发 PropertyChanged，
                // 会导致右侧进度数值永远不刷新。
                var prog = new Progress<EncodeProgress>(p =>
                {
                    App.RunOnUiThread(() =>
                    {
                        Progress.Percent = p.Percent;
                        Progress.Speed = p.Speed;
                        Progress.Fps = p.Fps;
                        Progress.Processed = p.Processed;
                        Progress.Remaining = p.Remaining;
                        Progress.CurrentFrame = p.CurrentFrame;
                        Progress.TotalFrames = p.TotalFrames;
                        Progress.BitrateKbps = p.BitrateKbps;
                        Progress.CurrentFile = p.CurrentFile;
                        Progress.StatusMessage = p.StatusMessage;
                        Progress.IsCompleted = p.IsCompleted;
                        Progress.HasError = p.HasError;
                        Progress.ErrorMessage = p.ErrorMessage;

                        item.Percent = p.Percent;
                        item.StatusText = p.StatusMessage;
                    });
                });

                var ok = await AppServices.Ffmpeg.EncodeAsync(Settings, GpuInfo, prog, _cts.Token);

                if (_cts.IsCancellationRequested)
                {
                    item.StatusText = "已取消";
                    item.IsEncoding = false;
                    break;
                }

                item.IsEncoding = false;
                item.IsDone = ok;
                item.HasError = !ok;
                item.Percent = ok ? 100 : item.Percent;
                item.StatusText = ok ? "完成" : "失败";
            }
        }
        catch (OperationCanceledException)
        {
            StatusText = "已取消";
        }
        catch (Exception ex)
        {
            // 任何异常都显式呈现给用户，而不是让进程崩溃。
            App.LogCrash(ex, "VideoViewModel.StartAsync");
            StatusText = $"编码出错：{ex.Message}";
        }
        finally
        {
            IsEncoding = false;
            _cts?.Dispose();
            _cts = null;
            StatusText = Queue.All(i => i.IsDone) ? "全部完成" : "编码结束";
        }
    }

    /// <summary>取消当前编码。</summary>
    [RelayCommand]
    private void Cancel()
    {
        _cts?.Cancel();
        StatusText = "正在取消…";
    }
}
