using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Navigation;

namespace MarukoBox.Pages;

/// <summary>
/// 尚未实现的模块占位页。通过导航参数接收标题。
/// </summary>
public sealed partial class PlaceholderPage : Page
{
    public PlaceholderPage()
    {
        InitializeComponent();
    }

    protected override void OnNavigatedTo(NavigationEventArgs e)
    {
        if (e.Parameter is string title && !string.IsNullOrEmpty(title))
        {
            TitleText.Text = title;
        }

        base.OnNavigatedTo(e);
    }
}
