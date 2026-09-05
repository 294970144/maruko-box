using System.Collections.ObjectModel;
using System.Diagnostics;
using System.IO;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using MarukoBox.Models;
using MarukoBox.Services;

namespace MarukoBox.ViewModels;

/// <summary>
/// 抽取页 ViewModel：分析源文件轨道、选择并抽取视频 / 音频 / 字幕流。
/// 进度回调统一经 <see cref="App.RunOnUiThread"/> 封送，避免跨线程更新可视化树。
/// </summary>
public partial class ExtractViewModel : ObservableObject
{
    private readonly IFfmpegService _ffmpeg = AppServices.Ffmpeg;
    private readonly IConfigService _config = AppServices.Config;
    private CancellationTokenSource? _cts;

    /// <summary>源文件路径。</summary>
    [ObservableProperty]
    public partial string SourcePath { get; set; } = string.Empty;

    /// <summary>输出目录。</summary>
    [ObservableProperty]
    public partial string OutputDir { get; set; } = string.Empty;

    /// <summary>解析出的轨道列表（绑定到轨道表）。</summary>
    public ObservableCollection<MediaStreamInfo> Streams { get; } = new();

    /// <summary>是否有轨道，供 UI 显隐列表使用（避免把 int 误喂给 bool 转换器）。</summary>
    public bool HasStreams => Streams.Count > 0;

    public ExtractViewModel()
    {
        Streams.CollectionChanged += (_, _) => OnPropertyChanged(nameof(HasStreams));
    }

    /// <summary>是否正在忙碌（分析 / 抽取中）。</summary>
    [ObservableProperty]
    public partial bool IsBusy { get; set; }

    /// <summary>状态文本。</summary>
    [ObservableProperty]
    public partial string StatusText { get; set; } = "请选择源文件后点击「分析轨道」";

    /// <summary>实时进度（绑定到进度条与统计区）。</summary>
    [ObservableProperty]
    public partial EncodeProgress Progress { get; set; } = new();

    /// <summary>已完成轨道数（聚合进度展示）。</summary>
    [ObservableProperty]
    public partial int DoneCount { get; set; }

    /// <summary>总轨道数。</summary>
    [ObservableProperty]
    public partial int TotalCount { get; set; }

    /// <summary>解析出的 ffmpeg 路径（按需从配置读取）。</summary>
    private string FfmpegPath => _config.Load().FfmpegPath;

    /// <summary>分析源文件轨道。</summary>
    [RelayCommand]
    private async Task AnalyzeAsync()
    {
        if (string.IsNullOrWhiteSpace(SourcePath) || !File.Exists(SourcePath))
        {
            StatusText = "源文件不存在，请重新选择";
            return;
        }

        IsBusy = true;
        StatusText = "正在分析轨道…";
        try
        {
            var streams = await _ffmpeg.ProbeStreamsAsync(FfmpegPath, SourcePath);
            Streams.Clear();
            foreach (var s in streams)
            {
                s.IsSelected = true; // 默认全选
                Streams.Add(s);
            }

            TotalCount = Streams.Count;
            DoneCount = 0;
            StatusText = Streams.Count > 0
                ? $"共发现 {Streams.Count} 条轨道，已默认全选"
                : "未发现可抽取轨道";
        }
        catch (Exception ex)
        {
            App.LogCrash(ex, "ExtractViewModel.AnalyzeAsync");
            StatusText = $"分析失败：{ex.Message}";
        }
        finally
        {
            IsBusy = false;
        }
    }

    /// <summary>开始抽取选中的轨道。</summary>
    [RelayCommand]
    private async Task ExtractAsync()
    {
        var selected = Streams.Where(s => s.IsSelected).ToList();
        if (selected.Count == 0)
        {
            StatusText = "请至少选择一条轨道";
            return;
        }

        if (string.IsNullOrWhiteSpace(OutputDir) || !Directory.Exists(OutputDir))
        {
            // 未指定输出目录时默认输出到源文件同目录。
            OutputDir = Path.GetDirectoryName(SourcePath) ?? ".";
        }

        IsBusy = true;
        _cts = new CancellationTokenSource();
        StatusText = "开始抽取…";

        try
        {
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

            var (success, done, total) =
                await _ffmpeg.ExtractStreamsAsync(FfmpegPath, SourcePath, OutputDir, selected, prog, _cts.Token);

            DoneCount = done;
            TotalCount = total;
            StatusText = success
                ? $"抽取完成：{done}/{total} 条轨道 → {OutputDir}"
                : "抽取失败，详见日志";
        }
        catch (OperationCanceledException)
        {
            StatusText = "已取消";
        }
        catch (Exception ex)
        {
            App.LogCrash(ex, "ExtractViewModel.ExtractAsync");
            StatusText = $"抽取出错：{ex.Message}";
        }
        finally
        {
            IsBusy = false;
            _cts?.Dispose();
            _cts = null;
        }
    }

    /// <summary>取消当前抽取。</summary>
    [RelayCommand]
    private void Cancel() => _cts?.Cancel();

    /// <summary>全选轨道。</summary>
    [RelayCommand]
    private void SelectAll()
    {
        foreach (var s in Streams) s.IsSelected = true;
    }

    /// <summary>清空选择。</summary>
    [RelayCommand]
    private void SelectNone()
    {
        foreach (var s in Streams) s.IsSelected = false;
    }
}
