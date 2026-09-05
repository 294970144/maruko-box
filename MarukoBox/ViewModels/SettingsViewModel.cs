using System.Collections.ObjectModel;
using System.Diagnostics;
using System.IO;
using System.Text;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using MarukoBox.Models;
using MarukoBox.Services;
using Microsoft.UI.Xaml.Controls;
using Windows.Storage.Pickers;

namespace MarukoBox.ViewModels;

/// <summary>
/// 设置页 ViewModel：负责软件更新检查（仅 GitHub）、依赖（ffmpeg）体检与内置 ffmpeg
/// 更新（含 NVENC API 门槛）、GPU 能力检测、用户级别/主题/默认编码参数与配置持久化。
/// 「检查更新」只查软件自身新版本；ffmpeg 相关检测归「检查依赖」。
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

    /// <summary>用户级别下拉选项（中文显示名；配置存储英文代码）。</summary>
    public ObservableCollection<string> UserLevelOptions { get; } = new()
    {
        "默认", "高手", "程序员"
    };

    /// <summary>完成后动作下拉选项。</summary>
    public ObservableCollection<string> AfterCompletionOptions { get; } = new()
    {
        "none", "shutdown", "hibernate", "exit"
    };

    /// <summary>当前用户级别（控制各页面控件显示范围；切换后重启生效）。</summary>
    public UserLevel UserLevel { get; } = UserLevels.Parse(AppServices.Config.Load().UserLevel);

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

    /// <summary>检查更新卡片的副标题：当前软件版本。</summary>
    [ObservableProperty]
    public partial string LocalVersionText { get; set; } = string.Empty;

    /// <summary>硬件能力卡片中显示的内置 ffmpeg 版本（jellyfin-ffmpeg 标记）。</summary>
    [ObservableProperty]
    public partial string BundledVersionText { get; set; } = string.Empty;

    [ObservableProperty]
    public partial bool IsCheckingUpdate { get; set; }

    [ObservableProperty]
    public partial bool IsDownloading { get; set; }

    [ObservableProperty]
    public partial double UpdateProgressPercent { get; set; }

    [ObservableProperty]
    public partial string UpdateStatusMessage { get; set; } = string.Empty;

    /// <summary>用户级别下拉的当前选中项（中文显示名）。</summary>
    [ObservableProperty]
    public partial string SelectedUserLevel { get; set; } = "默认";

    public SettingsViewModel()
    {
        var config = _config.Load();
        FfmpegPath = config.FfmpegPath;
        OutputDirectory = config.OutputDirectory;
        Theme = CodeToThemeDisplay(config.Theme);
        AfterCompletion = config.AfterCompletion;
        GpuDevice = config.GpuDevice;
        SelectedUserLevel = UserLevels.ToDisplay(UserLevels.Parse(config.UserLevel));

        SelectedEncoderOption = EncoderOptions.FirstOrDefault(o => o.Type.ToString() == config.DefaultEncoder)
                                ?? EncoderOptions[0];

        LocalVersionText = $"当前版本：{UpdateService.GetAppVersionStatic()}";
        BundledVersionText = GetBundledDisplayText();

        _ = DetectAsync();
    }

    /// <summary>内置 ffmpeg 版本的显示文案。</summary>
    private static string GetBundledDisplayText()
    {
        var ver = ConfigService.GetBundledVersion();
        return string.IsNullOrEmpty(ver) ? "无（使用外部 ffmpeg）" : ver;
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

    /// <summary>重新检测硬件能力（含 ffmpeg 运行版本与内置版本刷新）。</summary>
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
            BundledVersionText = GetBundledDisplayText();
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
            UserLevel = UserLevels.DisplayToCode(SelectedUserLevel)
        };
        _config.Save(config);
        StatusMessage = "配置已保存（主题与用户级别需重启应用后生效）";
    }

    // ---------- 检查更新（软件自身，仅 GitHub） ----------

    /// <summary>
    /// 检查 MarukoBox 软件更新：查询 GitHub 最新 Release 与当前版本比较，
    /// 有新版时弹窗确认并下载安装包，完成后启动安装程序并退出应用。
    /// 不检查 ffmpeg 依赖——那归「检查依赖」。
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
        UpdateStatusMessage = "正在检查软件更新…";
        try
        {
            var latest = await _update.GetLatestAppReleaseAsync();
            var current = _update.GetAppVersion();

            // 版本级比较（容忍 v 前缀差异），而非字符串相等
            if (UpdateService.CompareVersions(current, latest.Version) >= 0)
            {
                UpdateStatusMessage = $"已是最新版本（{current}）";
                return;
            }

            var confirmed = await ConfirmAppUpdateAsync(current, latest.Tag);
            if (!confirmed)
            {
                UpdateStatusMessage = "已取消更新";
                return;
            }

            IsDownloading = true;
            var progress = new Progress<double>(p => App.RunOnUiThread(() =>
            {
                UpdateProgressPercent = Math.Round(p, 1);
                UpdateStatusMessage = $"正在下载 {latest.Tag} 安装包… {p:F0}%";
            }));

            var installer = await _update.DownloadAppInstallerAsync(
                latest.DownloadUrl, latest.Version, progress);

            UpdateStatusMessage = $"安装包已就绪，正在启动安装程序（{latest.Tag}）…";
            await Task.Delay(600); // 让用户看到状态再退出

            Process.Start(new ProcessStartInfo
            {
                FileName = installer,
                UseShellExecute = true
            });
            App.Current.Exit();
        }
        catch (Exception ex)
        {
            UpdateStatusMessage = $"检查更新失败：{ex.Message}";
        }
        finally
        {
            IsCheckingUpdate = false;
            IsDownloading = false;
            UpdateProgressPercent = 0;
        }
    }

    /// <summary>弹窗确认是否下载安装新软件版本。</summary>
    private async Task<bool> ConfirmAppUpdateAsync(string currentVersion, string newTag)
    {
        try
        {
            var dialog = new ContentDialog
            {
                XamlRoot = App.Window.Content.XamlRoot,
                Title = "发现新版本",
                Content = $"当前版本：{currentVersion}\n最新版本：{newTag}\n\n" +
                          "是否下载并安装？（约 100 MB，下载完成后将启动安装程序，本应用将退出）",
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

    // ---------- 检查依赖（ffmpeg 体检 + 内置 ffmpeg 更新） ----------

    /// <summary>
    /// 检查运行依赖：ffmpeg / ffprobe 存在性与位置、内置版本标记、GPU 编码能力，
    /// 并查询内置 ffmpeg 新版（GitHub）——通过 NVENC API 门槛判定后提示更新。
    /// </summary>
    [RelayCommand]
    private async Task CheckDependenciesAsync()
    {
        if (IsCheckingUpdate)
        {
            return;
        }

        IsCheckingUpdate = true;
        UpdateStatusMessage = "正在检查依赖…";
        var sb = new StringBuilder();

        try
        {
            // 1) ffmpeg 生效路径
            var ffmpegPath = _config.Load().FfmpegPath;
            if (string.IsNullOrWhiteSpace(ffmpegPath) || !File.Exists(ffmpegPath))
            {
                sb.AppendLine("✗ ffmpeg：未找到（可在下方安装内置版，或手动设置路径）");
            }
            else
            {
                FfmpegPath = ffmpegPath; // 同步 UI 属性，确保随后的能力检测使用同一份路径
                sb.AppendLine($"✓ ffmpeg：{ffmpegPath}");

                // 2) ffprobe（与 ffmpeg 同目录）
                var probePath = Path.Combine(Path.GetDirectoryName(ffmpegPath) ?? ".", "ffprobe.exe");
                sb.AppendLine(File.Exists(probePath)
                    ? $"✓ ffprobe：{probePath}"
                    : "△ ffprobe：未找到（媒体信息相关功能不可用）");
            }

            // 3) 内置版本标记
            var local = _update.GetLocalVersion();
            BundledVersionText = string.IsNullOrEmpty(local) ? "无（使用外部 ffmpeg）" : local;
            sb.AppendLine(string.IsNullOrEmpty(local)
                ? "△ 内置 ffmpeg：无（使用外部 ffmpeg）"
                : $"✓ 内置 ffmpeg：{local}");

            // 4) GPU 编码能力快检（同时刷新 ffmpeg 运行版本）
            await DetectAsync();
            sb.AppendLine(GpuInfo.DetectionSucceeded
                ? (GpuInfo.HasAnyGpuEncoder
                    ? $"✓ GPU 编码：{GpuInfo.GpuName}"
                    : "△ GPU 编码：未检测到可用编码器，将使用 CPU")
                : $"✗ GPU 检测失败：{GpuInfo.ErrorMessage}");

            // 5) 内置 ffmpeg 新版检查（GitHub；NVENC API ≥13.1 门槛）
            try
            {
                var latest = await _update.GetLatestFfmpegAsync();
                if (string.IsNullOrEmpty(local) || UpdateService.CompareVersions(local, latest.Tag) < 0)
                {
                    var offer = _update.ShouldOfferFfmpegUpdate(GpuInfo, latest.Tag);
                    if (!offer.Offer)
                    {
                        // N 卡驱动过旧（NVENC API <13.1）：不推送，说明原因
                        sb.AppendLine($"△ 内置 ffmpeg 新版 {latest.Tag}：{offer.BlockReason}");
                    }
                    else
                    {
                        UpdateStatusMessage = sb.ToString().TrimEnd();
                        var confirmed = await ConfirmFfmpegUpdateAsync(local, latest.Tag);
                        if (confirmed)
                        {
                            await InstallFfmpegUpdateAsync(latest);
                        }
                        else
                        {
                            UpdateStatusMessage = sb.ToString().TrimEnd() + "\n已取消更新内置 ffmpeg";
                        }
                        return;
                    }
                }
                else
                {
                    sb.AppendLine($"✓ 内置 ffmpeg 已是最新（{local}）");
                }
            }
            catch (Exception ex)
            {
                sb.AppendLine($"△ 检查 ffmpeg 新版失败：{ex.Message}");
            }

            UpdateStatusMessage = sb.ToString().TrimEnd();
        }
        catch (Exception ex)
        {
            UpdateStatusMessage = $"依赖检查失败：{ex.Message}";
        }
        finally
        {
            IsCheckingUpdate = false;
            IsDownloading = false;
            UpdateProgressPercent = 0;
        }
    }

    /// <summary>弹窗确认是否下载安装新版内置 ffmpeg。</summary>
    private async Task<bool> ConfirmFfmpegUpdateAsync(string? localVersion, string newVersion)
    {
        try
        {
            var dialog = new ContentDialog
            {
                XamlRoot = App.Window.Content.XamlRoot,
                Title = "发现新版本",
                Content = $"当前内置 ffmpeg：{(string.IsNullOrEmpty(localVersion) ? "未安装" : localVersion)}\n" +
                          $"最新版本：{newVersion}\n\n" +
                          "是否下载并安装？（约 67 MB，安装期间请勿进行编码任务）",
                PrimaryButtonText = "下载并安装",
                CloseButtonText = "取消",
                DefaultButton = ContentDialogButton.Primary
            };
            var result = await dialog.ShowAsync();
            return result == ContentDialogResult.Primary;
        }
        catch
        {
            return true;
        }
    }

    /// <summary>下载并安装新版内置 ffmpeg（中断安全整目录替换），完成后重新检测。</summary>
    private async Task InstallFfmpegUpdateAsync(RemoteFfmpegVersion latest)
    {
        IsDownloading = true;
        UpdateProgressPercent = 0;
        var progress = new Progress<double>(p => App.RunOnUiThread(() =>
        {
            UpdateProgressPercent = Math.Round(p, 1);
            UpdateStatusMessage = $"正在下载 ffmpeg {latest.Tag}… {p:F0}%";
        }));

        await _update.DownloadAndInstallAsync(latest.DownloadUrl, latest.Tag, progress);

        // 内置版本已替换：重新解析生效路径（内置优先）并刷新能力检测
        FfmpegPath = _config.Load().FfmpegPath;
        BundledVersionText = latest.Tag;
        UpdateStatusMessage = $"内置 ffmpeg 已更新到 {latest.Tag}";

        await DetectAsync();
    }
}
