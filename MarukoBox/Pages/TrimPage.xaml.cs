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

        // 起止块的响应式布局：窗口尺寸变化、页面首次布局完成、载入新视频时都要重新判定
        Loaded += (_, _) => UpdateResponsiveLayout();
        PreviewGrid.SizeChanged += (_, _) => UpdateResponsiveLayout();

        // 画质滑杆的范围必须在代码后置里按「Maximum → Value → Minimum」的顺序设置。
        //
        // 为什么不能在 XAML 里写 Minimum="14" Maximum="32"：
        // ① 官方 RangeBase.Minimum 文档明确要求——XAML 中应先声明 Maximum 再声明 Minimum，
        //    顺序反了会导致赋值被忽略或范围被改写成意外值；
        // ② Slider 的 Value 默认是 0，先赋 Minimum=14 会让当前值落在范围外，
        //    实测直接抛 XamlParseException「Failed to assign to property 'RangeBase.Minimum'」，
        //    页面构造失败 → 导航被吞掉，表现为「点了裁剪页没反应」。
        //
        // 这里顺序写死：先把上界抬到 32（Value=0 合法），再把 Value 设成画质默认值 20，
        // 最后才落下界 14（此时 Value 已在范围内，不会越界）。
        QualitySlider.Maximum = 32;
        QualitySlider.Value = ViewModel.Quality;
        QualitySlider.Minimum = 14;
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

            // 视频元数据就绪后才能拿到固有宽高比，此时重新判定起止块挂两侧还是回下方
            player.MediaOpened += (_, _) => App.RunOnUiThread(UpdateResponsiveLayout);

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
            case nameof(TrimViewModel.HasVideo):
            case nameof(TrimViewModel.PreviewFailed):
                UpdateResponsiveLayout();
                break;

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

    // ---------- 响应式布局：起止块在「预览两侧」与「下方两列」之间切换 ----------

    private const double PreviewHeight = 300;
    private const double SideThumbHeight = 150;
    private const double BottomThumbHeight = 90;

    /// <summary>起止块无法实测时的宽度估算值（按按钮行「−1s −0.1s 设为当前位置 +0.1s +1s」的渲染宽度）。</summary>
    private const double SidePanelFallbackWidth = 320;

    /// <summary>判定余量与块宽之间留的呼吸间距。</summary>
    private const double SideGap = 8;

    /// <summary>当前是否处于宽模式（起止块挂在预览两侧）。</summary>
    private bool _wideLayout;

    /// <summary>
    /// 按「预览两侧的剩余空白是否装得下一个起止块」切换布局。
    ///
    /// 竖屏 / 方形视频在宽预览区两侧留有大片 letterbox 黑边，把开始 / 结束块
    /// 挂到两侧正好利用；横屏视频几乎占满整行、两侧无空白，回到下方两列。
    /// 视频固有尺寸取自播放器的 NaturalVideoWidth / Height（已含旋转处理）；
    /// 预览不可用或尺寸未知时保守回窄模式。
    ///
    /// 判据只依赖整行宽度与视频宽高比，与当前处于哪种模式无关——
    /// 因此在阈值附近拖动窗口不会来回抖动。
    /// </summary>
    private void UpdateResponsiveLayout()
    {
        var session = Session;
        var natW = (double)(session?.NaturalVideoWidth ?? 0);
        var natH = (double)(session?.NaturalVideoHeight ?? 0);

        if (!ViewModel.HasVideo || ViewModel.PreviewFailed ||
            natW <= 0 || natH <= 0 || PreviewGrid.ActualWidth <= 0)
        {
            ApplyLayout(wide: false);
            return;
        }

        var total = PreviewGrid.ActualWidth;
        var videoWidth = Math.Min(total, PreviewHeight * natW / natH);
        var sideSpace = (total - videoWidth) / 2;

        var need = StartPanel.ActualWidth > 0 ? StartPanel.ActualWidth : SidePanelFallbackWidth;
        ApplyLayout(wide: sideSpace >= need + SideGap);
    }

    private void ApplyLayout(bool wide)
    {
        if (wide == _wideLayout)
        {
            return;
        }

        _wideLayout = wide;

        if (wide)
        {
            MoveToSlot(StartPanel, SideSlotLeft);
            MoveToSlot(EndPanel, SideSlotRight);
            StartPanel.VerticalAlignment = VerticalAlignment.Center;
            EndPanel.VerticalAlignment = VerticalAlignment.Center;
            StartThumb.Height = SideThumbHeight;
            EndThumb.Height = SideThumbHeight;
            SideLeftColumn.Width = GridLength.Auto;
            SideRightColumn.Width = GridLength.Auto;
            BottomPanels.Visibility = Visibility.Collapsed;
        }
        else
        {
            MoveToSlot(StartPanel, BottomSlotLeft);
            MoveToSlot(EndPanel, BottomSlotRight);
            StartPanel.VerticalAlignment = VerticalAlignment.Top;
            EndPanel.VerticalAlignment = VerticalAlignment.Top;
            StartThumb.Height = BottomThumbHeight;
            EndThumb.Height = BottomThumbHeight;
            SideLeftColumn.Width = new GridLength(0);
            SideRightColumn.Width = new GridLength(0);
            BottomPanels.Visibility = Visibility.Visible;
        }
    }

    /// <summary>把起止块从当前容器搬到目标插槽（元素只有一份实例，reparent 而非复制）。</summary>
    private static void MoveToSlot(FrameworkElement element, Panel slot)
    {
        if (element.Parent is Panel current)
        {
            current.Children.Remove(element);
        }

        slot.Children.Add(element);
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

    /// <summary>离开裁剪页时把工作参数并入 session.json，确保切走也不丢失（配合「保持习惯」）。</summary>
    protected override void OnNavigatedFrom(Microsoft.UI.Xaml.Navigation.NavigationEventArgs e)
    {
        base.OnNavigatedFrom(e);
        MarukoBox.MainWindow.SaveSessionIfEnabled();
    }
}
