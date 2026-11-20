using System.Diagnostics;
using Avalonia;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Markup.Xaml;
using DeepVisionSharp.Presentation.Services;
using DeepVisionSharp.Shell.Avalonia.Composition;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;

namespace DeepVisionSharp.Shell.Avalonia;

/// <summary>
/// 进程入口：解析启动参数 → 建组合根（业务层 + Avalonia 端口）→ 显示主窗口。
/// 启动流程对齐 WPF Shell 的 <c>App.xaml.cs</c>，去掉 WPF 专属步骤
/// （文件日志、单实例互斥、WER dump 注册、异常上报窗口）。
/// </summary>
public partial class App : global::Avalonia.Application
{
    private IHost? _host;

    public override void Initialize() => AvaloniaXamlLoader.Load(this);

    public override void OnFrameworkInitializationCompleted()
    {
        if (ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
        {
            var options = DeepVisionLaunchOptions.Parse(desktop.Args ?? []);
            _host = AppComposition.Create(options);
            _host.Start();
            desktop.MainWindow = _host.Services.GetRequiredService<MainWindow>();
            desktop.Exit += OnDesktopExit;
        }

        base.OnFrameworkInitializationCompleted();
    }

    private void OnDesktopExit(object? sender, ControlledApplicationLifetimeExitEventArgs e)
    {
        if (_host is null)
        {
            return;
        }

        try
        {
            _host.StopAsync(TimeSpan.FromSeconds(3)).GetAwaiter().GetResult();
        }
        catch (Exception ex)
        {
            Trace.WriteLine($"Host stop failed during shutdown: {ex.Message}");
        }

        try
        {
            _host.Dispose();
        }
        catch (Exception ex)
        {
            Trace.WriteLine($"Host dispose failed during shutdown: {ex.Message}");
        }

        _host = null;
    }
}
