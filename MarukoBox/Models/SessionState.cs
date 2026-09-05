using System.Text.Json.Serialization;

namespace MarukoBox.Models;

/// <summary>
/// 「保持习惯」功能的会话快照：视频页的全部编码参数。
/// 退出时由 MainWindow 捕获写入 session.json，下次启动时恢复，
/// 实现「每次打开都和上次的设置一样」。
/// </summary>
public class SessionState
{
    // ---------- 编码器与模式 ----------
    public string Encoder { get; set; } = "Auto";
    public string RateControl { get; set; } = "2pass";
    public string CpuMode { get; set; } = "crf";
    public string QualityPreset { get; set; } = "high";

    // ---------- 数值参数 ----------
    public int Crf { get; set; } = 22;
    public int Quality { get; set; } = 22;
    public int BitrateKbps { get; set; } = 4000;
    public int MaxBitrateKbps { get; set; } = 4000;
    public int BufferSizeKbps { get; set; } = 8000;
    public int GpuPreset { get; set; } = 4;
    public string GpuTune { get; set; } = "hq";
    public string Profile { get; set; } = "main";
    public string CpuPreset { get; set; } = "medium";
    public string CustomArgs { get; set; } = string.Empty;

    // ---------- 输出 ----------
    public bool KeepOriginalResolution { get; set; } = true;
    public int Width { get; set; } = 960;
    public int Height { get; set; } = 540;
    public string Container { get; set; } = "mp4";
    public string AudioMode { get; set; } = "copy";
    public string SubtitleMode { get; set; } = "copy";
    public string OutputDir { get; set; } = string.Empty;
    public string AfterCompletion { get; set; } = "none";
}
