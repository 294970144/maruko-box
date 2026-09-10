using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;
using MarukoBox.Models;
using MarukoBox.ViewModels;
using Windows.ApplicationModel.DataTransfer;
using Windows.Storage;
using Windows.Storage.Pickers;

namespace MarukoBox.Pages;

/// <summary>
/// 视频页：批量队列、自适应编码参数与实时进度。
/// 文件选择与拖拽落在代码后置（需要 HWND 初始化 picker）。
/// </summary>
public sealed partial class VideoPage : Page
{
    public VideoViewModel ViewModel { get; } = new();

    public VideoPage()
    {
        InitializeComponent();
    }

    private async void AddFiles_Click(object sender, RoutedEventArgs e)
    {
        var picker = new FileOpenPicker
        {
            ViewMode = PickerViewMode.List,
            SuggestedStartLocation = PickerLocationId.VideosLibrary
        };
        picker.FileTypeFilter.Add("*");

        WinRT.Interop.InitializeWithWindow.Initialize(picker, App.WindowHandle);

        var files = await picker.PickMultipleFilesAsync();
        if (files is not null && files.Count > 0)
        {
            ViewModel.AddFiles(files.Select(f => f.Path));
        }
    }

    // ---------- 拖放：整个左侧栏都是放置区 ----------

    private void Sidebar_DragEnter(object sender, DragEventArgs e)
    {
        // 只认文件拖入；拖文本 / 链接进来不弹提示，也不接受放置
        if (e.DataView is null || !e.DataView.Contains(StandardDataFormats.StorageItems))
        {
            return;
        }

        e.AcceptedOperation = DataPackageOperation.Copy;
        if (e.DragUIOverride is not null)
        {
            e.DragUIOverride.Caption = "松开即可添加文件";
            e.DragUIOverride.IsCaptionVisible = true;
        }

        DropOverlay.Visibility = Visibility.Visible;
    }

    private void Sidebar_DragOver(object sender, DragEventArgs e)
    {
        if (e.DataView is not null && e.DataView.Contains(StandardDataFormats.StorageItems))
        {
            e.AcceptedOperation = DataPackageOperation.Copy;
        }
    }

    private void Sidebar_DragLeave(object sender, DragEventArgs e)
    {
        DropOverlay.Visibility = Visibility.Collapsed;
    }

    private async void Sidebar_Drop(object sender, DragEventArgs e)
    {
        DropOverlay.Visibility = Visibility.Collapsed;

        if (e.DataView is null)
        {
            return;
        }

        var items = await e.DataView.GetStorageItemsAsync();
        var paths = items
            .OfType<StorageFile>()
            .Select(f => f.Path)
            .ToList();

        if (paths.Count > 0)
        {
            ViewModel.AddFiles(paths);
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
