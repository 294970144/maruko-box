using System;

namespace MarukoBox.Models;

/// <summary>
/// 支持的视频编码器类型。GPU 编码器优先，CPU 编码器作为回退。
/// </summary>
public enum EncoderType
{
    /// <summary>自动检测：根据本机硬件选择最佳 GPU 编码器</summary>
    Auto,

    /// <summary>NVIDIA NVENC HEVC (H.265)</summary>
    NvencHevc,

    /// <summary>NVIDIA NVENC H.264</summary>
    NvencH264,

    /// <summary>AMD AMF HEVC (H.265)</summary>
    AmfHevc,

    /// <summary>Intel Quick Sync Video HEVC (H.265)</summary>
    QsvHevc,

    /// <summary>x264 软件编码 (CPU)</summary>
    X264,

    /// <summary>x265 软件编码 (CPU)</summary>
    X265
}

/// <summary>
/// EncoderType 的辅助扩展方法。
/// </summary>
public static class EncoderTypeExtensions
{
    /// <summary>是否为硬件(GPU)编码器。</summary>
    public static bool IsGpuEncoder(this EncoderType type) => type switch
    {
        EncoderType.NvencHevc or EncoderType.NvencH264
            or EncoderType.AmfHevc or EncoderType.QsvHevc => true,
        _ => false
    };

    /// <summary>转换为 ffmpeg 的 -c:v 编码器名称。</summary>
    public static string ToFfmpegCodec(this EncoderType type) => type switch
    {
        EncoderType.NvencHevc => "hevc_nvenc",
        EncoderType.NvencH264 => "h264_nvenc",
        EncoderType.AmfHevc => "hevc_amf",
        EncoderType.QsvHevc => "hevc_qsv",
        EncoderType.X264 => "libx264",
        EncoderType.X265 => "libx265",
        _ => "hevc_nvenc"   // Auto 在未解析时的默认值
    };

    /// <summary>UI 显示名称。</summary>
    public static string ToDisplayName(this EncoderType type) => type switch
    {
        EncoderType.Auto => "自动检测（推荐）",
        EncoderType.NvencHevc => "NVIDIA NVENC (HEVC)",
        EncoderType.NvencH264 => "NVIDIA NVENC (H.264)",
        EncoderType.AmfHevc => "AMD AMF (HEVC)",
        EncoderType.QsvHevc => "Intel QSV (HEVC)",
        EncoderType.X264 => "x264 (CPU)",
        EncoderType.X265 => "x265 (CPU)",
        _ => type.ToString()
    };

    /// <summary>
    /// 把 Auto 解析为具体的编码器（依据硬件检测结果）。
    /// 其他类型原样返回。
    /// </summary>
    public static EncoderType Resolve(this EncoderType type, GpuInfo gpuInfo)
    {
        if (type != EncoderType.Auto)
        {
            return type;
        }

        return gpuInfo.RecommendedEncoder;
    }
}
