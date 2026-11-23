using Avalonia.Controls;
using DeepVisionSharp.Presentation.Services.Dialogs;
using DeepVisionSharp.Shell.Avalonia.Views.Dialogs.Contents;
using Microsoft.Extensions.DependencyInjection;

namespace DeepVisionSharp.Shell.Avalonia.Services;

/// <summary>
/// Avalonia 对话框服务：按 <see cref="AppDialogKind"/> 取内容视图，DataContext 设成调用方给的 VM，
/// 交给 <see cref="AvaloniaDialogHost"/> 显示并回传结果。
/// 描述表与 WPF 的 <c>DialogCatalog</c> 逐项一致，共用框架无关的 <see cref="DialogDescriptor"/>。
/// </summary>
public sealed class AvaloniaDialogService : IAppDialogService
{
    private static readonly IReadOnlyDictionary<AppDialogKind, DialogDescriptor> Descriptors =
        new Dictionary<AppDialogKind, DialogDescriptor>
        {
            [AppDialogKind.AddLabel] = new(typeof(AddLabelDialogContent), "添加类别", "添加"),
            [AppDialogKind.EditLabelColor] = new(typeof(AddLabelDialogContent), "修改类别颜色", "保存"),
            [AppDialogKind.AutoInferenceClassification] = new(
                typeof(ClassificationAutoInferenceDialogContent), "图像分类 · 自动推理", "开始推理"),
            [AppDialogKind.AutoInferenceDetection] = new(
                typeof(DetectionAutoInferenceDialogContent), "目标检测 · 自动推理", "开始推理"),
            [AppDialogKind.InferenceDebug] = new(typeof(InferenceDebugDialogContent), "推理调试报告", "复制报告", "关闭"),
            [AppDialogKind.ProjectLabelStatistics] = new(
                typeof(ProjectLabelStatisticsDialogContent), "标签统计", "关闭", string.Empty),
            [AppDialogKind.GalleryBatchInference] = new(
                typeof(GalleryBatchInferenceDialogContent), "图库推理", "关闭", string.Empty),
            [AppDialogKind.InferenceImageSource] = new(
                typeof(InferenceImageSourceDialogContent), "图片来源", "完成", "取消"),
            [AppDialogKind.InferenceBatchReport] = new(
                typeof(InferenceBatchReportDialogContent), "批量推理分析报告", "复制报告", "关闭", "导出 HTML"),
            [AppDialogKind.CreateProject] = new(typeof(ProjectEditorDialogContent), "新建项目", "创建"),
            [AppDialogKind.EditProject] = new(typeof(ProjectEditorDialogContent), "编辑项目", "保存"),
            [AppDialogKind.CreateGallery] = new(typeof(GalleryEditorDialogContent), "新建图库", "创建"),
            [AppDialogKind.EditGallery] = new(typeof(GalleryEditorDialogContent), "编辑图库", "保存"),
            [AppDialogKind.EditModel] = new(typeof(ModelEditorDialogContent), "编辑模型", "保存"),
            [AppDialogKind.Confirm] = new(typeof(ConfirmDialogContent), "确认", "确认", "取消"),
            [AppDialogKind.RenameTrainingJob] = new(
                typeof(TrainingJobRenameDialogContent), "重命名训练任务", "保存"),
            [AppDialogKind.GalleryMerge] = new(
                typeof(GalleryTargetSelectionDialogContent), "合并图库", "合并"),
            [AppDialogKind.GalleryMoveSamples] = new(
                typeof(GalleryTargetSelectionDialogContent), "选择目标图库", "确定"),
            [AppDialogKind.GallerySamplePicker] = new(
                typeof(GallerySamplePickerDialogContent), "选择样本", "下一步"),
            [AppDialogKind.GallerySplitPlanner] = new(
                typeof(GallerySplitPlannerDialogContent), "拆分图库", "下一步"),
            [AppDialogKind.SettingsDebugAccess] = new(
                typeof(SettingsDebugAccessDialogContent), "维护验证", "登录"),
        };

    private readonly IServiceProvider _services;

    public AvaloniaDialogService(IServiceProvider services)
    {
        _services = services;
    }

    public async Task<AppDialogResult> ShowAsync(
        AppDialogKind kind,
        object dataContext,
        DialogOptions? options = null,
        CancellationToken cancellationToken = default)
    {
        var owner = ShellWindowHost.Window
            ?? throw new InvalidOperationException("主窗口尚未创建，无法显示对话框。");

        if (!Descriptors.TryGetValue(kind, out var descriptor))
        {
            throw new InvalidOperationException($"Dialog kind '{kind}' is not registered.");
        }

        var content = (Control)ActivatorUtilities.CreateInstance(_services, descriptor.ContentViewType);
        content.DataContext = dataContext;

        // CloseButtonText="" 是「显式不要关闭按钮」的契约，只有 null 才回落到默认文案。
        var merged = new DialogOptions(
            Title: options?.Title ?? descriptor.Title,
            PrimaryButtonText: options?.PrimaryButtonText ?? descriptor.PrimaryButtonText,
            SecondaryButtonText: options?.SecondaryButtonText ?? descriptor.SecondaryButtonText,
            CloseButtonText: options?.CloseButtonText ?? descriptor.CloseButtonText,
            DefaultButton: options?.DefaultButton ?? AppDialogDefaultButton.Primary,
            PrimaryButtonIsEnabledBindingPath: options?.PrimaryButtonIsEnabledBindingPath);

        return await AvaloniaDialogHost
            .ShowAsync(owner, merged, content, dataContext, cancellationToken)
            .ConfigureAwait(true);
    }
}
