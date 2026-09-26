using System.Collections.ObjectModel;
using System.Diagnostics;
using System.IO;
using System.Text;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using MarukoBox.Helpers;
using MarukoBox.Models;
using MarukoBox.Services;
using Microsoft.UI.Xaml;
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

    /// <summary>
    /// 用户级别下拉选项（v1.3.0 起改为「普通 / 高级 / 专家」；v1.2.0 及更早的
    /// 「默认 / 高手 / 程序员」仍被 <see cref="UserLevels.DisplayToCode"/> 兼容映射）。
    /// </summary>
    public ObservableCollection<string> UserLevelOptions { get; } = new()
    {
        "普通", "高级", "专家"
    };

    /// <summary>
    /// 输出文件命名规则下拉选项（预设组合，见 <see cref="OutputNaming.Options"/>）。
    /// 作用于全部页面的输出文件名（视频 / 图片 / 音频 / 封装 / 字幕）。
    /// </summary>
    public ObservableCollection<string> OutputFileNameRuleOptions { get; } = new(OutputNaming.Options);

    /// <summary>软件更新源下拉选项（GitHub 主源 / CN 国内镜像）。</summary>
    public ObservableCollection<string> UpdateSourceOptions { get; } = new()
    {
        "GitHub", "CN"
    };

    /// <summary>
    /// 「程序员」专列的 ffmpeg 版本列表（来自 GitHub releases 全量）。
    /// 仅当用户级别 = 专家时显示；点过「检查依赖」或单独的「刷新版本列表」后填充。
    /// 程序员可点击任意行强制安装，绕过 NVENC 驱动门槛。
    /// </summary>
    public ObservableCollection<FfmpegReleaseRow> FfmpegReleaseRows { get; } = new();

    /// <summary>
    /// ffmpeg 版本列表的整体可见性 = 用户级别 = 专家 且 已点过「刷新版本列表」。
    /// （「检查依赖」也会一并刷新，故需求里"点过检查更新"等价覆盖。）
    /// </summary>
    [ObservableProperty]
    public partial bool IsFfmpegListVisible { get; set; }

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
    public partial int GpuDevice { get; set; }

    [ObservableProperty]
    public partial EncoderOption? SelectedEncoderOption { get; set; }

    [ObservableProperty]
    public partial string StatusMessage { get; set; } = string.Empty;

    /// <summary>
    /// 「未保存」状态：任意被追踪的设置属性与上次保存基线不一致时为 true，
    /// 驱动悬浮保存按钮上的红点提示。基线在构造完成与每次保存成功后重新拍摄。
    /// </summary>
    [ObservableProperty]
    public partial bool IsDirty { get; set; }

    /// <summary>是否允许编辑自定义 ffmpeg 路径 = 无内置 ffmpeg。有内置时输入框置灰禁用。</summary>
    [ObservableProperty]
    public partial bool CanEditFfmpegPath { get; set; } = true;

    /// <summary>ffmpeg 路径校验错误文案（防抖后的 File.Exists 轻校验；空串 = 无错误）。</summary>
    [ObservableProperty]
    public partial string FfmpegPathError { get; set; } = string.Empty;

    /// <summary>ffmpeg 路径是否校验失败（驱动输入框红色边框）。</summary>
    [ObservableProperty]
    public partial bool IsFfmpegPathInvalid { get; set; }

    /// <summary>多 GPU 设备序号的动态上限 = 检测到的设备数 - 1；nvidia-smi 不可用时回退 3。</summary>
    [ObservableProperty]
    public partial int GpuDeviceMax { get; set; } = 3;

    /// <summary>默认输出目录是否非空（控制「打开」按钮可用性）。</summary>
    [ObservableProperty]
    public partial bool HasOutputDirectory { get; set; }

    /// <summary>悬浮保存按钮文字；保存成功后短暂显示「已保存 ✓」作为就近反馈。</summary>
    [ObservableProperty]
    public partial string SaveButtonText { get; set; } = "保存配置";

    /// <summary>检查更新卡片的副标题：当前软件版本。</summary>
    [ObservableProperty]
    public partial string LocalVersionText { get; set; } = string.Empty;

    /// <summary>硬件能力卡片中显示的内置 ffmpeg 版本（jellyfin-ffmpeg 标记）。</summary>
    [ObservableProperty]
    public partial string BundledVersionText { get; set; } = string.Empty;

    /// <summary>
    /// 是否存在内置 ffmpeg。存在时，设置页里手动指定的 ffmpeg.exe 路径<b>不会生效</b>
    /// ——路径解析优先级是「内置 → 手动 → PATH」。
    /// 不把这个事实讲清楚，用户会以为「设置了却不生效」是个 bug。
    /// </summary>
    [ObservableProperty]
    public partial bool HasBundledFfmpeg { get; set; }

    /// <summary>ffmpeg 路径卡片下的说明文案：被内置覆盖时明确告知。</summary>
    [ObservableProperty]
    public partial string FfmpegPathHint { get; set; } = string.Empty;

    [ObservableProperty]
    public partial bool IsCheckingUpdate { get; set; }

    [ObservableProperty]
    public partial bool IsDownloading { get; set; }

    [ObservableProperty]
    public partial double UpdateProgressPercent { get; set; }

    [ObservableProperty]
    public partial string UpdateStatusMessage { get; set; } = string.Empty;

    /// <summary>更新/依赖检查结果是否可见（驱动结果 InfoBar 的 IsOpen）。</summary>
    [ObservableProperty]
    public partial bool HasUpdateStatus { get; set; }

    /// <summary>更新/依赖检查结果的严重级别（四级状态体系：成功/警告/错误/信息）。</summary>
    [ObservableProperty]
    public partial InfoBarSeverity UpdateStatusSeverity { get; set; } = InfoBarSeverity.Informational;

    /// <summary>用户级别下拉的当前选中项（中文显示名）。</summary>
    [ObservableProperty]
    public partial string SelectedUserLevel { get; set; } = "普通";

    /// <summary>本次保存前记录的「主题」「用户级别」原值，用于在重启弹窗中判定是否真的需要重启。</summary>
    private string _savedThemeBeforeSave = string.Empty;
    private string _savedUserLevelBeforeSave = string.Empty;

    /// <summary>保持习惯：退出记住视频页参数、下次启动恢复（即时生效，无需重启）。</summary>
    [ObservableProperty]
    public partial bool RememberLastSession { get; set; } = true;

    /// <summary>当前选中的输出文件命名规则（中文显示名，直接持久化）。</summary>
    [ObservableProperty]
    public partial string SelectedOutputFileNameRule { get; set; } = OutputNaming.DefaultRule;

    /// <summary>命名规则的示例预览（让用户直观看到会生成什么文件名）。</summary>
    [ObservableProperty]
    public partial string OutputFileNamePreview { get; set; } = OutputNaming.Preview(OutputNaming.DefaultRule);

    /// <summary>当前选中的软件更新源（中文显示名；配置存储代码）。</summary>
    [ObservableProperty]
    public partial string SelectedUpdateSource { get; set; } = "GitHub";

    public SettingsViewModel()
    {
        var config = _config.Load();
        FfmpegPath = config.FfmpegPath;
        OutputDirectory = config.OutputDirectory;
        Theme = CodeToThemeDisplay(config.Theme);
        GpuDevice = config.GpuDevice;
        RememberLastSession = config.RememberLastSession;
        SelectedUserLevel = UserLevels.ToDisplay(UserLevels.Parse(config.UserLevel));
        SelectedOutputFileNameRule = OutputNaming.Normalize(config.OutputFileNameRule);
        OutputFileNamePreview = OutputNaming.Preview(SelectedOutputFileNameRule);
        SelectedUpdateSource = UpdateSourceCodeToDisplay(config.UpdateSource);

        // 记录"未保存前"的实际值，Save() 比对时使用——
        // 避免 UI 控件绑定初期就把原值覆写成新值，导致重启判定永远为"未变"。
        _savedThemeBeforeSave = Theme;
        _savedUserLevelBeforeSave = SelectedUserLevel;

        SelectedEncoderOption = EncoderOptions.FirstOrDefault(o => o.Type.ToString() == config.DefaultEncoder)
                                ?? EncoderOptions[0];

        LocalVersionText = $"当前版本：{UpdateService.GetAppVersionStatic()}";
        BundledVersionText = GetBundledDisplayText();

        // 【B3 修复】让「手动路径被内置覆盖」这件事在 UI 上可见，
        // 而不是让用户填了半天发现不生效。
        HasBundledFfmpeg = ConfigService.HasBundledFfmpeg;
        CanEditFfmpegPath = !HasBundledFfmpeg;
        FfmpegPathHint = HasBundledFfmpeg
            ? $"当前使用内置 ffmpeg {BundledVersionText}，下方路径已禁用；仅当内置 ffmpeg 不存在时才会使用手动路径。"
            : "留空则自动探测（内置 → PATH）；填写后仅在内置 ffmpeg 不存在时生效。";

        // 输出目录非空 → 「打开」按钮可用（置灰语义：空目录时打开数据目录会让人困惑）。
        HasOutputDirectory = !string.IsNullOrWhiteSpace(OutputDirectory);

        // 脏状态追踪：订阅自身属性变更，与基线快照比对驱动「未保存」红点。
        // 基线必须在构造（含所有绑定初始化）完成后拍摄——绑定初期会触发一串
        // PropertyChanged，若先订阅后初始化会把原值覆写误判成「已修改」。
        PropertyChanged += OnSelfPropertyChanged;
        _baseline = CaptureSnapshot();

        _ = DetectAsync();
    }

    // ---------- 脏状态追踪（未保存红点） ----------

    /// <summary>保存基线快照：与当前值不一致即为「有未保存修改」。</summary>
    private SettingSnapshot _baseline;

    /// <summary>ffmpeg 路径校验的防抖令牌：击键级轻校验不应连环触发。</summary>
    private CancellationTokenSource? _pathValidateCts;

    /// <summary>
    /// 全量设置快照（值类型/字符串，record 相等性比较）。
    /// 注意编码器以 Type 代码参与比较——EncoderOption 是引用类型，直接比较会恒等。
    /// </summary>
    private sealed record SettingSnapshot(
        string FfmpegPath,
        string OutputDirectory,
        string Theme,
        int GpuDevice,
        string EncoderCode,
        string UserLevelDisplay,
        bool RememberLastSession,
        string OutputRule,
        string UpdateSourceDisplay);

    private SettingSnapshot CaptureSnapshot() => new(
        FfmpegPath,
        OutputDirectory,
        Theme,
        GpuDevice,
        SelectedEncoderOption?.Type.ToString() ?? string.Empty,
        SelectedUserLevel,
        RememberLastSession,
        SelectedOutputFileNameRule,
        SelectedUpdateSource);

    /// <summary>被追踪的设置属性变更 → 重新计算 IsDirty。</summary>
    private void OnSelfPropertyChanged(object? sender, System.ComponentModel.PropertyChangedEventArgs e)
    {
        if (e.PropertyName is nameof(FfmpegPath) or nameof(OutputDirectory) or nameof(Theme)
            or nameof(GpuDevice) or nameof(SelectedEncoderOption) or nameof(SelectedUserLevel)
            or nameof(RememberLastSession) or nameof(SelectedOutputFileNameRule)
            or nameof(SelectedUpdateSource))
        {
            IsDirty = CaptureSnapshot() != _baseline;
        }
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
        // 检测状态统一由顶部横幅承载（Summary + DetectionSeverity）；
        // 底部 StatusMessage 只保留错误通道，避免成功态文案与横幅重复。
        StatusMessage = string.Empty;
        try
        {
            var info = await _gpu.DetectAsync(FfmpegPath);
            GpuInfo = info;
            BundledVersionText = GetBundledDisplayText();
            StatusMessage = info.DetectionSucceeded ? string.Empty : $"检测失败：{info.ErrorMessage}";
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

    /// <summary>打开默认输出目录；未设置时打开软件数据目录。</summary>
    [RelayCommand]
    private void OpenOutputFolder()
    {
        var dir = string.IsNullOrWhiteSpace(OutputDirectory)
            ? Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "MarukoBox")
            : OutputDirectory;
        ShellHelper.OpenFolder(dir);
    }

    /// <summary>
    /// 弹出当前 GPU 设备列表与对应序号（多 GPU 序号设置的引导）。
    /// 列表来自 nvidia-smi 枚举（<see cref="GpuInfo.GpuDevices"/>）；
    /// 无 NVIDIA 设备时明确告知该序号仅对 NVENC 生效。
    /// </summary>
    [RelayCommand]
    private void ShowGpuDevices()
    {
        var panel = new StackPanel { Spacing = 8 };

        var devices = GpuInfo.GpuDevices;
        if (devices.Count == 0)
        {
            panel.Children.Add(new TextBlock
            {
                Text = "未检测到 NVIDIA 设备。「多 GPU 设备序号」仅对 NVIDIA NVENC 编码生效，AMD / Intel 显卡无需设置。",
                TextWrapping = TextWrapping.Wrap
            });
        }
        else
        {
            panel.Children.Add(new TextBlock
            {
                Text = $"检测到 {devices.Count} 台 NVIDIA 设备，编码时使用的设备序号如下（0 = 自动/第一张）：",
                TextWrapping = TextWrapping.Wrap
            });
            foreach (var device in devices)
            {
                panel.Children.Add(new TextBlock
                {
                    Text = $"{device.Index}：{device.Name}",
                    IsTextSelectionEnabled = true
                });
            }
        }

        var dialog = new ContentDialog
        {
            XamlRoot = App.Window.Content.XamlRoot,
            Title = "GPU 设备列表",
            Content = panel,
            CloseButtonText = "关闭",
            DefaultButton = ContentDialogButton.Close
        };
        _ = dialog.ShowAsync();
    }

    /// <summary>
    /// 保存配置到磁盘。若本次保存涉及「主题」或「用户级别」与上次保存时不同，
    /// 则弹"立即重启？"对话框，主按钮触发 <see cref="RestartApp"/>。
    /// </summary>
    [RelayCommand]
    private async Task SaveAsync()
    {
        var themeChanged = Theme != _savedThemeBeforeSave;
        var levelChanged = SelectedUserLevel != _savedUserLevelBeforeSave;

        var config = new AppConfig
        {
            FfmpegPath = FfmpegPath,
            DefaultEncoder = SelectedEncoderOption?.Type.ToString() ?? "Auto",
            Theme = ThemeToCode(Theme),
            OutputDirectory = OutputDirectory,
            OutputFileNameRule = SelectedOutputFileNameRule,
            GpuDevice = GpuDevice,
            UserLevel = UserLevels.DisplayToCode(SelectedUserLevel),
            RememberLastSession = RememberLastSession,
            UpdateSource = UpdateSourceDisplayToCode(SelectedUpdateSource)
        };
        _config.Save(config);

        if (themeChanged || levelChanged)
        {
            StatusMessage = string.Empty;
            if (await PromptRestartAsync())
            {
                RestartApp();
                return;
            }

            // 用户选择稍后：刷新 baseline，下次 Save 同样值时不再提示；
            // 同时在「设置」导航项挂 InfoBadge，提醒重启后生效。
            _savedThemeBeforeSave = Theme;
            _savedUserLevelBeforeSave = SelectedUserLevel;
            (App.Window as MainWindow)?.SetRestartPending(true);
        }
        else
        {
            StatusMessage = string.Empty;
            // 主题 / 用户级别恢复原值（或本来就没改）：重启提醒不再需要。
            (App.Window as MainWindow)?.SetRestartPending(false);
        }

        // 保存成功：重拍基线（红点熄灭）+ 按钮就近反馈。
        _baseline = CaptureSnapshot();
        IsDirty = false;
        SaveButtonText = "已保存 ✓";
        try
        {
            await Task.Delay(1800);
        }
        finally
        {
            SaveButtonText = "保存配置";
        }
    }

    /// <summary>弹窗询问是否立即重启应用（主题 / 用户级别需重启生效）。</summary>
    private static async Task<bool> PromptRestartAsync()
    {
        try
        {
            var dialog = new ContentDialog
            {
                XamlRoot = App.Window.Content.XamlRoot,
                Title = "需要重启",
                Content = "已保存配置。主题与用户级别需要重启应用后才能生效。\n\n是否立即重启？",
                PrimaryButtonText = "立即重启",
                CloseButtonText = "稍后",
                DefaultButton = ContentDialogButton.Primary
            };
            return await dialog.ShowAsync() == ContentDialogResult.Primary;
        }
        catch
        {
            return false;
        }
    }

    /// <summary>
    /// 解绑式重启：先以 UseShellExecute 启动一个新进程（当前 exe），
    /// 再 <see cref="Application.Exit"/> 让旧进程退出。
    /// 这是 WinUI 3 unpackaged 唯一可靠的应用重启姿势（无 MSIX/AppLifecycle 桥接）。
    /// </summary>
    private static void RestartApp()
    {
        try
        {
            var exe = Process.GetCurrentProcess().MainModule?.FileName;
            if (!string.IsNullOrEmpty(exe) && File.Exists(exe))
            {
                Process.Start(new ProcessStartInfo
                {
                    FileName = exe,
                    UseShellExecute = true
                });
            }
        }
        catch
        {
            // 重启启动失败时，仍走 Shutdown 让用户自己手动重启
        }
        // 【N7 修复】Exit() 在 unpackaged 下行为未定义，统一走显式 Shutdown
        // （保存会话 → 关窗 → 兜底终止进程；新进程已先启动，二者短暂并存无碍）。
        App.Shutdown();
    }

    // ---------- 输出命名规则 ----------

    /// <summary>规则变化时同步刷新示例预览。</summary>
    partial void OnSelectedOutputFileNameRuleChanged(string value)
    {
        OutputFileNamePreview = OutputNaming.Preview(value);
    }

    /// <summary>输出目录变化时同步「打开」按钮可用性。</summary>
    partial void OnOutputDirectoryChanged(string value)
    {
        HasOutputDirectory = !string.IsNullOrWhiteSpace(value);
    }

    /// <summary>
    /// 检测结果更新后：按实际设备数收紧多 GPU 序号上限（nvidia-smi 缺失时回退 3），
    /// 并把越界的当前值钳回合法范围。
    /// </summary>
    partial void OnGpuInfoChanged(GpuInfo value)
    {
        GpuDeviceMax = value.GpuDeviceCount > 0 ? value.GpuDeviceCount - 1 : 3;
        if (GpuDevice > GpuDeviceMax)
        {
            GpuDevice = GpuDeviceMax;
        }
    }

    /// <summary>
    /// ffmpeg 路径击键级校验（400ms 防抖）：只做 File.Exists 轻校验。
    /// 绝不能在此触发 DetectAsync——一次完整检测要并发起 5 个外部进程，
    /// 且检测缓存按路径失效，击键级触发会造成进程风暴。
    /// 版本兼容性校验交给「重新检测」按钮。
    /// </summary>
    partial void OnFfmpegPathChanged(string value)
    {
        // 输入框禁用（有内置 ffmpeg）时不校验：禁用态本身就是状态。
        if (!CanEditFfmpegPath)
        {
            ClearFfmpegPathError();
            return;
        }

        _pathValidateCts?.Cancel();
        _pathValidateCts?.Dispose();
        _pathValidateCts = new CancellationTokenSource();
        var cts = _pathValidateCts;

        _ = ValidateFfmpegPathAsync(value, cts);
    }

    private async Task ValidateFfmpegPathAsync(string path, CancellationTokenSource cts)
    {
        try
        {
            await Task.Delay(400, cts.Token);
            if (cts.Token.IsCancellationRequested)
            {
                return;
            }

            // 延迟执行时重查：构造期间 CanEditFfmpegPath 尚未最终确定（内置检测在后），
            // 若此时已有内置 ffmpeg，输入框应处于禁用态，无需任何校验提示。
            if (!CanEditFfmpegPath)
            {
                ClearFfmpegPathError();
                return;
            }

            if (string.IsNullOrWhiteSpace(path))
            {
                // 留空 = 自动探测，不是错误
                ClearFfmpegPathError();
                return;
            }

            if (File.Exists(path))
            {
                ClearFfmpegPathError();
            }
            else
            {
                FfmpegPathError = $"未找到 ffmpeg.exe：{path}。保存后仍会回退到内置版本或 PATH。";
                IsFfmpegPathInvalid = true;
            }
        }
        catch (OperationCanceledException)
        {
            // 新输入到来，本次校验作废
        }
    }

    private void ClearFfmpegPathError()
    {
        FfmpegPathError = string.Empty;
        IsFfmpegPathInvalid = false;
    }

    /// <summary>
    /// 内置 ffmpeg 状态变化（安装/更新后）时刷新路径编辑可用性、说明文案与校验状态。
    /// </summary>
    private void RefreshFfmpegBundledState()
    {
        HasBundledFfmpeg = ConfigService.HasBundledFfmpeg;
        CanEditFfmpegPath = !HasBundledFfmpeg;
        FfmpegPathHint = HasBundledFfmpeg
            ? $"当前使用内置 ffmpeg {BundledVersionText}，下方路径已禁用；仅当内置 ffmpeg 不存在时才会使用手动路径。"
            : "留空则自动探测（内置 → PATH）；填写后仅在内置 ffmpeg 不存在时生效。";
        ClearFfmpegPathError();
    }

    // ---------- 更新/依赖检查状态（四级状态体系） ----------

    /// <summary>
    /// 统一设置更新/依赖检查结果：文案 + 严重级别 + 可见性。
    /// severity 缺省时按内容推断：含 ✗ → Error；含 △ → Warning；含 ✓ → Success；否则 Informational。
    /// </summary>
    private void SetUpdateStatus(string text, InfoBarSeverity? severity = null)
    {
        UpdateStatusMessage = text;
        HasUpdateStatus = !string.IsNullOrEmpty(text);
        UpdateStatusSeverity = severity ?? InferUpdateSeverity(text);
    }

    private static InfoBarSeverity InferUpdateSeverity(string text)
    {
        if (string.IsNullOrEmpty(text))
        {
            return InfoBarSeverity.Informational;
        }

        if (text.Contains('✗'))
        {
            return InfoBarSeverity.Error;
        }

        if (text.Contains('△'))
        {
            return InfoBarSeverity.Warning;
        }

        return text.Contains('✓') ? InfoBarSeverity.Success : InfoBarSeverity.Informational;
    }

    // ---------- 更新源显示名 <-> 配置代码 ----------

    /// <summary>
    /// 配置代码 → 下拉显示名。
    /// 旧配置里的 "gitee" 视为 "cn"（CN 源此前就是 Gitee 镜像），无需迁移。
    /// </summary>
    private static string UpdateSourceCodeToDisplay(string code) => code?.Trim().ToLowerInvariant() switch
    {
        "gitee" or "cn" => "CN",
        _ => "GitHub"
    };

    private static string UpdateSourceDisplayToCode(string display) => display switch
    {
        "CN" or "Gitee 镜像" => "cn",
        _ => "github"
    };

    // ---------- 检查更新（软件自身，支持 GitHub / Gitee 镜像） ----------

    /// <summary>
    /// 检查 MarukoBox 软件更新：按选定的更新源查询最新 Release 与当前版本比较，
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
        SetUpdateStatus("正在检查软件更新…", InfoBarSeverity.Informational);
        try
        {
            var source = SelectedUpdateSource == "CN" ? UpdateSource.CN : UpdateSource.GitHub;
            var latest = await _update.GetLatestAppReleaseAsync(source);
            var current = _update.GetAppVersion();

            // 版本级比较（容忍 v 前缀差异），而非字符串相等
            if (UpdateService.CompareVersions(current, latest.Version) >= 0)
            {
                SetUpdateStatus($"已是最新版本（{current}）", InfoBarSeverity.Success);
                return;
            }

            var confirmed = await ConfirmAppUpdateAsync(current, latest.Tag);
            if (!confirmed)
            {
                SetUpdateStatus("已取消更新", InfoBarSeverity.Informational);
                return;
            }

            IsDownloading = true;
            var progress = new Progress<double>(p => App.RunOnUiThread(() =>
            {
                UpdateProgressPercent = Math.Round(p, 1);
                SetUpdateStatus($"正在下载 {latest.Tag} 安装包… {p:F0}%", InfoBarSeverity.Informational);
            }));

            var installer = await _update.DownloadAppInstallerAsync(
                latest.DownloadUrl, latest.Version, progress);

            // 【N8】安装包来源未提供 .sha256 时显式告知（CN 源常态），状态升为警告级
            if (_update.LastChecksumMissing)
            {
                SetUpdateStatus($"安装包已就绪（注意：此来源未提供校验文件，已跳过 SHA-256 校验），正在启动安装程序（{latest.Tag}）…",
                    InfoBarSeverity.Warning);
            }
            else
            {
                SetUpdateStatus($"安装包已就绪，正在启动安装程序（{latest.Tag}）…", InfoBarSeverity.Informational);
            }
            await Task.Delay(600); // 让用户看到状态再退出

            Process.Start(new ProcessStartInfo
            {
                FileName = installer,
                UseShellExecute = true
            });
            // 【N7 修复】Exit() 在 unpackaged 下行为未定义（可能不关窗、不触发 Closed，
            // 新安装器与旧进程并存导致文件占用）。统一走显式 Shutdown。
            App.Shutdown();
        }
        catch (Exception ex)
        {
            SetUpdateStatus($"检查更新失败：{ex.Message}", InfoBarSeverity.Error);
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
        catch (Exception ex)
        {
            // 【M1 修复】弹窗异常时改为「不确认」——与关机确认 ConfirmPowerActionAsync 的保守策略对齐。
            // 此前这里是 return true（等于替用户点了「下载并安装」），
            // 窗口还没就绪就会自动开始下载并静默执行安装包，方向完全反了。
            // 没有明确的「用户同意」就不该动用户的系统。
            App.LogCrash(ex, "SettingsViewModel.ConfirmAppUpdateAsync");
            return false;
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
        SetUpdateStatus("正在检查依赖…", InfoBarSeverity.Informational);
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

            // 5) 内置 ffmpeg 新版检查（GitHub；按本机驱动兼容性推荐）
            try
            {
                var depSource = SelectedUpdateSource == "CN" ? UpdateSource.CN : UpdateSource.GitHub;
                var rec = await _update.GetRecommendedFfmpegAsync(GpuInfo, depSource);
                if (!rec.Recommended)
                {
                    // 全部候选被驱动门槛拦截（最常见：N 卡驱动 <610 无法跑 8.x NVENC）
                    sb.AppendLine($"△ 内置 ffmpeg 暂无可推送的新版：{rec.BlockReason}");
                }
                else if (string.IsNullOrEmpty(local)
                         || UpdateService.CompareVersions(local, rec.RecommendedTag!) < 0)
                {
                    SetUpdateStatus(sb.ToString().TrimEnd());
                    var confirmed = await ConfirmFfmpegUpdateAsync(local, rec.RecommendedTag!, rec.RecommendedSizeBytes);
                    if (confirmed)
                    {
                        await InstallFfmpegUpdateAsync(new FfmpegInstallTarget(rec.RecommendedTag!, rec.RecommendedDownloadUrl!));
                    }
                    else
                    {
                        SetUpdateStatus(sb.ToString().TrimEnd() + "\n已取消更新内置 ffmpeg");
                    }
                    return;
                }
                else
                {
                    sb.AppendLine($"✓ 内置 ffmpeg 已是最新（{local}）");
                }

                // 6) 「专家」专列版本列表：填充 FfmpegReleaseRows（点过本检查后可见）
                await RefreshFfmpegListAsync();
            }
            catch (Exception ex)
            {
                sb.AppendLine($"△ 检查 ffmpeg 新版失败：{ex.Message}");
            }

            SetUpdateStatus(sb.ToString().TrimEnd());
        }
        catch (Exception ex)
        {
            SetUpdateStatus($"依赖检查失败：{ex.Message}", InfoBarSeverity.Error);
        }
        finally
        {
            IsCheckingUpdate = false;
            IsDownloading = false;
            UpdateProgressPercent = 0;
        }
    }

    /// <summary>弹窗确认是否下载安装新版内置 ffmpeg。</summary>
    private async Task<bool> ConfirmFfmpegUpdateAsync(string? localVersion, string newVersion, long? sizeBytes = null)
    {
        try
        {
            // 【E5 修复】大小不再硬编码"约 67 MB"——用调用方传入的实际字节数格式化；
            // 镜像源（兰州索引页无大小字段，记 0）显示「大小未知」。
            var sizePart = sizeBytes is > 0
                ? $"约 {sizeBytes.Value / 1024d / 1024d:F0} MB"
                : "大小未知";
            var dialog = new ContentDialog
            {
                XamlRoot = App.Window.Content.XamlRoot,
                Title = "发现新版本",
                Content = $"当前内置 ffmpeg：{(string.IsNullOrEmpty(localVersion) ? "未安装" : localVersion)}\n" +
                          $"最新版本：{newVersion}\n\n" +
                          $"是否下载并安装？（{sizePart}，安装期间请勿进行编码任务）",
                PrimaryButtonText = "下载并安装",
                CloseButtonText = "取消",
                DefaultButton = ContentDialogButton.Primary
            };
            var result = await dialog.ShowAsync();
            return result == ContentDialogResult.Primary;
        }
        catch (Exception ex)
        {
            // 【M1 修复】同上：拿不到用户确认就当作取消，不能默认替用户同意安装。
            App.LogCrash(ex, "SettingsViewModel.ConfirmFfmpegUpdateAsync");
            return false;
        }
    }

    /// <summary>下载并安装新版内置 ffmpeg（中断安全整目录替换），完成后重新检测。</summary>
    private async Task InstallFfmpegUpdateAsync(FfmpegInstallTarget target)
    {
        IsDownloading = true;
        UpdateProgressPercent = 0;
        var progress = new Progress<double>(p => App.RunOnUiThread(() =>
        {
            UpdateProgressPercent = Math.Round(p, 1);
            SetUpdateStatus($"正在下载 ffmpeg {target.Tag}… {p:F0}%", InfoBarSeverity.Informational);
        }));

        await _update.DownloadAndInstallAsync(target.DownloadUrl, target.Tag, progress);

        // 内置版本已替换：重新解析生效路径（内置优先）并刷新能力检测
        FfmpegPath = _config.Load().FfmpegPath;
        BundledVersionText = target.Tag;
        RefreshFfmpegBundledState();

        // 【N8】CN 镜像等来源可能没有 .sha256 伴随文件——校验被跳过时必须让用户知道
        if (_update.LastChecksumMissing)
        {
            SetUpdateStatus($"内置 ffmpeg 已更新到 {target.Tag}（注意：此来源未提供校验文件，已跳过 SHA-256 校验）",
                InfoBarSeverity.Warning);
        }
        else
        {
            SetUpdateStatus($"内置 ffmpeg 已更新到 {target.Tag}", InfoBarSeverity.Success);
        }

        await DetectAsync();
    }

    /// <summary>ffmpeg 安装目标。包内 record，仅在「检查依赖」流程内部传递。</summary>
    private sealed record FfmpegInstallTarget(string Tag, string DownloadUrl);

    // ---------- 「专家」专列版本列表 ----------

    /// <summary>
    /// 从 GitHub 拉全量 jellyfin-ffmpeg release 并填充到 <see cref="FfmpegReleaseRows"/>。
    /// 列表整体可见性 <see cref="IsFfmpegListVisible"/> 由用户级别（= 专家）
    /// 与本集合是否非空共同决定；首次填充即视为"已经检查过下载站的文件列表"。
    /// </summary>
    [RelayCommand]
    private async Task RefreshFfmpegListAsync()
    {
        try
        {
            var listSource = SelectedUpdateSource == "CN" ? UpdateSource.CN : UpdateSource.GitHub;
            var releases = await _update.GetAllFfmpegReleasesAsync(listSource);
            FfmpegReleaseRows.Clear();
            foreach (var r in releases)
            {
                var offer = _update.ShouldOfferFfmpegUpdate(GpuInfo, r.Tag);
                FfmpegReleaseRows.Add(new FfmpegReleaseRow(r, offer, InstallFfmpegRowCommand));
            }
            IsFfmpegListVisible = UserLevel == UserLevel.Developer && FfmpegReleaseRows.Count > 0;
        }
        catch (Exception ex)
        {
            StatusMessage = $"加载版本列表失败：{ex.Message}";
        }
    }

    /// <summary>
    /// 供 XAML ListView 行内 [安装此版本] 按钮调用的入口：从 parameter 拿 <see cref="FfmpegReleaseRow"/>，
    /// 程序员可绕过 NVENC 门槛强制安装任意版本。
    /// </summary>
    [RelayCommand]
    private async Task InstallFfmpegRowAsync(FfmpegReleaseRow? row)
    {
        if (row is null || row.IsInstalling) return;

        var confirmed = await ConfirmForceFfmpegInstallAsync(row);
        if (!confirmed) return;

        row.IsInstalling = true;
        row.ProgressPercent = 0;
        try
        {
            var progress = new Progress<double>(p => App.RunOnUiThread(() =>
                row.ProgressPercent = Math.Round(p, 1)));
            await _update.DownloadAndInstallAsync(row.AssetUrl, row.Tag, progress);

            FfmpegPath = _config.Load().FfmpegPath;
            BundledVersionText = row.Tag;
            RefreshFfmpegBundledState();

            // 【N8】同上：校验缺失时用警告级状态显式告知
            if (_update.LastChecksumMissing)
            {
                SetUpdateStatus($"已安装 ffmpeg {row.Tag}（专家模式，跳过驱动兼容检查；注意：此来源未提供校验文件，已跳过 SHA-256 校验）",
                    InfoBarSeverity.Warning);
            }
            else
            {
                SetUpdateStatus($"已安装 ffmpeg {row.Tag}（专家模式，跳过驱动兼容检查）", InfoBarSeverity.Success);
            }
            await DetectAsync();
        }
        catch (Exception ex)
        {
            SetUpdateStatus($"安装 {row.Tag} 失败：{ex.Message}", InfoBarSeverity.Error);
        }
        finally
        {
            row.IsInstalling = false;
        }
    }

    /// <summary>「专家」安装任意版本前的二次确认（含驱动不兼容时的强装提示）。</summary>
    private static async Task<bool> ConfirmForceFfmpegInstallAsync(FfmpegReleaseRow row)
    {
        try
        {
            var dialog = new ContentDialog
            {
                XamlRoot = App.Window.Content.XamlRoot,
                Title = "安装此版本",
                Content = row.IsCompatible
                    ? $"将下载并安装 jellyfin-ffmpeg {row.Tag}（{row.SizeText}）。\n\n" +
                      "安装期间请勿进行编码任务。"
                    : $"将下载并安装 jellyfin-ffmpeg {row.Tag}（{row.SizeText}）。\n\n" +
                      $"⚠ 本机当前 {row.CompatibleText}\n" +
                      "强制安装后 NVENC 硬件编码可能不可用，但软件编码器照常使用。\n\n" +
                      "是否继续？",
                PrimaryButtonText = "继续安装",
                CloseButtonText = "取消",
                DefaultButton = ContentDialogButton.Primary
            };
            return await dialog.ShowAsync() == ContentDialogResult.Primary;
        }
        catch
        {
            return false;
        }
    }
}
