using MarukoBox.Services;

namespace MarukoBox;

/// <summary>
/// 轻量级服务定位器。
/// 集中持有各服务的单例，供 ViewModel 直接引用。
/// 不引入第三方 DI 容器，保持离线可用与零额外依赖。
/// </summary>
public static class AppServices
{
    /// <summary>GPU 与 ffmpeg 后端能力检测。</summary>
    public static IGpuDetectionService GpuDetection { get; } = new GpuDetectionService();

    /// <summary>ffmpeg 编码与进度解析。</summary>
    public static IFfmpegService Ffmpeg { get; } = new FfmpegService();

    /// <summary>配置持久化。</summary>
    public static IConfigService Config { get; } = new ConfigService();
}
