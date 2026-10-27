namespace DeepVisionSharp.Application.Models;

public static class BackendKeys
{
    /// <summary>进程内 TorchSharp 分类训练引擎。</summary>
    public const string TorchSharpClassification = "torchsharp:classification-train";

    /// <summary>进程内 TorchSharp 分类推理引擎。</summary>
    public const string InferenceTorchSharpClassification = "torchsharp:classification";
}
