namespace DeepVisionSharp.Application.Training;

/// <summary>
/// 训练相关命令 / FluentValidation 校验器 / 数据集规则共享的错误消息。
/// 集中定义以避免同一文案在 validator 与 handler 中多次复制后漂移。
/// </summary>
public static class TrainingValidationMessages
{
    public const string EmptyName = "请输入训练任务名称";

    public const string EmptyModelType = "训练模型类型不能为空";

    public const string IncompatibleWithWorkflow = "模型类型与项目工作流不兼容";

    public const string SelectDataset = "请选择数据集";

    public const string ResumeNotSupported = "当前产品不支持训练续训";

    public const string ValidationRatioRange = "验证集比例范围为 0.0–0.5";

    /// <summary>未知 / 不支持的产品模型类型（与 <see cref="Models.ModelCapabilityCatalog.IsSupported"/> 对应）。</summary>
    public static string UnsupportedModelType(string modelType) => $"不支持的训练模型类型：{modelType}";

    /// <summary>训练任务名称在当前范围（项目 / 全局）内已存在。</summary>
    public static string DuplicateName(string name) => $"训练任务名称 \"{name}\" 已存在，请使用其他名称";
}
