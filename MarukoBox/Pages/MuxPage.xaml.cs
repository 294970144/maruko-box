using System.IO;
using System.Linq;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using MarukoBox.Models;
using MarukoBox.ViewModels;
using Windows.Storage;
using Windows.Storage.Pickers;

namespace MarukoBox.Pages;

/// <summary>
/// 封装页：合并视频 / 音频 / 字幕到指定容器。文件选择需 HWND 初始化 picker。
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
        var picker = new FileOpenPicker
        {
            ViewMode = PickerViewMode.List,
            SuggestedStartLocation = kind == "audio" ? PickerLocationId.MusicLibrary : PickerLocationId.VideosLibrary
        };
        picker.FileTypeFilter.Add("*");

        WinRT.Interop.InitializeWithWindow.Initialize(picker, App.WindowHandle);

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

    private async void BrowseOutput_Click(object sender, RoutedEventArgs e)
    {
        var picker = new FolderPicker
        {
            SuggestedStartLocation = PickerLocationId.VideosLibrary
        };
        picker.FileTypeFilter.Add("*");

        WinRT.Interop.InitializeWithWindow.Initialize(picker, App.WindowHandle);

        var folder = await picker.PickSingleFolderAsync();
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

        ViewModel.OutputPath = Path.Combine(folder.Path, baseName + ext);
    }

    private void RemoveInput_Click(object sender, RoutedEventArgs e)
    {
        if (sender is FrameworkElement fe && fe.DataContext is MuxInput item)
        {
            ViewModel.RemoveInputCommand.Execute(item);
        }
    }
}
