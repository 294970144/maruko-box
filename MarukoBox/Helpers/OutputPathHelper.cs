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
}
