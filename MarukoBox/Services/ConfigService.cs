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
/// 跨会话保留的用户设置。
/// 字段与设置页控件一一对应。
/// </summary>
public class AppConfig
{
    /// <summary>ffmpeg.exe 路径。为空时由 <see cref="ResolveFfmpegPath"/> 探测。</summary>
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

    /// <inheritdoc/>
    public AppConfig Load()
    {
        try
        {
            if (!File.Exists(ConfigPath))
            {
                return CreateDefault();
            }

            var json = File.ReadAllText(ConfigPath);
            var config = JsonSerializer.Deserialize<AppConfig>(json);
            return config ?? CreateDefault();
        }
        catch
        {
            // 配置损坏时回退到默认，避免应用崩溃。
            return CreateDefault();
        }
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
    /// 探测 ffmpeg.exe 的默认位置。
    /// 优先使用已知工作路径，其次在 PATH 中查找。
    /// </summary>
    public static string ResolveFfmpegPath()
    {
        // 1) 已知可用路径（本机已验证的 ffmpeg 8.1.2 + 驱动 610.62）
        var known = @"E:\Git\WorkBuddy\日常\gpu-encode\ffmpeg.exe";
        if (File.Exists(known))
        {
            return known;
        }

        // 2) 在 PATH 中查找
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
