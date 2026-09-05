using Microsoft.Win32;
using System.Diagnostics;
using System.IO.Compression;
using System.Text;

namespace MarukoBoxSetup;

/// <summary>
/// 小丸工具箱 2026 自解压安装程序（Native AOT，零 .NET 依赖，零 UAC，零证书）。
///
/// payload 以 zip 形式追加在本 exe 文件末尾，布局：
///   [ ... exe 本体 ... ][ zip 数据 ][ 8 字节 zip 长度 (Int64 LE) ][ magic "MARUKOPAYLOAD01" ]
/// PE 加载器会忽略文件尾部附加数据，因此 exe 依然可正常执行。
/// </summary>
internal static class Program
{
    private const string Magic = "MARUKOPAYLOAD01";
    private const string AppName = "小丸工具箱 2026";
    private const string AppExeName = "MarukoBox.exe";
    private const string AppVersion = "1.0.0.0";
    private const string UninstallRegKey = @"Software\Microsoft\Windows\CurrentVersion\Uninstall\MarukoBox";

    private static readonly string InstallDir = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "Programs", "MarukoBox");

    private static readonly string StartMenuLnk = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
        "Microsoft", "Windows", "Start Menu", "Programs", AppName + ".lnk");

    private static int Main(string[] args)
    {
        try { Console.OutputEncoding = new UTF8Encoding(false); } catch { /* 控制台编码不可用时忽略 */ }

        Console.WriteLine("=========================================");
        Console.WriteLine($"   {AppName}  ·  安装程序");
        Console.WriteLine("=========================================");
        Console.WriteLine();

        // 注意：必须先解析 /silent 再处理 /uninstall，
        // 否则卸载路径拿不到 silent 标志，在重定向/无人值守环境下 ReadKey 会崩溃。
        bool silent = args.Any(a => a.Equals("/silent", StringComparison.OrdinalIgnoreCase));

        if (args.Any(a => a.Equals("/uninstall", StringComparison.OrdinalIgnoreCase) ||
                          a.Equals("-uninstall", StringComparison.OrdinalIgnoreCase)))
            return DoUninstall(silent);

        return DoInstall(silent);
    }

    // ---------------------------------------------------------------- 安装

    private static int DoInstall(bool silent)
    {
        Console.WriteLine($"安装位置：{InstallDir}");
        Console.WriteLine("（无需管理员权限，仅为当前用户安装）");
        Console.WriteLine();

        if (!silent && Directory.Exists(InstallDir))
        {
            Console.Write("检测到已安装，覆盖更新？[Y/n] ");
            var key = Console.ReadKey();
            Console.WriteLine();
            if (key.Key == ConsoleKey.N)
            {
                Console.WriteLine("已取消。");
                return 0;
            }
            Console.WriteLine();
        }

        var payload = FindPayload();
        if (payload is null)
        {
            Console.WriteLine("[错误] 安装包数据不完整（未找到内嵌 payload），请重新下载完整的安装程序。");
            if (!silent) { Console.WriteLine(); Console.Write("按任意键退出..."); Console.ReadKey(); }
            return 2;
        }

        Console.WriteLine($"[1/5] 释放程序文件（{payload.Value.Length / 1024 / 1024} MB）...");
        try
        {
            Directory.CreateDirectory(InstallDir);
            Extract(payload.Value, InstallDir);
            Console.WriteLine("      完成");
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[错误] 释放文件失败：{ex.Message}");
            if (!silent) { Console.WriteLine(); Console.Write("按任意键退出..."); Console.ReadKey(); }
            return 3;
        }

        Console.WriteLine("[2/5] 注册卸载信息（写入 HKCU，无需管理员）...");
        WriteUninstallInfo();
        Console.WriteLine("      完成");

        Console.WriteLine("[3/5] 创建开始菜单快捷方式...");
        CreateShortcut(Path.Combine(InstallDir, AppExeName), InstallDir, StartMenuLnk);
        Console.WriteLine("      完成");

        Console.WriteLine("[4/5] 检查 ffmpeg 依赖...");
        ReportFfmpeg();

        Console.WriteLine("[5/5] 收尾...");
        Console.WriteLine();
        Console.WriteLine("=========================================");
        Console.WriteLine("   安装成功！");
        Console.WriteLine("=========================================");
        Console.WriteLine();
        Console.WriteLine($"程序目录：{InstallDir}");
        Console.WriteLine("卸载方式：Windows 设置 → 应用 → 小丸工具箱 2026");
        Console.WriteLine();

        if (!silent)
        {
            Console.Write("是否立即启动？[Y/n] ");
            var k = Console.ReadKey();
            Console.WriteLine();
            if (k.Key != ConsoleKey.N)
                Launch(Path.Combine(InstallDir, AppExeName));
            else
            {
                Console.Write("按任意键退出...");
                Console.ReadKey();
            }
        }
        return 0;
    }

    // ---------------------------------------------------------------- 卸载

    private static int DoUninstall(bool silent)
    {
        if (!silent)
        {
            Console.WriteLine("正在卸载...");
            Console.WriteLine();
        }

        int failed = 0;

        Console.WriteLine("[1/3] 删除程序文件...");
        try
        {
            if (Directory.Exists(InstallDir))
            {
                Directory.Delete(InstallDir, recursive: true);
                Console.WriteLine("      完成");
            }
            else Console.WriteLine("      目录不存在，跳过");
        }
        catch (Exception ex) { Console.WriteLine($"      [警告] {ex.Message}"); failed++; }

        Console.WriteLine("[2/3] 删除开始菜单快捷方式...");
        try
        {
            // Explorer/开始菜单宿主可能短暂持有 .lnk 句柄，重试若干次再放弃。
            // 重试耗尽不算失败：处于"待删除"状态的文件会在系统重启后自动消失。
            for (int attempt = 0; attempt < 5; attempt++)
            {
                if (!File.Exists(StartMenuLnk)) break;
                try
                {
                    File.SetAttributes(StartMenuLnk, FileAttributes.Normal);
                    File.Delete(StartMenuLnk);
                }
                catch (Exception)
                {
                    if (attempt == 4) continue;   // 最后一轮也失败 → 落到下面的提示
                    Thread.Sleep(500);
                }
            }
            Console.WriteLine(File.Exists(StartMenuLnk) ? "      [提示] 快捷方式暂被系统占用，重启后将自动消失" : "      完成");
        }
        catch (Exception ex) { Console.WriteLine($"      [警告] {ex.Message}"); failed++; }

        Console.WriteLine("[3/3] 清除卸载注册信息...");
        try
        {
            Registry.CurrentUser.DeleteSubKeyTree(UninstallRegKey, throwOnMissingSubKey: false);
            Console.WriteLine("      完成");
        }
        catch (Exception ex) { Console.WriteLine($"      [警告] {ex.Message}"); failed++; }

        Console.WriteLine();
        Console.WriteLine(failed == 0 ? "卸载完成。" : "卸载完成（个别步骤有警告，可手动清理）。");
        if (!silent) { Console.Write("按任意键退出..."); Console.ReadKey(); }
        return failed == 0 ? 0 : 1;
    }

    // ------------------------------------------------------- payload 定位与解压

    private static (long Offset, long Length)? FindPayload()
    {
        string self = Environment.ProcessPath ?? "";
        if (string.IsNullOrEmpty(self) || !File.Exists(self)) return null;

        byte[] magicBytes = Encoding.ASCII.GetBytes(Magic);
        int tailSize = magicBytes.Length + 8;

        try
        {
            using var fs = new FileStream(self, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
            if (fs.Length < tailSize + 1024) return null;

            fs.Seek(-tailSize, SeekOrigin.End);
            var tail = new byte[tailSize];
            if (fs.Read(tail, 0, tailSize) != tailSize) return null;

            // 布局校验：[8 字节长度][magic]
            if (!tail.AsSpan(8, magicBytes.Length).SequenceEqual(magicBytes)) return null;

            long length = BitConverter.ToInt64(tail, 0);
            long offset = fs.Length - tailSize - length;
            if (offset <= 0 || length <= 0) return null;

            return (offset, length);
        }
        catch { return null; }
    }

    private static void Extract((long Offset, long Length) payload, string targetDir)
    {
        string self = Environment.ProcessPath ?? throw new InvalidOperationException("无法定位安装程序自身路径");

        using var fs = new FileStream(self, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
        fs.Seek(payload.Offset, SeekOrigin.Begin);

        // 用内存流中转，避免 ZipArchive 直接读大文件流时的边界问题
        var buffer = new byte[payload.Length];
        int total = 0;
        while (total < buffer.Length)
        {
            int n = fs.Read(buffer, total, buffer.Length - total);
            if (n == 0) break;
            total += n;
        }

        using var ms = new MemoryStream(buffer, 0, total, writable: false);
        using var zip = new ZipArchive(ms, ZipArchiveMode.Read);
        zip.ExtractToDirectory(targetDir, overwriteFiles: true);
    }

    // ---------------------------------------------------------------- 系统集成

    private static void WriteUninstallInfo()
    {
        try
        {
            // 卸载脚本：安装器本体有 100+ MB，不适合复制一份当卸载程序，
            // 因此生成一个轻量 ps1，并注册到「设置 → 应用」。
            var sb = new StringBuilder();
            sb.AppendLine("# 小丸工具箱 2026 卸载脚本（自动生成）");
            sb.AppendLine("Start-Sleep -Milliseconds 300");
            sb.AppendLine($"$dir = '{InstallDir}'");
            sb.AppendLine($"$lnk = '{StartMenuLnk}'");
            sb.AppendLine("if (Test-Path -LiteralPath $dir) { Remove-Item -LiteralPath $dir -Recurse -Force -ErrorAction SilentlyContinue }");
            sb.AppendLine("if (Test-Path -LiteralPath $lnk) { Remove-Item -LiteralPath $lnk -Force -ErrorAction SilentlyContinue }");
            sb.AppendLine($"Remove-Item -Path 'HKCU:\\{UninstallRegKey.Replace('\\', '\\')}' -Recurse -Force -ErrorAction SilentlyContinue");
            sb.AppendLine("Write-Host '小丸工具箱 2026 已卸载'");

            string scriptPath = Path.Combine(InstallDir, "uninstall.ps1");
            // PowerShell 5.1 按 ANSI 读取无 BOM 的 ps1，中文路径会乱码 → 必须带 BOM
            File.WriteAllText(scriptPath, sb.ToString(), new UTF8Encoding(encoderShouldEmitUTF8Identifier: true));

            using var key = Registry.CurrentUser.CreateSubKey(UninstallRegKey);
            key.SetValue("DisplayName", AppName);
            key.SetValue("DisplayVersion", AppVersion);
            key.SetValue("Publisher", "MarukoBox");
            key.SetValue("InstallLocation", InstallDir);
            key.SetValue("DisplayIcon", Path.Combine(InstallDir, AppExeName) + ",0");
            key.SetValue("UninstallString",
                $"powershell.exe -NoProfile -ExecutionPolicy Bypass -File \"{scriptPath}\"");
            key.SetValue("NoModify", 1, RegistryValueKind.DWord);
            key.SetValue("NoRepair", 1, RegistryValueKind.DWord);
        }
        catch (Exception ex)
        {
            Console.WriteLine($"      [警告] 卸载信息写入失败：{ex.Message}");
        }
    }

    private static void CreateShortcut(string targetExe, string workingDir, string lnkPath)
    {
        try
        {
            var dir = Path.GetDirectoryName(lnkPath);
            if (!string.IsNullOrEmpty(dir)) Directory.CreateDirectory(dir);

            // 两点考虑：
            // 1) 不使用 COM 互操作（AOT 下 dynamic 受限），改由 PowerShell 的 WScript.Shell 创建
            // 2) 不把中文路径拼进 -Command 命令行参数（进程参数传递存在编码风险，会导致快捷方式静默创建失败），
            //    改为写入临时 ps1（UTF-8 BOM）后用 -File 执行，确保中文路径原样送达
            var sb = new StringBuilder();
            sb.AppendLine("$ErrorActionPreference = 'Stop'");
            sb.AppendLine("$s = New-Object -ComObject WScript.Shell");
            sb.AppendLine($"$c = $s.CreateShortcut(\"{lnkPath}\")");
            sb.AppendLine($"$c.TargetPath = \"{targetExe}\"");
            sb.AppendLine($"$c.WorkingDirectory = \"{workingDir}\"");
            sb.AppendLine($"$c.Description = \"{AppName}\"");
            sb.AppendLine("$c.Save()");

            string tmpScript = Path.Combine(Path.GetTempPath(), "mb_mkshortcut.ps1");
            File.WriteAllText(tmpScript, sb.ToString(), new UTF8Encoding(encoderShouldEmitUTF8Identifier: true));

            var psi = new ProcessStartInfo("powershell.exe")
            {
                Arguments = $"-NoProfile -NonInteractive -ExecutionPolicy Bypass -File \"{tmpScript}\"",
                UseShellExecute = false,
                CreateNoWindow = true,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
            };

            using var p = Process.Start(psi);
            p?.WaitForExit(20000);

            try { File.Delete(tmpScript); } catch { /* 临时脚本清理失败无妨 */ }
        }
        catch (Exception ex)
        {
            Console.WriteLine($"      [警告] 快捷方式创建失败：{ex.Message}");
        }
    }

    private static void ReportFfmpeg()
    {
        string? found = null;
        var pathEnv = Environment.GetEnvironmentVariable("PATH") ?? "";
        foreach (var dir in pathEnv.Split(Path.PathSeparator))
        {
            if (string.IsNullOrWhiteSpace(dir)) continue;
            try
            {
                var candidate = Path.Combine(dir.Trim().Trim('"'), "ffmpeg.exe");
                if (File.Exists(candidate)) { found = candidate; break; }
            }
            catch { /* 忽略非法 PATH 项 */ }
        }

        if (found is not null)
        {
            Console.WriteLine($"      已检测到：{found}");
            Console.WriteLine("      应用会自动使用，也可在「设置」页另行指定路径。");
        }
        else
        {
            Console.WriteLine("      未检测到 ffmpeg（本工具箱的核心依赖）。");
            Console.WriteLine("      请下载 ffmpeg.exe 后，在应用「设置」页指定其路径。");
        }
    }

    private static void Launch(string exe)
    {
        try
        {
            Process.Start(new ProcessStartInfo(exe)
            {
                WorkingDirectory = InstallDir,
                UseShellExecute = true,
            });
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[警告] 启动失败：{ex.Message}");
        }
    }
}
