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
    /// The native window handle (HWND). Use for file pickers,
    /// <c>DataTransferManager</c>, and any WinRT interop that requires
    /// <c>InitializeWithWindow</c>.
    /// </summary>
    public static nint WindowHandle =>
        WinRT.Interop.WindowNative.GetWindowHandle(Window);

    /// <summary>
    /// 崩溃 / 诊断日志路径。任何未捕获异常都会落盘，便于定位闪退根因。
    /// </summary>
    public static readonly string LogPath = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.UserProfile),
        "marukobox_crash.log");

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
            File.AppendAllText(LogPath, sb.ToString());
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
            File.AppendAllText(LogPath, $"[{DateTime.Now:HH:mm:ss}] {message}\n");
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

        // ---------- 全局异常兜底（微软规范：运行中的应用是事实来源） ----------
        // UI 线程未捕获异常：标记 Handled=true 防止进程退出（闪退）。
        UnhandledException += OnUnhandledException;

        // 后台 / 线程池线程未捕获异常：记录日志，尽量阻止进程退出。
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
        e.Handled = true; // 关键：阻止 UI 线程异常导致闪退
    }

    /// <summary>
    /// Invoked when the application is launched.
    /// </summary>
    /// <param name="args">Details about the launch request and process.</param>
    protected override void OnLaunched(Microsoft.UI.Xaml.LaunchActivatedEventArgs args)
    {
        Window = new MainWindow();
        DispatcherQueue = Microsoft.UI.Dispatching.DispatcherQueue.GetForCurrentThread();
        Window.Activate();
    }
}
