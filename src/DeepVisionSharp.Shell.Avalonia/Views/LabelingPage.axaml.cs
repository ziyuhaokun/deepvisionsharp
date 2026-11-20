using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.VisualTree;
using DeepVisionSharp.Presentation.ViewModels.Labeling;
using DeepVisionSharp.Shell.Avalonia.Controls.Labeling;
using DeepVisionSharp.Shell.Avalonia.ViewModels;

namespace DeepVisionSharp.Shell.Avalonia.Views;

/// <summary>
/// 标记工作台页面：左中右三栏分别承载工具轨道、画布与样本条、鸟瞰图与详情轨道。
/// DataContext 来自容器的 <see cref="LabelingViewModel"/>；没有容器（设计时 / 无头渲染）时
/// 退回 <see cref="SampleLabelingContext"/>，绑定路径不变。
/// 画布事件（绘制 / 编辑）留空，样本条点击与右键菜单的显隐规则照 deepvision 原样。
/// </summary>
public partial class LabelingPage : UserControl
{
    private readonly LabelingViewModel? _viewModel;

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

    // 以下是画布交互的接线点：本波没有绘制/编辑逻辑，事件保留为空实现。
    private void OnAnnotationCreated(object? sender, AnnotationCreatedEventArgs e)
    {
    }

    private void OnAnnotationEditStarted(object? sender, EventArgs e)
    {
    }

    private void OnAnnotationEditCompleted(object? sender, AnnotationEditCompletedEventArgs e)
    {
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

    private void OnAnnotationViewportChanged(object? sender, EventArgs e) => SyncOverviewViewport();

    private void OnOverviewNavigateRequested(object? sender, OverviewNavigateEventArgs e)
    {
        AnnotationSurface.CenterViewportAt(e.ImagePoint);
        SyncOverviewViewport();
    }

    private void SyncOverviewViewport() =>
        OverviewSurface.Viewport = AnnotationSurface.GetViewportImageRect();

    private void OnSampleTilePointerReleased(object? sender, PointerReleasedEventArgs e)
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
            if (row is LabelingSampleRow sample)
            {
                _viewModel.Samples.SelectSampleCommand.Execute(sample.Id);
            }

            e.Handled = true;
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

    // 样本右键菜单里动作都落到业务命令；本波只接页面生命周期，菜单动作留待画布接线波。
    private void OnDeleteSampleMenuItemClicked(object? sender, RoutedEventArgs e)
    {
    }

    private void OnSetSampleAnchorMenuItemClicked(object? sender, RoutedEventArgs e)
    {
    }

    private void OnClearSampleAnchorMenuItemClicked(object? sender, RoutedEventArgs e)
    {
    }

    private void OnShortcutsHelpClicked(object? sender, RoutedEventArgs e)
    {
        if (sender is Control { ContextMenu: { } menu } element)
        {
            menu.Open(element);
        }
    }
}
