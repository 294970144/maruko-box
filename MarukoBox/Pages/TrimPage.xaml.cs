using System;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media.Imaging;
using MarukoBox.Helpers;
using MarukoBox.ViewModels;
using Windows.ApplicationModel.DataTransfer;
using Windows.Media.Core;
using Windows.Media.Playback;
using Windows.Storage.Pickers;
using WinRT.Interop;

namespace MarukoBox.Pages;

/// <summary>
/// 裁剪页：预览 + 双端点时间轴定区间 + 剪切。
///
/// 页面只负责三件事：把文件交给 ViewModel、把播放器的播放位置同步给时间轴、
/// 把时间轴上的拖动翻译成 seek。所有 ffmpeg 调用都在 ViewModel 里。
/// </summary>
public sealed partial class TrimPage : Page
{
    public TrimViewModel ViewModel { get; } = new();

    /// <summary>标记"这次位置变化是播放器自己报的"，避免再回头 seek 一次形成抖动。</summary>
    private bool _fromPlayer;

    public TrimPage()
    {
        InitializeComponent();

        Timeline.Edited += Timeline_Edited;
        Timeline.Committed += Timeline_Committed;
        ViewModel.PropertyChanged += ViewModel_PropertyChanged;

        QualitySlider.Value = ViewModel.Quality;
        QualityText.Text = ViewModel.Quality.ToString();
    }

    // ---------- 选文件 ----------

    private async void BrowseInput_Click(object sender, RoutedEventArgs e)
    {
        var picker = FileDropHelper.CreatePicker(FileDropHelper.Video, PickerLocationId.VideosLibrary);
        var file = await picker.PickSingleFileAsync();
        if (file is null)
        {
            return;
        }

        await LoadAsync(file.Path);
    }

    private async Task LoadAsync(string path)
    {
        var ok = await ViewModel.LoadAsync(path);
        if (!ok)
        {
            return;
        }

        Timeline.Duration = ViewModel.Duration;
        Timeline.Start = ViewModel.Start;
        Timeline.End = ViewModel.End;
        Timeline.Position = TimeSpan.Zero;

        SetupPlayer(path);
    }

    // ---------- 预览播放器 ----------

    private void SetupPlayer(string path)
    {
        try
        {
            PreviewPlayer.AutoPlay = false;
            PreviewPlayer.Source = MediaSource.CreateFromUri(new Uri(path));

            var player = PreviewPlayer.MediaPlayer;
            if (player is null)
            {
                ViewModel.MarkPreviewFailed();
                return;
            }

            player.MediaFailed += (_, _) => App.RunOnUiThread(() => ViewModel.MarkPreviewFailed());

            player.PlaybackSession.PositionChanged -= PlaybackSession_PositionChanged;
            player.PlaybackSession.PositionChanged += PlaybackSession_PositionChanged;

            BtnPlayPause.Content = "播放";
        }
        catch (Exception ex)
        {
            // 系统解码器不认这个文件时不要连累整个页面：降级为"仅时间轴 + 缩略图"。
            App.LogCrash(ex, "TrimPage.SetupPlayer");
            ViewModel.MarkPreviewFailed();
        }
    }

    private void PlaybackSession_PositionChanged(MediaPlaybackSession sender, object args)
    {
        var pos = sender.Position;
        App.RunOnUiThread(() =>
        {
            _fromPlayer = true;
            ViewModel.Position = pos;
            Timeline.Position = pos;

            // 播到终点自动停：方便反复"试听"选段，不用手动按暂停
            if (pos >= ViewModel.End && IsPlaying)
            {
                Pause();
            }

            _fromPlayer = false;
        });
    }

    private MediaPlaybackSession? Session => PreviewPlayer.MediaPlayer?.PlaybackSession;

    private bool IsPlaying =>
        Session?.PlaybackState == MediaPlaybackState.Playing;

    private void Play()
    {
        // 指针在选段之外时，从起点开始播——否则"点播放听到的却是段外内容"很反直觉
        if (ViewModel.Position < ViewModel.Start || ViewModel.Position >= ViewModel.End)
        {
            Seek(ViewModel.Start);
        }

        PreviewPlayer.MediaPlayer?.Play();
        BtnPlayPause.Content = "暂停";
    }

    private void Pause()
    {
        PreviewPlayer.MediaPlayer?.Pause();
        BtnPlayPause.Content = "播放";
    }

    /// <summary>跳转到指定时刻并同步 UI（暂停状态下也会刷新画面）。</summary>
    private void Seek(TimeSpan t)
    {
        if (!ViewModel.HasVideo || ViewModel.PreviewFailed)
        {
            return;
        }

        var clamped = t < TimeSpan.Zero ? TimeSpan.Zero
            : t > ViewModel.Duration ? ViewModel.Duration
            : t;

        if (Session is not null)
        {
            Session.Position = clamped;
        }

        ViewModel.Position = clamped;
        Timeline.Position = clamped;
    }

    private void PlayPause_Click(object sender, RoutedEventArgs e)
    {
        if (IsPlaying)
        {
            Pause();
        }
        else
        {
            Play();
        }
    }

    private void GoStart_Click(object sender, RoutedEventArgs e) => Seek(ViewModel.Start);

    private void GoEnd_Click(object sender, RoutedEventArgs e) => Seek(ViewModel.End);

    // ---------- 时间轴事件 ----------

    private void Timeline_Edited(object? sender, TrimEditEventArgs e)
    {
        ViewModel.Start = Timeline.Start;
        ViewModel.End = Timeline.End;

        if (_fromPlayer && e.Handle == TrimHandle.Playhead)
        {
            // 位置变化本身就是播放器报上来的，再 seek 会打断播放
            ViewModel.Position = Timeline.Position;
            return;
        }

        ViewModel.Position = Timeline.Position;

        // 拖动端点时把画面停在那一帧，选点才有意义
        Seek(e.Handle switch
        {
            TrimHandle.Start => Timeline.Start,
            TrimHandle.End => Timeline.End,
            _ => Timeline.Position
        });
    }

    private async void Timeline_Committed(object? sender, EventArgs e)
    {
        // 松手后才抽帧：拖动过程中每次移动都跑 ffmpeg 会把 UI 拖死
        await ViewModel.RefreshThumbnailsAsync();
    }

    private void ViewModel_PropertyChanged(object? sender, System.ComponentModel.PropertyChangedEventArgs e)
    {
        switch (e.PropertyName)
        {
            case nameof(TrimViewModel.Duration):
                Timeline.Duration = ViewModel.Duration;
                break;

            case nameof(TrimViewModel.Start):
                Timeline.Start = ViewModel.Start;
                break;

            case nameof(TrimViewModel.End):
                Timeline.End = ViewModel.End;
                break;

            case nameof(TrimViewModel.Position):
                if (!_fromPlayer)
                {
                    Timeline.Position = ViewModel.Position;
                }

                break;

            case nameof(TrimViewModel.StartThumbPath):
                UpdateThumb(StartThumb, ViewModel.StartThumbPath);
                break;

            case nameof(TrimViewModel.EndThumbPath):
                UpdateThumb(EndThumb, ViewModel.EndThumbPath);
                break;
        }
    }

    private static void UpdateThumb(Image image, string? path)
    {
        if (string.IsNullOrEmpty(path) || !File.Exists(path))
        {
            image.Source = null;
            return;
        }

        image.Source = new BitmapImage(new Uri(path));
    }

    // ---------- 输出与执行 ----------

    private async void BrowseOutput_Click(object sender, RoutedEventArgs e)
    {
        if (string.IsNullOrWhiteSpace(ViewModel.InputPath))
        {
            return;
        }

        var picker = new FileSavePicker
        {
            SuggestedStartLocation = PickerLocationId.VideosLibrary,
            SuggestedFileName = Path.GetFileNameWithoutExtension(ViewModel.InputPath) + "_trim"
        };
        picker.FileTypeChoices.Add("视频文件", new[] { Path.GetExtension(ViewModel.InputPath) });

        InitializeWithWindow.Initialize(picker, App.WindowHandle);

        var file = await picker.PickSaveFileAsync();
        if (file is not null)
        {
            ViewModel.OutputPath = file.Path;
        }
    }

    private void QualitySlider_ValueChanged(object sender, Microsoft.UI.Xaml.Controls.Primitives.RangeBaseValueChangedEventArgs e)
    {
        ViewModel.Quality = (int)Math.Round(e.NewValue);
        QualityText.Text = ViewModel.Quality.ToString();
    }

    // ---------- 拖放 ----------

    private async void InputCard_Drop(object sender, Microsoft.UI.Xaml.DragEventArgs e)
    {
        FileDropHelper.Restore(InputCard);

        if (!FileDropHelper.HasFiles(e))
        {
            return;
        }

        var paths = await FileDropHelper.GetPathsAsync(e);
        var split = FileDropHelper.Split(paths, FileDropHelper.Video);

        // 单文件语义页：拖多个只取第一个
        if (split.Accepted.Count > 0)
        {
            await LoadAsync(split.Accepted[0]);
        }

        if (split.Rejected.Count > 0)
        {
            await FileDropHelper.NotifyRejectedAsync(XamlRoot, split.Rejected.Count, FileDropHelper.Video);
        }
    }

    private void InputCard_DragEnter(object sender, Microsoft.UI.Xaml.DragEventArgs e)
    {
        if (!FileDropHelper.HasFiles(e))
        {
            return;
        }

        e.AcceptedOperation = DataPackageOperation.Copy;
        if (e.DragUIOverride is not null)
        {
            e.DragUIOverride.Caption = "松开即可载入视频";
            e.DragUIOverride.IsCaptionVisible = true;
        }

        FileDropHelper.Highlight(InputCard);
    }

    private void InputCard_DragOver(object sender, Microsoft.UI.Xaml.DragEventArgs e)
    {
        if (FileDropHelper.HasFiles(e))
        {
            e.AcceptedOperation = DataPackageOperation.Copy;
        }
    }

    private void InputCard_DragLeave(object sender, Microsoft.UI.Xaml.DragEventArgs e)
    {
        FileDropHelper.Restore(InputCard);
    }
}
