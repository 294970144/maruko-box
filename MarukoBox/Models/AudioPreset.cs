namespace MarukoBox.Models;

/// <summary>
/// 音频转码参数预设。「音频」页的编码器 / 比特率 / 声道 / 采样率 选项汇总于此，
/// 由 <c>FfmpegService.BuildAudioArguments</c> 转换为 ffmpeg 命令行。
/// </summary>
public class AudioPreset
{
    /// <summary>目标编码器：copy（复制）/ aac / opus / flac / mp3。</summary>
    public string Codec { get; set; } = "copy";

    /// <summary>目标比特率 (kbps)。0 表示由编码器自动决定。</summary>
    public int BitrateKbps { get; set; }

    /// <summary>目标声道数。0 表示保持原始声道。</summary>
    public int Channels { get; set; }

    /// <summary>目标采样率 (Hz)。0 表示保持原始采样率。</summary>
    public int SampleRate { get; set; }
}
