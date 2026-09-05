namespace MarukoBox.Models;

/// <summary>
/// GPU 硬件与 ffmpeg 后端检测结果。
/// 由 <c>GpuDetectionService</c> 填充，供设置页 InfoBar 绑定展示。
/// </summary>
public class GpuInfo
{
    // ---------- ffmpeg 后端 ----------

    /// <summary>ffmpeg.exe 是否可用（路径存在且可执行）。</summary>
    public bool FfmpegFound { get; set; }

    /// <summary>ffmpeg 版本字符串，例如 "8.1.2"。</summary>
    public string FfmpegVersion { get; set; } = "未知";

    /// <summary>ffmpeg.exe 的完整路径。</summary>
    public string FfmpegPath { get; set; } = string.Empty;

    // ---------- 显卡信息 ----------

    /// <summary>显卡型号，例如 "NVIDIA GeForce RTX 4060 Laptop GPU"。</summary>
    public string GpuName { get; set; } = "未检测到";

    /// <summary>显卡驱动版本，例如 "610.62"。</summary>
    public string DriverVersion { get; set; } = "未知";

    /// <summary>NVENC API 版本（由驱动版本推断），例如 "13.1"。</summary>
    public string NvencApiVersion { get; set; } = "未知";

    // ---------- 编码器可用性 ----------

    /// <summary>hevc_nvenc 是否可用。</summary>
    public bool HasNvencHevc { get; set; }

    /// <summary>h264_nvenc 是否可用。</summary>
    public bool HasNvencH264 { get; set; }

    /// <summary>hevc_amf (AMD) 是否可用。</summary>
    public bool HasAmf { get; set; }

    /// <summary>hevc_qsv (Intel) 是否可用。</summary>
    public bool HasQsv { get; set; }

    /// <summary>scale_cuda 滤镜是否可用（决定能否走全 GPU 缩放）。</summary>
    public bool HasCudaScale { get; set; }

    /// <summary>-hwaccel cuda 硬件解码是否可用。</summary>
    public bool HasCudaDecode { get; set; }

    // ---------- 诊断 ----------

    /// <summary>检测过程中出现的错误信息（正常时为 null）。</summary>
    public string? ErrorMessage { get; set; }

    /// <summary>检测是否顺利完成。</summary>
    public bool DetectionSucceeded { get; set; }

    // ---------- 派生属性 ----------

    /// <summary>是否存在任何可用的 GPU 编码器。</summary>
    public bool HasAnyGpuEncoder => HasNvencHevc || HasNvencH264 || HasAmf || HasQsv;

    /// <summary>
    /// 根据硬件能力推荐的编码器。优先 NVENC HEVC，其次 AMF/QSV，最后回退 x264。
    /// </summary>
    public EncoderType RecommendedEncoder =>
        HasNvencHevc ? EncoderType.NvencHevc :
        HasAmf ? EncoderType.AmfHevc :
        HasQsv ? EncoderType.QsvHevc :
        HasNvencH264 ? EncoderType.NvencH264 :
        EncoderType.X264;

    /// <summary>用于 UI 展示的一句话摘要。</summary>
    public string Summary => HasAnyGpuEncoder
        ? $"{GpuName} · 驱动 {DriverVersion} · NVENC API {NvencApiVersion}"
        : "未检测到可用的 GPU 编码器，将回退到 CPU 软件编码";
}
