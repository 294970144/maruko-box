using System.IO;
using System.Linq;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using MarukoBox.Helpers;
using MarukoBox.Models;
using MarukoBox.ViewModels;
using Windows.ApplicationModel.DataTransfer;
using Windows.Storage.Pickers;

namespace MarukoBox.Pages;

/// <summary>
/// 封装页：合并视频 / 音频 / 字幕到指定容器。文件选择需 HWND 初始化 picker。
/// 拖放到「轨道列表」卡片上的文件会按扩展名自动分流到视频 / 音频 / 字幕，
/// 不需要用户先点对应的「+ 添加」按钮。
/// </summary>
public sealed partial class MuxPage : Page
{
    public MuxViewModel ViewModel { get; } = new();

    public MuxPage()
    {
        InitializeComponent();
    }

    private async void AddVideo_Click(object sender, RoutedEventArgs e) => await PickAndAddAsync("video");

    private async void AddAudio_Click(object sender, RoutedEventArgs e) => await PickAndAddAsync("audio");

    private async void AddSubtitle_Click(object sender, RoutedEventArgs e) => await PickAndAddAsync("subtitle");

    private async Task PickAndAddAsync(string kind)
    {
        var allowed = AllowedFor(kind);
        var location = kind == "audio" ? PickerLocationId.MusicLibrary : PickerLocationId.VideosLibrary;

        var picker = FileDropHelper.CreatePicker(allowed, location);

        var files = await picker.PickMultipleFilesAsync();
        if (files is null || files.Count == 0)
        {
            return;
        }

        var paths = files.Select(f => f.Path);
        var cmd = kind switch
        {
            "audio" => ViewModel.AddAudioCommand,
            "subtitle" => ViewModel.AddSubtitleCommand,
            _ => ViewModel.AddVideoCommand
        };
        cmd.Execute(paths);
    }

    /// <summary>某一类轨道允许的文件扩展名（选择器与拖放共用同一份）。</summary>
    private static string[] AllowedFor(string kind) => kind switch
    {
        "audio" => FileDropHelper.Audio,
        "subtitle" => FileDropHelper.Subtitle,
        _ => FileDropHelper.Video
    };

    /// <summary>清空轨道（P0-D 危险确认）：不可撤销，需二次确认。</summary>
    private async void ClearInputs_Click(object sender, RoutedEventArgs e)
    {
        var count = ViewModel.Inputs.Count;
        var message = count > 0
            ? $"将移除全部 {count} 条轨道输入，此操作不可撤销。"
            : "当前没有输入文件。";
        if (await UiConfirm.ShowAsync("清空轨道", message, "清空"))
        {
            ViewModel.ClearInputsCommand.Execute(null);
        }
    }

    private async void BrowseOutput_Click(object sender, RoutedEventArgs e)
    {
        var folder = await PickerHelper.PickFolderAsync();
        if (folder is null)
        {
            return;
        }

        var baseName = "muxed";
        var firstVideo = ViewModel.Inputs.FirstOrDefault(i => i.Kind == MuxKind.Video);
        if (firstVideo is not null)
        {
            baseName = Path.GetFileNameWithoutExtension(firstVideo.FileName);
        }

        var ext = ViewModel.SelectedContainer switch
        {
            "mkv" => ".mkv",
            "mov" => ".mov",
            "webm" => ".webm",
            _ => ".mp4"
        };

        ViewModel.OutputPath = Path.Combine(folder, baseName + ext);
    }

    private void RemoveInput_Click(object sender, RoutedEventArgs e)
    {
        if (sender is FrameworkElement fe && fe.DataContext is MuxInput item)
        {
            ViewModel.RemoveInputCommand.Execute(item);
        }
    }

    // ---------- 拖放：整张「轨道列表」卡片都是放置区，按扩展名自动分流 ----------

    private void InputsCard_DragEnter(object sender, DragEventArgs e)
    {
        if (!FileDropHelper.HasFiles(e))
        {
            return;
        }

        e.AcceptedOperation = DataPackageOperation.Copy;
        if (e.DragUIOverride is not null)
        {
            e.DragUIOverride.Caption = "松开即可加入对应轨道";
            e.DragUIOverride.IsCaptionVisible = true;
        }

        FileDropHelper.Highlight(InputsCard);
    }

    private void InputsCard_DragOver(object sender, DragEventArgs e)
    {
        if (FileDropHelper.HasFiles(e))
        {
            e.AcceptedOperation = DataPackageOperation.Copy;
        }
    }

    private void InputsCard_DragLeave(object sender, DragEventArgs e)
    {
        FileDropHelper.Restore(InputsCard);
    }

    private async void InputsCard_Drop(object sender, DragEventArgs e)
    {
        FileDropHelper.Restore(InputsCard);

        if (!FileDropHelper.HasFiles(e))
        {
            return;
        }

        var paths = await FileDropHelper.GetPathsAsync(e);

        // 三类轨道各归各位：视频进视频轨、音频进音频轨、字幕进字幕轨
        var videos = FileDropHelper.Split(paths, FileDropHelper.Video).Accepted;
        var audios = FileDropHelper.Split(paths, FileDropHelper.Audio).Accepted;
        var subtitles = FileDropHelper.Split(paths, FileDropHelper.Subtitle).Accepted;

        if (videos.Count > 0)
        {
            ViewModel.AddVideoCommand.Execute(videos);
        }

        if (audios.Count > 0)
        {
            ViewModel.AddAudioCommand.Execute(audios);
        }

        if (subtitles.Count > 0)
        {
            ViewModel.AddSubtitleCommand.Execute(subtitles);
        }

        // 三类都不认的文件才提示忽略（若本页不开放 new-media 组合，这里就是全部可识别格式）
        var known = FileDropHelper.Media.Concat(FileDropHelper.Subtitle).ToArray();
        var rejected = FileDropHelper.Split(paths, known).Rejected.Count;
        await FileDropHelper.NotifyRejectedAsync(XamlRoot, rejected, known);
    }
}
