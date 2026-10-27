using DeepVisionSharp.Application.Models;
using DeepVisionSharp.Domain.Common;

namespace DeepVisionSharp.Infrastructure.Services;

internal sealed class InferenceBackendRegistry : KeyedRegistry<IInferenceBackend>
{
    public InferenceBackendRegistry(IEnumerable<IInferenceBackend> backends)
        : base(backends, item => item.BackendKey, nameof(InferenceBackendRegistry))
    {
    }

    public IReadOnlyList<IInferenceBackend> Backends => Items;

    protected override string EmptyKeyMessage => "推理后端键不能为空";

    protected override string GetNotRegisteredMessage(string key) => $"未注册的推理后端：{key}";

    public Result<IInferenceBackend> Resolve(InferenceBackendContext context)
    {
        ArgumentNullException.ThrowIfNull(context);

        if (!ModelCapabilityCatalog.TryResolve(context.ModelType, out var capabilities))
        {
            return Result<IInferenceBackend>.Failure($"不支持的推理模型类型：{context.ModelType}");
        }

        if (string.IsNullOrWhiteSpace(capabilities.InferenceBackendKey))
        {
            return Result<IInferenceBackend>.Failure(
                $"模型类型无标准推理后端：{context.ModelType}");
        }

        var byKey = ResolveByKey(capabilities.InferenceBackendKey);
        if (byKey.IsFailure || byKey.Value is null)
        {
            return byKey;
        }

        if (!byKey.Value.CanHandle(context))
        {
            return Result<IInferenceBackend>.Failure(
                $"推理后端 {capabilities.InferenceBackendKey} 不支持模型类型：{context.ModelType}");
        }

        return byKey;
    }
}
