using System.Collections.ObjectModel;
using System.IO;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using MarukoBox.Helpers;
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

    // 数值输入统一 NumberBox（P0-B）：Value 为 double，直接双向绑定；
    // 缩放宽高用 NaN 表示「不缩放」——NumberBox 对 NaN 显示为空 + 占位符。
    [ObservableProperty]
    public partial double TimeSeconds { get; set; }

    [ObservableProperty]
    public partial double IntervalSeconds { get; set; } = 5;

    [ObservableProperty]
    public partial double ScaleWidth { get; set; } = double.NaN;

    [ObservableProperty]
    public partial double ScaleHeight { get; set; } = double.NaN;

    public ObservableCollection<OptionEntry> FormatOptions { get; } = new()
    {
        new() { Value = "png", Name = "PNG" },
        new() { Value = "jpg", Name = "JPG" }
    };

    [ObservableProperty]
    public partial string SelectedFormat { get; set; } = "png";

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

    private string FfmpegPath => _config.Load().ResolvedFfmpegPath;

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
            Format = SelectedFormat,
            TimeSeconds = TimeSeconds,
            IntervalSeconds = IntervalSeconds
        };

        // 缩放：NaN / 非正数 = 不缩放（NumberBox 空值即 NaN）
        if (!double.IsNaN(ScaleWidth) && ScaleWidth > 0)
        {
            opt.ScaleWidth = (int)Math.Round(ScaleWidth);
        }

        if (!double.IsNaN(ScaleHeight) && ScaleHeight > 0)
        {
            opt.ScaleHeight = (int)Math.Round(ScaleHeight);
        }

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

    /// <summary>当前生效的输出文件命名规则（每个 VM 实例只读一次配置，之后缓存）。</summary>
    private string? _namingRule;

    private string NamingRule =>
        _namingRule ??= OutputNaming.Normalize(_config.Load().OutputFileNameRule);

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

        var ext = SelectedImageFormat switch
        {
            "jpg" => ".jpg",
            "webp" => ".webp",
            _ => ".png"
        };
        var outPath = OutputNaming.BuildOutputPath(NamingRule, InputImage, null,
            new OutputNamingContext(InputImage, "conv", SelectedImageFormat, string.Empty, ext));

        IsBusy = true;
        _cts = new CancellationTokenSource();
        StatusText = "开始转码…";

        try
        {
            var ok = await _ffmpeg.ConvertImageAsync(FfmpegPath, InputImage, outPath, SelectedImageFormat, MakeProgress(), _cts.Token);
            StatusText = ok ? $"转码完成 → {outPath}" : "转码失败，详见日志";
            if (ok)
            {
                LastOutputPath = outPath;
            }
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

    /// <summary>打开输出位置：转码成功则定位文件，否则打开输出目录。</summary>
    [RelayCommand]
    private void OpenOutputFolder() =>
        ShellHelper.OpenOutputLocation(LastOutputPath, OutputDir, InputImage);

    /// <summary>最近一次成功产出的文件（仅转码单文件时记录；抽帧按目录定位）。</summary>
    [ObservableProperty]
    public partial string LastOutputPath { get; set; } = string.Empty;
}
