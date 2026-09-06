using MarukoBox.Services;
using Microsoft.UI.Xaml.Controls;

namespace MarukoBox.Pages;

/// <summary>
/// 关于页。
/// </summary>
public sealed partial class AboutPage : Page
{
    public AboutPage()
    {
        InitializeComponent();
        // v1.4.1（E1）修复：此前硬编码 "0.1.0 (Preview)"，与实际发布版本（1.4.1）严重不符。
        // 改为读取程序集版本，杜绝漂移。
        VersionText.Text = UpdateService.GetAppVersionStatic();
    }
}
