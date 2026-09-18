using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using MarukoBox.Helpers;
using MarukoBox.ViewModels;
using Windows.ApplicationModel.DataTransfer;

namespace MarukoBox.Pages;

/// <summary>
/// 工具页：媒体信息查看。
/// 支持把媒体文件拖到「媒体文件」卡片上，自动填入待分析的文件。
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
        // 与拖放共用同一份白名单：不要退回 FileTypeFilter.Add("*")
        var picker = FileDropHelper.CreatePicker(FileDropHelper.Media);

        var file = await picker.PickSingleFileAsync();
        if (file is not null)
        {
            ViewModel.InputFile = file.Path;
        }
    }

    // ---------- 拖放：整张「媒体文件」卡片都是放置区 ----------
    // 媒体信息一次只看一个文件，所以这里只取第一个受支持的文件，其余忽略。

    private void InputCard_DragEnter(object sender, DragEventArgs e)
    {
        if (!FileDropHelper.HasFiles(e))
        {
            return;
        }

        e.AcceptedOperation = DataPackageOperation.Copy;
        if (e.DragUIOverride is not null)
        {
            e.DragUIOverride.Caption = "松开即可查看媒体信息";
            e.DragUIOverride.IsCaptionVisible = true;
        }

        FileDropHelper.Highlight(InputCard);
    }

    private void InputCard_DragOver(object sender, DragEventArgs e)
    {
        if (FileDropHelper.HasFiles(e))
        {
            e.AcceptedOperation = DataPackageOperation.Copy;
        }
    }

    private void InputCard_DragLeave(object sender, DragEventArgs e)
    {
        FileDropHelper.Restore(InputCard);
    }

    private async void InputCard_Drop(object sender, DragEventArgs e)
    {
        FileDropHelper.Restore(InputCard);

        if (!FileDropHelper.HasFiles(e))
        {
            return;
        }

        var paths = await FileDropHelper.GetPathsAsync(e);
        var (accepted, rejected) = FileDropHelper.Split(paths, FileDropHelper.Media);

        if (accepted.Count > 0)
        {
            ViewModel.InputFile = accepted[0];
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
