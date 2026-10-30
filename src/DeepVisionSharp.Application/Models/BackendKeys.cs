namespace DeepVisionSharp.Application.Models;

public static class BackendKeys
{
    /// <summary>进程内 TorchSharp 分类训练引擎。</summary>
    public const string TorchSharpClassification = "torchsharp:classification-train";

    /// <summary>进程内 TorchSharp 分类推理引擎。</summary>
    public const string InferenceTorchSharpClassification = "torchsharp:classification";

    /// <summary>进程内 TorchSharp 目标检测训练引擎。</summary>
    public const string TorchSharpDetection = "torchsharp:detection-train";

    /// <summary>进程内 TorchSharp 目标检测推理引擎。</summary>
    public const string InferenceTorchSharpDetection = "torchsharp:detection";
}
