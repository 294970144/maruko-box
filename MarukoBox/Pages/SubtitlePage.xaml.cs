using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using MarukoBox.ViewModels;
using Windows.Storage;
using Windows.Storage.Pickers;

namespace MarukoBox.Pages;

/// <summary>
/// 字幕页：抽取 / 嵌入 / 转换三个功能区，复用 FfmpegService 对应能力。
/// 文件选择器需要 HWND 初始化 picker（WinRT.Interop.InitializeWithWindow）。
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
        var file = await PickFileAsync(new[] { ".mp4", ".mkv", ".mov", ".webm", ".avi", ".ts", ".flv", ".wmv" });
        if (file is not null)
        {
            ViewModel.ExtractVideo = file.Path;
        }
    }

    private async void BrowseEmbedVideo_Click(object sender, RoutedEventArgs e)
    {
        var file = await PickFileAsync(new[] { ".mp4", ".mkv", ".mov", ".webm", ".avi", ".ts", ".flv", ".wmv" });
        if (file is not null)
        {
            ViewModel.EmbedVideo = file.Path;
        }
    }

    private async void BrowseEmbedSubtitle_Click(object sender, RoutedEventArgs e)
    {
        var file = await PickFileAsync(new[] { ".srt", ".ass", ".vtt", ".ssa", ".sub", ".idx", ".smi", ".ttml" });
        if (file is not null)
        {
            ViewModel.EmbedSubtitle = file.Path;
        }
    }

    private async void BrowseConvert_Click(object sender, RoutedEventArgs e)
    {
        var file = await PickFileAsync(new[] { ".srt", ".ass", ".vtt", ".ssa", ".sub", ".smi", ".ttml" });
        if (file is not null)
        {
            ViewModel.ConvertInput = file.Path;
        }
    }

    private async Task<StorageFile?> PickFileAsync(string[] extensions)
    {
        var picker = new FileOpenPicker
        {
            ViewMode = PickerViewMode.List,
            SuggestedStartLocation = PickerLocationId.VideosLibrary
        };
        foreach (var ext in extensions)
        {
            picker.FileTypeFilter.Add(ext);
        }

        WinRT.Interop.InitializeWithWindow.Initialize(picker, App.WindowHandle);

        return await picker.PickSingleFileAsync();
    }
}
