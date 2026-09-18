using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using MarukoBox.Helpers;
using MarukoBox.ViewModels;
using Windows.ApplicationModel.DataTransfer;

namespace MarukoBox.Pages;

/// <summary>
/// 抽取页：轨道分析、选择、无损抽取。
/// 文件 / 文件夹选择需要 HWND 初始化 picker。
/// 支持把媒体文件拖到「源文件 / 输出目录」卡片上，自动填入源文件。
/// </summary>
public sealed partial class ExtractPage : Page
{
    public ExtractViewModel ViewModel { get; } = new();

    public ExtractPage()
    {
        InitializeComponent();
    }

    private async void BrowseSource_Click(object sender, RoutedEventArgs e)
    {
        var picker = FileDropHelper.CreatePicker(FileDropHelper.Media);

        var file = await picker.PickSingleFileAsync();
        if (file is not null)
        {
            ViewModel.SourcePath = file.Path;
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

    // ---------- 拖放：整张「源文件 / 输出目录」卡片都是放置区 ----------
    // 抽取的对象是单个容器文件，所以这里只取第一个受支持的文件，其余忽略。

    private void SourceCard_DragEnter(object sender, DragEventArgs e)
    {
        if (!FileDropHelper.HasFiles(e))
        {
            return;
        }

        e.AcceptedOperation = DataPackageOperation.Copy;
        if (e.DragUIOverride is not null)
        {
            e.DragUIOverride.Caption = "松开即可设为源文件";
            e.DragUIOverride.IsCaptionVisible = true;
        }

        FileDropHelper.Highlight(SourceCard);
    }

    private void SourceCard_DragOver(object sender, DragEventArgs e)
    {
        if (FileDropHelper.HasFiles(e))
        {
            e.AcceptedOperation = DataPackageOperation.Copy;
        }
    }

    private void SourceCard_DragLeave(object sender, DragEventArgs e)
    {
        FileDropHelper.Restore(SourceCard);
    }

    private async void SourceCard_Drop(object sender, DragEventArgs e)
    {
        FileDropHelper.Restore(SourceCard);

        if (!FileDropHelper.HasFiles(e))
        {
            return;
        }

        var paths = await FileDropHelper.GetPathsAsync(e);
        var (accepted, rejected) = FileDropHelper.Split(paths, FileDropHelper.Media);

        if (accepted.Count > 0)
        {
            ViewModel.SourcePath = accepted[0];
        }

        // 本页是单文件场景：多拖进来的受支持文件也一并算作忽略
        var ignored = rejected.Count;
        if (accepted.Count > 1)
        {
            ignored += accepted.Count - 1;
        }

        await FileDropHelper.NotifyRejectedAsync(XamlRoot, ignored, FileDropHelper.Media);
    }
}
