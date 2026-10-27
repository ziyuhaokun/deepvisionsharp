using DeepVisionSharp.Application.Models;
using DeepVisionSharp.Domain.Common;

namespace DeepVisionSharp.Infrastructure.Services;

internal sealed class ModelArtifactHandlerRegistry : KeyedRegistry<IModelArtifactHandler>
{
    public ModelArtifactHandlerRegistry(IEnumerable<IModelArtifactHandler> handlers)
        : base(handlers, item => item.HandlerKey, nameof(ModelArtifactHandlerRegistry))
    {
    }

    protected override string EmptyKeyMessage => "模型产物 Handler 键不能为空";

    protected override string GetNotRegisteredMessage(string key) => $"未注册的模型产物 Handler：{key}";

    public Result<IModelArtifactHandler> Resolve(ModelArtifactContext context)
    {
        ArgumentNullException.ThrowIfNull(context);

        if (!ModelCapabilityCatalog.TryResolve(context.ModelType, out var capabilities))
        {
            return Result<IModelArtifactHandler>.Failure($"不支持的模型产物类型：{context.ModelType}");
        }

        var byKey = ResolveByKey(capabilities.ArtifactHandlerKey);
        if (byKey.IsFailure || byKey.Value is null)
        {
            return byKey;
        }

        if (!byKey.Value.CanHandle(context))
        {
            return Result<IModelArtifactHandler>.Failure(
                $"产物 Handler {capabilities.ArtifactHandlerKey} 不支持模型类型：{context.ModelType}");
        }

        return byKey;
    }
}
