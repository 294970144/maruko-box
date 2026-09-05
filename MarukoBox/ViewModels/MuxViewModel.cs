using System.Collections.ObjectModel;
using System.IO;
using System.Linq;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using MarukoBox.Models;
using MarukoBox.Services;

namespace MarukoBox.ViewModels;

/// <summary>
/// 封装页 ViewModel：将视频 / 音频 / 字幕文件无损合并（-c copy）到指定容器。
/// 进度回调统一经 <see cref="App.RunOnUiThread"/> 封送，避免跨线程更新可视化树。
/// </summary>
public partial class MuxViewModel : ObservableObject
{
    private readonly IFfmpegService _ffmpeg = AppServices.Ffmpeg;
    private readonly IConfigService _config = AppServices.Config;
    private CancellationTokenSource? _cts;

    /// <summary>参与合并的输入列表（视频 / 音频 / 字幕）。</summary>
    public ObservableCollection<MuxInput> Inputs { get; } = new();

    /// <summary>是否有输入，供 UI 显隐列表使用。</summary>
    public bool HasInputs => Inputs.Count > 0;

    public MuxViewModel()
    {
        Inputs.CollectionChanged += (_, _) => OnPropertyChanged(nameof(HasInputs));
    }

    /// <summary>可选容器格式。</summary>
    public ObservableCollection<OptionEntry> ContainerOptions { get; } = new()
    {
        new() { Value = "mp4", Name = "MP4" },
        new() { Value = "mkv", Name = "MKV" },
        new() { Value = "mov", Name = "MOV" },
        new() { Value = "webm", Name = "WebM" }
    };

    [ObservableProperty]
    public partial string SelectedContainer { get; set; } = "mp4";

    /// <summary>输出文件路径（含后缀）。未设置时开始封装会自动生成。</summary>
    [ObservableProperty]
    public partial string OutputPath { get; set; } = string.Empty;

    [ObservableProperty]
    public partial bool IsBusy { get; set; }

    [ObservableProperty]
    public partial string StatusText { get; set; } = "请添加视频 / 音频 / 字幕文件后开始封装";

    [ObservableProperty]
    public partial EncodeProgress Progress { get; set; } = new();

    private string FfmpegPath => _config.Load().FfmpegPath;

    [RelayCommand]
    private void AddVideo(IEnumerable<string> paths) => AddInputs(paths);

    [RelayCommand]
    private void AddAudio(IEnumerable<string> paths) => AddInputs(paths);

    [RelayCommand]
    private void AddSubtitle(IEnumerable<string> paths) => AddInputs(paths);

    private void AddInputs(IEnumerable<string> paths)
    {
        foreach (var p in paths)
        {
            if (!Inputs.Any(i => i.FilePath == p))
            {
                Inputs.Add(new MuxInput(p));
            }
        }

        if (Inputs.Count > 0)
        {
            StatusText = $"已添加 {Inputs.Count} 个输入";
        }
    }

    [RelayCommand]
    private void RemoveInput(MuxInput? item)
    {
        if (item is not null)
        {
            Inputs.Remove(item);
        }
    }

    [RelayCommand]
    private void ClearInputs()
    {
        if (!IsBusy)
        {
            Inputs.Clear();
            StatusText = "已清空输入列表";
        }
    }

    /// <summary>开始封装（无损合并）。</summary>
    [RelayCommand]
    private async Task StartAsync()
    {
        if (Inputs.Count == 0 || IsBusy)
        {
            return;
        }

        // 输出路径未设置时，基于第一个输入自动生成（同目录 + _muxed + 容器后缀）。
        if (string.IsNullOrWhiteSpace(OutputPath))
        {
            var first = Inputs[0];
            var dir = Path.GetDirectoryName(first.FilePath) ?? ".";
            var baseName = Path.GetFileNameWithoutExtension(first.FilePath);
            OutputPath = Path.Combine(dir, baseName + "_muxed" + ContainerExt(SelectedContainer));
        }

        var dirOut = Path.GetDirectoryName(OutputPath);
        if (!string.IsNullOrEmpty(dirOut) && !Directory.Exists(dirOut))
        {
            StatusText = "输出目录不存在，请重新选择输出路径";
            return;
        }

        IsBusy = true;
        _cts = new CancellationTokenSource();
        StatusText = "开始封装…";

        try
        {
            var inputs = Inputs.Select(i => i.FilePath).ToList();
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

            var ok = await _ffmpeg.RemuxAsync(FfmpegPath, inputs, SelectedContainer, OutputPath, prog, _cts.Token);
            StatusText = ok ? $"封装完成 → {OutputPath}" : "封装失败，详见日志";
        }
        catch (OperationCanceledException)
        {
            StatusText = "已取消";
        }
        catch (Exception ex)
        {
            App.LogCrash(ex, "MuxViewModel.StartAsync");
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

    private static string ContainerExt(string container) => container switch
    {
        "mkv" => ".mkv",
        "mov" => ".mov",
        "webm" => ".webm",
        _ => ".mp4"
    };
}
