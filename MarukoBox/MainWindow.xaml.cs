using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using Microsoft.UI.Input;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Media;
using Windows.Foundation;
using Windows.UI;
using MarukoBox.Models;
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

    /// <summary>
    /// 将窗口提到前台（系统通知被点击时调用）。
    /// 最小化状态下也经 <see cref="Microsoft.UI.Windowing.AppWindow.Show"/> 恢复显示。
    /// </summary>
    public void BringToForeground()
    {
        try
        {
            AppWindow.Show();
        }
        catch (Exception ex)
        {
            App.LogCrash(ex, "MainWindow.BringToForeground");
        }
    }

    /// <summary>窗口最小宽度（三列布局的可用地：左 200 + 中 300 + 右 220 + 间距与内边距）。</summary>
    private const int MinWindowWidth = 1000;

    /// <summary>窗口最小高度。</summary>
    private const int MinWindowHeight = 640;

    /// <summary>
    /// 「设置」导航项上的重启提醒徽标：主题 / 用户级别保存后选择「稍后重启」时点亮，
    /// 直到用户不再有未生效的重启类修改（再次保存恢复原值）或应用重启。
    /// <para>经 <see cref="NavItemModel.RestartBadgeVisibility"/> 驱动（列表项已数据绑定）；
    /// 若在导航列表构建前被调用（如启动早期），值暂存待构建后套用。</para>
    /// </summary>
    public void SetRestartPending(bool visible)
    {
        _restartPending = visible;
        ApplyBadgeState();
    }

    /// <summary>
    /// 「设置」导航项上的新版提醒徽标：启动自动检查发现新版本时点亮，
    /// 用户进入「检查更新」流程后由 SettingsViewModel 熄灭。
    /// </summary>
    public void SetUpdateAvailable(bool visible)
    {
        _updateAvailable = visible;
        ApplyBadgeState();
    }

    /// <summary>徽标状态暂存（导航列表构建完成前也允许设置，构建后自动套用）。</summary>
    private bool _restartPending;

    private bool _updateAvailable;

    private void ApplyBadgeState()
    {
        if (_settingsItem is null)
        {
            return; // 尚未构建，值已暂存，构建时套用
        }

        _settingsItem.RestartBadgeVisibility = _restartPending ? Visibility.Visible : Visibility.Collapsed;
        _settingsItem.UpdateBadgeVisibility = _updateAvailable ? Visibility.Visible : Visibility.Collapsed;
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
        // 构建侧栏列表（功能页按「保持习惯」记忆的顺序 + 固定系统项），并默认高亮视频页。
        BuildNavItems();

        // 默认选中「视频」页：设置 SelectedItem 触发 SelectionChanged 完成导航；
        // 若所选 Tag 当前不存在（理论上不会），兜底直接 Navigate。
        var start = _navItems.FirstOrDefault(i => i.Tag == DefaultStartTag);
        if (start is not null)
        {
            FeatureList.SelectedItem = start;
        }

        if (ContentFrame.Content is null)
        {
            ContentFrame.Navigate(typeof(VideoPage));
        }
    }

    /// <summary>侧栏选中项变更 → 导航（功能页与系统项统一走模型上的 PageType，不再按 Tag 硬编码）。</summary>
    private void FeatureList_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (FeatureList.SelectedItem is not NavItemModel model)
        {
            return; // 重排 / 清空时的中间态，不导航
        }

        if (model.PageType == typeof(PlaceholderPage))
        {
            ContentFrame.Navigate(model.PageType, model.Label);
        }
        else
        {
            ContentFrame.Navigate(model.PageType);
        }
    }

    /// <summary>
    /// 起拖前拦截：固定项（设置 / 关于）不允许被拖走。
    /// 注意 e.Items 可能含多项（多选时），任一为固定项即整单取消。
    /// </summary>
    private void FeatureList_DragItemsStarting(object sender, DragItemsStartingEventArgs e)
    {
        if (e.Items.Any(i => i is NavItemModel model && model.IsFixed))
        {
            e.Cancel = true;
            return;
        }

        // 消除「两张卡」：ListView 内置重排会把源项继续留在列表里渲染（作为被拖占位），
        // 同时系统又生成一张跟随鼠标的 drag visual —— 于是原位与被拖卡同时可见，
        // 让位动画经过时更会与相邻项重叠（Edge 观感里原位只应留下一个空槽）。
        // 故起拖时把源容器隐藏：用 Opacity=0（而非 Visibility=Collapsed）保留占位高度，
        // 这样"空槽"会随插入位置一起移动，正是「经过时让出空位」的视觉。
        foreach (var item in e.Items)
        {
            if (FeatureList.ContainerFromItem(item) is UIElement container)
            {
                container.Opacity = 0;
                _hiddenDragContainers.Add(container);
            }
        }
    }

    /// <summary>
    /// 拖拽完成：系统已按插入位置重排 ItemsSource（ObservableCollection）。
    /// 先恢复被隐藏的源容器，再把固定项强制归位到列表末尾（防止被插到功能页之间），最后持久化顺序。
    /// 注意：取消拖拽（ESC / 拖出列表外）同样会触发本事件，故恢复逻辑必须无条件执行。
    /// </summary>
    private void FeatureList_DragItemsCompleted(ListViewBase sender, DragItemsCompletedEventArgs args)
    {
        RestoreDragContainers();
        NormalizeFixedItemsToEnd();
        PersistNavOrder();
    }

    /// <summary>
    /// 恢复拖拽期间被隐藏的源容器；并全量兜底一次——容器可能被虚拟化回收/复用，
    /// 仅按记录恢复存在「留下永久透明项」的风险。
    /// </summary>
    private void RestoreDragContainers()
    {
        foreach (var container in _hiddenDragContainers)
        {
            container.Opacity = 1;
        }

        _hiddenDragContainers.Clear();

        foreach (var model in _navItems)
        {
            if (FeatureList.ContainerFromItem(model) is UIElement container && container.Opacity < 1)
            {
                container.Opacity = 1;
            }
        }
    }

    // ---------- 导航栏：ListView 集合驱动 + WinUI 内置拖拽重排（v1.10.6 修订） ----------
    // 官方依据（Microsoft Learn《NavigationView》/ WinUI Gallery 拖拽示例）：
    // NavigationView 的菜单区内部是 ItemsRepeater，**没有内置重排与让位动画**——
    // 自绘拖拽必然「跳格生硬」。WinUI 的 ListView 则有官方 drag-reorder：
    // CanDragItems + AllowDrop + CanReorderItems 三件套，系统自带「被拖项跟随鼠标」的
    // 拖动视觉、插入指示线与平滑让位动画。故整个侧栏（功能页 + 设置/关于）统一放在
    // NavigationView.PaneCustomContent 的 ListView 里，顺序由 ListView 自己维护。
    //
    // 硬性前提（踩坑点，勿改）：
    //  ①数据源必须 ObservableCollection——用 List 则重排后 UI 不更新；
    //  ②ItemsPanel 必须实现 IInsertionPanel（默认面板满足），换成自定义面板
    //    （如 WrapPanel）会导致拖拽时显示红色禁止图标；
    //  ③三件套需在 UI 树构建完成后生效（本处在 XAML 静态声明，等价 Loaded 后设置）。
    //
    // 设置 / 关于标 IsFixed=true：DragItemsStarting 里 Cancel 禁拖，
    // 并在拖拽完成后强制归位到列表末尾（防止被插进功能页之间）。
    // 「保持习惯」开启时，功能页顺序写入 session.json 的 NavItemOrder。

    /// <summary>启动默认选中的功能页 Tag（即便自定义过顺序也不改默认进入页）。</summary>
    private const string DefaultStartTag = "Video";

    /// <summary>侧栏导航项数据源（功能页 + 固定系统项），集合顺序即 UI 顺序。</summary>
    private readonly ObservableCollection<NavItemModel> _navItems = new();

    /// <summary>「设置」项实例（新版 / 重启徽标经其模型属性驱动）。</summary>
    private NavItemModel? _settingsItem;

    /// <summary>拖拽期间被临时隐藏的源项容器（拖完统一恢复，避免「原位残留」永久透明）。</summary>
    private readonly List<UIElement> _hiddenDragContainers = new();

    /// <summary>构建侧栏列表（仅首次）：功能页按已记忆顺序 + 固定系统项置尾。</summary>
    private void BuildNavItems()
    {
        if (_navItems.Count > 0)
        {
            return;
        }

        foreach (var tag in ResolveOrderFromSession())
        {
            var def = NavItemModel.DefaultFeatures.FirstOrDefault(f => f.Tag == tag);
            if (def is not null)
            {
                _navItems.Add(def);
            }
        }

        foreach (var sys in NavItemModel.DefaultSystemItems)
        {
            _navItems.Add(sys);
            if (sys.Tag == "Settings")
            {
                _settingsItem = sys;
            }
        }

        // 套用列表构建前就已设置的徽标状态（如启动自动检查发现新版）
        ApplyBadgeState();

        FeatureList.ItemsSource = _navItems;
    }

    /// <summary>读取「保持习惯」下的自定义顺序；关闭或缺失时回落默认顺序（向前兼容）。</summary>
    private List<string> ResolveOrderFromSession()
    {
        try
        {
            if (!AppServices.Config.Load().RememberLastSession)
            {
                return NavItemModel.DefaultFeatureTags.ToList();
            }

            var saved = ConfigService.LoadSession()?.NavItemOrder;
            if (saved is null || saved.Count == 0)
            {
                return NavItemModel.DefaultFeatureTags.ToList();
            }

            // 以已存顺序为基准：过滤非法 Tag、去重，并补齐缺失的默认 Tag（向前兼容）。
            var valid = saved.Where(t => NavItemModel.DefaultFeatureTags.Contains(t)).Distinct().ToList();
            foreach (var t in NavItemModel.DefaultFeatureTags)
            {
                if (!valid.Contains(t))
                {
                    valid.Add(t);
                }
            }

            return valid;
        }
        catch
        {
            return NavItemModel.DefaultFeatureTags.ToList();
        }
    }

    /// <summary>把当前功能页顺序写入 session（仅「保持习惯」开启时；固定项不写入）。</summary>
    private void PersistNavOrder()
    {
        try
        {
            if (!AppServices.Config.Load().RememberLastSession)
            {
                return; // 关闭则不持久化，顺序仅本次会话生效
            }

            var session = ConfigService.LoadSession() ?? new SessionState();
            session.NavItemOrder = _navItems.Where(i => !i.IsFixed).Select(i => i.Tag).ToList();
            ConfigService.SaveSession(session);
        }
        catch
        {
            // 持久化失败不阻塞交互
        }
    }

    /// <summary>恢复导航栏默认顺序（设置页「恢复默认导航顺序」调用）。</summary>
    public void ResetNavOrderToDefault()
    {
        var selected = FeatureList.SelectedItem;
        var fixedItems = _navItems.Where(i => i.IsFixed).ToList();

        _navItems.Clear();
        foreach (var f in NavItemModel.DefaultFeatures)
        {
            _navItems.Add(f);
        }

        foreach (var s in fixedItems)
        {
            _navItems.Add(s);
        }

        // 重建集合会清空 ListView 选中，实例仍在集合中故可直接还原
        if (selected is not null)
        {
            FeatureList.SelectedItem = selected;
        }

        PersistNavOrder();
    }

    /// <summary>把固定项（设置 / 关于）归位到列表末尾，保持其相对顺序。</summary>
    private void NormalizeFixedItemsToEnd()
    {
        var firstFixed = -1;
        for (var i = 0; i < _navItems.Count; i++)
        {
            if (_navItems[i].IsFixed)
            {
                firstFixed = i;
                break;
            }
        }

        if (firstFixed < 0)
        {
            return;
        }

        // 固定项已全部位于末尾则无需重建（避免无谓的容器重建与选中闪烁）
        var alreadyAtEnd = true;
        for (var i = firstFixed; i < _navItems.Count; i++)
        {
            if (!_navItems[i].IsFixed)
            {
                alreadyAtEnd = false;
                break;
            }
        }

        if (alreadyAtEnd)
        {
            return;
        }

        var selected = FeatureList.SelectedItem;
        var features = _navItems.Where(i => !i.IsFixed).ToList();
        var fixedItems = _navItems.Where(i => i.IsFixed).ToList();

        _navItems.Clear();
        foreach (var f in features)
        {
            _navItems.Add(f);
        }

        foreach (var s in fixedItems)
        {
            _navItems.Add(s);
        }

        if (selected is not null)
        {
            FeatureList.SelectedItem = selected;
        }
    }
}
