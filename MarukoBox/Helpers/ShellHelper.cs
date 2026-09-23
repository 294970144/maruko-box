using System.Diagnostics;
using System.IO;

namespace MarukoBox.Helpers;

/// <summary>
/// Windows 文件管理器（explorer.exe）辅助：定位并选中文件，或打开目录。
/// </summary>
public static class ShellHelper
{
    /// <summary>
    /// 打开输出位置：有产出文件则选中该文件；否则打开输出目录
    /// （目录未指定时回退到源文件所在目录）。
    /// </summary>
    public static void OpenOutputLocation(string? lastFile, string? outputDir, string? sourcePath)
    {
        if (!string.IsNullOrWhiteSpace(lastFile))
        {
            OpenFileLocation(lastFile);
            return;
        }

        var dir = !string.IsNullOrWhiteSpace(outputDir)
            ? outputDir
            : (!string.IsNullOrWhiteSpace(sourcePath) ? Path.GetDirectoryName(sourcePath) : null);
        OpenFolder(dir);
    }

    /// <summary>在文件管理器中选中并定位指定文件。</summary>
    public static void OpenFileLocation(string filePath)
    {
        if (string.IsNullOrWhiteSpace(filePath))
        {
            return;
        }

        try
        {
            if (File.Exists(filePath))
            {
                Process.Start(new ProcessStartInfo
                {
                    FileName = "explorer.exe",
                    Arguments = $"/select,\"{filePath}\"",
                    UseShellExecute = true
                });
            }
            else
            {
                OpenFolder(Path.GetDirectoryName(filePath));
            }
        }
        catch
        {
            // 文件管理器打开失败（极少发生）时静默忽略，不影响主流程。
        }
    }

    /// <summary>打开目录；目录不存在时向上回退到首个存在的父目录。</summary>
    public static void OpenFolder(string? dir)
    {
        if (string.IsNullOrWhiteSpace(dir))
        {
            return;
        }

        try
        {
            var target = dir;
            while (!string.IsNullOrEmpty(target) && !Directory.Exists(target))
            {
                target = Path.GetDirectoryName(target);
            }

            if (string.IsNullOrEmpty(target))
            {
                return;
            }

            Process.Start(new ProcessStartInfo
            {
                FileName = target,
                UseShellExecute = true
            });
        }
        catch
        {
            // 同上，静默忽略。
        }
    }
}
