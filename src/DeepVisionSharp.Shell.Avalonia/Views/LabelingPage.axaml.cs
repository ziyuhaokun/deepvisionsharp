using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.VisualTree;
using DeepVisionSharp.Presentation.Input;
using DeepVisionSharp.Presentation.ViewModels.Labeling;
using DeepVisionSharp.Shell.Avalonia.Controls.Labeling;
using DeepVisionSharp.Shell.Avalonia.Services;
using DeepVisionSharp.Shell.Avalonia.ViewModels;

namespace DeepVisionSharp.Shell.Avalonia.Views;

/// <summary>
/// 标记工作台页面：左中右三栏分别承载工具轨道、画布与样本条、鸟瞰图与详情轨道。
/// DataContext 来自容器的 <see cref="LabelingViewModel"/>；没有容器（设计时 / 无头渲染）时
/// 退回 <see cref="SampleLabelingContext"/>，绑定路径不变。
/// 画布事件接到真实 Editor 命令；样本条点击与右键菜单的动作同样落到业务命令。
/// </summary>
public partial class LabelingPage : UserControl
{
    private readonly LabelingViewModel? _viewModel;
    private bool _shortcutInFlight;

    public LabelingPage()
        : this(null)
    {
    }

    public LabelingPage(LabelingViewModel? viewModel)
    {
        InitializeComponent();

        _viewModel = viewModel;
        DataContext = (object?)viewModel ?? new SampleLabelingContext();
        AttachAdaptiveLayout();
        AddHandler(InputElement.KeyDownEvent, OnPreviewKeyDown, RoutingStrategies.Tunnel);
    }

    /// <summary>窄窗口下收起工具/详情栏，改用顶部开关以浮层展开（与推理页同一套实现）。</summary>
    private void AttachAdaptiveLayout()
    {
        if (this.FindControl<Grid>("WorkbenchGrid") is not { } grid
            || this.FindControl<Border>("ToolsPane") is not { } tools
            || this.FindControl<Border>("DetailsPane") is not { } details
            || this.FindControl<StackPanel>("PaneToolbar") is not { } toolbar
            || this.FindControl<Button>("ToolsPaneToggle") is not { } toolsToggle
            || this.FindControl<Button>("DetailsPaneToggle") is not { } detailsToggle)
        {
            return;
        }

        _ = new AdaptiveWorkbenchLayout(grid, tools, details, toolbar, toolsToggle, detailsToggle);
    }

    private async void OnLoaded(object? sender, RoutedEventArgs e)
    {
        AnnotationSurface.FitToView();
        AnnotationSurface.ViewportChanged -= OnAnnotationViewportChanged;
        AnnotationSurface.ViewportChanged += OnAnnotationViewportChanged;
        SyncOverviewViewport();

        if (_viewModel is null)
        {
            return;
        }

        try
        {
            await _viewModel.LoadCommand.ExecuteAsync(null);
        }
        catch (OperationCanceledException)
        {
            // 导航走了／页面被取消，下一次进入会重新加载。
        }
        catch (Exception ex)
        {
            // async void：逃出去的异常会打断整个 UI 线程，这里就地记下。
            System.Diagnostics.Trace.WriteLine($"LabelingPage load failed: {ex}");
        }

        AnnotationSurface.FitToView();
        SyncOverviewViewport();
    }

    private async void OnUnloaded(object? sender, RoutedEventArgs e)
    {
        AnnotationSurface.ViewportChanged -= OnAnnotationViewportChanged;

        if (_viewModel is null)
        {
            return;
        }

        try
        {
            await _viewModel.DeactivateAsync();
        }
        catch (Exception ex)
        {
            System.Diagnostics.Trace.WriteLine($"LabelingPage deactivate failed: {ex}");
        }
    }

    private void OnAnnotationCreated(object? sender, AnnotationCreatedEventArgs e)
    {
        if (_viewModel is not null)
        {
            _viewModel.Editor.SelectBoxCommand.Execute(e.CreatedBox);
            return;
        }

        ((SampleLabelingContext)DataContext!).Editor.SelectBoxCommand.Execute(e.CreatedBox);
    }

    private void OnAnnotationEditStarted(object? sender, EventArgs e)
    {
        if (_viewModel is not null)
        {
            _viewModel.Editor.BeginBoxEditCommand.Execute(null);
            return;
        }

        ((SampleLabelingContext)DataContext!).Editor.BeginBoxEditCommand.Execute(null);
    }

    private void OnAnnotationEditCompleted(object? sender, AnnotationEditCompletedEventArgs e)
    {
        if (_viewModel is not null)
        {
            _viewModel.Editor.NotifyBoxesEditedCommand.Execute(null);
            return;
        }

        ((SampleLabelingContext)DataContext!).Editor.NotifyBoxesEditedCommand.Execute(null);
    }

    private async void OnPreviewKeyDown(object? sender, KeyEventArgs e)
    {
        if (_viewModel is null || !_viewModel.Workspace.HasGallery)
        {
            return;
        }

        if (_shortcutInFlight)
        {
            e.Handled = true;
            return;
        }

        if (IsTextInputTarget(e.Source) || IsCanvasEditingKey(e))
        {
            return;
        }

        var key = AvaloniaInputMapping.ToUiKey(e.Key);
        if (key == UiKey.None)
        {
            return;
        }

        // 与 WPF 预览键盘一致：先标记已处理，避免列表焦点把方向键再消费一次。
        if (IsKnownViewModelShortcut(key, e.KeyModifiers))
        {
            e.Handled = true;
        }

        _shortcutInFlight = true;
        try
        {
            if (await _viewModel.TryHandleKeyboardShortcutAsync(
                key, AvaloniaInputMapping.ToUiModifierKeys(e.KeyModifiers)))
            {
                e.Handled = true;
            }
        }
        catch (Exception ex)
        {
            System.Diagnostics.Trace.WriteLine($"LabelingPage shortcut failed: {ex}");
        }
        finally
        {
            _shortcutInFlight = false;
        }
    }

    private bool IsCanvasEditingKey(KeyEventArgs e)
    {
        if (e.Source is not Visual visual
            || !visual.GetSelfAndVisualAncestors().Any(item => ReferenceEquals(item, AnnotationSurface)))
        {
            return false;
        }

        // Delete / Esc / Ctrl+Z / Ctrl+Y 由画布自己处理并 e.Handled，页面隧道层让路。
        return e.Key is Key.Delete or Key.Escape
            || (e.KeyModifiers == KeyModifiers.Control && e.Key is Key.Z or Key.Y);
    }

    private static bool IsKnownViewModelShortcut(UiKey key, KeyModifiers modifiers)
    {
        if (key is UiKey.Left or UiKey.Right
            or UiKey.Digit0 or UiKey.Digit1 or UiKey.Digit2 or UiKey.Digit3 or UiKey.Digit4
            or UiKey.Digit5 or UiKey.Digit6 or UiKey.Digit7 or UiKey.Digit8 or UiKey.Digit9
            or UiKey.NumPad0 or UiKey.NumPad1 or UiKey.NumPad2 or UiKey.NumPad3 or UiKey.NumPad4
            or UiKey.NumPad5 or UiKey.NumPad6 or UiKey.NumPad7 or UiKey.NumPad8 or UiKey.NumPad9)
        {
            return true;
        }

        return key == UiKey.S && modifiers == KeyModifiers.Control;
    }

    private static bool IsTextInputTarget(object? source)
    {
        if (source is not Visual visual)
        {
            return false;
        }

        foreach (var element in visual.GetSelfAndVisualAncestors())
        {
            if (element is TextBox
                or ComboBox { IsEditable: true }
                or ComboBox { IsDropDownOpen: true })
            {
                return true;
            }
        }

        return false;
    }

    private void OnZoomInClicked(object? sender, RoutedEventArgs e) => AnnotationSurface.ZoomIn();

    private void OnZoomOutClicked(object? sender, RoutedEventArgs e) => AnnotationSurface.ZoomOut();

    private void OnFitClicked(object? sender, RoutedEventArgs e)
    {
        AnnotationSurface.FitToView();
        SyncOverviewViewport();
    }

    private void OnUndoClicked(object? sender, RoutedEventArgs e)
    {
        if (_viewModel is not null)
        {
            _viewModel.Editor.UndoBoxEditCommand.Execute(null);
            return;
        }

        ((SampleLabelingContext)DataContext!).Editor.UndoCommand.Execute(null);
    }

    private void OnRedoClicked(object? sender, RoutedEventArgs e)
    {
        if (_viewModel is not null)
        {
            _viewModel.Editor.RedoBoxEditCommand.Execute(null);
            return;
        }

        ((SampleLabelingContext)DataContext!).Editor.RedoCommand.Execute(null);
    }

    private void OnAnnotationViewportChanged(object? sender, EventArgs e)
    {
        SyncOverviewViewport();
        // VM 的 ViewportRect 是 object（跨框架的透传矩形），初始为 null。
        // 不让 XAML 直接把 null 转成 Avalonia.Rect；由画布事件把本框架的 Rect 写回。
        if (_viewModel?.Editor is not null)
        {
            _viewModel.Editor.ViewportRect = AnnotationSurface.ViewportRect;
        }
    }

    private void OnOverviewNavigateRequested(object? sender, OverviewNavigateEventArgs e)
    {
        AnnotationSurface.CenterViewportAt(e.ImagePoint);
        SyncOverviewViewport();
    }

    private void SyncOverviewViewport() =>
        OverviewSurface.Viewport = AnnotationSurface.GetViewportImageRect();

    private async void OnSampleTilePointerReleased(object? sender, PointerReleasedEventArgs e)
    {
        if (e.InitialPressMouseButton != MouseButton.Left)
        {
            return;
        }

        if (e.Source is Visual source
            && (source is CheckBox || source.FindAncestorOfType<CheckBox>() is not null))
        {
            return;
        }

        if (sender is not Control { DataContext: { } row })
        {
            return;
        }

        // 真实 VM 自己管「当前样本」：点样本条等于切样本（会重新载图并刷新标注）。
        if (_viewModel is not null)
        {
            if (row is LabelingSampleRow sample
                && _viewModel.Samples.SelectSampleCommand.CanExecute(sample.Id))
            {
                e.Handled = true;
                await GuardAsync(
                    () => _viewModel.Samples.SelectSampleCommand.ExecuteAsync(sample.Id),
                    "LabelingPage.SelectSample");
            }

            return;
        }

        foreach (var item in ((SampleLabelingContext)DataContext!).Samples.SamplePageItems)
        {
            item.IsActive = ReferenceEquals(item, row);
        }

        e.Handled = true;
    }

    private void OnSampleTileContextRequested(object? sender, ContextRequestedEventArgs e)
    {
        if (sender is not Control { ContextMenu: { } menu } element)
        {
            return;
        }

        var isAnchor = element.DataContext is LabelingSampleRow realRow
            ? realRow.IsAnchor
            : (element.DataContext as SampleLabelingSampleRow)?.IsAnchor == true;

        foreach (var item in menu.Items)
        {
            if (item is not Control control)
            {
                continue;
            }

            switch (control.Tag)
            {
                case "anchor-set":
                    control.IsVisible = !isAnchor;
                    break;
                case "anchor-clear":
                    control.IsVisible = isAnchor;
                    break;
            }
        }
    }

    // 样本右键菜单里动作都落到与 WPF 同一组业务命令。
    private async void OnDeleteSampleMenuItemClicked(object? sender, RoutedEventArgs e)
    {
        if (_viewModel is not null
            && GetMenuSampleRow(sender) is LabelingSampleRow row
            && _viewModel.Samples.ConfirmDeleteSampleCommand.CanExecute(row.Id))
        {
            await GuardAsync(
                () => _viewModel.Samples.ConfirmDeleteSampleCommand.ExecuteAsync(row.Id),
                "LabelingPage.DeleteSample");
        }
    }

    private async void OnSetSampleAnchorMenuItemClicked(object? sender, RoutedEventArgs e)
    {
        if (_viewModel is not null
            && GetMenuSampleRow(sender) is LabelingSampleRow row
            && _viewModel.Samples.SetSampleAnchorCommand.CanExecute(row))
        {
            await GuardAsync(
                () => _viewModel.Samples.SetSampleAnchorCommand.ExecuteAsync(row),
                "LabelingPage.SetAnchor");
        }
    }

    private async void OnClearSampleAnchorMenuItemClicked(object? sender, RoutedEventArgs e)
    {
        if (_viewModel is not null
            && _viewModel.Samples.ClearSampleAnchorCommand.CanExecute(null))
        {
            await GuardAsync(
                () => _viewModel.Samples.ClearSampleAnchorCommand.ExecuteAsync(null),
                "LabelingPage.ClearAnchor");
        }
    }

    /// <summary>右键菜单项的数据上下文不一定继承 PlacementTarget，按 WPF 的取法再兜一层。</summary>
    private static object? GetMenuSampleRow(object? sender)
    {
        if (sender is Control { DataContext: { } row })
        {
            return row;
        }

        var menu = (sender as Control)?.FindAncestorOfType<ContextMenu>();
        return menu?.PlacementTarget is Control { DataContext: { } placementRow } ? placementRow : null;
    }

    private static async Task GuardAsync(Func<Task> action, string source)
    {
        try
        {
            await action();
        }
        catch (OperationCanceledException)
        {
            // 导航/页面卸载导致的取消不算错误。
        }
        catch (Exception ex)
        {
            UnhandledExceptionReporter.ReportStatic(ex, source, promptUser: false);
        }
    }

    private void OnShortcutsHelpClicked(object? sender, RoutedEventArgs e)
    {
        if (sender is Control { ContextMenu: { } menu } element)
        {
            menu.Open(element);
        }
    }
}
