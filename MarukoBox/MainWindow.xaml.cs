using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using MarukoBox.Pages;

namespace MarukoBox;

/// <summary>
/// 应用主窗口。承载 NavigationView 外壳与内容 Frame。
/// 导航逻辑按官方 WinUI 3 范式在代码后置中处理（选区变更驱动 Frame.Navigate）。
/// </summary>
public sealed partial class MainWindow : Window
{
    public MainWindow()
    {
        InitializeComponent();

        ExtendsContentIntoTitleBar = true;
        SetTitleBar(AppTitleBar);

        AppWindow.SetIcon("Assets/AppIcon.ico");
    }

    private void NavView_Loaded(object sender, RoutedEventArgs e)
    {
        // 默认进入「视频」页（XAML 已标记 IsSelected）。
        ContentFrame.Navigate(typeof(VideoPage));
    }

    private void NavView_SelectionChanged(NavigationView sender, NavigationViewSelectionChangedEventArgs args)
    {
        if (args.SelectedItem is not NavigationViewItem item)
        {
            return;
        }

        var tag = item.Tag?.ToString() ?? string.Empty;

        var pageType = tag switch
        {
            "Video" => typeof(VideoPage),
            "Extract" => typeof(ExtractPage),
            "Audio" => typeof(AudioPage),
            "Mux" => typeof(MuxPage),
            "Image" => typeof(ImagePage),
            "Tools" => typeof(ToolsPage),
            "Subtitle" => typeof(SubtitlePage),
            "Settings" => typeof(SettingsPage),
            "About" => typeof(AboutPage),
            _ => typeof(PlaceholderPage)
        };

        if (pageType == typeof(PlaceholderPage))
        {
            ContentFrame.Navigate(pageType, item.Content?.ToString() ?? tag);
        }
        else
        {
            ContentFrame.Navigate(pageType);
        }
    }
}
