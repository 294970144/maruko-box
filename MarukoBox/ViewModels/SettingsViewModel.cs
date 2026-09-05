using System.Collections.ObjectModel;
using System.Diagnostics;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using MarukoBox.Converters;
using MarukoBox.Models;
using MarukoBox.Services;
using Windows.Storage.Pickers;

namespace MarukoBox.ViewModels;

/// <summary>
/// 设置页 ViewModel：负责 GPU/ffmpeg 能力检测、ffmpeg 路径选择、
/// 默认编码参数与配置持久化。采用 partial property 语法以符合 WinUI 3 AOT 规范。
/// </summary>
public partial class SettingsViewModel : ObservableObject
{
    private readonly IConfigService _config = AppServices.Config;
    private readonly IGpuDetectionService _gpu = AppServices.GpuDetection;

    /// <summary>编码器下拉选项（与 EncoderType 枚举一致）。</summary>
    public ObservableCollection<EncoderOption> EncoderOptions { get; } = new()
    {
        new() { Type = EncoderType.Auto, Name = "自动检测（推荐）" },
        new() { Type = EncoderType.NvencHevc, Name = "NVIDIA NVENC (HEVC)" },
        new() { Type = EncoderType.NvencH264, Name = "NVIDIA NVENC (H.264)" },
        new() { Type = EncoderType.AmfHevc, Name = "AMD AMF (HEVC)" },
        new() { Type = EncoderType.QsvHevc, Name = "Intel QSV (HEVC)" },
        new() { Type = EncoderType.X264, Name = "x264 (CPU)" },
        new() { Type = EncoderType.X265, Name = "x265 (CPU)" }
    };

    /// <summary>主题下拉选项。</summary>
    public ObservableCollection<string> ThemeOptions { get; } = new() { "System", "Light", "Dark" };

    /// <summary>完成后动作下拉选项。</summary>
    public ObservableCollection<string> AfterCompletionOptions { get; } = new()
    {
        "none", "shutdown", "hibernate", "exit"
    };

    [ObservableProperty]
    public partial GpuInfo GpuInfo { get; set; } = new();

    [ObservableProperty]
    public partial bool IsDetecting { get; set; }

    [ObservableProperty]
    public partial string FfmpegPath { get; set; } = string.Empty;

    [ObservableProperty]
    public partial string OutputDirectory { get; set; } = string.Empty;

    [ObservableProperty]
    public partial string Theme { get; set; } = "System";

    [ObservableProperty]
    public partial string AfterCompletion { get; set; } = "none";

    [ObservableProperty]
    public partial int GpuDevice { get; set; }

    [ObservableProperty]
    public partial EncoderOption? SelectedEncoderOption { get; set; }

    [ObservableProperty]
    public partial string StatusMessage { get; set; } = "未检测";

    public SettingsViewModel()
    {
        var config = _config.Load();
        FfmpegPath = config.FfmpegPath;
        OutputDirectory = config.OutputDirectory;
        Theme = config.Theme;
        AfterCompletion = config.AfterCompletion;
        GpuDevice = config.GpuDevice;

        SelectedEncoderOption = EncoderOptions.FirstOrDefault(o => o.Type.ToString() == config.DefaultEncoder)
                                ?? EncoderOptions[0];

        _ = DetectAsync();
    }

    /// <summary>重新检测硬件能力。</summary>
    [RelayCommand]
    private async Task DetectAsync()
    {
        if (string.IsNullOrWhiteSpace(FfmpegPath))
        {
            StatusMessage = "请先设置 ffmpeg.exe 路径";
            return;
        }

        IsDetecting = true;
        StatusMessage = "检测中…";
        try
        {
            var info = await _gpu.DetectAsync(FfmpegPath);
            GpuInfo = info;
            StatusMessage = info.DetectionSucceeded
                ? (info.HasAnyGpuEncoder ? "检测完成，GPU 编码器可用" : "检测完成，将使用 CPU 编码")
                : $"检测失败：{info.ErrorMessage}";
        }
        catch (Exception ex)
        {
            StatusMessage = $"检测异常：{ex.Message}";
        }
        finally
        {
            IsDetecting = false;
        }
    }

    /// <summary>通过文件选择器定位 ffmpeg.exe。</summary>
    [RelayCommand]
    private async Task BrowseFfmpegAsync()
    {
        var picker = new FileOpenPicker
        {
            ViewMode = PickerViewMode.List,
            SuggestedStartLocation = PickerLocationId.ComputerFolder
        };
        picker.FileTypeFilter.Add(".exe");

        WinRT.Interop.InitializeWithWindow.Initialize(picker, App.WindowHandle);

        var file = await picker.PickSingleFileAsync();
        if (file is not null)
        {
            FfmpegPath = file.Path;
            await DetectAsync();
        }
    }

    /// <summary>通过文件夹选择器设定默认输出目录。</summary>
    [RelayCommand]
    private async Task BrowseOutputAsync()
    {
        var picker = new FolderPicker();
        picker.FileTypeFilter.Add("*");
        WinRT.Interop.InitializeWithWindow.Initialize(picker, App.WindowHandle);

        var folder = await picker.PickSingleFolderAsync();
        if (folder is not null)
        {
            OutputDirectory = folder.Path;
        }
    }

    /// <summary>保存配置到磁盘。</summary>
    [RelayCommand]
    private void Save()
    {
        var config = new AppConfig
        {
            FfmpegPath = FfmpegPath,
            DefaultEncoder = SelectedEncoderOption?.Type.ToString() ?? "Auto",
            Theme = Theme,
            OutputDirectory = OutputDirectory,
            AfterCompletion = AfterCompletion,
            GpuDevice = GpuDevice
        };
        _config.Save(config);
        StatusMessage = "配置已保存";
    }
}
