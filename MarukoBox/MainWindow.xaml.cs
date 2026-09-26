using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;
using MarukoBox.Pages;
using MarukoBox.Services;

namespace MarukoBox;

/// <summary>
/// 应用主窗口。承载 NavigationView 外壳与内容 Frame。
/// 导航逻辑按官方 WinUI 3 范式在代码后置中处理（选区变更驱动 Frame.Navigate）。
/// </summary>
public sealed partial class MainWindow : Window
{
    public MainWindow()
    {
        InitializeComponent();

        ExtendsContentIntoTitleBar = true;
        SetTitleBar(AppTitleBar);

        // 用绝对路径（BaseDirectory 即 exe 所在目录），避免从开始菜单快捷方式启动时
        // 当前工作目录(CWD)≠exe 目录导致相对路径 "Assets/AppIcon.ico" 解析失败、图标静默失效。
        AppWindow.SetIcon(System.IO.Path.Combine(AppContext.BaseDirectory, "Assets", "AppIcon.ico"));

        // WinUI 3 的 Window 没有 MinWidth / MinHeight 属性，最小尺寸只能经
        // OverlappedPresenter 设置；低于该尺寸时三列布局会挤成一团，故设下限。
        if (AppWindow.Presenter is Microsoft.UI.Windowing.OverlappedPresenter presenter)
        {
            presenter.PreferredMinimumWidth = MinWindowWidth;
            presenter.PreferredMinimumHeight = MinWindowHeight;
        }

        // 导航窗格宽度/紧凑态恢复（config 独立字段，始终记忆，不受「保持习惯」开关影响）
        ApplyNavPaneStateFromConfig();
    }

    /// <summary>窗口最小宽度（三列布局的可用地：左 200 + 中 300 + 右 220 + 间距与内边距）。</summary>
    private const int MinWindowWidth = 1000;

    /// <summary>窗口最小高度。</summary>
    private const int MinWindowHeight = 640;

    /// <summary>
    /// 「设置」导航项上的重启提醒徽标：主题 / 用户级别保存后选择「稍后重启」时点亮，
    /// 直到用户不再有未生效的重启类修改（再次保存恢复原值）或应用重启。
    /// </summary>
    public void SetRestartPending(bool visible)
    {
        if (RestartPendingBadge is not null)
        {
            RestartPendingBadge.Visibility = visible ? Visibility.Visible : Visibility.Collapsed;
        }
    }

    /// <summary>
    /// 「设置」导航项上的新版提醒徽标：启动自动检查发现新版本时点亮，
    /// 用户进入「检查更新」流程后由 SettingsViewModel 熄灭。
    /// </summary>
    public void SetUpdateAvailable(bool visible)
    {
        if (UpdateAvailableBadge is not null)
        {
            UpdateAvailableBadge.Visibility = visible ? Visibility.Visible : Visibility.Collapsed;
        }
    }

    // ---------- 导航窗格拖拽调宽（v1.9.1 根网格热区方案，参考 Edge 侧边栏交互） ----------
    // 官方依据（Microsoft Learn《NavigationView》）：NavigationView 没有内置的
    // 拖拽调宽能力，但明确支持在应用代码中自行管理显示模式；本方案 Pin 死
    // PaneDisplayMode=Left（始终展开、带文字），运行时修改 OpenPaneLength 为
    // 官方支持路径（属性变更联动 NavigationViewTemplateSettings.OpenPaneLength
    // 驱动模板重新布局）。
    //
    // v1.9.0 失败教训（根因，勿改回）：
    //  ①原方案用 10px 透明 Border 叠在窗格右缘，PointerEntered 里把 Background
    //    换成从 Application.Current.Resources 找 "SystemAccentColor" 的画刷——
    //    WinUI 3 框架主题资源不在 Application.Resources 里，TryGetValue 失败返回
    //    null；**Background=null 的元素失去命中测试**，指针首次悬停后热区即失效，
    //    Pressed/Moved 永不触发，表现为「拖拽完全无效」。
    //  ②窄条位置靠 Margin 手动同步（仅构造时+拖动中更新），窗口尺寸变化后漂移。
    // 新方案（v1.9.1）：整个根网格监听指针，按「指针 X 距窗格右缘 ≤6px」判定
    // 热区；热区内显示左右调整光标、按下开始拖拽并捕获指针。位置零维护，任何
    // 布局变化自动正确；非热区事件不标记 Handled，导航项点击不受影响。
    //
    // v1.9.1 追加（Master 反馈）：放弃「拖窄过阈值自动收成纯图标（LeftCompact）」。
    // 该切换是离散跳变，文字↔无文字之间体验突兀；改为仅拖拽调宽、始终带文字，
    // 不再切任何显示模式。

    /// <summary>窗格最小宽度（始终带文字，不再收成纯图标）。</summary>
    private const double NavPaneMinExpandedWidth = 160;

    /// <summary>窗格最大宽度（窗口最小宽 1000 时内容区仍 ≥ 520）。</summary>
    private const double NavPaneMaxExpandedWidth = 480;

    /// <summary>窗格右缘两侧的热区半宽（px）：光标反馈与按下命中都按它判定。</summary>
    private const double NavPaneResizeHitHalfWidth = 6;

    /// <summary>是否正处于拖拽中（已按下并捕获指针）。</summary>
    private bool _paneDragging;

    /// <summary>光标是否已置于热区态（避免每次 PointerMoved 重复设置光标）。</summary>
    private bool _paneHotZone;

    /// <summary>
    /// WinUI 3 的 <c>UIElement.ProtectedCursor</c> 是 protected 成员，外部无法直接赋值；
    /// 反射缓存该属性实现「给任意元素设置光标」——悬停 ↔ 光标是拖拽调宽的可发现性关键。
    /// 在根网格上设置时对整棵子树生效（子元素未自行设置光标时继承）。
    /// </summary>
    private static readonly System.Reflection.PropertyInfo? ElementCursorProperty =
        typeof(UIElement).GetProperty("ProtectedCursor",
            System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);

    private static void SetElementCursor(UIElement element, Microsoft.UI.Input.InputCursor? cursor)
    {
        ElementCursorProperty?.SetValue(element, cursor);
    }

    private static Microsoft.UI.Input.InputCursor SizeWestEastCursor { get; } =
        Microsoft.UI.Input.InputSystemCursor.Create(
            Microsoft.UI.Input.InputSystemCursorShape.SizeWestEast);

    /// <summary>构造尾部调用：按 config 恢复窗格模式与展开宽度。</summary>
    private void ApplyNavPaneStateFromConfig()
    {
        try
        {
            // 始终展开模式（带文字），仅恢复上次记忆的展开宽度
            var config = AppServices.Config.Load();
            NavView.OpenPaneLength = Math.Clamp(config.NavPaneExpandedWidth, NavPaneMinExpandedWidth, NavPaneMaxExpandedWidth);
        }
        catch
        {
            // 恢复失败用 XAML 默认值（Left / 默认 OpenPaneLength），不阻塞启动
        }
    }

    /// <summary>当前窗格右缘在窗口坐标系的 X（始终展开态 = OpenPaneLength）。</summary>
    private double PaneEdgeX => NavView.OpenPaneLength;

    /// <summary>指针 X 是否落在窗格右缘的拖拽热区内。</summary>
    private bool IsNearPaneEdge(double x) =>
        Math.Abs(x - PaneEdgeX) <= NavPaneResizeHitHalfWidth;

    private void RootGrid_PointerMoved(object sender, PointerRoutedEventArgs e)
    {
        // 指针在 NavigationView 坐标系的 X（NavView 从根网格左缘铺满，与窗口系一致）
        var x = e.GetCurrentPoint(NavView).Position.X;

        if (_paneDragging)
        {
            HandlePaneDrag(x);
            e.Handled = true;
            return;
        }

        // 非拖拽态：仅在热区边界跨越时切换光标（悬停反馈）
        UpdatePaneHotZone(IsNearPaneEdge(x));
    }

    /// <summary>切换热区光标（带状态记忆，避免每帧反射赋值）。</summary>
    private void UpdatePaneHotZone(bool hot)
    {
        if (_paneHotZone == hot)
        {
            return;
        }

        _paneHotZone = hot;
        SetElementCursor(RootGrid, hot ? SizeWestEastCursor : null);
    }

    /// <summary>
    /// 热区内按下 → 开始拖拽并捕获指针。仅此时标记 Handled；
    /// 热区外（含落在导航项上的按压）事件照常冒泡，导航交互不受影响。
    /// </summary>
    private void RootGrid_PointerPressed(object sender, PointerRoutedEventArgs e)
    {
        var point = e.GetCurrentPoint(NavView);
        if (!IsNearPaneEdge(point.Position.X) || !point.Properties.IsLeftButtonPressed)
        {
            return;
        }

        _paneDragging = true;
        _paneHotZone = true;
        RootGrid.CapturePointer(e.Pointer);
        SetElementCursor(RootGrid, SizeWestEastCursor);
        e.Handled = true;
    }

    /// <summary>拖拽中：跟随指针改窗格展开宽度。始终带文字，不做任何显示模式切换。</summary>
    private void HandlePaneDrag(double x)
    {
        NavView.OpenPaneLength = Math.Clamp(x, NavPaneMinExpandedWidth, NavPaneMaxExpandedWidth);
    }

    private void RootGrid_PointerReleased(object sender, PointerRoutedEventArgs e)
    {
        if (!_paneDragging)
        {
            return;
        }

        EndPaneDrag(e);
        e.Handled = true;
    }

    private void RootGrid_PointerCanceled(object sender, PointerRoutedEventArgs e)
    {
        if (!_paneDragging)
        {
            return;
        }

        EndPaneDrag(e);
    }

    private void RootGrid_PointerCaptureLost(object sender, PointerRoutedEventArgs e)
    {
        if (!_paneDragging)
        {
            return;
        }

        // 捕获意外丢失（如系统打断）：同样收尾，布局状态以当前值为准照常持久化
        EndPaneDrag(e);
    }

    /// <summary>拖拽结束：释放捕获、复位光标，把当前展开宽度写入 config。</summary>
    private void EndPaneDrag(PointerRoutedEventArgs e)
    {
        _paneDragging = false;
        _paneHotZone = false;
        RootGrid.ReleasePointerCapture(e.Pointer);
        SetElementCursor(RootGrid, null);
        PersistNavPaneState();
    }

    /// <summary>把当前展开宽度写入 config（即改即存，一次拖动只写一次盘）。</summary>
    private void PersistNavPaneState()
    {
        try
        {
            var config = AppServices.Config.Load();
            config.NavPaneExpandedWidth = NavView.OpenPaneLength;
            AppServices.Config.Save(config);
        }
        catch
        {
            // 持久化失败不阻塞交互；本次会话内布局仍生效，下次启动回默认
        }
    }

    /// <summary>
    /// 保持习惯：若配置开启，则从当前 Frame 找到视频页并把参数快照写入 session.json。
    /// 由 App.OnLaunched 挂到 Window.Closed（含「立即重启」的 Exit 流程）。
    /// 静态方法 + 容错：任何失败都不应阻塞退出。
    /// </summary>
    public static void SaveSessionIfEnabled()
    {
        try
        {
            if (!AppServices.Config.Load().RememberLastSession)
            {
                return;
            }

            if (App.Window?.Content is null)
            {
                return;
            }

            // 【N1 修复】直接用 XAML 里 x:Name 的 ContentFrame。
            // 此前经 FindDescendant<Frame> 在视觉树里找 Frame，但该实现只递归 Panel 派生类的
            // Children——NavigationView（ContentControl）与 TitleBar（Control）都不是 Panel，
            // 其模板内的 Frame 属于结构性盲区，递归恒返回 null，导致「保持习惯」从未真正保存。
            // 官方范式就是直接引用命名元素，无需视觉树遍历。
            var frame = (App.Window as MainWindow)?.ContentFrame;

            // 合并式保存：读出已有快照，只更新「当前可见页」对应的块，再整体写回，
            // 避免视频页与裁剪页的「保持习惯」参数互相覆盖（此前只会保存视频页）。
            var existing = ConfigService.LoadSession() ?? new MarukoBox.Models.SessionState();

            var page = frame?.Content;
            if (page is VideoPage video)
            {
                var snapshot = video.ViewModel.CaptureSession();
                existing.Settings = snapshot.Settings;
                existing.QualityPreset = snapshot.QualityPreset;
                existing.OutputDir = snapshot.OutputDir;
            }
            else if (page is TrimPage trim)
            {
                existing.Trim = trim.ViewModel.CaptureTrimSession();
            }

            ConfigService.SaveSession(existing);
        }
        catch
        {
            // 会话保存失败不阻塞退出
        }
    }

    private void NavView_Loaded(object sender, RoutedEventArgs e)
    {
        // 默认进入「视频」页（XAML 已标记 IsSelected）。
        ContentFrame.Navigate(typeof(VideoPage));
    }

    private void NavView_SelectionChanged(NavigationView sender, NavigationViewSelectionChangedEventArgs args)
    {
        if (args.SelectedItem is not NavigationViewItem item)
        {
            return;
        }

        var tag = item.Tag?.ToString() ?? string.Empty;

        var pageType = tag switch
        {
            "Video" => typeof(VideoPage),
            "Trim" => typeof(TrimPage),
            "Extract" => typeof(ExtractPage),
            "Audio" => typeof(AudioPage),
            "Mux" => typeof(MuxPage),
            "Image" => typeof(ImagePage),
            "Tools" => typeof(ToolsPage),
            "Subtitle" => typeof(SubtitlePage),
            "Settings" => typeof(SettingsPage),
            "About" => typeof(AboutPage),
            _ => typeof(PlaceholderPage)
        };

        if (pageType == typeof(PlaceholderPage))
        {
            ContentFrame.Navigate(pageType, item.Content?.ToString() ?? tag);
        }
        else
        {
            ContentFrame.Navigate(pageType);
        }
    }
}
