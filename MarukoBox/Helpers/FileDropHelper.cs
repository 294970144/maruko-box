using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Threading.Tasks;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using Windows.ApplicationModel.DataTransfer;
using Windows.Storage;
using Windows.Storage.Pickers;

namespace MarukoBox.Helpers;

/// <summary>
/// 文件拖放与格式白名单的统一封装。
///
/// 存在的意义：原先各页的 FileOpenPicker 一律 <c>FileTypeFilter.Add("*")</c>，
/// 用户可以把任意文件塞进来，直到 ffmpeg 执行时才炸。这里把"哪些文件允许进"这件事
/// 收敛到一处，并让 <b>拖放</b> 与 <b>文件选择器</b> 共用同一份白名单，
/// 保证两条入口的行为完全一致。
/// </summary>
public static class FileDropHelper
{
    // ---------- 白名单：按"这步操作实际能处理什么"划分 ----------

    /// <summary>视频容器（含常见封装格式）。</summary>
    public static readonly string[] Video =
    {
        ".mp4", ".mkv", ".mov", ".webm", ".avi", ".flv", ".wmv", ".ts", ".m2ts",
        ".m4v", ".mpg", ".mpeg", ".3gp", ".vob"
    };

    /// <summary>纯音频格式。</summary>
    public static readonly string[] Audio =
    {
        ".mp3", ".wav", ".aac", ".flac", ".m4a", ".ogg", ".opus", ".wma", ".aiff", ".aif", ".ape"
    };

    /// <summary>字幕格式（外挂 / 可转换）。</summary>
    public static readonly string[] Subtitle =
    {
        ".srt", ".ass", ".ssa", ".vtt", ".sub", ".smi", ".ttml"
    };

    /// <summary>图片格式（ffmpeg 能直接解码转码的常见静态图）。</summary>
    public static readonly string[] Image =
    {
        ".png", ".jpg", ".jpeg", ".bmp", ".webp", ".tiff", ".tif", ".gif"
    };

    /// <summary>
    /// 音视频通吃：用于"源文件可以是视频也可以是音频"的场景
    /// （音频提取页：既能从 mp4 抽音轨，也能把 mp3 转成 aac）。
    /// </summary>
    public static readonly string[] Media = Video.Concat(Audio).ToArray();

    // ---------- 拖放：通用读取与过滤 ----------

    /// <summary>本次拖拽是否携带文件（拖文本 / 链接进来一律不接受）。</summary>
    public static bool HasFiles(DragEventArgs e)
        => e.DataView?.Contains(StandardDataFormats.StorageItems) == true;

    /// <summary>取出拖拽中的所有文件路径（目录会被忽略）。</summary>
    public static async Task<List<string>> GetPathsAsync(DragEventArgs e)
    {
        if (e.DataView is null)
        {
            return new List<string>();
        }

        var items = await e.DataView.GetStorageItemsAsync();
        return items.OfType<StorageFile>().Select(f => f.Path).ToList();
    }

    /// <summary>判断单个文件是否在白名单内（扩展名忽略大小写）。</summary>
    public static bool IsAllowed(string path, IEnumerable<string> allowed)
        => allowed.Contains(Path.GetExtension(path), StringComparer.OrdinalIgnoreCase);

    /// <summary>
    /// 按白名单把一批路径拆成「接受」与「拒绝」两组，
    /// 让调用方能分别执行"添加"与"提示忽略了多少个"。
    /// </summary>
    public static (List<string> Accepted, List<string> Rejected) Split(
        IEnumerable<string> paths, IEnumerable<string> allowed)
    {
        var accepted = new List<string>();
        var rejected = new List<string>();

        foreach (var p in paths)
        {
            if (IsAllowed(p, allowed))
            {
                accepted.Add(p);
            }
            else
            {
                rejected.Add(p);
            }
        }

        return (accepted, rejected);
    }

    /// <summary>把白名单拼成给用户看的短文案，例如「mp4 / mkv / mov…」。</summary>
    public static string Describe(IEnumerable<string> allowed, int max = 6)
    {
        var list = allowed.Select(e => e.TrimStart('.')).ToList();
        var head = list.Take(max);
        var text = string.Join(" / ", head);
        return list.Count > max ? text + "…" : text;
    }

    // ---------- 文件选择器：与拖放共用白名单 ----------

    /// <summary>
    /// 创建一个已按白名单设好过滤并初始化 HWND 的 FileOpenPicker，
    /// 取代原先各处手写的 <c>FileTypeFilter.Add("*")</c>。
    /// </summary>
    public static FileOpenPicker CreatePicker(
        IEnumerable<string> allowed,
        PickerLocationId location = PickerLocationId.VideosLibrary,
        PickerViewMode viewMode = PickerViewMode.List)
    {
        var picker = new FileOpenPicker
        {
            ViewMode = viewMode,
            SuggestedStartLocation = location
        };

        foreach (var ext in allowed)
        {
            picker.FileTypeFilter.Add(ext);
        }

        WinRT.Interop.InitializeWithWindow.Initialize(picker, App.WindowHandle);
        return picker;
    }

    /// <summary>拖放结束后：提示用户有多少个文件因格式不受支持而被忽略。</summary>
    public static async Task NotifyRejectedAsync(XamlRoot? root, int rejectedCount, IEnumerable<string> allowed)
    {
        if (rejectedCount <= 0 || root is null)
        {
            return;
        }

        var dialog = new ContentDialog
        {
            Title = "已忽略不支持的文件",
            Content = $"忽略了 {rejectedCount} 个不受支持的文件。\n\n此处仅支持：{Describe(allowed)}",
            CloseButtonText = "知道了",
            XamlRoot = root
        };

        await dialog.ShowAsync();
    }

    // ---------- 视觉反馈：拖入时卡片描边高亮 ----------

    // 用弱表记录卡片原来的边框样式，避免拖放结束后把它写死成高亮色。
    private static readonly ConditionalWeakTable<Border, BorderSnapshot> Snapshots = new();

    private sealed class BorderSnapshot
    {
        public Brush? Brush { get; init; }
        public Thickness Thickness { get; init; }
    }

    /// <summary>DragEnter：给卡片套上强调色描边，告诉用户"放这儿可以"。</summary>
    public static void Highlight(Border border)
    {
        if (!Snapshots.TryGetValue(border, out _))
        {
            Snapshots.Add(border, new BorderSnapshot
            {
                Brush = border.BorderBrush,
                Thickness = border.BorderThickness
            });
        }

        border.BorderBrush = AccentBrush();
        border.BorderThickness = new Thickness(2);
    }

    /// <summary>DragLeave / Drop：还原卡片原本的边框样式。</summary>
    public static void Restore(Border border)
    {
        if (!Snapshots.TryGetValue(border, out var snap))
        {
            return;
        }

        border.BorderBrush = snap.Brush;
        border.BorderThickness = snap.Thickness;
        Snapshots.Remove(border);
    }

    private static Brush AccentBrush()
    {
        if (Application.Current.Resources.TryGetValue("AccentFillColorDefaultBrush", out var v) && v is Brush b)
        {
            return b;
        }

        return new SolidColorBrush(Windows.UI.Color.FromArgb(255, 0, 120, 215));
    }
}
