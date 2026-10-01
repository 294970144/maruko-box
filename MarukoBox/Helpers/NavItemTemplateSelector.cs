using MarukoBox.Models;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;

namespace MarukoBox.Helpers;

/// <summary>
/// 侧栏导航项的模板选择器：功能页走普通模板，固定系统项（设置 / 关于）
/// 走带顶部分组分隔线与徽标位的模板。
/// </summary>
public sealed class NavItemTemplateSelector : DataTemplateSelector
{
    /// <summary>功能页模板。</summary>
    public DataTemplate? Feature { get; set; }

    /// <summary>固定系统项模板（设置 / 关于）。</summary>
    public DataTemplate? System { get; set; }

    protected override DataTemplate? SelectTemplateCore(object item) =>
        item is NavItemModel model && model.IsFixed ? System : Feature;

    protected override DataTemplate? SelectTemplateCore(object item, DependencyObject container) =>
        SelectTemplateCore(item);
}
