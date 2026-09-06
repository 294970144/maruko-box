namespace MarukoBox.Models;

/// <summary>
/// 一次编码任务所需的全部参数。
/// 字段划分与设计稿「视频页」的控件一一对应。
/// </summary>
public class EncodeSettings
{
    // ==================== 文件 ====================

    /// <summary>输入视频文件路径。</summary>
    public string InputPath { get; set; } = string.Empty;

    /// <summary>输出文件路径（不含扩展名时由 <see cref="Container"/> 补齐）。</summary>
    public string OutputPath { get; set; } = string.Empty;

    // ==================== 编码器 ====================

    /// <summary>视频编码器。</summary>
    public EncoderType Encoder { get; set; } = EncoderType.Auto;

    /// <summary>多显卡时选择第几张 GPU（0 = 自动/第一张）。</summary>
    public int GpuDevice { get; set; }

    // ==================== GPU 编码器参数 ====================

    /// <summary>
    /// 码率控制模式：cbr / vbr / cqp / 2pass。
    /// v1.4.1 起默认 cqp（恒定质量）——与产品定位「恒定质量优先」一致，
    /// 也避免「普通」级选了质量档却仍走 vbr 默认码率导致档位失效（见 B1）。
    /// </summary>
    public string RateControl { get; set; } = "cqp";

    /// <summary>目标码率 (kbps)，用于 cbr / vbr / 2pass。</summary>
    public int BitrateKbps { get; set; } = 4000;

    /// <summary>最大码率 (kbps)。</summary>
    public int MaxBitrateKbps { get; set; } = 4000;

    /// <summary>缓冲区大小 (kbps)。</summary>
    public int BufferSizeKbps { get; set; } = 8000;

    /// <summary>
    /// 恒定质量值 (CQP)，0 = 无损，51 = 最差。用于 cqp 模式。
    /// 默认 22 = 质量档「高」，与 SelectedQualityPreset 默认值保持一致。
    /// </summary>
    public int Quality { get; set; } = 22;

    /// <summary>NVENC Preset：p1(最快) ~ p7(最慢)。</summary>
    public int GpuPreset { get; set; } = 4;

    /// <summary>NVENC Tune：hq / ll / ull / lossless。</summary>
    public string GpuTune { get; set; } = "hq";

    /// <summary>H.265 Profile：main / main10 / rext。</summary>
    public string Profile { get; set; } = "main";

    /// <summary>是否启用多遍编码（2-pass）。</summary>
    public bool Multipass { get; set; } = true;

    /// <summary>是否启用空间自适应量化 (Spatial AQ)。</summary>
    public bool SpatialAq { get; set; } = true;

    /// <summary>Spatial AQ 强度，1 ~ 15。</summary>
    public int AqStrength { get; set; } = 8;

    /// <summary>码率控制前瞻帧数，0 ~ 100。</summary>
    public int RcLookahead { get; set; } = 32;

    /// <summary>B 帧数量，0 ~ 8。</summary>
    public int BFrames { get; set; } = 4;

    /// <summary>参考帧数量。</summary>
    public int RefFrames { get; set; } = 4;

    /// <summary>是否强制 IDR 关键帧。</summary>
    public bool ForcedIdr { get; set; }

    // ==================== CPU 编码器参数 ====================

    /// <summary>x264/x265 模式：crf / 2pass / custom。</summary>
    public string CpuMode { get; set; } = "crf";

    /// <summary>命令自定义模式的原始 ffmpeg 视频参数（仅视频编码器部分，不含 -i / -c:v / 输出）。</summary>
    public string CustomArgs { get; set; } = string.Empty;

    /// <summary>CRF 值，0 ~ 51。默认 22 = 质量档「高」。</summary>
    public int Crf { get; set; } = 22;

    /// <summary>x264/x265 Preset：ultrafast ~ placebo。</summary>
    public string CpuPreset { get; set; } = "medium";

    /// <summary>x264/x265 Tune：film / animation / grain 等。</summary>
    public string CpuTune { get; set; } = "animation";

    /// <summary>AQ 模式，0 ~ 3。</summary>
    public int AqMode { get; set; } = 2;

    /// <summary>AQ 强度，0 ~ 3。</summary>
    public double CpuAqStrength { get; set; } = 0.8;

    /// <summary>psy-rd 参数，格式 "强度:阈值"，例如 "0.3:0"。</summary>
    public string PsyRd { get; set; } = "0.3:0";

    /// <summary>最大关键帧间隔。</summary>
    public int KeyInt { get; set; } = 250;

    /// <summary>最小关键帧间隔。</summary>
    public int MinKeyInt { get; set; } = 25;

    // ==================== 通用参数 ====================

    /// <summary>起始帧（0 = 从头）。</summary>
    public int StartFrame { get; set; }

    /// <summary>编码帧数（0 = 全部）。</summary>
    public int FrameCount { get; set; }

    /// <summary>输出宽度（像素）。</summary>
    public int Width { get; set; } = 960;

    /// <summary>输出高度（像素）。</summary>
    public int Height { get; set; } = 540;

    /// <summary>是否保持原始分辨率（忽略 Width/Height）。</summary>
    public bool KeepOriginalResolution { get; set; } = true;

    // ==================== 输出设置 ====================

    /// <summary>容器格式：mp4 / mkv / mov / m4v。</summary>
    public string Container { get; set; } = "mp4";

    /// <summary>音轨处理：copy / aac128 / aac192 / opus128 / mute。</summary>
    public string AudioMode { get; set; } = "copy";

    /// <summary>
    /// 字幕处理：copy / drop。
    /// TODO: 注释曾声称支持 burn（烧录），但 <c>FfmpegService.BuildAudioArgs</c>
    /// 只实现了 copy 与 drop，烧录未实现——此处按实际能力修正注释。
    /// </summary>
    public string SubtitleMode { get; set; } = "copy";

    /// <summary>
    /// 编码完成后动作：none / shutdown / hibernate / exit。
    /// v1.4.1 起由 VideoViewModel.StartAsync 真正消费（此前仅记录不执行）。
    /// 注意：本字段不落 config.json（AppConfig 无对应项），仅存在于当前会话
    /// 与「保持习惯」的 session.json 中——关机/休眠属于一次性意图，不宜长期记忆。
    /// </summary>
    public string AfterCompletion { get; set; } = "none";
}
