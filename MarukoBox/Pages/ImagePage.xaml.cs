using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using MarukoBox.Helpers;
using MarukoBox.ViewModels;
using Windows.ApplicationModel.DataTransfer;
using Windows.Storage.Pickers;

namespace MarukoBox.Pages;

/// <summary>
/// 图片页：视频抽帧 + 图片转码。文件 / 文件夹选择需 HWND 初始化 picker。
/// 两个功能区各是一块拖放区，且白名单不同：抽帧卡收视频，转码卡收图片。
/// </summary>
public sealed partial class ImagePage : Page
{
    public ImageViewModel ViewModel { get; } = new();

    public ImagePage()
    {
        InitializeComponent();
    }

    private async void BrowseVideo_Click(object sender, RoutedEventArgs e)
    {
        var picker = FileDropHelper.CreatePicker(
            FileDropHelper.Video, PickerLocationId.VideosLibrary, PickerViewMode.Thumbnail);

        var file = await picker.PickSingleFileAsync();
        if (file is not null)
        {
            ViewModel.InputVideo = file.Path;
        }
    }

    private async void BrowseOutput_Click(object sender, RoutedEventArgs e)
    {
        var folder = await PickerHelper.PickFolderAsync();
        if (folder is not null)
        {
            ViewModel.OutputDir = folder;
        }
    }

    private async void BrowseImage_Click(object sender, RoutedEventArgs e)
    {
        var picker = FileDropHelper.CreatePicker(
            FileDropHelper.Image, PickerLocationId.PicturesLibrary, PickerViewMode.Thumbnail);

        var file = await picker.PickSingleFileAsync();
        if (file is not null)
        {
            ViewModel.InputImage = file.Path;
        }
    }

    // ---------- 拖放 ①：视频抽帧卡片（只认视频） ----------

    private void FrameCard_DragEnter(object sender, DragEventArgs e)
    {
        if (!FileDropHelper.HasFiles(e))
        {
            return;
        }

        e.AcceptedOperation = DataPackageOperation.Copy;
        if (e.DragUIOverride is not null)
        {
            e.DragUIOverride.Caption = "松开即可设为源视频";
            e.DragUIOverride.IsCaptionVisible = true;
        }

        FileDropHelper.Highlight(FrameCard);
    }

    private void FrameCard_DragOver(object sender, DragEventArgs e)
    {
        if (FileDropHelper.HasFiles(e))
        {
            e.AcceptedOperation = DataPackageOperation.Copy;
        }
    }

    private void FrameCard_DragLeave(object sender, DragEventArgs e)
    {
        FileDropHelper.Restore(FrameCard);
    }

    private async void FrameCard_Drop(object sender, DragEventArgs e)
    {
        FileDropHelper.Restore(FrameCard);

        if (!FileDropHelper.HasFiles(e))
        {
            return;
        }

        var paths = await FileDropHelper.GetPathsAsync(e);
        var (accepted, rejected) = FileDropHelper.Split(paths, FileDropHelper.Video);

        if (accepted.Count > 0)
        {
            ViewModel.InputVideo = accepted[0];
        }

        var ignored = rejected.Count;
        if (accepted.Count > 1)
        {
            ignored += accepted.Count - 1;
        }

        await FileDropHelper.NotifyRejectedAsync(XamlRoot, ignored, FileDropHelper.Video);
    }

    // ---------- 拖放 ②：图片转码卡片（只认图片） ----------

    private void ConvertCard_DragEnter(object sender, DragEventArgs e)
    {
        if (!FileDropHelper.HasFiles(e))
        {
            return;
        }

        e.AcceptedOperation = DataPackageOperation.Copy;
        if (e.DragUIOverride is not null)
        {
            e.DragUIOverride.Caption = "松开即可设为源图片";
            e.DragUIOverride.IsCaptionVisible = true;
        }

        FileDropHelper.Highlight(ConvertCard);
    }

    private void ConvertCard_DragOver(object sender, DragEventArgs e)
    {
        if (FileDropHelper.HasFiles(e))
        {
            e.AcceptedOperation = DataPackageOperation.Copy;
        }
    }

    private void ConvertCard_DragLeave(object sender, DragEventArgs e)
    {
        FileDropHelper.Restore(ConvertCard);
    }

    private async void ConvertCard_Drop(object sender, DragEventArgs e)
    {
        FileDropHelper.Restore(ConvertCard);

        if (!FileDropHelper.HasFiles(e))
        {
            return;
        }

        var paths = await FileDropHelper.GetPathsAsync(e);
        var (accepted, rejected) = FileDropHelper.Split(paths, FileDropHelper.Image);

        if (accepted.Count > 0)
        {
            ViewModel.InputImage = accepted[0];
        }

        var ignored = rejected.Count;
        if (accepted.Count > 1)
        {
            ignored += accepted.Count - 1;
        }

        await FileDropHelper.NotifyRejectedAsync(XamlRoot, ignored, FileDropHelper.Image);
    }
}
