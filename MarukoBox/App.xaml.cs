using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Data;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Navigation;
using System;
using System.IO;
using System.Text;
using System.Threading.Tasks;

// To learn more about WinUI, the WinUI project structure,
// and more about our project templates, see: http://aka.ms/winui-project-info.

namespace MarukoBox;

/// <summary>
/// Provides application-specific behavior to supplement the default Application class.
/// </summary>
public partial class App : Application
{
    /// <summary>
    /// The main application window. Use <c>App.Window</c> from any class that needs
    /// the window reference (for dialogs, pickers, interop, etc.).
    /// </summary>
    public static Window Window { get; private set; } = null!;

    /// <summary>
    /// The UI thread dispatcher. Use <c>App.DispatcherQueue</c> to marshal calls
    /// to the UI thread. Fully qualified to avoid CS0104 ambiguity with
    /// <see cref="Windows.System.DispatcherQueue"/>.
    /// </summary>
    public static Microsoft.UI.Dispatching.DispatcherQueue DispatcherQueue { get; private set; } = null!;

    /// <summary>
    /// 启动自动检查发现的待处理新版本 tag（null = 无）。
    /// 由 <see cref="RunStartupUpdateCheckAsync"/> 写入；设置页打开时读取并展示 InfoBar，
    /// 用户手动「检查更新」后清除。
    /// </summary>
    public static string? PendingUpdateTag { get; set; }

    /// <summary>
    /// The native window handle (HWND). Use for file pickers,
    /// <c>DataTransferManager</c>, and any WinRT interop that requires
    /// <c>InitializeWithWindow</c>.
    /// </summary>
    public static nint WindowHandle =>
        WinRT.Interop.WindowNative.GetWindowHandle(Window);

    /// <summary>
    /// 崩溃 / 诊断日志目录。任何未捕获异常都会落盘，便于定位闪退根因。
    /// <para>
    /// v1.4.1（S4）：原先写在 <c>%USERPROFILE%\marukobox_crash.log</c>——
    /// 用户根目录常被云同步/备份工具扫描，且该位置「显眼又不合适」。
    /// 现在与 config.json 同域：<c>%LOCALAPPDATA%\MarukoBox\logs\</c>。
    /// </para>
    /// </summary>
    public static readonly string LogDirectory = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "MarukoBox", "logs");

    /// <summary>崩溃 / 诊断日志路径。</summary>
    public static readonly string LogPath = Path.Combine(LogDirectory, "marukobox.log");

    /// <summary>单个日志文件的大小上限（超过则轮转）。</summary>
    private const long MaxLogBytes = 5L * 1024 * 1024;

    /// <summary>最多保留的历史日志份数（.1.log ~ .3.log）。</summary>
    private const int MaxLogGenerations = 3;

    /// <summary>日志写入锁：stderr 线程与 UI 线程可能同时写。</summary>
    private static readonly object LogGate = new();

    /// <summary>
    /// 写入一段日志文本。写入前按需轮转：超过 5 MB 时
    /// .2.log → .3.log、.1.log → .2.log、当前 → .1.log，最多保留 3 份历史。
    /// </summary>
    private static void WriteLog(string text)
    {
        lock (LogGate)
        {
            try
            {
                Directory.CreateDirectory(LogDirectory);
                RotateIfNeeded();
                File.AppendAllText(LogPath, text);
            }
            catch
            {
                // 日志本身失败也不应影响主流程
            }
        }
    }

    private static void RotateIfNeeded()
    {
        try
        {
            var info = new FileInfo(LogPath);
            if (!info.Exists || info.Length < MaxLogBytes)
            {
                return;
            }

            // 先删最老的一代，再依次后移
            var oldest = LogPath + $".{MaxLogGenerations}.log";
            if (File.Exists(oldest))
            {
                File.Delete(oldest);
            }

            for (var gen = MaxLogGenerations - 1; gen >= 1; gen--)
            {
                var from = LogPath + $".{gen}.log";
                if (File.Exists(from))
                {
                    File.Move(from, LogPath + $".{gen + 1}.log");
                }
            }

            File.Move(LogPath, LogPath + ".1.log");
        }
        catch
        {
            // 轮转失败不阻塞写入
        }
    }

    /// <summary>
    /// 记录一条崩溃异常（含堆栈），并尽量保证同步落盘。
    /// </summary>
    public static void LogCrash(Exception ex, string where)
    {
        try
        {
            var sb = new StringBuilder();
            sb.AppendLine($"[{DateTime.Now:yyyy-MM-dd HH:mm:ss}] 位置: {where}");
            sb.AppendLine($"异常类型: {ex.GetType().FullName}");
            sb.AppendLine($"消息: {ex.Message}");
            sb.AppendLine("堆栈:");
            sb.AppendLine(ex.StackTrace);
            if (ex.InnerException is not null)
            {
                sb.AppendLine($"内部异常: {ex.InnerException}");
            }

            sb.AppendLine(new string('-', 60));
            WriteLog(sb.ToString());
        }
        catch
        {
            // 日志本身失败也不应影响主流程
        }
    }

    /// <summary>
    /// 记录一条普通诊断信息（非异常）。
    /// </summary>
    public static void LogInfo(string message)
    {
        try
        {
            WriteLog($"[{DateTime.Now:HH:mm:ss}] {message}\n");
        }
        catch
        {
            // 忽略
        }
    }

    /// <summary>
    /// 将 <paramref name="action"/> 封送到 UI 线程执行。
    /// <para>
    /// 在后台线程（如 ffmpeg 的 stderr / stdout 读取线程）上更新绑定到 UI 的属性时，
    /// 必须经由此方法，否则 XAML 框架会因跨线程访问可视化树而触发
    /// STATUS_FATAL_APP_EXIT (0xc000027b) 致命快速失败，导致进程闪退——
    /// 该异常是 native fast-fail，托管层的 try/catch 与 UnhandledException 均无法拦截。
    /// </para>
    /// </summary>
    public static void RunOnUiThread(Action action)
    {
        // 注：Harness 等无窗口场景下 DispatcherQueue 为 null（AppStub 未初始化），
        // 此时直接同步执行，与注释描述一致。
        var dq = DispatcherQueue;
        if (dq is null || dq.HasThreadAccess)
        {
            // 无 DispatcherQueue（如测试 Harness）或当前已是 UI 线程，直接执行。
            action();
        }
        else
        {
            dq.TryEnqueue(() => action());
        }
    }

    /// <summary>
    /// Initializes the singleton application object.
    /// </summary>
    public App()
    {
        InitializeComponent();

        // ---------- 主题（必须在任何窗口内容加载前设置） ----------
        // "System"（跟随系统）不设置，让框架跟随 Windows 主题；
        // Light / Dark 显式设置 RequestedTheme。改动后需重启应用生效。
        ApplyThemeFromConfig();

        // ---------- 全局异常兜底（微软规范：运行中的应用是事实来源） ----------
        // UI 线程未捕获异常：标记 Handled=true 防止进程退出（闪退）。
        UnhandledException += OnUnhandledException;

        // 后台 / 线程池线程未捕获异常：记录日志，留下现场。
        // 【N14 修复】注释更正：AppDomain.UnhandledException 本身无法阻止进程终止
        // （.NET 规范如此），"尽量阻止进程退出"的原注释是误导。
        AppDomain.CurrentDomain.UnhandledException += (_, e) =>
            LogCrash((Exception)e.ExceptionObject, "AppDomain.UnhandledException");

        TaskScheduler.UnobservedTaskException += (_, e) =>
        {
            LogCrash(e.Exception, "TaskScheduler.UnobservedTaskException");
            e.SetObserved(); // 标记为已观察，避免进程因未观察任务异常而终止
        };
    }

    private void OnUnhandledException(object sender, Microsoft.UI.Xaml.UnhandledExceptionEventArgs e)
    {
        LogCrash(e.Exception, "Application.UnhandledException");

#if DEBUG
        // 【N14 修复】调试期不吞异常：Handled=true 会阻止调试器在未处理异常处中断，
        // 问题被静默吞掉难定位。Debug 构建让异常照常抛出（调试器/本地日志都有现场）。
#else
        e.Handled = true; // 关键：阻止 UI 线程异常导致闪退（仅 Release）
#endif
    }

    /// <summary>
    /// 【N7 修复】统一退出入口。不依赖 <see cref="Microsoft.UI.Xaml.Application.Exit"/> 的
    /// 隐式行为——官方文档对其只有一句 "Shuts down the app."，未对 unpackaged 应用承诺
    /// 进程终止或 Window.Closed 触发（microsoft-ui-xaml#5301 系列已知问题）。
    /// 显式完成：会话落盘 → 关窗 → 兜底终止进程。
    /// </summary>
    public static void Shutdown()
    {
        // ① 会话先落盘：Closed 可能不触发，显式保存是唯一可靠点（SaveSessionIfEnabled 幂等）。
        MainWindow.SaveSessionIfEnabled();

        // ② 正常关窗：关掉最后一个窗口后消息循环结束、进程自然退出（Closed 再保存一次，无害）。
        (Window as MainWindow)?.Close();

        // ③ 兜底：若消息循环未如预期终止（unpackaged 生命周期属未定义区域），
        //    显式终止进程——「编码完成后退出」绝不能成为摆设。
        Environment.Exit(0);
    }

    /// <summary>
    /// 从配置读取主题并在 App 构造阶段应用。
    /// WinUI 3 的 <see cref="Application.RequestedTheme"/> 只能在窗口内容加载前设置，
    /// 运行中切换需要重启应用才能生效。
    /// </summary>
    private void ApplyThemeFromConfig()
    {
        try
        {
            var theme = AppServices.Config.Load().Theme;
            if (string.Equals(theme, "Light", StringComparison.OrdinalIgnoreCase))
            {
                RequestedTheme = ApplicationTheme.Light;
            }
            else if (string.Equals(theme, "Dark", StringComparison.OrdinalIgnoreCase))
            {
                RequestedTheme = ApplicationTheme.Dark;
            }
        }
        catch
        {
            // 主题读取失败不致命：回落到跟随系统
        }
    }

    /// <summary>
    /// 启动时自动检查更新（仅一次，静默）：延后 15 秒错开启动高峰（GPU 检测 / 会话恢复），
    /// 按 config.AutoCheckUpdates 开关与当前更新源查询最新 Release；
    /// 发现新版 → 点亮「设置」导航项徽标并记录 <see cref="PendingUpdateTag"/> 供设置页提示。
    /// 不自动下载——安装包的下载与执行必须经用户手动确认（与 M1/S1 的安全立场一致）。
    /// 任何失败完全静默（仅日志），绝不打扰用户。
    /// </summary>
    private static async Task RunStartupUpdateCheckAsync()
    {
        try
        {
            await Task.Delay(TimeSpan.FromSeconds(15));

            var config = AppServices.Config.Load();
            if (!config.AutoCheckUpdates)
            {
                LogInfo("启动检查更新：已关闭（AutoCheckUpdates=false），跳过");
                return;
            }

            var source = string.Equals(config.UpdateSource?.Trim(), "cn", StringComparison.OrdinalIgnoreCase)
                ? MarukoBox.Services.UpdateSource.CN
                : MarukoBox.Services.UpdateSource.GitHub;
            var latest = await AppServices.Update.GetLatestAppReleaseAsync(source);

            var current = AppServices.Update.GetAppVersion();
            if (MarukoBox.Services.UpdateService.CompareVersions(current, latest.Version) >= 0)
            {
                LogInfo($"启动检查更新：已是最新版本（{current}）");
                return;
            }

            PendingUpdateTag = latest.Tag;
            LogInfo($"启动检查更新：发现新版本 {latest.Tag}，已点亮设置导航徽标");
            RunOnUiThread(() => (Window as MainWindow)?.SetUpdateAvailable(true));
        }
        catch (Exception ex)
        {
            // 自动检查失败（网络不可用 / 源超时等常态）完全静默，仅留日志
            LogInfo($"启动更新检查未完成：{ex.GetType().Name} {ex.Message}");
        }
    }

    /// <summary>
    /// Invoked when the application is launched.
    /// </summary>
    /// <param name="args">Details about the launch request and process.</param>
    protected override void OnLaunched(Microsoft.UI.Xaml.LaunchActivatedEventArgs args)
    {
        Window = new MainWindow();
        DispatcherQueue = Microsoft.UI.Dispatching.DispatcherQueue.GetForCurrentThread();

        // 保持习惯：窗口关闭（含重启流程的 Exit）时保存视频页会话快照。
        // 开关由 MainWindow 内部判断——关闭时跳过保存即可。
        Window.Closed += (_, _) => MainWindow.SaveSessionIfEnabled();

        Window.Activate();

        // 自动检查更新：仅启动时一次、后台静默（开关与说明见 RunStartupUpdateCheckAsync）。
        // fire-and-forget：内部已全量 try/catch，绝不影响启动路径。
        _ = RunStartupUpdateCheckAsync();
    }
}
