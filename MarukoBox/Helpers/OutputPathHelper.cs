namespace MarukoBox.Helpers;

/// <summary>
/// 输出目录解析：用户显式指定的目录优先；未指定时回退到源文件所在目录
/// （即「当前路径」），满足「不修改则默认当前路径」的需求。
/// </summary>
public static class OutputPathHelper
{
    /// <summary>
    /// 解析最终输出目录。
    /// </summary>
    /// <param name="sourceDir">源文件所在目录（回退值）。</param>
    /// <param name="userDir">用户在界面上指定的输出文件夹；为空或空白则使用 sourceDir。</param>
    /// <returns>最终输出目录。</returns>
    public static string ResolveDir(string sourceDir, string userDir)
    {
        if (!string.IsNullOrWhiteSpace(userDir))
        {
            return userDir;
        }

        return string.IsNullOrWhiteSpace(sourceDir) ? "." : sourceDir;
    }

    /// <summary>
    /// 【M5 修复】校验并解析输出目录：用户指定了目录但**该目录已不存在**（拔掉的移动硬盘、
    /// 被删掉的文件夹、手输错的路径）时，回退到源文件所在目录，而不是把无效路径直接交给
    /// ffmpeg 让它失败——此前只有 Extract / Image / Mux / Trim 四个页面做了这层校验，
    /// Audio / Video / Subtitle 三页直接把配置里的 OutputDir 透传，配置指向已失效的盘时
    /// 这三页会直接失败（且因 H2 拿不到失败原因）。
    /// </summary>
    /// <param name="userDir">用户在界面上指定的输出文件夹（可空）。</param>
    /// <param name="sourcePath">源文件完整路径（用于回退到其所在目录；可空）。</param>
    /// <returns>可用的输出目录；无法解析时返回 null（调用方应提示用户）。</returns>
    public static string? EnsureOutputDir(string? userDir, string? sourcePath)
    {
        if (!string.IsNullOrWhiteSpace(userDir) && Directory.Exists(userDir))
        {
            return userDir;
        }

        var fallback = string.IsNullOrWhiteSpace(sourcePath)
            ? null
            : Path.GetDirectoryName(sourcePath);

        return !string.IsNullOrWhiteSpace(fallback) && Directory.Exists(fallback)
            ? fallback
            : null;
    }
}
