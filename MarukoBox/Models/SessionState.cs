namespace MarukoBox.Models;

/// <summary>
/// 「保持习惯」功能的会话快照：视频页的全部编码参数。
/// 退出时由 MainWindow 捕获写入 session.json，下次启动时恢复，
/// 实现「每次打开都和上次的设置一样」。
/// <para>
/// v1.4.1 起改为直接序列化整个 <see cref="EncodeSettings"/>：
/// 此前逐字段手工搬运（且漏了 CpuTune / AqMode / PsyRd / KeyInt / BFrames 等），
/// 只要给这些参数补上 UI 就会出现「部分参数不记忆」的缺口。整体序列化一劳永逸。
/// </para>
/// </summary>
public class SessionState
{
    /// <summary>
    /// 编码参数全量快照。InputPath / OutputPath 不参与持久化（逐项编码时的临时值）。
    /// </summary>
    public EncodeSettings Settings { get; set; } = new();

    /// <summary>「普通」级质量档（low/medium/high/veryhigh），纯 UI 选择状态。</summary>
    public string QualityPreset { get; set; } = QualityPresets.Default;

    /// <summary>输出文件夹（VideoViewModel 独有，不在 EncodeSettings 里）。</summary>
    public string OutputDir { get; set; } = string.Empty;

    /// <summary>
    /// v1.4.0 及更早的扁平格式快照。仅用于把旧 session.json 迁移到新格式，
    /// 避免升级后用户「保持习惯」的参数被整体清空。
    /// </summary>
    public sealed class Legacy
    {
        public string Encoder { get; set; } = "Auto";
        public string RateControl { get; set; } = "cqp";
        public string CpuMode { get; set; } = "crf";
        public string QualityPreset { get; set; } = QualityPresets.Default;
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
        public bool KeepOriginalResolution { get; set; } = true;
        public int Width { get; set; } = 960;
        public int Height { get; set; } = 540;
        public string Container { get; set; } = "mp4";
        public string AudioMode { get; set; } = "copy";
        public string SubtitleMode { get; set; } = "copy";
        public string OutputDir { get; set; } = string.Empty;
        public string AfterCompletion { get; set; } = "none";

        /// <summary>把扁平旧格式转换为新的 <see cref="SessionState"/>。</summary>
        public SessionState ToSessionState() => new()
        {
            QualityPreset = QualityPreset,
            OutputDir = OutputDir,
            Settings = new EncodeSettings
            {
                Encoder = Enum.TryParse<EncoderType>(Encoder, out var enc) ? enc : EncoderType.Auto,
                RateControl = RateControl,
                CpuMode = CpuMode,
                Crf = Crf,
                Quality = Quality,
                BitrateKbps = BitrateKbps,
                MaxBitrateKbps = MaxBitrateKbps,
                BufferSizeKbps = BufferSizeKbps,
                GpuPreset = GpuPreset,
                GpuTune = GpuTune,
                Profile = Profile,
                CpuPreset = CpuPreset,
                CustomArgs = CustomArgs,
                KeepOriginalResolution = KeepOriginalResolution,
                Width = Width,
                Height = Height,
                Container = Container,
                AudioMode = AudioMode,
                SubtitleMode = SubtitleMode,
                AfterCompletion = AfterCompletion
            }
        };
    }
}
