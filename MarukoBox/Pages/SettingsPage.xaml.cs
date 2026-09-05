using Microsoft.UI.Xaml.Controls;
using MarukoBox.ViewModels;

namespace MarukoBox.Pages;

/// <summary>
/// 设置页：硬件检测、ffmpeg 路径、默认参数与配置持久化。
/// </summary>
public sealed partial class SettingsPage : Page
{
    public SettingsViewModel ViewModel { get; } = new();

    public SettingsPage()
    {
        InitializeComponent();
    }
}
