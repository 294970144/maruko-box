using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using MarukoBox.Models;
using MarukoBox.ViewModels;
using Windows.Storage;
using Windows.Storage.Pickers;

namespace MarukoBox.Pages;

/// <summary>
/// 音频页：批量音频转码。文件选择需要 HWND 初始化 picker。
/// </summary>
public sealed partial class AudioPage : Page
{
    public AudioViewModel ViewModel { get; } = new();

    public AudioPage()
    {
        InitializeComponent();
    }

    private async void AddFiles_Click(object sender, RoutedEventArgs e)
    {
        var picker = new FileOpenPicker
        {
            ViewMode = PickerViewMode.List,
            SuggestedStartLocation = PickerLocationId.MusicLibrary
        };
        picker.FileTypeFilter.Add("*");

        WinRT.Interop.InitializeWithWindow.Initialize(picker, App.WindowHandle);

        var files = await picker.PickMultipleFilesAsync();
        if (files is not null && files.Count > 0)
        {
            ViewModel.AddFilesCommand.Execute(files.Select(f => f.Path));
        }
    }

    private void RemoveItem_Click(object sender, RoutedEventArgs e)
    {
        if (sender is FrameworkElement fe && fe.DataContext is EncodeItem item)
        {
            ViewModel.RemoveItemCommand.Execute(item);
        }
    }
}
