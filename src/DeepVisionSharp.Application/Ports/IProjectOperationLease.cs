namespace DeepVisionSharp.Application.Ports;

/// <summary>
/// 长时间占用一个项目的租约。持有期间，删除该项目会被拒绝。
/// </summary>
/// <remarks>
/// 训练启动、导入、导出、批量推理都走这里。删除走相反的方向：它要求没有任何租约在持有。
/// 释放租约即 <see cref="IDisposable.Dispose"/>。
/// </remarks>
public interface IProjectOperationLease
{
    /// <summary>
    /// 占用一个项目。项目正在删除时抛出 <see cref="Domain.ValueObjects.ProjectBusyException"/>。
    /// </summary>
    IDisposable Enter(Guid projectId);
}
