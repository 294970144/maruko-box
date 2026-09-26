using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;
using MarukoBox.Helpers;
using MarukoBox.Models;
using MarukoBox.ViewModels;
using Windows.ApplicationModel.DataTransfer;

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
        // 与拖放共用同一份白名单：不要退回 FileTypeFilter.Add("*")
        var picker = FileDropHelper.CreatePicker(FileDropHelper.Media);

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
        if (!FileDropHelper.HasFiles(e))
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
        if (FileDropHelper.HasFiles(e))
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

        if (!FileDropHelper.HasFiles(e))
        {
            return;
        }

        var paths = await FileDropHelper.GetPathsAsync(e);
        var (accepted, rejected) = FileDropHelper.Split(paths, FileDropHelper.Media);

        if (accepted.Count > 0)
        {
            ViewModel.AddFiles(accepted);
        }

        await FileDropHelper.NotifyRejectedAsync(XamlRoot, rejected.Count, FileDropHelper.Media);
    }

    private void RemoveItem_Click(object sender, RoutedEventArgs e)
    {
        if (sender is FrameworkElement fe && fe.DataContext is EncodeItem item)
        {
            ViewModel.RemoveItemCommand.Execute(item);
        }
    }

    /// <summary>清空队列（P0-D 危险确认）：不可撤销，需二次确认。</summary>
    private async void ClearQueue_Click(object sender, RoutedEventArgs e)
    {
        var count = ViewModel.Queue.Count;
        var message = count > 0
            ? $"将移除队列中的全部 {count} 个文件，未开始的任务不会转码，此操作不可撤销。"
            : "队列为空。";
        if (await UiConfirm.ShowAsync("清空队列", message, "清空"))
        {
            ViewModel.ClearQueueCommand.Execute(null);
        }
    }

    /// <summary>离开视频页时把编码参数并入 session.json，确保切走也不丢失（配合「保持习惯」）。</summary>
    protected override void OnNavigatedFrom(Microsoft.UI.Xaml.Navigation.NavigationEventArgs e)
    {
        base.OnNavigatedFrom(e);
        MarukoBox.MainWindow.SaveSessionIfEnabled();
    }
}
