namespace MarukoBox;

/// <summary>
/// 测试桩：替代真实 WinUI App 类，仅提供日志方法，便于无 UI 环境下复用服务层源码。
/// </summary>
public static class App
{
    public static void LogCrash(Exception ex, string where)
    {
        Console.WriteLine($"[CRASH] {where}: {ex.GetType().Name}: {ex.Message}");
        Console.WriteLine(ex.StackTrace);
    }

    public static void LogInfo(string message) => Console.WriteLine($"[INFO] {message}");
}
