using System.Windows.Input;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

using DeepVisionSharp.Application.Models;
using DeepVisionSharp.Domain.Entities;
using DeepVisionSharp.Presentation.Services;
using DeepVisionSharp.Presentation.ViewModels;

namespace DeepVisionSharp.Shell.Avalonia.ViewModels;

/// <summary>
/// 训练页 stub 上下文：绑定路径与 deepvision 的 TrainingViewModel 同名同层
/// （JobList / Config / Monitor / Log / ConfirmDeleteJobCommand）。
/// 造的是一个「YOLO 检测任务跑到第 12 轮」的现场：任务卡 3 张、指标曲线有数据、历史表 8 行、日志 20 行。
/// </summary>
public sealed class SampleTrainingContext : ObservableObject
{
    public SampleTrainingContext()
    {
        Monitor = new SampleTrainingMonitor();
        ConfirmDeleteJobCommand = new RelayCommand(() => { });
        RenameJobByIdCommand = new RelayCommand<object?>(_ => { });
        DuplicateJobByIdCommand = new RelayCommand<object?>(_ => { });
        SaveAsModelByIdCommand = new RelayCommand<object?>(_ => { });
        StopJobByIdCommand = new RelayCommand<object?>(_ => { });
        ShowLogsByIdCommand = new RelayCommand<object?>(_ => { });
        OpenOutputFolderByIdCommand = new RelayCommand<object?>(_ => { });
        OpenLogFolderByIdCommand = new RelayCommand<object?>(_ => { });
        ConfirmDeleteJobByIdCommand = new RelayCommand<object?>(_ => { });
    }

    public SampleTrainingJobList JobList { get; } = new();

    public SampleTrainingConfig Config { get; } = new();

    public SampleTrainingMonitor Monitor { get; }

    public SampleTrainingLog Log { get; } = new();

    public ICommand ConfirmDeleteJobCommand { get; }

    public ICommand RenameJobByIdCommand { get; }

    public ICommand DuplicateJobByIdCommand { get; }

    public ICommand SaveAsModelByIdCommand { get; }

    public ICommand StopJobByIdCommand { get; }

    public ICommand ShowLogsByIdCommand { get; }

    public ICommand OpenOutputFolderByIdCommand { get; }

    public ICommand OpenLogFolderByIdCommand { get; }

    public ICommand ConfirmDeleteJobByIdCommand { get; }
}

/// <summary>训练任务列表 stub（对应 TrainingJobListViewModel）。</summary>
public sealed class SampleTrainingJobList : ObservableObject
{
    private SampleTrainingJob _selectedJob;

    public SampleTrainingJobList()
    {
        _selectedJob = new SampleTrainingJob
        {
            Id = "job-2",
            Name = "冷轧线划伤检测 · yolo11n",
            Status = TrainingJobStatus.Running,
            StatusDisplay = "训练中",
            MetaText = "第 12 / 50 轮 · 已训练 6 分 21 秒 · GPU",
            BestModelPath = "runs/detect/job-2/weights/best.pt",
            IsSelected = true,
        };

        Jobs =
        [
            new SampleTrainingJob
            {
                Id = "job-1",
                Name = "电芯外观分类 · yolo11s-cls",
                Status = TrainingJobStatus.Succeeded,
                StatusDisplay = "已完成",
                MetaText = "best top1 0.962 · 用时 18 分 07 秒",
                BestModelPath = "runs/classify/job-1/weights/best.pt",
            },
            _selectedJob,
            new SampleTrainingJob
            {
                Id = "job-3",
                Name = "注塑件异常检测 · patchcore",
                Status = TrainingJobStatus.Draft,
                StatusDisplay = "草稿",
                MetaText = "已选数据集：注塑件良品采样（980 张）",
                BestModelPath = string.Empty,
            },
        ];

        CreateJobCommand = new RelayCommand(() => { });
        DuplicateJobCommand = new RelayCommand(() => { });
        LoadCommand = new RelayCommand(() => { });
        Galleries = [];
    }

    public IReadOnlyList<SampleTrainingJob> Jobs { get; }

    /// <summary>数据集选择器用的图库列表；本波没有图库数据，留空。</summary>
    public IReadOnlyList<string> Galleries { get; }

    public SampleTrainingJob SelectedJob
    {
        get => _selectedJob;
        set => SetProperty(ref _selectedJob, value);
    }

    public int JobCount => Jobs.Count;

    /// <summary>任务头副标题（与真 VM 同口径的空安全投影）。</summary>
    public string SelectedJobMetaText => SelectedJob?.MetaText ?? string.Empty;

    /// <summary>结果区「最佳模型」路径（与真 VM 同口径的空安全投影）。</summary>
    public string SelectedJobBestModelPath => SelectedJob?.BestModelPath ?? string.Empty;

    public bool IsTrainingWorkflow => true;

    public bool ShowYoloJobDetail => true;

    public bool ShowSelectJobPrompt => false;

    public bool ShowDetailPreparing => false;

    public bool ShowProjectRequired => false;

    public bool HasEmptyJobList => Jobs.Count == 0;

    public bool IsLoading => false;

    public ICommand CreateJobCommand { get; }

    public ICommand DuplicateJobCommand { get; }

    public ICommand LoadCommand { get; }
}

/// <summary>左栏一张训练任务卡（对应 TrainingJobRow）。</summary>
public sealed class SampleTrainingJob : ObservableObject
{
    private bool _isSelected;

    public required string Id { get; init; }

    public required string Name { get; init; }

    public required TrainingJobStatus Status { get; init; }

    public required string StatusDisplay { get; init; }

    public required string MetaText { get; init; }

    public required string BestModelPath { get; init; }

    public bool IsSelected
    {
        get => _isSelected;
        set => SetProperty(ref _isSelected, value);
    }
}

/// <summary>训练配置 stub（对应 TrainingConfigViewModel）：只填检测任务的可见字段，其余按默认值留白。</summary>
public sealed class SampleTrainingConfig
{
    public SampleTrainingConfig()
    {
        StartTrainingCommand = new RelayCommand(() => { });
        StopTrainingCommand = new RelayCommand(() => { });
        ResumeTrainingCommand = new RelayCommand(() => { });
        SaveAsModelCommand = new RelayCommand(() => { });
    }

    public SampleTrainingDatasetConfig Dataset { get; } = new();

    public SampleTrainingAugmentConfig Cls { get; } = new();

    public SampleTrainingSpatialConfig Spatial { get; } = new();

    public string ModelName => "冷轧线划伤检测 · yolo11n";

    public string SelectedJobStatusDisplay => "训练中";

    public bool IsJobLocked => false;

    public bool CanDuplicateJob => true;

    public bool CanDeleteJob => true;

    public bool CanResumeTraining => false;

    public string DeleteJobToolTip => "删除这个训练任务";

    public bool IsFormEditable => false;

    /// <summary>检测 / 分类两族模型都走同一个「高级参数」页签。</summary>
    public bool IsYoloModelSelected => true;

    public bool IsClassificationModelSelected => false;

    /// <summary>检测专属参数（NMS IoU / 评估置信度 / 最大框数 / 水平翻转）的可见性。</summary>
    public bool ShowsDetectionOptions => true;

    public string SelectedModelType => ModelTypeNames.DefaultDetection;

    public IReadOnlyList<ModelTypeOption> AvailableModelTypes =>
    [
        new(ModelTypeNames.DefaultDetection, ModelTypeNames.GetDisplayLabel(ModelTypeNames.DefaultDetection)),
    ];

    public bool UsesNetworkInputSize => Spatial.UsesNetworkInputSize;

    public double DetectionNmsIou => 0.45;

    public double DetectionConfidence => 0.25;

    public int DetectionMaxDetections => 100;

    public bool DetectionHorizontalFlip => true;

    public bool IsBasicConfigurationComplete => true;

    public bool IsDatasetConfigurationComplete => true;

    public bool HasStartTrainingBlockReason => false;

    public string StartTrainingBlockReason => string.Empty;

    public bool ShowDatasetSplitControls => true;

    public double Epochs => 50;

    public double BatchSize => 16;

    public int EarlyStoppingPatience { get; set; } = 0;

    public double LearningRate => 0.001;

    public string Optimizer => "SGD";

    public string InferredModelTypeDisplay => ModelTypeNames.GetDisplayLabel(SelectedModelType);

    public bool ShowsClassificationAugmentation => IsClassificationModelSelected;

    public string ClassificationAugmentationSummary => Cls.BuildSummary(IsClassificationModelSelected);

    public IReadOnlyList<string> OptimizerOptions => ["SGD", "Adam", "AdamW", "NAdam"];

    public string StartTrainingToolTip => "用当前配置开始训练";

    public string StopTrainingToolTip => "停止当前训练";

    public string ResumeTrainingToolTip => "从中断处继续训练";

    public string SaveAsModelToolTip => "把选中的 checkpoint 存进模型库";

    public ICommand StartTrainingCommand { get; }

    public ICommand StopTrainingCommand { get; }

    public ICommand ResumeTrainingCommand { get; }

    public ICommand SaveAsModelCommand { get; }
}

/// <summary>数据集分区 stub（对应 TrainingDatasetConfigViewModel）。</summary>
public sealed class SampleTrainingDatasetConfig
{
    public SampleTrainingDatasetConfig()
    {
        SetGalleryTrainingCommand = new RelayCommand(() => { });
        SetGalleryValidationCommand = new RelayCommand(() => { });
        SetGalleryTrainingAndValidationCommand = new RelayCommand(() => { });
        SetGalleryNoneCommand = new RelayCommand(() => { });
        SwitchToPurposeAssignmentModeCommand = new RelayCommand(() => { });
        SwitchToAutoSplitModeCommand = new RelayCommand(() => { });
    }

    public string DatasetSummaryText => "训练 1,120 张 · 验证 128 张（按用途划分）";

    public bool HasDatasetValidationMessage => false;

    public string DatasetValidationMessage => string.Empty;

    public bool IsAutoSplitMode => false;

    public bool IsPurposeAssignmentMode => true;

    public bool ShowsInteractiveGalleryPicker => true;

    public bool ShowsStandardGalleryRoles => true;

    public double ValidationRatioPercent => 10;

    public double TrainRatioPercent => 90;

    public ICommand SetGalleryTrainingCommand { get; }

    public ICommand SetGalleryValidationCommand { get; }

    public ICommand SetGalleryTrainingAndValidationCommand { get; }

    public ICommand SetGalleryNoneCommand { get; }

    public ICommand SwitchToPurposeAssignmentModeCommand { get; }

    public ICommand SwitchToAutoSplitModeCommand { get; }
}

/// <summary>分类增强 stub（对应 TrainingClassificationAugmentationViewModel）：本仓库只有翻转概率。</summary>
public sealed class SampleTrainingAugmentConfig
{
    public double FlipLeftRight => 0.5;

    public double FlipUpDown => 0;

    public string BuildSummary(bool isClassificationModelSelected) =>
        isClassificationModelSelected
            ? $"fliplr={FlipLeftRight:0.##} · flipud={FlipUpDown:0.##}"
            : "—";
}

/// <summary>输入尺寸 stub（对应 TrainingSpatialSettingsViewModel）：按模型固定边长。</summary>
public sealed class SampleTrainingSpatialConfig
{
    public bool UsesNetworkInputSize => true;

    public double NetworkInputWidth => 640;

    public double NetworkInputHeight => 640;

    public double NetworkInputSizeMin => 320;

    public double NetworkInputSizeMax => 1280;

    public double NetworkInputSizeStep => 32;

    public string NetworkInputSizeHint => "必须是 32 的整数倍";
}/// <summary>训练监控 stub（对应 TrainingMonitorViewModel）：本波固定在「运行模式 / 指标」分区。</summary>
public sealed class SampleTrainingMonitor
{
    public SampleTrainingMonitor()
    {
        BestCheckpointCard = new SampleTrainingCheckpointCard
        {
            Title = "最佳模型",
            EpochText = "第 11 轮",
            MetricText = "mAP50 0.734",
            PathText = "runs/detect/job-2/weights/best.pt",
            Footnote = "训练结束后可保存到模型库",
            HasFootnote = true,
        };

        LastCheckpointCard = new SampleTrainingCheckpointCard
        {
            Title = "最新模型",
            EpochText = "第 12 轮",
            MetricText = "mAP50 0.712",
            PathText = "runs/detect/job-2/weights/last.pt",
            Footnote = "每轮覆盖写入",
            HasFootnote = true,
        };

        ShowAdvancedSectionCommand = new RelayCommand(() => { });
        ShowBasicSectionCommand = new RelayCommand(() => { });
        ShowDatasetSectionCommand = new RelayCommand(() => { });
        ShowHyperparametersSectionCommand = new RelayCommand(() => { });
        ShowOverviewSectionCommand = new RelayCommand(() => { });
        ShowConfigPreviewSectionCommand = new RelayCommand(() => { });
        ShowPreviewSectionCommand = new RelayCommand(() => { });
        ShowMetricsSectionCommand = new RelayCommand(() => { });
        ShowLogsSectionCommand = new RelayCommand(() => { });
        ShowResourcesSectionCommand = new RelayCommand(() => { });
        OpenSelectedCheckpointFolderCommand = new RelayCommand(() => { });
        CopyFailureDetailCommand = new RelayCommand(() => { });

    }

    public SampleTrainingMetricsSummary Metrics { get; } = new();

    public SampleTrainingPreviewSummary Preview { get; } = new();

    public SampleTrainingResourcesSummary Resources { get; } = new();

    public SampleTrainingCheckpointCard BestCheckpointCard { get; }

    public SampleTrainingCheckpointCard LastCheckpointCard { get; }



    public bool IsRunMode => true;

    public bool IsSettingsMode => false;

    public bool IsRunning => true;

    public bool AreResultTabsEnabled => true;

    public bool IsOverviewSectionActive => false;

    public bool IsConfigPreviewSectionActive => false;

    public bool IsPreviewSectionActive => false;

    public bool IsMetricsSectionActive => true;

    public bool IsLogsSectionActive => false;

    public bool IsResourcesSectionActive => false;

    public bool IsBasicSectionActive => false;

    public bool IsDatasetSectionActive => false;

    public bool IsHyperparametersSectionActive => false;

    public bool IsAdvancedSectionActive => false;

    public bool IsBestCheckpointSelected => true;

    public bool IsLastCheckpointSelected => false;

    public bool IsSelectedJobClassificationModel => false;

    public bool ShowResultsContent => true;

    public bool ShowResultsEmptyState => false;

    public bool ShowDualCheckpointResults => true;

    public bool ShowSingleCheckpointResults => false;

    public bool ShowFailureSummary => false;

    public bool ShowFailureTraceback => false;

    public string FailureSummary => string.Empty;

    public string FailureTraceback => string.Empty;

    public string TrainingStartedAtText => "2025-11-18 09:12:04";

    public string TrainingFinishedAtText => "进行中";

    public string TrainingDurationText => "6 分 21 秒";

    public string ResultsBestMetricLabel => "最佳 mAP50";

    public string ResultsBestMap50Text => "0.734";

    public string ResultsBestMap50EpochText => "@ 第 11 轮";

    public string ResultsEmptyTitle => "暂无训练结果";

    public string ResultsEmptyDescription => "训练开始后这里会出现指标与 checkpoint";

    public string SaveSelectedCheckpointToolTip => "把选中的 checkpoint 存进模型库";

    public ICommand ShowBasicSectionCommand { get; }

    public ICommand ShowDatasetSectionCommand { get; }

    public ICommand ShowHyperparametersSectionCommand { get; }

    public ICommand ShowAdvancedSectionCommand { get; }

    public ICommand ShowOverviewSectionCommand { get; }

    public ICommand ShowConfigPreviewSectionCommand { get; }

    public ICommand ShowPreviewSectionCommand { get; }

    public ICommand ShowMetricsSectionCommand { get; }

    public ICommand ShowLogsSectionCommand { get; }

    public ICommand ShowResourcesSectionCommand { get; }

    public ICommand OpenSelectedCheckpointFolderCommand { get; }

    public ICommand CopyFailureDetailCommand { get; }
}

/// <summary>一张 checkpoint 卡（对应 TrainingCheckpointCardViewModel）。</summary>
public sealed class SampleTrainingCheckpointCard
{
    public required string Title { get; init; }

    public required string EpochText { get; init; }

    public required string MetricText { get; init; }

    public required string PathText { get; init; }

    public required string Footnote { get; init; }

    public bool HasFootnote { get; init; }
}

/// <summary>指标分区 stub（对应 TrainingMetricsViewModel）：4 张指标卡 + 3 条曲线 + 8 行历史。</summary>
public sealed class SampleTrainingMetricsSummary
{
    public SampleTrainingMetricsSummary()
    {
        MetricCards =
        [
            new SampleTrainingMetricCard
            {
                Title = "mAP50",
                ValueText = "0.712",
                DeltaText = "↑ 0.018 较上轮",
                BestEpochText = "最佳 0.734 @ 第 11 轮",
                Footnote = "验证集 128 张",
                HasFootnote = true,
            },
            new SampleTrainingMetricCard
            {
                Title = "mAP50-95",
                ValueText = "0.486",
                DeltaText = "↑ 0.011 较上轮",
                BestEpochText = "最佳 0.503 @ 第 11 轮",
                Footnote = string.Empty,
                HasFootnote = false,
            },
            new SampleTrainingMetricCard
            {
                Title = "Precision",
                ValueText = "0.842",
                DeltaText = "↓ 0.004 较上轮",
                BestEpochText = "最佳 0.861 @ 第 9 轮",
                Footnote = string.Empty,
                HasFootnote = false,
            },
            new SampleTrainingMetricCard
            {
                Title = "Recall",
                ValueText = "0.778",
                DeltaText = "↑ 0.009 较上轮",
                BestEpochText = "最佳 0.791 @ 第 11 轮",
                Footnote = string.Empty,
                HasFootnote = false,
            },
        ];

        MetricHistory =
        [
            HistoryRow(5, "1.284", "0.412", "0.238", "0.771", "0.682", "0.00932"),
            HistoryRow(6, "1.061", "0.478", "0.291", "0.802", "0.701", "0.00841"),
            HistoryRow(7, "0.912", "0.536", "0.337", "0.818", "0.719", "0.00756"),
            HistoryRow(8, "0.808", "0.581", "0.378", "0.826", "0.734", "0.00683"),
            HistoryRow(9, "0.734", "0.612", "0.402", "0.861", "0.741", "0.00612"),
            HistoryRow(10, "0.681", "0.669", "0.441", "0.853", "0.752", "0.00551"),
            HistoryRow(11, "0.642", "0.734", "0.503", "0.846", "0.769", "0.00496"),
            HistoryRow(12, "0.618", "0.712", "0.486", "0.842", "0.778", "0.00447"),
        ];

        QualityChartSeries =
        [
            Series("mAP50", [(1, 0.238), (2, 0.271), (3, 0.309), (4, 0.362), (5, 0.412), (6, 0.478), (7, 0.536),
                (8, 0.581), (9, 0.612), (10, 0.669), (11, 0.734), (12, 0.712)]),
            Series("mAP50-95", [(1, 0.106), (2, 0.129), (3, 0.158), (4, 0.196), (5, 0.238), (6, 0.291), (7, 0.337),
                (8, 0.378), (9, 0.402), (10, 0.441), (11, 0.503), (12, 0.486)]),
        ];

        LossChartSeries =
        [
            Series("train/box_loss", [(1, 2.412), (2, 2.041), (3, 1.782), (4, 1.596), (5, 1.284), (6, 1.061),
                (7, 0.912), (8, 0.808), (9, 0.734), (10, 0.681), (11, 0.642), (12, 0.618)]),
            Series("val/box_loss", [(1, 2.618), (2, 2.184), (3, 1.914), (4, 1.702), (5, 1.412), (6, 1.231),
                (7, 1.104), (8, 0.985), (9, 0.912), (10, 0.861), (11, 0.804), (12, 0.792)]),
        ];

        LrChartSeries =
        [
            Series("lr/pg0", [(1, 0.00032), (2, 0.00096), (3, 0.00161), (4, 0.00225), (5, 0.00289), (6, 0.00354),
                (7, 0.00418), (8, 0.00482), (9, 0.00547), (10, 0.00611), (11, 0.00675), (12, 0.00740)]),
        ];
    }

    /// <summary>指标卡列表：UniformGrid 的列数取自它的 Count，所以用 List 而不是数组。</summary>
    public List<SampleTrainingMetricCard> MetricCards { get; }

    public IReadOnlyList<SampleTrainingMetricHistoryRow> MetricHistory { get; }

    public IReadOnlyList<TrainingChartSeries> QualityChartSeries { get; }

    public IReadOnlyList<TrainingChartSeries> LossChartSeries { get; }

    public IReadOnlyList<TrainingChartSeries> LrChartSeries { get; }

    /// <summary>最新一轮 per-class 指标（对应 TrainingMetricsViewModel.PerClassRows）。</summary>
    public IReadOnlyList<TrainingPerClassRow> PerClassRows { get; } =
    [
        new()
        {
            ClassName = "划伤",
            PrecisionText = "0.842",
            RecallText = "0.778",
            F1Text = "0.809",
            SupportText = "68",
        },
        new()
        {
            ClassName = "凹陷",
            PrecisionText = "0.731",
            RecallText = "0.695",
            F1Text = "0.713",
            SupportText = "42",
        },
        new()
        {
            ClassName = "污渍",
            PrecisionText = "0.906",
            RecallText = "0.861",
            F1Text = "0.883",
            SupportText = "86",
        },
    ];

    public bool HasPerClassRows => PerClassRows.Count > 0;

    public string PerClassPanelTitle => "检测指标（最新一轮）";

    public bool ShowMetricCards => true;

    public bool ShowProfileMetricsCards => true;

    public bool ShowDetectionMetricsCards => true;

    public bool ShowClassificationMetricsCards => false;

    /// <summary>
    /// 恒为 false：真实 presenter 里 Detection / Classification 都带「主指标说明」文案，
    /// 同时把 ShowsSecondaryTrainLossRow 传成 false —— 两行是互斥的，同格同时可见会叠在一起。
    /// </summary>
    public bool ShowSecondaryTrainLossRow => false;

    public bool ShowMetricsEmptyState => false;

    public bool ShowMetricsFooterHint => true;

    public bool ShowLearningRateChartColumn => true;

    public bool HasMetricsPollingError => false;

    public string MetricsPollingError => string.Empty;

    public string QualityChartTitle => "验证集精度";

    public string QualityChartEmptyText => "等待验证指标";

    public string CurrentMetricEpochText => "第 12 轮 / 共 50 轮";

    public string CurrentTrainLossText => "0.618";

    public string TrainLossDeltaText => "↓ 0.024 较上轮";

    public string CurrentLrText => "0.00740";

    public string MetricsFooterHint => "曲线按轮次刷新；mAP 取自验证集，损失为训练集与验证集的 box loss。";

    private static SampleTrainingMetricHistoryRow HistoryRow(
        int epoch,
        string trainLoss,
        string valMap,
        string valMap50,
        string precision,
        string recall,
        string lr)
        => new()
        {
            Epoch = epoch,
            TrainLossText = trainLoss,
            ValLossText = valMap,
            ValMapText = valMap,
            ValMap50Text = valMap50,
            PrecisionText = precision,
            RecallText = recall,
            LrText = lr,
            MaskMapText = "—",
            MaskMap50Text = "—",
            BoxMapText = valMap,
            BoxMap50Text = valMap50,
            DetHmeanText = "—",
            DetPrecisionText = precision,
            DetRecallText = recall,
            OcrAccuracyText = "—",
            OcrNormalizedEditSimilarityText = "—",
            AccuracyTop1Text = "—",
            AccuracyTop5Text = "—",
            BestEpochText = $"第 {epoch} 轮",
        };

    private static TrainingChartSeries Series(string name, (int Epoch, double Value)[] points)
        => new(name, points.Select(point => new TrainingChartPoint(point.Epoch, point.Value)).ToList());
}

/// <summary>一张指标卡（对应 TrainingMetricCardViewModel）。</summary>
public sealed class SampleTrainingMetricCard
{
    public required string Title { get; init; }

    public required string ValueText { get; init; }

    public required string DeltaText { get; init; }

    public required string BestEpochText { get; init; }

    public required string Footnote { get; init; }

    public bool HasFootnote { get; init; }
}

/// <summary>历史表一行（对应 TrainingMetricHistoryRow）：各列都是预格式化文本。</summary>
public sealed class SampleTrainingMetricHistoryRow
{
    public required int Epoch { get; init; }

    public required string TrainLossText { get; init; }

    public required string ValLossText { get; init; }

    public required string ValMapText { get; init; }

    public required string ValMap50Text { get; init; }

    public required string PrecisionText { get; init; }

    public required string RecallText { get; init; }

    public required string LrText { get; init; }

    public required string MaskMapText { get; init; }

    public required string MaskMap50Text { get; init; }

    public required string BoxMapText { get; init; }

    public required string BoxMap50Text { get; init; }

    public required string DetHmeanText { get; init; }

    public required string DetPrecisionText { get; init; }

    public required string DetRecallText { get; init; }

    public required string OcrAccuracyText { get; init; }

    public required string OcrNormalizedEditSimilarityText { get; init; }

    public required string AccuracyTop1Text { get; init; }

    public required string AccuracyTop5Text { get; init; }

    public required string BestEpochText { get; init; }
}

/// <summary>预览分区 stub（对应 TrainingPreviewViewModel）：本波没有产物目录，停在空状态。</summary>
public sealed class SampleTrainingPreviewSummary
{
    public SampleTrainingPreviewSummary()
    {
        ClosePreviewLightboxCommand = new RelayCommand(() => { });
        OpenTrainingPlotsFolderCommand = new RelayCommand(() => { });
        OpenPreviewLightboxCommand = new RelayCommand<string>(_ => { });
        CopyPreviewImageCommand = new RelayCommand<string>(_ => { });
    }

    public IReadOnlyList<TrainingPreviewItem> PreviewImages => [];

    public IReadOnlyList<string> TrainingPlotImagePaths => [];

    public string PreviewLightboxImagePath => string.Empty;

    public bool IsPreviewLightboxOpen => false;

    public bool HasPreviewImages => false;

    public bool ShowPreviewEmptyState => true;

    public bool ShowClassificationTrainingPlots => false;

    public bool HasPreviewPollingError => false;

    public string PreviewPollingError => string.Empty;

    public string PreviewEmptyTitle => "暂无预览图";

    public string PreviewEmptyHint => "训练开始后，验证集预测图会按间隔写入 preview 目录";

    public ICommand ClosePreviewLightboxCommand { get; }

    public ICommand OpenTrainingPlotsFolderCommand { get; }

    public ICommand OpenPreviewLightboxCommand { get; }

    public ICommand CopyPreviewImageCommand { get; }
}

/// <summary>资源分区 stub（对应 TrainingResourcesViewModel）：给一组像样的占用率。</summary>
public sealed class SampleTrainingResourcesSummary
{
    public bool HasResourceData => true;

    public bool HasGpuResourceData => true;

    public bool HasResourcePollingError => false;

    public string ResourcePollingError => string.Empty;

    public double ResourceCpuPercent => 38.4;

    public double ResourceMemoryPercent => 52.7;

    public double ResourceGpuPercent => 61.2;
}

/// <summary>运行日志 stub（对应 TrainingLogViewModel）：20 行 Ultralytics 风格的训练日志。</summary>
public sealed class SampleTrainingLog : ObservableObject
{
    private bool _autoScrollLog = true;

    public SampleTrainingLog()
    {
        LiveLog = string.Join(
            Environment.NewLine,
            [
                "Ultralytics 8.3.42  PyTorch 2.6.0  CUDA:0 (NVIDIA RTX 4070, 12282MiB)",
                "engine/trainer: task=detect, mode=train, model=yolo11n.pt, data=dataset.yaml",
                "overrides: epochs=50, batch=16, imgsz=640, optimizer=SGD, lr0=0.001, amp=True",
                "train: Scanning runs/detect/job-2/train... 1120 images, 0 backgrounds, 0 corrupt",
                "val: Scanning runs/detect/job-2/val... 128 images, 0 backgrounds, 0 corrupt",
                "Plotting labels to runs/detect/job-2/labels.jpg...",
                "optimizer: SGD(lr=0.001, momentum=0.937, weight_decay=0.0005)",
                "Starting training for 50 epochs...",
                "      Epoch    GPU_mem   box_loss   cls_loss   dfl_loss  Instances       Size",
                "        1/50      3.21G      2.412      2.884      1.612         42        640",
                "                 Class     Images  Instances      Box(P          R      mAP50  mAP50-95)",
                "                   all        128        196      0.771      0.682      0.238      0.106",
                "        2/50      3.18G      2.041      2.512      1.484         38        640",
                "                   all        128        196      0.802      0.701      0.271      0.129",
                "        3/50      3.18G      1.782      2.284      1.398         45        640",
                "                   all        128        196      0.818      0.719      0.309      0.158",
                "        9/50      3.22G      0.734      1.612      1.142         40        640",
                "                   all        128        196      0.861      0.741      0.612      0.402",
                "       11/50      3.22G      0.642      1.508      1.098         41        640",
                "                   all        128        196      0.846      0.769      0.734      0.503",
                "       12/50      3.24G      0.618      1.472      1.081         39        640",
                "                   all        128        196      0.842      0.778      0.712      0.486",
                "Results saved to runs/detect/job-2",
            ]);

        CopyLogCommand = new RelayCommand(() => { });
        ClearLogCommand = new RelayCommand(() => { });
    }

    public string LiveLog { get; }

    public bool HasLogContent => !string.IsNullOrWhiteSpace(LiveLog);

    public bool AutoScrollLog
    {
        get => _autoScrollLog;
        set => SetProperty(ref _autoScrollLog, value);
    }

    public ICommand CopyLogCommand { get; }

    public ICommand ClearLogCommand { get; }
}

/// <summary>下拉选项（对应 deepvision 的 *Option 记录）：Value 是选中值，DisplayText 是显示文案。</summary>
public sealed record SampleTrainingValueOption(string Value, string DisplayText);

/// <summary>只有显示文案的选项（输入尺寸策略用）。</summary>
public sealed record SampleTrainingLabelOption(string Label);
