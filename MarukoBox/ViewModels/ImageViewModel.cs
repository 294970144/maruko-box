using System.Collections.ObjectModel;
using System.Globalization;
using System.IO;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using MarukoBox.Models;
using MarukoBox.Services;

namespace MarukoBox.ViewModels;

/// <summary>
/// 图片页 ViewModel：视频抽帧（单张 / 序列帧）+ 图片转码。
/// 进度回调统一经 <see cref="App.RunOnUiThread"/> 封送，避免跨线程更新可视化树。
/// </summary>
public partial class ImageViewModel : ObservableObject
{
    private readonly IFfmpegService _ffmpeg = AppServices.Ffmpeg;
    private readonly IConfigService _config = AppServices.Config;
    private CancellationTokenSource? _cts;

    // ---------- 抽帧区 ----------
    [ObservableProperty]
    public partial string InputVideo { get; set; } = string.Empty;

    public ObservableCollection<OptionEntry> ModeOptions { get; } = new()
    {
        new() { Value = "single", Name = "单张截图（指定时间）" },
        new() { Value = "interval", Name = "序列帧（每隔 N 秒）" }
    };

    [ObservableProperty]
    public partial string SelectedMode { get; set; } = "single";

    [ObservableProperty]
    public partial string TimeSecondsText { get; set; } = "0";

    [ObservableProperty]
    public partial string IntervalSecondsText { get; set; } = "5";

    public ObservableCollection<OptionEntry> FormatOptions { get; } = new()
    {
        new() { Value = "png", Name = "PNG" },
        new() { Value = "jpg", Name = "JPG" }
    };

    [ObservableProperty]
    public partial string SelectedFormat { get; set; } = "png";

    [ObservableProperty]
    public partial string ScaleWidthText { get; set; } = string.Empty;

    [ObservableProperty]
    public partial string ScaleHeightText { get; set; } = string.Empty;

    [ObservableProperty]
    public partial string OutputDir { get; set; } = string.Empty;

    // ---------- 转码区 ----------
    [ObservableProperty]
    public partial string InputImage { get; set; } = string.Empty;

    public ObservableCollection<OptionEntry> ImageFormatOptions { get; } = new()
    {
        new() { Value = "png", Name = "PNG" },
        new() { Value = "jpg", Name = "JPG" },
        new() { Value = "webp", Name = "WebP" }
    };

    [ObservableProperty]
    public partial string SelectedImageFormat { get; set; } = "png";

    // ---------- 共享状态 ----------
    [ObservableProperty]
    public partial bool IsBusy { get; set; }

    [ObservableProperty]
    public partial string StatusText { get; set; } = "请选择源视频或图片";

    [ObservableProperty]
    public partial EncodeProgress Progress { get; set; } = new();

    private string FfmpegPath => _config.Load().FfmpegPath;

    [RelayCommand]
    private async Task ExtractFramesAsync()
    {
        if (IsBusy)
        {
            return;
        }

        if (string.IsNullOrWhiteSpace(InputVideo) || !File.Exists(InputVideo))
        {
            StatusText = "请先选择有效的源视频";
            return;
        }

        if (string.IsNullOrWhiteSpace(OutputDir) || !Directory.Exists(OutputDir))
        {
            OutputDir = Path.GetDirectoryName(InputVideo) ?? ".";
        }

        var opt = new FrameExtractOptions
        {
            Mode = SelectedMode == "interval" ? FrameMode.Interval : FrameMode.Single,
            Format = SelectedFormat
        };

        if (double.TryParse(TimeSecondsText, NumberStyles.Any, CultureInfo.InvariantCulture, out var t)) opt.TimeSeconds = t;
        if (double.TryParse(IntervalSecondsText, NumberStyles.Any, CultureInfo.InvariantCulture, out var iv)) opt.IntervalSeconds = iv;
        if (int.TryParse(ScaleWidthText, out var w)) opt.ScaleWidth = w;
        if (int.TryParse(ScaleHeightText, out var h)) opt.ScaleHeight = h;

        IsBusy = true;
        _cts = new CancellationTokenSource();
        StatusText = "开始抽帧…";

        try
        {
            var ok = await _ffmpeg.ExtractFramesAsync(FfmpegPath, InputVideo, OutputDir, opt, MakeProgress(), _cts.Token);
            StatusText = ok ? $"抽帧完成 → {OutputDir}" : "抽帧失败，详见日志";
        }
        catch (Exception ex)
        {
            App.LogCrash(ex, "ImageViewModel.ExtractFramesAsync");
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
    private async Task ConvertImageAsync()
    {
        if (IsBusy)
        {
            return;
        }

        if (string.IsNullOrWhiteSpace(InputImage) || !File.Exists(InputImage))
        {
            StatusText = "请先选择有效的源图片";
            return;
        }

        var dir = Path.GetDirectoryName(InputImage) ?? ".";
        var baseName = Path.GetFileNameWithoutExtension(InputImage);
        var ext = SelectedImageFormat switch
        {
            "jpg" => ".jpg",
            "webp" => ".webp",
            _ => ".png"
        };
        var outPath = Path.Combine(dir, baseName + "_conv" + ext);

        IsBusy = true;
        _cts = new CancellationTokenSource();
        StatusText = "开始转码…";

        try
        {
            var ok = await _ffmpeg.ConvertImageAsync(FfmpegPath, InputImage, outPath, SelectedImageFormat, MakeProgress(), _cts.Token);
            StatusText = ok ? $"转码完成 → {outPath}" : "转码失败，详见日志";
        }
        catch (Exception ex)
        {
            App.LogCrash(ex, "ImageViewModel.ConvertImageAsync");
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
