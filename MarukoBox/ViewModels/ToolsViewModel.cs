using System.IO;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using MarukoBox.Models;
using MarukoBox.Services;

namespace MarukoBox.ViewModels;

/// <summary>
/// 工具页 ViewModel：媒体信息查看（总时长 + 流列表）。
/// 探测为同步短任务，异常就地记录，不阻塞 UI 线程。
/// </summary>
public partial class ToolsViewModel : ObservableObject
{
    private readonly IFfmpegService _ffmpeg = AppServices.Ffmpeg;
    private readonly IConfigService _config = AppServices.Config;

    [ObservableProperty]
    public partial string InputFile { get; set; } = string.Empty;

    [ObservableProperty]
    public partial MediaFileInfo Info { get; set; } = new();

    /// <summary>是否已解析出流信息，供 UI 显隐列表使用。</summary>
    public bool HasInfo => Info is { Streams.Count: > 0 };

    [ObservableProperty]
    public partial bool IsBusy { get; set; }

    [ObservableProperty]
    public partial string StatusText { get; set; } = "请选择媒体文件查看信息";

    private string FfmpegPath => _config.Load().FfmpegPath;

    [RelayCommand]
    private async Task ProbeAsync()
    {
        if (IsBusy)
        {
            return;
        }

        if (string.IsNullOrWhiteSpace(InputFile) || !File.Exists(InputFile))
        {
            StatusText = "请先选择有效的媒体文件";
            return;
        }

        IsBusy = true;
        StatusText = "正在分析…";

        try
        {
            var info = await _ffmpeg.ProbeInfoAsync(FfmpegPath, InputFile);
            Info = info;
            OnPropertyChanged(nameof(HasInfo));
            StatusText = info.Streams.Count > 0
                ? $"分析完成：{info.DurationText} · {info.StreamCountText}"
                : "未解析到流信息";
        }
        catch (Exception ex)
        {
            App.LogCrash(ex, "ToolsViewModel.ProbeAsync");
            StatusText = $"出错：{ex.Message}";
        }
        finally
        {
            IsBusy = false;
        }
    }
}
