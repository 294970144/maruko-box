using System.Collections.ObjectModel;
using System.Diagnostics;
using System.IO;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using MarukoBox.Helpers;
using MarukoBox.Models;
using MarukoBox.Services;

namespace MarukoBox.ViewModels;

/// <summary>
/// 音频页 ViewModel：批量音频转码（编码器 / 比特率 / 声道 / 采样率）。
/// 进度回调统一经 <see cref="App.RunOnUiThread"/> 封送，避免跨线程更新可视化树。
/// </summary>
public partial class AudioViewModel : ObservableObject
{
    private readonly IFfmpegService _ffmpeg = AppServices.Ffmpeg;
    private readonly IConfigService _config = AppServices.Config;
    private CancellationTokenSource? _cts;

    /// <summary>批量队列（复用 EncodeItem，仅用到音视频无关字段）。</summary>
    public ObservableCollection<EncodeItem> Queue { get; } = new();

    /// <summary>队列是否非空，供 UI 显隐列表使用（避免把 int 误喂给 bool 转换器）。</summary>
    public bool HasQueue => Queue.Count > 0;

    /// <summary>当前用户级别（控制界面控件显示范围；重启生效）。</summary>
    public UserLevel UserLevel { get; } = UserLevels.Parse(AppServices.Config.Load().UserLevel);

    public AudioViewModel()
    {
        Queue.CollectionChanged += (_, _) => OnPropertyChanged(nameof(HasQueue));
        OutputDir = _config.Load().OutputDirectory;
    }

    public ObservableCollection<OptionEntry> CodecOptions { get; } = new()
    {
        new() { Value = "copy", Name = "复制（不重编码）" },
        new() { Value = "aac", Name = "AAC" },
        new() { Value = "opus", Name = "Opus" },
        new() { Value = "flac", Name = "FLAC（无损）" },
        new() { Value = "mp3", Name = "MP3（LAME）" }
    };

    public ObservableCollection<OptionEntry> BitrateOptions { get; } = new()
    {
        new() { Value = "0", Name = "自动" },
        new() { Value = "96", Name = "96 kbps" },
        new() { Value = "128", Name = "128 kbps" },
        new() { Value = "192", Name = "192 kbps" },
        new() { Value = "256", Name = "256 kbps" },
        new() { Value = "320", Name = "320 kbps" }
    };

    public ObservableCollection<OptionEntry> ChannelOptions { get; } = new()
    {
        new() { Value = "0", Name = "自动" },
        new() { Value = "1", Name = "单声道" },
        new() { Value = "2", Name = "立体声" },
        new() { Value = "6", Name = "5.1 环绕" },
        new() { Value = "8", Name = "7.1 全景声" }
    };

    public ObservableCollection<OptionEntry> SampleRateOptions { get; } = new()
    {
        new() { Value = "0", Name = "自动" },
        new() { Value = "22050", Name = "22050 Hz" },
        new() { Value = "44100", Name = "44100 Hz" },
        new() { Value = "48000", Name = "48000 Hz" },
        new() { Value = "96000", Name = "96000 Hz" }
    };

    [ObservableProperty]
    public partial string SelectedCodec { get; set; } = "copy";

    [ObservableProperty]
    public partial string SelectedBitrate { get; set; } = "128";

    [ObservableProperty]
    public partial string SelectedChannels { get; set; } = "0";

    [ObservableProperty]
    public partial string SelectedSampleRate { get; set; } = "0";

    /// <summary>输出文件夹。留空则输出到源文件同目录（当前路径）。</summary>
    [ObservableProperty]
    public partial string OutputDir { get; set; } = string.Empty;

    /// <summary>目标格式摘要，供队列每行展示当前生效的转码参数。</summary>
    public string TargetSummary
    {
        get
        {
            var codec = CodecOptions.FirstOrDefault(o => o.Value == SelectedCodec)?.Name ?? SelectedCodec;
            var parts = new List<string> { codec };
            if (SelectedBitrate != "0") parts.Add(SelectedBitrate + " kbps");
            var ch = ChannelOptions.FirstOrDefault(o => o.Value == SelectedChannels)?.Name;
            if (SelectedChannels != "0" && ch is not null) parts.Add(ch);
            if (SelectedSampleRate != "0") parts.Add(SelectedSampleRate + " Hz");
            return string.Join(" · ", parts);
        }
    }

    partial void OnSelectedCodecChanged(string value) => OnPropertyChanged(nameof(TargetSummary));
    partial void OnSelectedBitrateChanged(string value) => OnPropertyChanged(nameof(TargetSummary));
    partial void OnSelectedChannelsChanged(string value) => OnPropertyChanged(nameof(TargetSummary));
    partial void OnSelectedSampleRateChanged(string value) => OnPropertyChanged(nameof(TargetSummary));

    [ObservableProperty]
    public partial bool IsBusy { get; set; }

    [ObservableProperty]
    public partial string StatusText { get; set; } = "就绪";

    [ObservableProperty]
    public partial EncodeProgress Progress { get; set; } = new();

    private string FfmpegPath => _config.Load().FfmpegPath;

    /// <summary>添加文件到队列（去重）。</summary>
    [RelayCommand]
    private void AddFiles(IEnumerable<string> paths)
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

    [RelayCommand]
    private void RemoveItem(EncodeItem? item)
    {
        if (item is not null)
        {
            Queue.Remove(item);
        }
    }

    [RelayCommand]
    private void ClearQueue()
    {
        if (!IsBusy)
        {
            Queue.Clear();
            StatusText = "就绪";
        }
    }

    /// <summary>开始批量音频转码。</summary>
    [RelayCommand]
    private async Task StartAsync()
    {
        if (Queue.Count == 0 || IsBusy)
        {
            return;
        }

        var preset = new AudioPreset
        {
            Codec = SelectedCodec,
            BitrateKbps = int.TryParse(SelectedBitrate, out var b) ? b : 0,
            Channels = int.TryParse(SelectedChannels, out var c) ? c : 0,
            SampleRate = int.TryParse(SelectedSampleRate, out var r) ? r : 0
        };

        IsBusy = true;
        _cts = new CancellationTokenSource();
        StatusText = "开始转码…";

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
                item.StatusText = "转码中";
                item.Percent = 0;

                var outDir = OutputPathHelper.ResolveDir(Path.GetDirectoryName(item.InputPath) ?? ".", OutputDir);
                var ext = preset.Codec switch
                {
                    "aac" => ".m4a",
                    "opus" => ".opus",
                    "flac" => ".flac",
                    "mp3" => ".mp3",
                    _ => Path.GetExtension(item.InputPath)
                };
                var outPath = Path.Combine(outDir, Path.GetFileNameWithoutExtension(item.InputPath) + "_audio" + ext);

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
                        item.Percent = p.Percent;
                        item.StatusText = p.StatusMessage;
                    });
                });

                var ok = await _ffmpeg.TranscodeAudioAsync(FfmpegPath, item.InputPath, outPath, preset, prog, _cts.Token);

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
            App.LogCrash(ex, "AudioViewModel.StartAsync");
            StatusText = $"出错：{ex.Message}";
        }
        finally
        {
            IsBusy = false;
            _cts?.Dispose();
            _cts = null;
            StatusText = Queue.All(i => i.IsDone) ? "全部完成" : "转码结束";
        }
    }

    [RelayCommand]
    private void Cancel() => _cts?.Cancel();

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
}
