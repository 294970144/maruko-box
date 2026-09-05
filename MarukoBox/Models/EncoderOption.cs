namespace MarukoBox.Models;

/// <summary>
/// 编码器下拉选项，用于设置页与视频页的 ComboBox 绑定。
/// </summary>
public class EncoderOption
{
    /// <summary>对应的编码器类型。</summary>
    public EncoderType Type { get; init; }

    /// <summary>UI 显示名称。</summary>
    public string Name { get; init; } = string.Empty;
}
