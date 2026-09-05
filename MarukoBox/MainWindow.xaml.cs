using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using MarukoBox.Pages;
using MarukoBox.Services;

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

    /// <summary>
    /// 保持习惯：若配置开启，则从当前 Frame 找到视频页并把参数快照写入 session.json。
    /// 由 App.OnLaunched 挂到 Window.Closed（含「立即重启」的 Exit 流程）。
    /// 静态方法 + 容错：任何失败都不应阻塞退出。
    /// </summary>
    public static void SaveSessionIfEnabled()
    {
        try
        {
            if (!AppServices.Config.Load().RememberLastSession)
            {
                return;
            }

            if (App.Window?.Content is not FrameworkElement root)
            {
                return;
            }

            var frame = FindDescendant<Frame>(root);
            var video = frame?.Content as VideoPage;
            video?.ViewModel.SaveSession();
        }
        catch
        {
            // 会话保存失败不阻塞退出
        }
    }

    private static T? FindDescendant<T>(FrameworkElement root) where T : class
    {
        foreach (var child in (root as Panel)?.Children ?? Enumerable.Empty<UIElement>())
        {
            if (child is T match)
            {
                return match;
            }
            if (child is FrameworkElement fe)
            {
                var sub = FindDescendant<T>(fe);
                if (sub is not null)
                {
                    return sub;
                }
            }
        }
        return null;
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
