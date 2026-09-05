using System.Text.Json;

namespace MarukoBox.Services;

/// <summary>
/// 应用配置的持久化接口。
/// 配置以 JSON 形式保存在本地应用数据目录，跨会话保留用户设置。
/// </summary>
public interface IConfigService
{
    /// <summary>加载配置；文件不存在或解析失败时返回带默认值的配置。</summary>
    AppConfig Load();

    /// <summary>保存配置到磁盘。</summary>
    void Save(AppConfig config);
}

/// <summary>
/// 用户级别：控制界面控件显示的详细程度。
/// 存储于 <see cref="AppConfig.UserLevel"/>（字符串代码），界面显示用中文。
/// </summary>
public enum UserLevel
{
    /// <summary>默认（小白）：只显示最常用功能。</summary>
    Default = 0,

    /// <summary>高手：在默认基础上显示进阶编码参数。</summary>
    Expert = 1,

    /// <summary>程序员：显示全部功能（含自定义 ffmpeg 参数），即完整界面。</summary>
    Developer = 2
}

/// <summary>UserLevel 的解析与显示辅助。</summary>
public static class UserLevels
{
    /// <summary>配置代码（default/expert/developer，大小写不敏感）→ 枚举；无效值回落 Default。</summary>
    public static UserLevel Parse(string? code) => code?.Trim().ToLowerInvariant() switch
    {
        "expert" => UserLevel.Expert,
        "developer" => UserLevel.Developer,
        _ => UserLevel.Default
    };

    /// <summary>枚举 → 配置存储代码。</summary>
    public static string ToCode(UserLevel level) => level switch
    {
        UserLevel.Expert => "expert",
        UserLevel.Developer => "developer",
        _ => "default"
    };

    /// <summary>枚举 → 设置页下拉的中文显示名。</summary>
    public static string ToDisplay(UserLevel level) => level switch
    {
        UserLevel.Expert => "高手",
        UserLevel.Developer => "程序员",
        _ => "默认"
    };

    /// <summary>设置页中文显示名 → 配置代码。</summary>
    public static string DisplayToCode(string display) => display switch
    {
        "高手" => "expert",
        "程序员" => "developer",
        _ => "default"
    };
}

/// <summary>
/// 跨会话保留的用户设置。
/// 字段与设置页控件一一对应。
/// </summary>
public class AppConfig
{
    /// <summary>ffmpeg.exe 路径。为空时由 <see cref="ConfigService.ResolveFfmpegPath"/> 探测。</summary>
    public string FfmpegPath { get; set; } = string.Empty;

    /// <summary>默认视频编码器：Auto / NvencHevc / NvencH264 / AmfHevc / QsvHevc / X264 / X265。</summary>
    public string DefaultEncoder { get; set; } = "Auto";

    /// <summary>界面主题：System / Light / Dark。</summary>
    public string Theme { get; set; } = "System";

    /// <summary>默认输出目录。为空时输出到源文件同目录。</summary>
    public string OutputDirectory { get; set; } = string.Empty;

    /// <summary>编码完成后的动作：none / shutdown / hibernate / exit。</summary>
    public string AfterCompletion { get; set; } = "none";

    /// <summary>多 GPU 时使用的设备序号（0 = 自动/第一张）。</summary>
    public int GpuDevice { get; set; }

    /// <summary>用户级别：default（默认/小白）/ expert（高手）/ developer（程序员）。
    /// 控制各页面控件显示的详细程度，切换后重启生效。</summary>
    public string UserLevel { get; set; } = "default";
}

/// <inheritdoc cref="IConfigService"/>
public sealed class ConfigService : IConfigService
{
    private static readonly string ConfigDirectory =
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "MarukoBox");

    private static readonly string ConfigPath = Path.Combine(ConfigDirectory, "config.json");

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping
    };

    /// <summary>内置 ffmpeg.exe 的期望路径：安装目录下的 ffmpeg\ffmpeg.exe。</summary>
    /// <remarks>
    /// 由安装包构建脚本（build-installer.ps1）捆绑，或由「检查更新」功能下载落盘。
    /// </remarks>
    public static string BundledFfmpegPath =>
        Path.Combine(AppContext.BaseDirectory, "ffmpeg", "ffmpeg.exe");

    /// <summary>内置 ffprobe.exe 的期望路径。</summary>
    public static string BundledFfprobePath =>
        Path.Combine(AppContext.BaseDirectory, "ffmpeg", "ffprobe.exe");

    /// <summary>内置 ffmpeg 是否可用。</summary>
    public static bool HasBundledFfmpeg => File.Exists(BundledFfmpegPath);

    /// <summary>内置 ffmpeg 的版本标记（构建/更新时写入 ffmpeg\VERSION；无内置返回空字符串）。</summary>
    public static string GetBundledVersion()
    {
        try
        {
            var marker = Path.Combine(AppContext.BaseDirectory, "ffmpeg", "VERSION");
            return File.Exists(marker) ? File.ReadAllText(marker).Trim() : string.Empty;
        }
        catch
        {
            return string.Empty;
        }
    }

    /// <inheritdoc/>
    public AppConfig Load()
    {
        AppConfig config;
        try
        {
            if (!File.Exists(ConfigPath))
            {
                config = CreateDefault();
            }
            else
            {
                var json = File.ReadAllText(ConfigPath);
                config = JsonSerializer.Deserialize<AppConfig>(json) ?? CreateDefault();
            }
        }
        catch
        {
            // 配置损坏时回退到默认，避免应用崩溃。
            config = CreateDefault();
        }

        // 自动纠偏：内置优先。配置路径失效、指向旧开发路径或内置可用时，
        // 统一重解析出实际生效路径，全应用所有页面无需各自判断。
        RecoverBundledBackup();
        config.FfmpegPath = ResolveFfmpegPath(config.FfmpegPath);
        return config;
    }

    /// <inheritdoc/>
    public void Save(AppConfig config)
    {
        try
        {
            Directory.CreateDirectory(ConfigDirectory);
            var json = JsonSerializer.Serialize(config, JsonOptions);
            File.WriteAllText(ConfigPath, json);
        }
        catch
        {
            // 写入失败（如权限问题）时静默忽略，不影响主流程。
        }
    }

    /// <summary>构造一份带合理默认值的配置。</summary>
    public static AppConfig CreateDefault()
    {
        return new AppConfig
        {
            FfmpegPath = ResolveFfmpegPath()
        };
    }

    /// <summary>
    /// 更新中断的自愈：UpdateService 替换内置 ffmpeg 时采用「ffmpeg\ → ffmpeg.old → 移入新版」
    /// 流程，若在两步 move 之间被中断，此处按状态恢复或清理备份，保证内置始终可用。
    /// </summary>
    private static void RecoverBundledBackup()
    {
        try
        {
            var current = Path.Combine(AppContext.BaseDirectory, "ffmpeg");
            var backup = current + ".old";
            if (!Directory.Exists(backup))
            {
                return;
            }

            if (!File.Exists(BundledFfmpegPath))
            {
                // 新版未移入：恢复备份的旧版
                Directory.Move(backup, current);
            }
            else
            {
                // 新版已就位：备份是残留，清掉
                Directory.Delete(backup, recursive: true);
            }
        }
        catch
        {
            // 自愈失败不致命，保持现状
        }
    }

    /// <summary>探测 ffmpeg.exe 的默认位置：内置优先，其次 PATH。</summary>
    public static string ResolveFfmpegPath() => ResolveFfmpegPath(null);

    /// <summary>
    /// 解析实际生效的 ffmpeg.exe 路径，优先级：
    /// ① 安装目录内置的 ffmpeg\ffmpeg.exe（开箱即用，随「检查更新」升级）
    /// ② 显式配置且文件存在（用户自定义路径）
    /// ③ PATH 环境变量查找。
    /// </summary>
    /// <param name="configuredPath">用户配置的路径；null 或空表示未配置。</param>
    public static string ResolveFfmpegPath(string? configuredPath)
    {
        // 1) 内置：安装目录下的 ffmpeg\ffmpeg.exe
        var bundled = BundledFfmpegPath;
        if (File.Exists(bundled))
        {
            return bundled;
        }

        // 2) 显式配置且文件存在
        if (!string.IsNullOrWhiteSpace(configuredPath))
        {
            var trimmed = configuredPath.Trim();
            if (File.Exists(trimmed))
            {
                return trimmed;
            }
        }

        // 3) 在 PATH 中查找
        try
        {
            var psi = new System.Diagnostics.ProcessStartInfo
            {
                FileName = "where",
                Arguments = "ffmpeg.exe",
                UseShellExecute = false,
                RedirectStandardOutput = true,
                CreateNoWindow = true
            };
            using var proc = System.Diagnostics.Process.Start(psi);
            if (proc is not null)
            {
                var output = proc.StandardOutput.ReadToEnd();
                proc.WaitForExit();
                var firstLine = output.Split('\n', StringSplitOptions.RemoveEmptyEntries)
                    .FirstOrDefault()?.Trim();
                if (!string.IsNullOrEmpty(firstLine) && File.Exists(firstLine))
                {
                    return firstLine;
                }
            }
        }
        catch
        {
            // PATH 查找失败不致命
        }

        return string.Empty;
    }
}
