namespace MarukoBox.Helpers;

/// <summary>
/// 构造输出文件名所需的上下文。
/// 各处理器按照自身能力填充字段；用不到的字段留空即可——
/// 命名时会自动丢弃空片段，不会出现 "名字__时间" 这类空洞。
/// </summary>
/// <param name="SourcePath">源文件路径（用于取「原名」）。</param>
/// <param name="Suffix">处理类型后缀，如 encoded / conv / audio / muxed / withsub / s1。</param>
/// <param name="Codec">编码格式短名，如 h264 / hevc / aac；无则留空。</param>
/// <param name="Resolution">分辨率，形如 1920x1080；未知则留空。</param>
/// <param name="Extension">输出扩展名（可带可不带前导点）。</param>
public sealed record OutputNamingContext(
    string SourcePath,
    string Suffix,
    string Codec,
    string Resolution,
    string Extension);

/// <summary>
/// 输出文件命名规则：一组预设 + 统一的构造入口。
/// <para>
/// 此前各页面各自硬编码后缀（_encoded / _conv / _audio / _muxed / _withsub），
/// 无法配置。现在统一收口到这里，设置页选择「输出文件命名规则」即可全局生效。
/// </para>
/// <para>
/// 配置里存的是中文显示名（与主题 / 用户级别一致的做法），
/// 旧的或非法的值由 <see cref="Normalize"/> 兜底为 <see cref="DefaultRule"/>。
/// </para>
/// </summary>
public static class OutputNaming
{
    /// <summary>原名。</summary>
    public const string NameOnly = "原名";

    /// <summary>原名 + 处理类型（旧行为，如 xxx_encoded）。</summary>
    public const string NameSuffix = "原名 + 处理类型";

    /// <summary>原名 + 日期（yyyyMMdd）。</summary>
    public const string NameDate = "原名 + 日期";

    /// <summary>原名 + 时间（yyyyMMdd_HHmmss，默认规则）。</summary>
    public const string NameDateTime = "原名 + 时间";

    /// <summary>原名 + 编码格式。</summary>
    public const string NameCodec = "原名 + 编码格式";

    /// <summary>原名 + 编码格式 + 分辨率。</summary>
    public const string NameCodecRes = "原名 + 编码格式 + 分辨率";

    /// <summary>原名 + 编码格式 + 时间。</summary>
    public const string NameCodecDateTime = "原名 + 编码格式 + 时间";

    /// <summary>原名 + 编码格式 + 分辨率 + 时间。</summary>
    public const string NameCodecResDateTime = "原名 + 编码格式 + 分辨率 + 时间";

    /// <summary>原名 + 分辨率。</summary>
    public const string NameRes = "原名 + 分辨率";

    /// <summary>原名 + 分辨率 + 时间。</summary>
    public const string NameResDateTime = "原名 + 分辨率 + 时间";

    /// <summary>默认规则：原名 + 时间。</summary>
    public const string DefaultRule = NameDateTime;

    /// <summary>设置页下拉的全部可选项（顺序即展示顺序）。</summary>
    public static readonly IReadOnlyList<string> Options = new[]
    {
        NameOnly,
        NameSuffix,
        NameDate,
        NameDateTime,
        NameCodec,
        NameCodecRes,
        NameCodecDateTime,
        NameCodecResDateTime,
        NameRes,
        NameResDateTime
    };

    /// <summary>归一化规则值：非法或未知回退默认规则。</summary>
    public static string Normalize(string? rule) =>
        !string.IsNullOrWhiteSpace(rule) && Options.Contains(rule) ? rule : DefaultRule;

    /// <summary>
    /// 按规则构造输出文件名（含扩展名）。
    /// 空片段会被丢弃，文件名非法字符替换为下划线。
    /// </summary>
    public static string BuildFileName(string? rule, OutputNamingContext ctx)
    {
        var name = Path.GetFileNameWithoutExtension(ctx.SourcePath ?? string.Empty);
        var now = DateTime.Now;

        var date = now.ToString("yyyyMMdd");
        var dateTime = now.ToString("yyyyMMdd_HHmmss");
        var codec = ctx.Codec ?? string.Empty;
        var res = ctx.Resolution ?? string.Empty;
        var suffix = ctx.Suffix ?? string.Empty;

        string[] segments = Normalize(rule) switch
        {
            NameOnly => new[] { name },
            NameSuffix => new[] { name, suffix },
            NameDate => new[] { name, date },
            NameDateTime => new[] { name, dateTime },
            NameCodec => new[] { name, codec },
            NameCodecRes => new[] { name, codec, res },
            NameCodecDateTime => new[] { name, codec, dateTime },
            NameCodecResDateTime => new[] { name, codec, res, dateTime },
            NameRes => new[] { name, res },
            NameResDateTime => new[] { name, res, dateTime },
            _ => new[] { name, dateTime }
        };

        var joined = string.Join("_", segments.Where(s => !string.IsNullOrWhiteSpace(s)));
        if (joined.Length == 0)
        {
            joined = "output";
        }

        var ext = (ctx.Extension ?? string.Empty).Trim();
        if (ext.Length > 0 && !ext.StartsWith('.'))
        {
            ext = "." + ext;
        }

        return Sanitize(joined) + ext;
    }

    /// <summary>
    /// 按规则构造完整输出路径：目录沿用 <see cref="OutputPathHelper.ResolveDir"/> 的语义，
    /// 并额外保证结果不会与源文件同路径（避免「原名」规则下把源文件直接覆盖掉）。
    /// </summary>
    public static string BuildOutputPath(string? rule, string sourcePath, string? userDir, OutputNamingContext ctx)
    {
        var dir = OutputPathHelper.ResolveDir(Path.GetDirectoryName(sourcePath) ?? ".", userDir ?? string.Empty);
        var file = BuildFileName(rule, ctx);
        var full = Path.Combine(dir, file);

        return IsSamePath(full, sourcePath) ? InsertSuffix(full, "out") : full;
    }

    /// <summary>设置页示例预览：用一份固定的假想上下文生成示例文件名。</summary>
    public static string Preview(string? rule) => BuildFileName(rule,
        new OutputNamingContext("示例视频.mp4", "encoded", "hevc", "1920x1080", ".mp4"));

    private static bool IsSamePath(string a, string b)
    {
        if (string.IsNullOrEmpty(a) || string.IsNullOrEmpty(b))
        {
            return false;
        }

        try
        {
            return string.Equals(Path.GetFullPath(a), Path.GetFullPath(b), StringComparison.OrdinalIgnoreCase);
        }
        catch
        {
            return string.Equals(a, b, StringComparison.OrdinalIgnoreCase);
        }
    }

    /// <summary>在扩展名之前插入一段后缀：a/b.mp4 + "out" → a/b_out.mp4。</summary>
    private static string InsertSuffix(string path, string suffix)
    {
        var dir = Path.GetDirectoryName(path) ?? string.Empty;
        var name = Path.GetFileNameWithoutExtension(path);
        var ext = Path.GetExtension(path);
        var combined = string.IsNullOrEmpty(name) ? "output" : name + "_" + suffix;
        return dir.Length == 0 ? combined + ext : Path.Combine(dir, combined + ext);
    }

    /// <summary>把 Windows 文件名非法字符替换为下划线。</summary>
    private static string Sanitize(string name)
    {
        var invalid = Path.GetInvalidFileNameChars();
        var chars = name.ToCharArray();
        for (var i = 0; i < chars.Length; i++)
        {
            if (Array.IndexOf(invalid, chars[i]) >= 0)
            {
                chars[i] = '_';
            }
        }

        var result = new string(chars).Trim().TrimEnd('.');
        return result.Length == 0 ? "output" : result;
    }
}
