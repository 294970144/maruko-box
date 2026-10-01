using System;
using System.IO;
using System.Runtime.InteropServices;
using System.Threading.Tasks;

#if !MARUKO_HARNESS
using Microsoft.Windows.AppNotifications;
using Microsoft.Windows.AppNotifications.Builder;
using MarukoBox;
using MarukoBox.Helpers;
#endif

namespace MarukoBox.Services;

public enum NotificationSendResult
{
    NotReady,
    QueueRejected,
    Submitted,
    Failed
}

public static class NotificationSendResultPolicy
{
    public static NotificationSendResult Resolve(bool available, bool queued, bool showSucceeded)
    {
        if (!available) return NotificationSendResult.NotReady;
        if (!queued) return NotificationSendResult.QueueRejected;
        return showSucceeded ? NotificationSendResult.Submitted : NotificationSendResult.Failed;
    }
}

/// <summary>
/// 系统通知（Toast / Windows 操作中心）封装。
/// <para>
/// 采用 Windows App SDK 的 <see cref="Microsoft.Windows.AppNotifications.AppNotificationManager"/>。
/// 本应用为 unpackaged 部署，官方文档说明 <c>Register()</c> 会自动注册 COM 服务器并从 shell
/// 获取应用显示名/图标；但在 self-contained 部署 + 某些 WinAppSDK 版本下，Register() 可能
/// 抛出已知的资源 DLL 缺失异常（0x8007007E）。GitHub issue #6774 证实：该异常下
/// <c>Show()</c> 通常仍能把 Toast 弹出来，只是点击通知的激活可能由新进程处理。
/// 因此本实现采取「注册失败但允许尝试显示」的降级策略，而非一刀切关闭通知。
/// </para>
/// <para>
/// 另外，为兼容旧版 Windows / 某些 Shell 行为，<see cref="Initialize"/> 仍会通过
/// <see cref="ShortcutHelper.EnsureShortcut"/> 确保开始菜单存在一条带 AUMID 的快捷方式
///（文件名与 Inno Setup 的「MarukoBox 2026.lnk」一致），作为应用身份的兜底。
/// </para>
/// <para>
/// 本次仅落地「任务完成」这一类通知（媒体转码批量完成）；其余类型（错误 / 更新完成）后续可复用本服务扩展。
/// 注册失败绝不向上抛异常——宁可本轮不弹通知，也不允许阻断应用启动。
/// </para>
/// </summary>
public static class NotificationService
{
#if !MARUKO_HARNESS
    // unpackaged 下的自定义 AUMID（与开始菜单快捷方式的 System.AppUserModel.ID 保持一致）。
    private const string Aumid = "MarukoBox.DesktopNotification";
    // 与 Installer-Inno/marukobox.iss 的 [Icons]（{autoprograms}\MarukoBox 2026）保持一致。
    private const string ShortcutDisplayName = "MarukoBox 2026";

    // WinAppSDK self-contained unpackaged 已知问题：Register() 内部缺少
    // Microsoft.WindowsAppRuntime.Insights.Resource.dll，抛 0x8007007E。
    // 该错误不阻断 Show() 显示 Toast，因此视为「可降级注册」。
    private const int ErrorModNotFound = unchecked((int)0x8007007E);
    // WinAppSDK 2.4.0 无参 Register() 遍历 %LOCALAPPDATA%\Application Data junction 抛的错误。
    private const int ErrorUntrustedVolumeMountPoint = unchecked((int)0x800701C0);

    private static bool _initialized;
    private static bool _registered;
    private static bool _degradedSendAllowed;

    /// <summary>通知通道是否已成功注册。</summary>
    public static bool IsRegistered => _registered;

    /// <summary>通知可尝试发送。降级状态不代表通道已正式注册。</summary>
    public static bool IsAvailable => _registered || _degradedSendAllowed;

    /// <summary>
    /// 系统通知总开关（由设置项「系统通知」驱动）。默认开启；
    /// 关闭时即使通道可用也完全不发送通知。在 <see cref="App.OnLaunched"/> 与
    /// 设置页开关变更时同步。
    /// </summary>
    public static bool Enabled { get; set; } = true;

    /// <summary>
    /// 注册通知通道。应在 UI 线程、窗口创建前调用（<see cref="App.OnLaunched"/>）。
    /// 内部已全量 try/catch，任何异常都降级为「本轮运行不发送通知」，不向上抛。
    /// </summary>
    public static void Initialize()
    {
        if (_initialized)
        {
            return;
        }

        _initialized = true;
        _registered = false;
        _degradedSendAllowed = false;

        try
        {
            // unpackaged 注册前提：开始菜单必须存在带 System.AppUserModel.ID 的快捷方式。
            // 首次运行（开发 / 全新安装）创建它；已存在（Inno 安装入口）则补设 AUMID。
            try
            {
                ShortcutHelper.EnsureShortcut(Aumid, ShortcutDisplayName);
                App.LogInfo("通知用开始菜单快捷方式已就绪");
            }
            catch (Exception ex)
            {
                // 快捷方式建立失败也要继续尝试注册（开发环境可能无 Shell 写权限）。
                App.LogCrash(ex, "NotificationService.EnsureShortcut");
            }

            var mgr = AppNotificationManager.Default;
            mgr.NotificationInvoked += OnNotificationInvoked;

            try
            {
                mgr.Register();
                _registered = true;
                App.LogInfo("通知通道注册成功");
            }
            catch (COMException comEx) when (comEx.HResult == ErrorModNotFound)
            {
                // self-contained unpackaged 下 Register() 抛 0x8007007E 为已知问题，
                // Show() 通常仍可显示通知。允许继续，但点击通知的激活可能由新进程处理。
                _degradedSendAllowed = true;
                App.LogInfo($"通知通道注册返回 0x8007007E（资源 DLL 缺失），尝试继续发送通知");
            }
            catch (COMException comEx) when (comEx.HResult == ErrorUntrustedVolumeMountPoint)
            {
                // 0x800701C0：WinAppSDK 2.5.1 在 self-contained unpackaged 某些路径下仍会
                // 遍历到 %LOCALAPPDATA%\Application Data junction。与 0x8007007E 一样，
                // 属于「注册失败但 Show() 仍可能成功」的已知问题，因此走降级而不是直接关闭。
                _degradedSendAllowed = true;
                App.LogCrash(comEx, "NotificationService.Initialize.Register");
                App.LogInfo("通知通道注册返回 0x800701C0（不受信任装入点），尝试继续发送通知");
            }
            catch (Exception ex)
            {
                mgr.NotificationInvoked -= OnNotificationInvoked;
                _initialized = false;
                _registered = false;
                App.LogCrash(ex, "NotificationService.Initialize.Register");
                App.LogInfo($"通知通道注册失败：HRESULT=0x{ex.HResult:X8}");
            }
        }
        catch (Exception ex)
        {
            _registered = false;
            _degradedSendAllowed = false;
            App.LogCrash(ex, "NotificationService.Initialize");
            App.LogInfo("通知通道注册失败：本次运行不发送系统通知");
        }
    }

    /// <summary>
    /// 注销通知通道，释放 COM 服务器。退出时调用（<see cref="App.Shutdown"/>）。
    /// </summary>
    public static void Uninitialize()
    {
        if (!_initialized)
        {
            return;
        }

        try
        {
            var manager = AppNotificationManager.Default;
            manager.NotificationInvoked -= OnNotificationInvoked;
            if (_registered)
            {
                manager.Unregister();
            }
        }
        catch (Exception ex)
        {
            App.LogCrash(ex, "NotificationService.Uninitialize");
        }
        finally
        {
            _initialized = false;
            _registered = false;
            _degradedSendAllowed = false;
        }
    }

    /// <summary>
    /// 显示一条「任务完成」通知。未成功注册时静默跳过。
    /// 内部经 <see cref="App.RunOnUiThread"/> 封送，可安全地在后台完成回调中调用。
    /// </summary>
    public static Task<NotificationSendResult> ShowTaskCompletedAsync(string title, string message)
    {
        if (!IsAvailable || !Enabled)
        {
            return Task.FromResult(NotificationSendResultPolicy.Resolve(
                available: false, queued: false, showSucceeded: false));
        }

        var completion = new TaskCompletionSource<NotificationSendResult>(
            TaskCreationOptions.RunContinuationsAsynchronously);

        void Send()
        {
            try
            {
                var toast = new AppNotificationBuilder()
                    .AddText(title)
                    .AddText(message)
                    .BuildNotification();
                AppNotificationManager.Default.Show(toast);
                completion.TrySetResult(NotificationSendResultPolicy.Resolve(
                    available: true, queued: true, showSucceeded: true));
            }
            catch (Exception ex)
            {
                App.LogCrash(ex, "NotificationService.ShowTaskCompleted");
                completion.TrySetResult(NotificationSendResultPolicy.Resolve(
                    available: true, queued: true, showSucceeded: false));
            }
        }

        try
        {
            var dispatcher = App.DispatcherQueue;
            if (dispatcher is null || dispatcher.HasThreadAccess)
            {
                Send();
            }
            else if (!dispatcher.TryEnqueue(Send))
            {
                completion.TrySetResult(NotificationSendResultPolicy.Resolve(
                    available: true, queued: false, showSucceeded: false));
            }
        }
        catch (Exception ex)
        {
            App.LogCrash(ex, "NotificationService.ShowTaskCompleted.Enqueue");
            completion.TrySetResult(NotificationSendResultPolicy.Resolve(
                available: true, queued: false, showSucceeded: false));
        }

        return completion.Task;
    }

    private static void OnNotificationInvoked(AppNotificationManager sender, AppNotificationActivatedEventArgs args)
    {
        // 点击通知：把应用提到前台（本次通知无需解析激活参数）。
        App.RunOnUiThread(() =>
        {
            try
            {
                (App.Window as MainWindow)?.BringToForeground();
            }
            catch (Exception ex)
            {
                App.LogCrash(ex, "NotificationService.OnNotificationInvoked");
            }
        });
    }
#else
    // Harness（冒烟测试工程）不引用 WindowsAppSDK，也不编译 App.xaml.cs，
    // 故仅提供空实现，保证 Services/*.cs 在 Harness 下可编译、调用点编译通过。
    public static bool IsRegistered => false;
    public static bool IsAvailable => false;
    public static void Initialize() { }
    public static void Uninitialize() { }
    public static Task<NotificationSendResult> ShowTaskCompletedAsync(string title, string message) =>
        Task.FromResult(NotificationSendResult.NotReady);
#endif
}
