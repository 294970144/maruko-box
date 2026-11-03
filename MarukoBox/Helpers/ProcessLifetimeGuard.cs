using System.Diagnostics;
using System.Runtime.InteropServices;

namespace MarukoBox.Helpers;

/// <summary>
/// 【C7 修复·补全】子进程生命周期兜底。原修复只拦「当前页可见任务」，漏了两类路径：
/// 切到其它标签页后关窗（任务在后台 VM 里继续跑）、以及经 App.Shutdown() 的退出
/// （更新流/重启流）——孤儿 ffmpeg 照旧。根因是把「当前页」当成了「当前任务」。
/// <para>
/// ① <b>Job Object</b>（KILL_ON_JOB_CLOSE）：主进程以任何方式退出（关窗 / App.Shutdown /
///    Environment.Exit / 崩溃 / 被任务管理器结束）时，由内核连带终止所有已分配的子进程——
///    不依赖任何调用方配合，是唯一可靠的兜底。
/// </para>
/// <para>
/// ② <b>取消注册表</b>：跨页面跟踪进行中的 <see cref="CancellationTokenSource"/>，
///    关窗确认「取消任务并退出」时一次性取消全部任务，而非仅当前页。
/// </para>
/// </summary>
internal static class ProcessLifetimeGuard
{
    // ---------- Job Object ----------

    private static readonly IntPtr JobHandle = CreateKillOnCloseJob();

    private static IntPtr CreateKillOnCloseJob()
    {
        var handle = CreateJobObject(IntPtr.Zero, null);
        if (handle == IntPtr.Zero)
        {
            App.LogCrash(new InvalidOperationException("CreateJobObject 失败"), "ProcessLifetimeGuard");
            return handle;
        }

        var info = new JOBOBJECT_EXTENDED_LIMIT_INFORMATION();
        info.BasicLimitInformation.LimitFlags = JOBOBJECT_LIMIT_KILL_ON_JOB_CLOSE;
        if (!SetInformationJobObject(handle, JobObjectExtendedLimitInformation,
                ref info, (uint)Marshal.SizeOf(info)))
        {
            App.LogCrash(new InvalidOperationException("SetInformationJobObject 失败"), "ProcessLifetimeGuard");
        }

        return handle;
    }

    /// <summary>把已启动的子进程分配给 Job。失败仅记录日志，不阻断业务流程。</summary>
    internal static void AssignToJob(Process process)
    {
        if (JobHandle == IntPtr.Zero)
        {
            return;
        }

        try
        {
            // 已退出的进程没有可分配对象（AssignProcessToJobObject 对其必然失败）。
            // 注意方向：原实现写成 !HasExited，会让所有运行中的子进程直接 return，
            // Job 兜底形同虚设（2026-11-03 复核发现）。
            if (process.HasExited)
            {
                return;
            }

            if (!AssignProcessToJobObject(JobHandle, process.Handle))
            {
                // 调试器/某些宿主会把进程并入自己的 Job 导致拒绝；此时退出仍依赖取消路径
                App.LogInfo($"子进程加入 Job 失败（Win32 {Marshal.GetLastWin32Error()}），退出时依赖取消路径");
            }
        }
        catch (Exception ex)
        {
            App.LogCrash(ex, "ProcessLifetimeGuard.AssignToJob");
        }
    }

    // ---------- 编码任务取消注册表 ----------

    private static readonly List<CancellationTokenSource> ActiveTasks = new();

    private static readonly object Gate = new();

    /// <summary>注册进行中的任务（各 ViewModel StartAsync 创建 _cts 后调用）。</summary>
    internal static void RegisterTask(CancellationTokenSource cts)
    {
        lock (Gate)
        {
            ActiveTasks.Add(cts);
        }
    }

    /// <summary>注销任务（StartAsync 的 finally 中、Dispose 之前调用）。</summary>
    internal static void UnregisterTask(CancellationTokenSource cts)
    {
        lock (Gate)
        {
            ActiveTasks.Remove(cts);
        }
    }

    /// <summary>取消全部已注册任务（关窗确认「取消任务并退出」时调用）。</summary>
    internal static void CancelAllTasks()
    {
        lock (Gate)
        {
            foreach (var cts in ActiveTasks.ToArray())
            {
                try
                {
                    cts.Cancel();
                }
                catch
                {
                    // 已取消/已释放的忽略
                }
            }
        }
    }

    // ---------- Win32 ----------

    private const int JobObjectExtendedLimitInformation = 9;

    private const uint JOBOBJECT_LIMIT_KILL_ON_JOB_CLOSE = 0x2000;

    [StructLayout(LayoutKind.Sequential)]
    private struct JOBOBJECT_BASIC_LIMIT_INFORMATION
    {
        public long PerProcessUserTimeLimit;
        public long PerJobUserTimeLimit;
        public uint LimitFlags;
        public UIntPtr MinimumWorkingSetSize;
        public UIntPtr MaximumWorkingSetSize;
        public uint ActiveProcessLimit;
        public UIntPtr Affinity;
        public uint PriorityClass;
        public uint SchedulingClass;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct IO_COUNTERS
    {
        public ulong ReadOperationCount;
        public ulong WriteOperationCount;
        public ulong OtherOperationCount;
        public ulong ReadTransferCount;
        public ulong WriteTransferCount;
        public ulong OtherTransferCount;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct JOBOBJECT_EXTENDED_LIMIT_INFORMATION
    {
        public JOBOBJECT_BASIC_LIMIT_INFORMATION BasicLimitInformation;
        public IO_COUNTERS IoInfo;
        public UIntPtr ProcessMemoryLimit;
        public UIntPtr JobMemoryLimit;
        public UIntPtr PeakProcessMemoryUsed;
        public UIntPtr PeakJobMemoryUsed;
    }

    [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern IntPtr CreateJobObject(IntPtr lpJobAttributes, string? lpName);

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern bool SetInformationJobObject(IntPtr hJob, int infoClass,
        ref JOBOBJECT_EXTENDED_LIMIT_INFORMATION info, uint size);

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern bool AssignProcessToJobObject(IntPtr hJob, IntPtr hProcess);
}
