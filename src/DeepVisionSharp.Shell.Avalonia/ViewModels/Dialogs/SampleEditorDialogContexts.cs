using Avalonia.Media;
using Avalonia.Media.Imaging;
using CommunityToolkit.Mvvm.Input;

namespace DeepVisionSharp.Shell.Avalonia.ViewModels.Dialogs;

/// <summary>项目编辑对话框 stub（名称 / 描述 / 类型 + 校验错误）。</summary>
public sealed class SampleProjectEditorDialogContext
{
    public SampleProjectEditorDialogContext()
    {
        SelectableProjectTypes =
        [
            new SampleValueLabelItem("ObjectDetection", "目标检测"),
            new SampleValueLabelItem("Classification", "图像分类"),
            new SampleValueLabelItem("InstanceSegmentation", "实例分割"),
            new SampleValueLabelItem("SemanticSegmentation", "语义分割"),
            new SampleValueLabelItem("VariationClassification", "差异分类"),
            new SampleValueLabelItem("UnsupervisedAnomaly", "异常检测"),
            new SampleValueLabelItem("TextRecognition", "字符识别"),
        ];

        DraftType = SelectableProjectTypes[0].Value;
    }

    public string DraftName { get; set; } = "钢板表面缺陷检测";

    public string DraftNameFormatError => "项目名称不能包含 \\ / : * ? \" < > |";

    public bool HasDraftNameFormatError => false;

    public string DraftDescription { get; set; } =
        "检测冷轧带钢表面的划伤、凹坑与锈斑，按框标注后训练检测模型。";

    public IReadOnlyList<SampleValueLabelItem> SelectableProjectTypes { get; }

    public bool CanEditProjectType => true;

    public string DraftType { get; set; }

    public string SelectedTypeDescription => "在整图上绘制矩形框，标注每个缺陷的位置与类别。";

    public string DialogErrorMessage => string.Empty;

    public bool HasDialogErrorMessage => false;
}

/// <summary>模型编辑对话框 stub（新建时选规格，编辑时只读类型与类别）。</summary>
public sealed class SampleModelEditorDialogContext
{
    public SampleModelEditorDialogContext()
    {
        ModelTypeOptions =
        [
            new SampleValueLabelItem("ObjectDetection", "目标检测"),
            new SampleValueLabelItem("Classification", "图像分类"),
            new SampleValueLabelItem("InstanceSegmentation", "实例分割"),
        ];

        DraftModelType = ModelTypeOptions[0].Value;
    }

    public string DraftName { get; set; } = "yolov8s-defect";

    public bool IsEditingModel => false;

    public string EditingModelStatus => "已就绪";

    public bool ShowCreateModelTypePicker => true;

    public IReadOnlyList<SampleValueLabelItem> ModelTypeOptions { get; }

    public string DraftModelType { get; set; }

    public bool ShowCreateModelTypeFixed => false;

    public string DraftModelTypeDisplay => "目标检测";

    public bool ShowsDraftInputChannelsOption => true;

    public bool DraftYoloGrayscaleInput { get; set; }

    public IReadOnlyList<string> EditingClasses { get; } = ["划伤", "凹坑", "锈斑"];

    public string EditingClassesCountText => "3 类";

    public bool HasEditingClasses => EditingClasses.Count > 0;

    public string DraftNote { get; set; } = string.Empty;

    public string DialogErrorMessage => string.Empty;

    public bool HasDialogErrorMessage => false;
}

/// <summary>新增 / 编辑类别对话框 stub（名称 + 调色板下拉）。</summary>
public sealed class SampleAddLabelDialogContext
{
    public SampleAddLabelDialogContext()
    {
        PaletteColors =
        [
            new SolidColorBrush(Color.Parse("#EF4444")),
            new SolidColorBrush(Color.Parse("#F59E0B")),
            new SolidColorBrush(Color.Parse("#10B981")),
            new SolidColorBrush(Color.Parse("#3B82F6")),
            new SolidColorBrush(Color.Parse("#8B5CF6")),
        ];

        DraftLabelColor = PaletteColors[0];
    }

    public bool IsLabelNameEditable => true;

    public string DraftLabelName { get; set; } = "压痕";

    public bool IsEditLabelColorMode => false;

    public IReadOnlyList<IBrush> PaletteColors { get; }

    public IBrush DraftLabelColor { get; set; }

    public string DialogErrorMessage => string.Empty;

    public bool HasDialogErrorMessage => false;
}

/// <summary>通用确认对话框 stub。</summary>
public sealed class SampleConfirmDialogContext
{
    public string Message { get; set; } = "确定要删除这个项目吗？项目下的样本与标注会一并删除，且无法恢复。";
}

/// <summary>训练任务重命名对话框 stub。</summary>
public sealed class SampleTrainingJobRenameDialogContext
{
    public string Name { get; set; } = "钢板缺陷 · yolov8s";

    public string DialogErrorMessage => string.Empty;

    public bool HasDialogErrorMessage => false;
}

/// <summary>维护密码对话框 stub。</summary>
public sealed class SampleSettingsDebugAccessDialogContext
{
    public string Password { get; set; } = string.Empty;

    public string ErrorMessage => "密码不正确。";

    public bool HasErrorMessage => false;
}

/// <summary>开始训练确认对话框 stub（参数行 + 预计耗时 / GPU + 注意事项）。</summary>
public sealed class SampleStartTrainingConfirmDialogContext
{
    public SampleStartTrainingConfirmDialogContext()
    {
        Rows =
        [
            new SampleDetailRow("项目", "钢板表面缺陷检测 · 目标检测"),
            new SampleDetailRow("数据集", "1,286 张样本 / 1,029 训练 · 257 验证"),
            new SampleDetailRow("基座模型", "yolov8s.pt"),
            new SampleDetailRow("轮次 / 批次", "100 epochs · batch 16"),
            new SampleDetailRow("图像尺寸", "640 × 640"),
        ];

        Notes =
        [
            new SampleTrainingNote(true, "训练期间不要关闭应用", "任务在后台线程执行，关闭窗口会中断训练。"),
            new SampleTrainingNote(false, "目标目录已有同名任务", "会自动追加时间后缀，不会覆盖历史权重。"),
        ];
    }

    public string Subtitle => "确认以下参数后开始训练，任务会立即进入队列。";

    public IReadOnlyList<SampleDetailRow> Rows { get; }

    public string DurationValue => "约 1 小时 12 分";

    public string DurationNote => "按当前设备近期同规模任务的中位耗时估算。";

    public string GpuValue => "RTX 4060 8 GB";

    public string GpuNote => "显存余量约 3.4 GB，可以开启批次 16。";

    public bool HasNotes => Notes.Count > 0;

    public IReadOnlyList<SampleTrainingNote> Notes { get; }

    public string StatusText => "可开始训练。";

    public bool StatusIsWarning => false;
}

/// <summary>模板匹配对话框 stub（模板预览 + 两个阈值滑杆）。</summary>
public sealed class SampleTemplateMatchDialogContext
{
    public bool HasTemplate => true;

    public bool HasNoTemplate => false;

    /// <summary>stub 没有模板缩略图，Image 的 Source 保持空。</summary>
    public Bitmap? TemplatePreviewImage => null;

    public double TemplateMatchThreshold { get; set; } = 0.78;

    public double TemplateMatchNms { get; set; } = 0.35;

    public bool ShowTemplateMatchConfigWarning => false;

    public string TemplateMatchDialogErrorMessage => string.Empty;

    public bool HasTemplateMatchDialogErrorMessage => false;
}

/// <summary>对话框详情行（标签 + 值）。</summary>
public sealed record SampleDetailRow(string Label, string Value);

/// <summary>训练前注意事项行。</summary>
public sealed class SampleTrainingNote
{
    public SampleTrainingNote(bool isWarning, string title, string detail)
    {
        IsWarning = isWarning;
        Title = title;
        Detail = detail;
    }

    public bool IsWarning { get; }

    public bool IsInfo => !IsWarning;

    public string Title { get; }

    public string Detail { get; }
}
