using System.Collections.ObjectModel;
using System.Diagnostics;
using System.IO;
using System.Text;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using MarukoBox.Converters;
using MarukoBox.Models;
using MarukoBox.Services;
using Microsoft.UI.Xaml.Controls;
using Windows.Storage.Pickers;

namespace MarukoBox.ViewModels;

/// <summary>
/// 设置页 ViewModel：负责 GPU/ffmpeg 能力检测、ffmpeg 路径选择、
/// 内置 ffmpeg 的检查更新与依赖检查、默认编码参数与配置持久化。
/// 采用 partial property 语法以符合 WinUI 3 规范。
/// </summary>
public partial class SettingsViewModel : ObservableObject
{
    private readonly IConfigService _config = AppServices.Config;
    private readonly IGpuDetectionService _gpu = AppServices.GpuDetection;
    private readonly IUpdateService _update = AppServices.Update;

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

    /// <summary>主题下拉选项（中文显示名；配置存储英文代码）。</summary>
    public ObservableCollection<string> ThemeOptions { get; } = new()
    {
        "跟随系统", "浅色模式", "深色模式"
    };

    /// <summary>更新渠道下拉选项（中文显示名；配置存储 mirror / github）。</summary>
    public ObservableCollection<string> UpdateChannelOptions { get; } = new()
    {
        "国内镜像站", "GitHub"
    };

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
    public partial string Theme { get; set; } = "跟随系统";

    [ObservableProperty]
    public partial string AfterCompletion { get; set; } = "none";

    [ObservableProperty]
    public partial int GpuDevice { get; set; }

    [ObservableProperty]
    public partial EncoderOption? SelectedEncoderOption { get; set; }

    [ObservableProperty]
    public partial string StatusMessage { get; set; } = "未检测";

    [ObservableProperty]
    public partial string LocalVersionText { get; set; } = string.Empty;

    [ObservableProperty]
    public partial bool IsCheckingUpdate { get; set; }

    [ObservableProperty]
    public partial bool IsDownloading { get; set; }

    [ObservableProperty]
    public partial double UpdateProgressPercent { get; set; }

    [ObservableProperty]
    public partial string UpdateStatusMessage { get; set; } = string.Empty;

    [ObservableProperty]
    public partial string SelectedUpdateChannel { get; set; } = "国内镜像站";

    public SettingsViewModel()
    {
        var config = _config.Load();
        FfmpegPath = config.FfmpegPath;
        OutputDirectory = config.OutputDirectory;
        Theme = CodeToThemeDisplay(config.Theme);
        AfterCompletion = config.AfterCompletion;
        GpuDevice = config.GpuDevice;
        SelectedUpdateChannel = UpdateService.ChannelDisplayName(UpdateService.ParseChannel(config.UpdateChannel));

        SelectedEncoderOption = EncoderOptions.FirstOrDefault(o => o.Type.ToString() == config.DefaultEncoder)
                                ?? EncoderOptions[0];

        var localVer = _update.GetLocalVersion();
        LocalVersionText = string.IsNullOrEmpty(localVer)
            ? "未内置（使用外部 ffmpeg）"
            : $"当前内置版本：{localVer}";

        _ = DetectAsync();
    }

    // ---------- 主题显示名 <-> 配置代码 ----------

    private static string ThemeToCode(string display) => display switch
    {
        "浅色模式" => "Light",
        "深色模式" => "Dark",
        _ => "System"
    };

    private static string CodeToThemeDisplay(string code) => code switch
    {
        "Light" => "浅色模式",
        "Dark" => "深色模式",
        _ => "跟随系统"
    };

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
            Theme = ThemeToCode(Theme),
            OutputDirectory = OutputDirectory,
            AfterCompletion = AfterCompletion,
            GpuDevice = GpuDevice,
            // 中文显示名 -> 存储代码（非 "GitHub" 一律视为国内镜像）
            UpdateChannel = SelectedUpdateChannel == "GitHub" ? "github" : "mirror"
        };
        _config.Save(config);
        StatusMessage = "配置已保存（主题需重启应用后生效）";
    }

    // ---------- 检查更新 ----------

    /// <summary>
    /// 检查内置 ffmpeg 更新：查询当前渠道最新版本，与本地比较，
    /// 有新版时弹窗确认并下载安装（整目录替换），完成后自动重新检测。
    /// </summary>
    [RelayCommand]
    private async Task CheckUpdateAsync()
    {
        if (IsCheckingUpdate)
        {
            return;
        }

        IsCheckingUpdate = true;
        IsDownloading = false;
        UpdateProgressPercent = 0;
        UpdateStatusMessage = "正在检查更新…";
        try
        {
            var channel = SelectedUpdateChannel == "GitHub" ? UpdateChannel.GitHub : UpdateChannel.Mirror;
            var latest = await _update.GetLatestVersionAsync(channel);
            var local = _update.GetLocalVersion();

            if (string.Equals(local, latest.Tag, StringComparison.OrdinalIgnoreCase))
            {
                UpdateStatusMessage = $"已是最新版本（{local}）";
                return;
            }

            var localText = string.IsNullOrEmpty(local) ? "未安装内置 ffmpeg" : local;
            var sizeHint = channel == UpdateChannel.GitHub ? "约 34 MB" : "约 34 MB";
            var confirmed = await ConfirmUpdateAsync(localText, latest.Tag, sizeHint);
            if (!confirmed)
            {
                UpdateStatusMessage = "已取消更新";
                return;
            }

            IsDownloading = true;
            var channelName = UpdateService.ChannelDisplayName(channel);
            var progress = new Progress<double>(p => App.RunOnUiThread(() =>
            {
                UpdateProgressPercent = Math.Round(p, 1);
                UpdateStatusMessage = $"正在从{channelName}下载 {latest.Tag}… {p:F0}%";
            }));

            await _update.DownloadAndInstallAsync(latest.DownloadUrl, latest.Tag, progress);

            // 内置版本已替换：重新解析生效路径（内置优先）并刷新能力检测
            FfmpegPath = _config.Load().FfmpegPath;
            LocalVersionText = $"当前内置版本：{latest.Tag}";
            UpdateStatusMessage = $"更新完成，内置 ffmpeg 已升级到 {latest.Tag}";

            await DetectAsync();
        }
        catch (Exception ex)
        {
            UpdateStatusMessage = $"更新失败：{ex.Message}";
        }
        finally
        {
            IsCheckingUpdate = false;
            IsDownloading = false;
            UpdateProgressPercent = 0;
        }
    }

    /// <summary>弹窗确认是否下载安装新版本。</summary>
    private async Task<bool> ConfirmUpdateAsync(string localVersion, string newVersion, string sizeHint)
    {
        try
        {
            var dialog = new ContentDialog
            {
                XamlRoot = App.Window.Content.XamlRoot,
                Title = "发现新版本",
                Content = $"当前内置 ffmpeg：{localVersion}\n最新版本：{newVersion}\n\n" +
                          $"是否下载并安装？（{sizeHint}，安装期间请勿进行编码任务）",
                PrimaryButtonText = "下载并安装",
                CloseButtonText = "取消",
                DefaultButton = ContentDialogButton.Primary
            };
            var result = await dialog.ShowAsync();
            return result == ContentDialogResult.Primary;
        }
        catch
        {
            // 对话框不可用（如窗口尚未就绪）时直接视为确认，避免功能被卡死
            return true;
        }
    }

    // ---------- 检查依赖 ----------

    /// <summary>
    /// 检查运行依赖：ffmpeg / ffprobe 存在性与位置、内置版本标记、GPU 编码能力。
    /// 结果汇总写入更新状态消息。
    /// </summary>
    [RelayCommand]
    private async Task CheckDependenciesAsync()
    {
        UpdateStatusMessage = "正在检查依赖…";
        var sb = new StringBuilder();

        // 1) ffmpeg 生效路径
        var ffmpegPath = _config.Load().FfmpegPath;
        if (string.IsNullOrWhiteSpace(ffmpegPath) || !File.Exists(ffmpegPath))
        {
            sb.AppendLine("✗ ffmpeg：未找到（请使用「检查更新」安装内置版，或手动设置路径）");
            UpdateStatusMessage = sb.ToString().TrimEnd();
            return;
        }
        FfmpegPath = ffmpegPath; // 同步 UI 属性，确保随后的能力检测使用同一份路径
        sb.AppendLine($"✓ ffmpeg：{ffmpegPath}");

        // 2) ffprobe（与 ffmpeg 同目录）
        var probePath = Path.Combine(Path.GetDirectoryName(ffmpegPath) ?? ".", "ffprobe.exe");
        sb.AppendLine(File.Exists(probePath)
            ? $"✓ ffprobe：{probePath}"
            : "△ ffprobe：未找到（媒体信息相关功能不可用）");

        // 3) 内置版本标记
        var ver = _update.GetLocalVersion();
        sb.AppendLine(string.IsNullOrEmpty(ver)
            ? "△ 内置版本标记：无（使用外部 ffmpeg）"
            : $"✓ 内置版本：{ver}");

        // 4) GPU 编码能力快检
        await DetectAsync();
        sb.AppendLine(GpuInfo.DetectionSucceeded
            ? (GpuInfo.HasAnyGpuEncoder
                ? $"✓ GPU 编码：{GpuInfo.GpuName}"
                : "△ GPU 编码：未检测到可用编码器，将使用 CPU")
            : $"✗ GPU 检测失败：{GpuInfo.ErrorMessage}");

        UpdateStatusMessage = sb.ToString().TrimEnd();
    }
}
