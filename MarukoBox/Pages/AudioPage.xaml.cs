using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using MarukoBox.Helpers;
using MarukoBox.Models;
using MarukoBox.ViewModels;
using Windows.ApplicationModel.DataTransfer;
using Windows.Storage.Pickers;

namespace MarukoBox.Pages;

/// <summary>
/// 音频页：批量音频转码。文件选择需要 HWND 初始化 picker。
/// 支持把文件直接拖到「音频队列」卡片上加入队列，并与文件选择器共用同一份格式白名单。
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
        // 视频容器也可以作为音频来源（抽出音轨后再编码），这里用 Media 而非纯音频
        var picker = FileDropHelper.CreatePicker(FileDropHelper.Media, PickerLocationId.MusicLibrary);

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

    // ---------- 拖放：整张「音频队列」卡片都是放置区 ----------

    private void QueueCard_DragEnter(object sender, DragEventArgs e)
    {
        if (!FileDropHelper.HasFiles(e))
        {
            return;
        }

        e.AcceptedOperation = DataPackageOperation.Copy;
        if (e.DragUIOverride is not null)
        {
            e.DragUIOverride.Caption = "松开即可加入队列";
            e.DragUIOverride.IsCaptionVisible = true;
        }

        FileDropHelper.Highlight(QueueCard);
    }

    private void QueueCard_DragOver(object sender, DragEventArgs e)
    {
        if (FileDropHelper.HasFiles(e))
        {
            e.AcceptedOperation = DataPackageOperation.Copy;
        }
    }

    private void QueueCard_DragLeave(object sender, DragEventArgs e)
    {
        FileDropHelper.Restore(QueueCard);
    }

    private async void QueueCard_Drop(object sender, DragEventArgs e)
    {
        FileDropHelper.Restore(QueueCard);

        if (!FileDropHelper.HasFiles(e))
        {
            return;
        }

        var paths = await FileDropHelper.GetPathsAsync(e);
        var (accepted, rejected) = FileDropHelper.Split(paths, FileDropHelper.Media);

        if (accepted.Count > 0)
        {
            ViewModel.AddFilesCommand.Execute(accepted);
        }

        await FileDropHelper.NotifyRejectedAsync(XamlRoot, rejected.Count, FileDropHelper.Media);
    }
}
