namespace MarukoBox.Models;

/// <summary>
/// 「普通」级恒定质量四档的映射与落地。
/// <para>
/// 抽成独立静态模型的原因（v1.4.1 / B1 回归）：质量档此前作为
/// <c>VideoViewModel</c> 的私有函数存在，Harness（无 UI 的控制台脚手架）
/// 无法触达，导致「四档在 GPU 路径下完全失效」的缺陷一路带到发布。
/// 独立后可直接对 <see cref="Apply"/> 与 <c>FfmpegService.BuildArguments</c>
/// 的组合做回归断言。
/// </para>
/// </summary>
public static class QualityPresets
{
    /// <summary>四档的 UI 取值（低/中/高/非常高）。</summary>
    public const string Low = "low";
    public const string Medium = "medium";
    public const string High = "high";
    public const string VeryHigh = "veryhigh";

    /// <summary>默认档位。</summary>
    public const string Default = High;

    /// <summary>
    /// 质量档 → CRF/CQP 数值映射。数值越低质量越高、体积越大。
    /// 低=30、中=26、高=22、非常高=18；未知值回落「中」。
    /// </summary>
    public static int ToValue(string? preset) => preset switch
    {
        Low => 30,
        High => 22,
        VeryHigh => 18,
        _ => 26
    };

    /// <summary>
    /// 把质量档真正落到编码参数上。
    /// <para>
    /// 关键点（B1 的修复核心）：只写 CRF / CQP 数值是不够的——GPU 分支仅在
    /// <c>RateControl == "cqp"</c> 时才读取 <c>Quality</c>，CPU 分支仅在
    /// <c>CpuMode == "crf"</c> 时才读取 <c>Crf</c>。因此必须同时切换模式，
    /// 否则选了「非常高」却仍走 <c>-rc vbr -b:v 4000k</c>，四档输出完全相同。
    /// </para>
    /// </summary>
    /// <param name="settings">目标编码参数。</param>
    /// <param name="preset">质量档取值；null 或空按「中」处理。</param>
    public static void Apply(EncodeSettings settings, string? preset)
    {
        var value = ToValue(preset);
        settings.Crf = value;      // CPU（x264/x265）路径
        settings.Quality = value;  // GPU（NVENC/AMF/QSV）路径

        // 让上面两个数值真正被命令行读到
        settings.RateControl = "cqp";
        settings.CpuMode = "crf";
    }
}
