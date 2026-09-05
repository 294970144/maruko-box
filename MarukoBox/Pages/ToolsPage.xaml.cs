using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using MarukoBox.ViewModels;
using Windows.Storage;
using Windows.Storage.Pickers;

namespace MarukoBox.Pages;

/// <summary>
/// 工具页：媒体信息查看。文件选择需 HWND 初始化 picker。
/// </summary>
public sealed partial class ToolsPage : Page
{
    public ToolsViewModel ViewModel { get; } = new();

    public ToolsPage()
    {
        InitializeComponent();
    }

    private async void Browse_Click(object sender, RoutedEventArgs e)
    {
        var picker = new FileOpenPicker
        {
            ViewMode = PickerViewMode.List,
            SuggestedStartLocation = PickerLocationId.VideosLibrary
        };
        picker.FileTypeFilter.Add("*");

        WinRT.Interop.InitializeWithWindow.Initialize(picker, App.WindowHandle);

        var file = await picker.PickSingleFileAsync();
        if (file is not null)
        {
            ViewModel.InputFile = file.Path;
        }
    }
}
