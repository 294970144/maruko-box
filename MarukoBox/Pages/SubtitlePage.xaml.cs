using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using MarukoBox.Helpers;
using MarukoBox.ViewModels;
using Windows.ApplicationModel.DataTransfer;
using Windows.Storage;
using Windows.Storage.Pickers;

namespace MarukoBox.Pages;

/// <summary>
/// 字幕页：抽取 / 嵌入 / 转换三个功能区，复用 FfmpegService 对应能力。
/// 文件选择器需要 HWND 初始化 picker（WinRT.Interop.InitializeWithWindow）。
/// 三个功能区各自是一块拖放区：抽取收视频，嵌入按类型分别接收视频与字幕，转换收字幕。
/// </summary>
public sealed partial class SubtitlePage : Page
{
    public SubtitleViewModel ViewModel { get; } = new();

    public SubtitlePage()
    {
        InitializeComponent();
    }

    private async void BrowseExtractVideo_Click(object sender, RoutedEventArgs e)
    {
        var file = await PickFileAsync(FileDropHelper.Video);
        if (file is not null)
        {
            ViewModel.ExtractVideo = file.Path;
        }
    }

    private async void BrowseEmbedVideo_Click(object sender, RoutedEventArgs e)
    {
        var file = await PickFileAsync(FileDropHelper.Video);
        if (file is not null)
        {
            ViewModel.EmbedVideo = file.Path;
        }
    }

    private async void BrowseEmbedSubtitle_Click(object sender, RoutedEventArgs e)
    {
        var file = await PickFileAsync(FileDropHelper.Subtitle);
        if (file is not null)
        {
            ViewModel.EmbedSubtitle = file.Path;
        }
    }

    private async void BrowseConvert_Click(object sender, RoutedEventArgs e)
    {
        var file = await PickFileAsync(FileDropHelper.Subtitle);
        if (file is not null)
        {
            ViewModel.ConvertInput = file.Path;
        }
    }

    private async Task<StorageFile?> PickFileAsync(string[] extensions)
    {
        var picker = FileDropHelper.CreatePicker(extensions);
        return await picker.PickSingleFileAsync();
    }

    // ---------- 拖放 ①：抽取区（只认视频） ----------

    private void ExtractCard_DragEnter(object sender, DragEventArgs e)
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

        FileDropHelper.Highlight(ExtractCard);
    }

    private void ExtractCard_DragOver(object sender, DragEventArgs e)
    {
        if (FileDropHelper.HasFiles(e))
        {
            e.AcceptedOperation = DataPackageOperation.Copy;
        }
    }

    private void ExtractCard_DragLeave(object sender, DragEventArgs e)
    {
        FileDropHelper.Restore(ExtractCard);
    }

    private async void ExtractCard_Drop(object sender, DragEventArgs e)
    {
        FileDropHelper.Restore(ExtractCard);

        if (!FileDropHelper.HasFiles(e))
        {
            return;
        }

        var paths = await FileDropHelper.GetPathsAsync(e);
        var (accepted, rejected) = FileDropHelper.Split(paths, FileDropHelper.Video);

        if (accepted.Count > 0)
        {
            ViewModel.ExtractVideo = accepted[0];
        }

        var ignored = rejected.Count;
        if (accepted.Count > 1)
        {
            ignored += accepted.Count - 1;
        }

        await FileDropHelper.NotifyRejectedAsync(XamlRoot, ignored, FileDropHelper.Video);
    }

    // ---------- 拖放 ②：嵌入区（视频 → 视频文件；字幕 → 字幕文件） ----------

    private void EmbedCard_DragEnter(object sender, DragEventArgs e)
    {
        if (!FileDropHelper.HasFiles(e))
        {
            return;
        }

        e.AcceptedOperation = DataPackageOperation.Copy;
        if (e.DragUIOverride is not null)
        {
            e.DragUIOverride.Caption = "视频 / 字幕会自动填入对应位置";
            e.DragUIOverride.IsCaptionVisible = true;
        }

        FileDropHelper.Highlight(EmbedCard);
    }

    private void EmbedCard_DragOver(object sender, DragEventArgs e)
    {
        if (FileDropHelper.HasFiles(e))
        {
            e.AcceptedOperation = DataPackageOperation.Copy;
        }
    }

    private void EmbedCard_DragLeave(object sender, DragEventArgs e)
    {
        FileDropHelper.Restore(EmbedCard);
    }

    private async void EmbedCard_Drop(object sender, DragEventArgs e)
    {
        FileDropHelper.Restore(EmbedCard);

        if (!FileDropHelper.HasFiles(e))
        {
            return;
        }

        var paths = await FileDropHelper.GetPathsAsync(e);

        // 同一个放置区按文件类型分流：视频填视频框，字幕填字幕框
        var videos = FileDropHelper.Split(paths, FileDropHelper.Video).Accepted;
        var subtitles = FileDropHelper.Split(paths, FileDropHelper.Subtitle).Accepted;

        if (videos.Count > 0)
        {
            ViewModel.EmbedVideo = videos[0];
        }

        if (subtitles.Count > 0)
        {
            ViewModel.EmbedSubtitle = subtitles[0];
        }

        var known = FileDropHelper.Video.Concat(FileDropHelper.Subtitle).ToArray();
        var rejected = FileDropHelper.Split(paths, known).Rejected.Count;
        await FileDropHelper.NotifyRejectedAsync(XamlRoot, rejected, known);
    }

    // ---------- 拖放 ③：转换区（只认字幕） ----------

    private void ConvertCard_DragEnter(object sender, DragEventArgs e)
    {
        if (!FileDropHelper.HasFiles(e))
        {
            return;
        }

        e.AcceptedOperation = DataPackageOperation.Copy;
        if (e.DragUIOverride is not null)
        {
            e.DragUIOverride.Caption = "松开即可设为源字幕";
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
        var (accepted, rejected) = FileDropHelper.Split(paths, FileDropHelper.Subtitle);

        if (accepted.Count > 0)
        {
            ViewModel.ConvertInput = accepted[0];
        }

        var ignored = rejected.Count;
        if (accepted.Count > 1)
        {
            ignored += accepted.Count - 1;
        }

        await FileDropHelper.NotifyRejectedAsync(XamlRoot, ignored, FileDropHelper.Subtitle);
    }
}
