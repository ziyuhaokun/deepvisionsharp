using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.VisualTree;
using DeepVisionSharp.Shell.Avalonia.Controls.Labeling;
using DeepVisionSharp.Shell.Avalonia.ViewModels;

namespace DeepVisionSharp.Shell.Avalonia.Views;

/// <summary>
/// 标记工作台页面：左中右三栏分别承载工具轨道、画布与样本条、鸟瞰图与详情轨道。
/// 本波用 stub 数据，画布事件（绘制 / 编辑 / 撤销）留空，样本条点击与右键菜单的显隐规则照 deepvision 原样。
/// </summary>
public partial class LabelingPage : UserControl
{
    private readonly SampleLabelingContext _context = new();

    public LabelingPage()
    {
        InitializeComponent();
        DataContext = _context;
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

    private void OnLoaded(object? sender, RoutedEventArgs e)
    {
        AnnotationSurface.FitToView();
        AnnotationSurface.ViewportChanged -= OnAnnotationViewportChanged;
        AnnotationSurface.ViewportChanged += OnAnnotationViewportChanged;
        SyncOverviewViewport();
    }

    private void OnUnloaded(object? sender, RoutedEventArgs e)
        => AnnotationSurface.ViewportChanged -= OnAnnotationViewportChanged;

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

    private void OnSegmentationPolygonCreated(object? sender, SegmentationPolygonCreatedEventArgs e)
    {
    }

    private void OnSegmentationPolygonEditCompleted(object? sender, SegmentationPolygonEditCompletedEventArgs e)
    {
    }

    private void OnZoomInClicked(object? sender, RoutedEventArgs e) => AnnotationSurface.ZoomIn();

    private void OnZoomOutClicked(object? sender, RoutedEventArgs e) => AnnotationSurface.ZoomOut();

    private void OnFitClicked(object? sender, RoutedEventArgs e)
    {
        AnnotationSurface.FitToView();
        SyncOverviewViewport();
    }

    private void OnUndoClicked(object? sender, RoutedEventArgs e) => _context.Editor.UndoCommand.Execute(null);

    private void OnRedoClicked(object? sender, RoutedEventArgs e) => _context.Editor.RedoCommand.Execute(null);

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

        if (sender is not Control { DataContext: SampleLabelingSampleRow row })
        {
            return;
        }

        foreach (var item in _context.Samples.SamplePageItems)
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

        var row = element.DataContext as SampleLabelingSampleRow;
        var aiJudgeVisible = _context.AiJudge.IsVisible;
        var isAnchor = row?.IsAnchor == true;

        foreach (var item in menu.Items)
        {
            if (item is not Control control)
            {
                continue;
            }

            switch (control.Tag)
            {
                case "ai-judge":
                    control.IsVisible = aiJudgeVisible;
                    break;
                case "anchor-set":
                    control.IsVisible = !isAnchor;
                    break;
                case "anchor-clear":
                    control.IsVisible = isAnchor;
                    break;
            }
        }
    }

    // 样本右键菜单里动作都落到业务命令，本波没有业务层，只保留菜单结构。
    private void OnDeleteSampleMenuItemClicked(object? sender, RoutedEventArgs e)
    {
    }

    private void OnSetSampleAnchorMenuItemClicked(object? sender, RoutedEventArgs e)
    {
    }

    private void OnClearSampleAnchorMenuItemClicked(object? sender, RoutedEventArgs e)
    {
    }

    private void OnAddSampleAsOkStandardMenuItemClicked(object? sender, RoutedEventArgs e)
    {
    }

    private void OnAddSampleAsNgStandardMenuItemClicked(object? sender, RoutedEventArgs e)
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
