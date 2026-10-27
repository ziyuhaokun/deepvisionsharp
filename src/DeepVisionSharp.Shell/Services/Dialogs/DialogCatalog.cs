using DeepVisionSharp.Shell.Views.Dialogs.Contents;

namespace DeepVisionSharp.Shell.Services.Dialogs;

public sealed class DialogCatalog
{
    private readonly IReadOnlyDictionary<AppDialogKind, DialogDescriptor> _descriptors =
        new Dictionary<AppDialogKind, DialogDescriptor>
        {
            [AppDialogKind.AddLabel] = new DialogDescriptor(
                typeof(AddLabelDialogContent),
                Title: "添加类别",
                PrimaryButtonText: "添加"),
            [AppDialogKind.EditLabelColor] = new DialogDescriptor(
                typeof(AddLabelDialogContent),
                Title: "修改类别颜色",
                PrimaryButtonText: "保存"),
            [AppDialogKind.AutoInferenceClassification] = new DialogDescriptor(
                typeof(ClassificationAutoInferenceDialogContent),
                Title: "图像分类 · 自动推理",
                PrimaryButtonText: "开始推理"),
            [AppDialogKind.InferenceDebug] = new DialogDescriptor(
                typeof(InferenceDebugDialogContent),
                Title: "推理调试报告",
                PrimaryButtonText: "复制报告",
                CloseButtonText: "关闭"),
            [AppDialogKind.ProjectLabelStatistics] = new DialogDescriptor(
                typeof(ProjectLabelStatisticsDialogContent),
                Title: "标签统计",
                PrimaryButtonText: "关闭",
                CloseButtonText: string.Empty),
            [AppDialogKind.GalleryBatchInference] = new DialogDescriptor(
                typeof(GalleryBatchInferenceDialogContent),
                Title: "图库推理",
                PrimaryButtonText: "关闭",
                CloseButtonText: string.Empty),
            [AppDialogKind.InferenceImageSource] = new DialogDescriptor(
                typeof(InferenceImageSourceDialogContent),
                Title: "图片来源",
                PrimaryButtonText: "完成",
                CloseButtonText: "取消"),
            [AppDialogKind.InferenceBatchReport] = new DialogDescriptor(
                typeof(InferenceBatchReportDialogContent),
                Title: "批量推理分析报告",
                PrimaryButtonText: "复制报告",
                CloseButtonText: "关闭",
                SecondaryButtonText: "导出 HTML"),
            [AppDialogKind.CreateProject] = new DialogDescriptor(
                typeof(ProjectEditorDialogContent),
                Title: "新建项目",
                PrimaryButtonText: "创建"),
            [AppDialogKind.EditProject] = new DialogDescriptor(
                typeof(ProjectEditorDialogContent),
                Title: "编辑项目",
                PrimaryButtonText: "保存"),
            [AppDialogKind.CreateGallery] = new DialogDescriptor(
                typeof(GalleryEditorDialogContent),
                Title: "新建图库",
                PrimaryButtonText: "创建"),
            [AppDialogKind.EditGallery] = new DialogDescriptor(
                typeof(GalleryEditorDialogContent),
                Title: "编辑图库",
                PrimaryButtonText: "保存"),
            [AppDialogKind.EditModel] = new DialogDescriptor(
                typeof(ModelEditorDialogContent),
                Title: "编辑模型",
                PrimaryButtonText: "保存"),
            [AppDialogKind.Confirm] = new DialogDescriptor(
                typeof(ConfirmDialogContent),
                Title: "确认",
                PrimaryButtonText: "确认",
                CloseButtonText: "取消"),
            [AppDialogKind.RenameTrainingJob] = new DialogDescriptor(
                typeof(TrainingJobRenameDialogContent),
                Title: "重命名训练任务",
                PrimaryButtonText: "保存"),
            [AppDialogKind.GalleryMerge] = new DialogDescriptor(
                typeof(GalleryTargetSelectionDialogContent),
                Title: "合并图库",
                PrimaryButtonText: "合并"),
            [AppDialogKind.GalleryMoveSamples] = new DialogDescriptor(
                typeof(GalleryTargetSelectionDialogContent),
                Title: "选择目标图库",
                PrimaryButtonText: "确定"),
            [AppDialogKind.GallerySamplePicker] = new DialogDescriptor(
                typeof(GallerySamplePickerDialogContent),
                Title: "选择样本",
                PrimaryButtonText: "下一步"),
            [AppDialogKind.GallerySplitPlanner] = new DialogDescriptor(
                typeof(GallerySplitPlannerDialogContent),
                Title: "拆分图库",
                PrimaryButtonText: "下一步"),
            [AppDialogKind.SettingsDebugAccess] = new DialogDescriptor(
                typeof(SettingsDebugAccessDialogContent),
                Title: "维护验证",
                PrimaryButtonText: "登录"),
        };

    public DialogDescriptor Get(AppDialogKind kind)
    {
        if (_descriptors.TryGetValue(kind, out var descriptor))
        {
            return descriptor;
        }

        throw new InvalidOperationException($"Dialog kind '{kind}' is not registered.");
    }
}
