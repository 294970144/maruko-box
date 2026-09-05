using System.Collections.ObjectModel;
using System.IO;
using System.Linq;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using MarukoBox.Helpers;
using MarukoBox.Models;
using MarukoBox.Services;

namespace MarukoBox.ViewModels;

/// <summary>
/// 字幕页 ViewModel：抽取（从视频提取字幕流）/ 嵌入（视频 + 外部字幕合并）/ 转换（srt↔ass↔vtt）。
/// 进度回调统一经 <see cref="App.RunOnUiThread"/> 封送，避免跨线程更新可视化树。
/// </summary>
public partial class SubtitleViewModel : ObservableObject
{
    private readonly IFfmpegService _ffmpeg = AppServices.Ffmpeg;
    private readonly IConfigService _config = AppServices.Config;
    private CancellationTokenSource? _cts;

    // ---------- 抽取区 ----------
    [ObservableProperty]
    public partial string ExtractVideo { get; set; } = string.Empty;

    public ObservableCollection<MediaStreamInfo> SubtitleStreams { get; } = new();

    public bool HasSubtitleStreams => SubtitleStreams.Count > 0;

    // ---------- 嵌入区 ----------
    [ObservableProperty]
    public partial string EmbedVideo { get; set; } = string.Empty;

    [ObservableProperty]
    public partial string EmbedSubtitle { get; set; } = string.Empty;

    // ---------- 转换区 ----------
    [ObservableProperty]
    public partial string ConvertInput { get; set; } = string.Empty;

    public ObservableCollection<OptionEntry> TargetFormatOptions { get; } = new()
    {
        new() { Value = "srt", Name = "SRT" },
        new() { Value = "ass", Name = "ASS" },
        new() { Value = "vtt", Name = "WebVTT" }
    };

    [ObservableProperty]
    public partial string SelectedTargetFormat { get; set; } = "ass";

    /// <summary>输出文件夹。留空则输出到源文件同目录（当前路径）。</summary>
    [ObservableProperty]
    public partial string OutputDir { get; set; } = string.Empty;

    // ---------- 共享状态 ----------
    [ObservableProperty]
    public partial bool IsBusy { get; set; }

    [ObservableProperty]
    public partial string StatusText { get; set; } = "字幕工具：抽取 / 嵌入 / 转换";

    [ObservableProperty]
    public partial EncodeProgress Progress { get; set; } = new();

    private string FfmpegPath => _config.Load().FfmpegPath;

    public SubtitleViewModel()
    {
        SubtitleStreams.CollectionChanged += (_, _) => OnPropertyChanged(nameof(HasSubtitleStreams));
        OutputDir = _config.Load().OutputDirectory;
    }

    [RelayCommand]
    private async Task AnalyzeAsync()
    {
        if (IsBusy)
        {
            return;
        }

        if (string.IsNullOrWhiteSpace(ExtractVideo) || !File.Exists(ExtractVideo))
        {
            StatusText = "请先选择有效的视频文件";
            return;
        }

        IsBusy = true;
        StatusText = "正在分析字幕流…";

        try
        {
            var all = await _ffmpeg.ProbeStreamsAsync(FfmpegPath, ExtractVideo);
            SubtitleStreams.Clear();
            foreach (var s in all.Where(s => s.Type == StreamType.Subtitle))
            {
                s.IsSelected = true;
                SubtitleStreams.Add(s);
            }

            StatusText = SubtitleStreams.Count > 0
                ? $"发现 {SubtitleStreams.Count} 条字幕流"
                : "未发现字幕流";
            OnPropertyChanged(nameof(HasSubtitleStreams));
        }
        catch (Exception ex)
        {
            App.LogCrash(ex, "SubtitleViewModel.AnalyzeAsync");
            StatusText = $"出错：{ex.Message}";
        }
        finally
        {
            IsBusy = false;
        }
    }

    [RelayCommand]
    private async Task ExtractAsync()
    {
        if (IsBusy)
        {
            return;
        }

        var selected = SubtitleStreams.Where(s => s.IsSelected).ToList();
        if (selected.Count == 0)
        {
            StatusText = "请至少选择一条字幕流";
            return;
        }

        if (string.IsNullOrWhiteSpace(ExtractVideo))
        {
            StatusText = "请先选择视频";
            return;
        }

        var outDir = OutputPathHelper.ResolveDir(Path.GetDirectoryName(ExtractVideo) ?? ".", OutputDir);

        IsBusy = true;
        _cts = new CancellationTokenSource();
        StatusText = "开始抽取字幕…";

        try
        {
            var (ok, done, total) = await _ffmpeg.ExtractStreamsAsync(
                FfmpegPath, ExtractVideo, outDir, selected, MakeProgress(), _cts.Token);
            StatusText = ok ? $"抽取完成 {done}/{total} → {outDir}" : "抽取失败，详见日志";
        }
        catch (Exception ex)
        {
            App.LogCrash(ex, "SubtitleViewModel.ExtractAsync");
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
    private async Task EmbedAsync()
    {
        if (IsBusy)
        {
            return;
        }

        if (string.IsNullOrWhiteSpace(EmbedVideo) || !File.Exists(EmbedVideo))
        {
            StatusText = "请选择视频";
            return;
        }

        if (string.IsNullOrWhiteSpace(EmbedSubtitle) || !File.Exists(EmbedSubtitle))
        {
            StatusText = "请选择字幕文件";
            return;
        }

        var dir = OutputPathHelper.ResolveDir(Path.GetDirectoryName(EmbedVideo) ?? ".", OutputDir);
        var baseName = Path.GetFileNameWithoutExtension(EmbedVideo);
        var outVideo = Path.Combine(dir, baseName + "_withsub.mkv");

        IsBusy = true;
        _cts = new CancellationTokenSource();
        StatusText = "开始嵌入字幕…";

        try
        {
            var ok = await _ffmpeg.EmbedSubtitleAsync(FfmpegPath, EmbedVideo, EmbedSubtitle, outVideo, MakeProgress(), _cts.Token);
            StatusText = ok ? $"嵌入完成 → {outVideo}" : "嵌入失败，详见日志";
        }
        catch (Exception ex)
        {
            App.LogCrash(ex, "SubtitleViewModel.EmbedAsync");
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
    private async Task ConvertAsync()
    {
        if (IsBusy)
        {
            return;
        }

        if (string.IsNullOrWhiteSpace(ConvertInput) || !File.Exists(ConvertInput))
        {
            StatusText = "请选择字幕文件";
            return;
        }

        var dir = OutputPathHelper.ResolveDir(Path.GetDirectoryName(ConvertInput) ?? ".", OutputDir);
        var baseName = Path.GetFileNameWithoutExtension(ConvertInput);
        var ext = SelectedTargetFormat switch
        {
            "srt" => ".srt",
            "vtt" => ".vtt",
            _ => ".ass"
        };
        var outSub = Path.Combine(dir, baseName + "_conv" + ext);

        IsBusy = true;
        _cts = new CancellationTokenSource();
        StatusText = "开始转换字幕…";

        try
        {
            var ok = await _ffmpeg.ConvertSubtitleAsync(FfmpegPath, ConvertInput, outSub, MakeProgress(), _cts.Token);
            StatusText = ok ? $"转换完成 → {outSub}" : "转换失败，详见日志";
        }
        catch (Exception ex)
        {
            App.LogCrash(ex, "SubtitleViewModel.ConvertAsync");
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

    [RelayCommand]
    private void SelectAll()
    {
        foreach (var s in SubtitleStreams)
        {
            s.IsSelected = true;
        }
    }

    [RelayCommand]
    private void SelectNone()
    {
        foreach (var s in SubtitleStreams)
        {
            s.IsSelected = false;
        }
    }

    private Progress<EncodeProgress> MakeProgress() => new(p =>
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
}
