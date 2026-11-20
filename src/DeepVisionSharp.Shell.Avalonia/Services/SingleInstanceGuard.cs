using System.Diagnostics;
using System.Threading;

namespace DeepVisionSharp.Shell.Avalonia.Services;

/// <summary>
/// 进程级单实例互斥：拿不到互斥量说明已经有一个实例在跑，启动流程据此提示并退出（与 WPF Shell 行为一致）。
/// </summary>
/// <remarks>
/// 用会话内的名字（不加 <c>Global\</c> 前缀）：应用数据在 <c>%LOCALAPPDATA%</c> 下、按用户隔离，
/// 机器级互斥量会把另一个用户挡在门外，而两个用户之间并没有共享状态。
/// <para>
/// 名字带 <c>AvaloniaShell</c>，与 WPF Shell 的互斥量区分开：迁移期两套 Shell 需要能同时打开做对比，
/// 各自的「同一 Shell 只许一个实例」语义不受影响。
/// </para>
/// </remarks>
internal sealed class SingleInstanceGuard : IDisposable
{
    private const string MutexName = "Local\\Vision.DeepVisionSharp.AvaloniaShell.SingleInstance";

    private readonly Mutex _mutex;
    private readonly bool _ownsMutex;

    private SingleInstanceGuard(Mutex mutex, bool ownsMutex)
    {
        _mutex = mutex;
        _ownsMutex = ownsMutex;
    }

    public static bool TryAcquire(out SingleInstanceGuard? guard)
    {
        Mutex mutex;
        bool createdNew;
        try
        {
            mutex = new Mutex(initiallyOwned: true, MutexName, out createdNew);
        }
        catch (Exception ex) when (ex is UnauthorizedAccessException or IOException or WaitHandleCannotBeOpenedException)
        {
            // 互斥量建不起来（权限/命名冲突）不作为启动失败理由：宁可放行，也不要用户打不开应用。
            Trace.WriteLine($"Single-instance mutex unavailable, continuing without it: {ex.Message}");
            guard = null;
            return true;
        }

        if (createdNew)
        {
            guard = new SingleInstanceGuard(mutex, ownsMutex: true);
            return true;
        }

        mutex.Dispose();
        guard = null;
        return false;
    }

    public void Dispose()
    {
        if (_ownsMutex)
        {
            try
            {
                _mutex.ReleaseMutex();
            }
            catch (ApplicationException)
            {
                // 已释放或不属于当前线程：退出路径不再重试。
            }
        }

        _mutex.Dispose();
    }
}
