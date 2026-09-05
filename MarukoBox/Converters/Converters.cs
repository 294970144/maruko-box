using Microsoft.UI;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Data;
using Microsoft.UI.Xaml.Media;
using MarukoBox.Models;
using MarukoBox.Services;

namespace MarukoBox.Converters;

/// <summary>
/// 将检测是否成功（bool）映射为 InfoBar 的严重度。
/// </summary>
public sealed class BoolToInfoBarSeverity : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, string language)
        => value is true ? InfoBarSeverity.Success : InfoBarSeverity.Error;

    public object ConvertBack(object value, Type targetType, object parameter, string language)
        => value is InfoBarSeverity.Success;
}

/// <summary>
/// 将检测是否成功（bool）映射为 FontIcon 的字形（对勾 / 警告）。
/// </summary>
public sealed class BoolToGlyph : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, string language)
        => value is true ? "\uE73E" : "\uE783";

    public object ConvertBack(object value, Type targetType, object parameter, string language)
        => throw new NotImplementedException();
}

/// <summary>
/// 将 bool 映射为可见性（true→Visible）。
/// </summary>
public sealed class BoolToVisibility : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, string language)
        => value is true ? Visibility.Visible : Visibility.Collapsed;

    public object ConvertBack(object value, Type targetType, object parameter, string language)
        => value is Visibility.Visible;
}

/// <summary>
/// 将 bool 取反（true→false）。
/// </summary>
public sealed class InvertBool : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, string language)
        => value is not true;

    public object ConvertBack(object value, Type targetType, object parameter, string language)
        => value is not true;
}

/// <summary>
/// 将 bool 取反后映射为可见性（true→Collapsed）。
/// 用于「未勾选时显示」的场景。
/// </summary>
public sealed class InvertBoolToVisibility : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, string language)
        => value is true ? Visibility.Collapsed : Visibility.Visible;

    public object ConvertBack(object value, Type targetType, object parameter, string language)
        => value is not Visibility.Visible;
}

/// <summary>
/// 将字符串映射为可见性（空/null→Collapsed，非空→Visible）。
/// 用于「有状态消息时才显示」的场景。
/// </summary>
public sealed class EmptyToCollapsed : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, string language)
        => string.IsNullOrEmpty(value as string) ? Visibility.Collapsed : Visibility.Visible;

    public object ConvertBack(object value, Type targetType, object parameter, string language)
        => throw new NotImplementedException();
}

/// <summary>
/// 将媒体流类型映射为色标画刷（视频=绿 / 音频=蓝 / 字幕=橙 / 其它=灰）。
/// 用于「抽取」页轨道列表的类型标签。
/// </summary>
public sealed class StreamTypeToBrush : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, string language)
    {
        if (value is StreamType t)
        {
            return t switch
            {
                StreamType.Video => new SolidColorBrush(ColorHelper.FromArgb(255, 122, 199, 122)),
                StreamType.Audio => new SolidColorBrush(ColorHelper.FromArgb(255, 122, 162, 232)),
                StreamType.Subtitle => new SolidColorBrush(ColorHelper.FromArgb(255, 232, 170, 92)),
                _ => new SolidColorBrush(ColorHelper.FromArgb(255, 150, 150, 150))
            };
        }

        return new SolidColorBrush(Colors.Gray);
    }

    public object ConvertBack(object value, Type targetType, object parameter, string language)
        => throw new NotImplementedException();
}

/// <summary>
/// 将封装输入类型（<see cref="MuxKind"/>）映射为色标画刷（视频=绿 / 音频=蓝 / 字幕=橙）。
/// 配色与 <see cref="StreamTypeToBrush"/> 对齐，用于「封装」页轨道列表的类型标签。
/// </summary>
public sealed class MuxKindToBrush : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, string language)
    {
        if (value is MuxKind k)
        {
            return k switch
            {
                MuxKind.Video => new SolidColorBrush(ColorHelper.FromArgb(255, 122, 199, 122)),
                MuxKind.Audio => new SolidColorBrush(ColorHelper.FromArgb(255, 122, 162, 232)),
                MuxKind.Subtitle => new SolidColorBrush(ColorHelper.FromArgb(255, 232, 170, 92)),
                _ => new SolidColorBrush(ColorHelper.FromArgb(255, 150, 150, 150))
            };
        }

        return new SolidColorBrush(Colors.Gray);
    }

    public object ConvertBack(object value, Type targetType, object parameter, string language)
        => throw new NotImplementedException();
}

/// <summary>
/// 按用户级别控制控件可见性：当前级别 ≥ ConverterParameter 指定的最低级别时显示。
/// value 绑定当前 <see cref="UserLevel"/>（枚举或字符串代码均可）；
/// parameter 为所需最低级别（Default / Expert / Developer，大小写不敏感）。
/// 例：ConverterParameter='Expert' 表示高级及以上可见，普通（小白）级别隐藏。
/// </summary>
public sealed class UserLevelToVisibility : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, string language)
    {
        var current = value switch
        {
            UserLevel level => level,
            string s => UserLevels.Parse(s),
            _ => UserLevel.Default
        };
        var required = UserLevels.Parse(parameter as string);
        return current >= required ? Visibility.Visible : Visibility.Collapsed;
    }

    public object ConvertBack(object value, Type targetType, object parameter, string language)
        => throw new NotImplementedException();
}

/// <summary>
/// 仅当用户级别「等于」ConverterParameter 指定级别时显示。
/// 用于普通（小白）专属的简化控件：升级后隐藏，避免与完整参数面板重复。
/// </summary>
public sealed class UserLevelEqualsToVisibility : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, string language)
    {
        var current = value switch
        {
            UserLevel level => level,
            string s => UserLevels.Parse(s),
            _ => UserLevel.Default
        };
        var required = UserLevels.Parse(parameter as string);
        return current == required ? Visibility.Visible : Visibility.Collapsed;
    }

    public object ConvertBack(object value, Type targetType, object parameter, string language)
        => throw new NotImplementedException();
}
