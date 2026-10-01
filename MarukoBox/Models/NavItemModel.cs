using System.Collections.Generic;
using System.Linq;
using CommunityToolkit.Mvvm.ComponentModel;
using MarukoBox.Pages;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;

namespace MarukoBox.Models;

/// <summary>
/// 左侧导航栏的数据模型（功能页 + 固定系统项）。
/// <para>
/// 导航项原本全部静态写在 <c>MainWindow.xaml</c> 的 <c>MenuItems</c> 里、顺序固定。
/// v1.10.6 起改为集合驱动：整个侧栏（功能页 + 设置/关于）由本模型集合驱动，
/// 承载控件是 <c>NavigationView.PaneCustomContent</c> 里的 <see cref="ListView"/>——
/// 因为 NavigationView 的菜单区内部是 ItemsRepeater、**没有内置重排与让位动画**，
/// 而 ListView 有官方 drag-reorder（CanDragItems/AllowDrop/CanReorderItems），
/// 自带拖动视觉（被拖项跟随鼠标）、插入指示线与平滑让位动画。
/// </para>
/// <para>
/// <see cref="IsFixed"/> = true 的项（设置 / 关于）不参与重排：<c>DragItemsStarting</c>
/// 里 Cancel 禁拖，且重排后强制归位到列表末尾。
/// </para>
/// </summary>
public sealed partial class NavItemModel : ObservableObject
{
    /// <summary>导航 Tag，也是路由键与顺序持久化键。</summary>
    public string Tag { get; }

    /// <summary>显示文本。</summary>
    public string Label { get; }

    /// <summary>图标（WinUI <see cref="Symbol"/> 枚举）。</summary>
    public Symbol Icon { get; }

    /// <summary>点击后导航到的页面类型。</summary>
    public Type PageType { get; }

    /// <summary>是否固定项（设置 / 关于）：不可拖动、重排后强制归位末尾。</summary>
    public bool IsFixed { get; }

    /// <summary>「设置」项上的新版提醒徽标可见性（启动自动检查发现新版本时点亮）。</summary>
    private Visibility _updateBadgeVisibility = Visibility.Collapsed;
    public Visibility UpdateBadgeVisibility
    {
        get => _updateBadgeVisibility;
        set => SetProperty(ref _updateBadgeVisibility, value);
    }

    /// <summary>「设置」项上的重启提醒徽标可见性（主题 / 用户级别改后选「稍后重启」时点亮）。</summary>
    private Visibility _restartBadgeVisibility = Visibility.Collapsed;
    public Visibility RestartBadgeVisibility
    {
        get => _restartBadgeVisibility;
        set => SetProperty(ref _restartBadgeVisibility, value);
    }

    /// <summary>「设置」项上方的分组分隔线可见性（仅首个系统项显示，区分功能页 / 系统入口）。</summary>
    private Visibility _dividerVisibility = Visibility.Collapsed;
    public Visibility DividerVisibility
    {
        get => _dividerVisibility;
        set => SetProperty(ref _dividerVisibility, value);
    }

    public NavItemModel(string tag, string label, Symbol icon, Type pageType, bool isFixed = false)
    {
        Tag = tag;
        Label = label;
        Icon = icon;
        PageType = pageType;
        IsFixed = isFixed;
    }

    /// <summary>功能页的默认顺序（出厂排列）。可拖拽重排。</summary>
    public static IReadOnlyList<NavItemModel> DefaultFeatures { get; } = new List<NavItemModel>
    {
        new("Video", "视频", Symbol.Video, typeof(VideoPage)),
        new("Audio", "音频", Symbol.Audio, typeof(AudioPage)),
        new("Mux", "封装", Symbol.Library, typeof(MuxPage)),
        new("Subtitle", "字幕", Symbol.Font, typeof(SubtitlePage)),
        new("Extract", "抽取", Symbol.Download, typeof(ExtractPage)),
        new("Image", "图片", Symbol.Pictures, typeof(ImagePage)),
        new("Tools", "工具", Symbol.Repair, typeof(ToolsPage)),
        new("Trim", "裁剪", Symbol.Crop, typeof(TrimPage)),
    };

    /// <summary>固定系统项（设置 / 关于）：始终位于列表末尾，不参与重排。</summary>
    public static IReadOnlyList<NavItemModel> DefaultSystemItems { get; } = new List<NavItemModel>
    {
        new("Settings", "设置", Symbol.Setting, typeof(SettingsPage), isFixed: true)
        {
            DividerVisibility = Visibility.Visible, // 功能页 / 系统入口 之间的分组分隔线
        },
        new("About", "关于", Symbol.Help, typeof(AboutPage), isFixed: true),
    };

    /// <summary>默认顺序的 Tag 列表（用于校验 / 重建已存顺序）。</summary>
    public static IReadOnlyList<string> DefaultFeatureTags { get; } =
        DefaultFeatures.Select(f => f.Tag).ToList();
}
