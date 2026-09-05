namespace MarukoBox.Models;

/// <summary>
/// 通用下拉选项（值 + 显示名），用于视频页各 ComboBox。
/// </summary>
public class OptionEntry
{
    /// <summary>实际值（写入设置）。</summary>
    public string Value { get; init; } = string.Empty;

    /// <summary>UI 显示名称。</summary>
    public string Name { get; init; } = string.Empty;
}
