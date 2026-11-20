using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Markup.Xaml.Styling;
using Avalonia.Threading;
using DeepVisionSharp.Application.Projects;
using DeepVisionSharp.Presentation.Ports;
using DeepVisionSharp.Presentation.Services;
using DeepVisionSharp.Presentation.Services.Navigation;
using DeepVisionSharp.Presentation.ViewModels;
using DeepVisionSharp.Presentation.ViewModels.Dashboard;
using DeepVisionSharp.Presentation.ViewModels.Models;
using DeepVisionSharp.Shell.Avalonia;
using DeepVisionSharp.Shell.Avalonia.Composition;
using DeepVisionSharp.Shell.Avalonia.Services;
using DeepVisionSharp.Shell.Avalonia.Services.Hosting;
using DeepVisionSharp.Shell.Avalonia.Views;
using MediatR;
using Microsoft.Extensions.DependencyInjection;

// 真实数据渲染工厂：走 AppComposition 建容器，用真实 ViewModel 与真实数据根解析视图，
// 然后泵发 Dispatcher 等首屏加载完毕再出图（离屏宿主自己的 Settle 不等异步加载）。
// DV_RENDER_PAGE 决定进哪一页：dashboard / gallery / models / labeling / training / inference / settings。
// 所有页面都从主窗口进：离屏宿主拍的始终是 MainWindow，页面挂在壳层内容区里，
// 避免直接渲染裸页面丢掉标题栏与侧边导航（那样会产生「标题重叠」之类的伪影）。
internal static class ViewFactory
{
    private static readonly string[] ResourceSources =
    [
        "avares://yu-ui.avalonia/Tokens/Palette.axaml",
        "avares://yu-ui.avalonia/Tokens/Metrics.axaml",
        "avares://yu-ui.avalonia/Tokens/Typography.axaml",
        "avares://yu-ui.avalonia/Tokens/Motion.axaml",
        "avares://yu-ui.avalonia/Themes/Generic.axaml",
        "avares://yu-ui.avalonia/Themes/Forms.axaml",
        "avares://yu-ui.avalonia/Styles/Common.axaml",
        "avares://yu-ui.avalonia/Styles/Button.axaml",
        "avares://yu-ui.avalonia/Styles/ButtonImplicit.axaml",
        "avares://yu-ui.avalonia/Styles/TextInput.axaml",
        "avares://yu-ui.avalonia/Styles/Selection.axaml",
        "avares://yu-ui.avalonia/Styles/Switch.axaml",
        "avares://yu-ui.avalonia/Styles/ProgressBar.axaml",
        "avares://yu-ui.avalonia/Styles/Container.axaml",
        "avares://yu-ui.avalonia/Styles/ScrollBar.axaml",
        "avares://yu-ui.avalonia/Styles/Slider.axaml",
        "avares://yu-ui.avalonia/Styles/List.axaml",
        "avares://yu-ui.avalonia/Styles/ComboBox.axaml",
        "avares://yu-ui.avalonia/Styles/TreeView.axaml",
        "avares://yu-ui.avalonia/Styles/Menu.axaml",
    ];

    /// <summary>DV_RENDER_PAGE → 路由与页面类型。页面类型只用于核对「导航服务是否已经挂上真页面」以及未迁移时的兜底。</summary>
    private static readonly Dictionary<string, (AppRoute Route, Type PageType)> Pages =
        new(StringComparer.Ordinal)
        {
            ["dashboard"] = (AppRoute.Dashboard, typeof(DashboardPage)),
            ["gallery"] = (AppRoute.Gallery, typeof(GalleryPage)),
            ["models"] = (AppRoute.Models, typeof(ModelsPage)),
            ["labeling"] = (AppRoute.Labeling, typeof(LabelingPage)),
            ["training"] = (AppRoute.Training, typeof(TrainingPage)),
            ["inference"] = (AppRoute.Inference, typeof(InferencePage)),
            ["settings"] = (AppRoute.Settings, typeof(SettingsPage)),
        };

    private static IServiceProvider? _services;
    private static Func<bool>? _isLoading;

    public static Control Create(string theme)
    {
        AddResources();

        MainWindow window;
        try
        {
            _services ??= AppComposition.Create(DeepVisionLaunchOptions.Default).Services;
            window = _services.GetRequiredService<MainWindow>();
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[render] composition failed, falling back to stub window: {ex.Message}");
            return new MainWindow();
        }

        // 主窗口构造时按用户设置应用主题（可能是深色），主题以渲染参数为准，这里覆盖一次。
        _services.GetService<IAppThemeService>()?.Apply(
            theme == "dark" ? ShellThemePreference.Dark : ShellThemePreference.Light);

        var pageName = (Environment.GetEnvironmentVariable("DV_RENDER_PAGE") ?? "dashboard").Trim().ToLowerInvariant();
        if (!Pages.TryGetValue(pageName, out var target))
        {
            throw new InvalidOperationException(
                $"DV_RENDER_PAGE={pageName} 不是已知页面，可用值：{string.Join(" / ", Pages.Keys)}");
        }

        // 显式开关：只有 DV_RENDER_STUB=1 且目标就是训练页时，才用页面自己的 stub 上下文出图。
        // 真 ViewModel 进页不选任务（要用户点一下），指标区就不渲染，只能看到任务列表 +「请选择任务」；
        // 指标卡片的视觉基线才需要这一张。缺省不设这个变量就永远走真 ViewModel，没有隐式兜底。
        var useStub = Environment.GetEnvironmentVariable("DV_RENDER_STUB") == "1";
        if (useStub && target.PageType != typeof(TrainingPage))
        {
            Console.WriteLine("[render] DV_RENDER_STUB=1 只对 training 生效，本页仍用真实 ViewModel");
            useStub = false;
        }

        // 主窗口构造时已经进了仪表盘，其余页面在窗口上再导航一次。
        if (target.Route == AppRoute.Dashboard)
        {
            Console.WriteLine("[render] real composition, MainWindow (dashboard)");
        }
        else if (useStub)
        {
            EnterProject(_services);
            ShowStubTrainingPage(window);
        }
        else
        {
            if (NeedsProject(target.Route))
            {
                EnterProject(_services);
            }

            NavigateInShell(window, target.Route, target.PageType);
        }

        _isLoading = LoadingProbe(window);
        return window;
    }

    public static void WaitUntilReady(Control view)
    {
        if (_services is null)
        {
            return;
        }

        if (_isLoading is { } isLoading)
        {
            var started = WaitUntil(isLoading, TimeSpan.FromSeconds(3));
            var finished = WaitUntil(() => !isLoading(), TimeSpan.FromSeconds(40));
            Console.WriteLine($"[render] loading started={started} finished={finished}");
        }
        else
        {
            Console.WriteLine("[render] page has no container ViewModel: pumping dispatcher for a fixed window");
        }

        Pump(TimeSpan.FromMilliseconds(600));

        if (_services.GetService(typeof(DashboardViewModel)) is DashboardViewModel dashboard)
        {
            Console.WriteLine($"[render] overview: {dashboard.List.OverviewSummary}");
            Console.WriteLine($"[render] projects={dashboard.List.ProjectCount} filtered={dashboard.List.FilteredProjectCount}");
            foreach (var project in dashboard.List.FilteredProjects)
            {
                Console.WriteLine($"[render]   - {project.Name} | {project.TypeLabel} | {project.SampleCountText} | {project.LabelCountText}");
            }
        }
    }

    public static void Cleanup(Control view)
    {
    }

    /// <summary>
    /// DV_RENDER_STUB=1 的专用路径：用页面自己的 stub 兜底构造（TrainingPage() → SampleTrainingContext），
    /// 再挂进壳层内容区。不走导航服务，因为容器解析出的页面会先拿真实 ViewModel 建 XAML，
    /// 那批真 VM 的绑定错误会污染这张纯视觉基线图。
    /// </summary>
    private static void ShowStubTrainingPage(MainWindow window)
    {
        if (Activator.CreateInstance(typeof(TrainingPage)) is not Control page)
        {
            throw new InvalidOperationException("TrainingPage 无法用无参构造创建，stub 基线不可用");
        }

        ((IPageHost)window).Show(page, AppRoute.Training);
        Console.WriteLine("[render] DV_RENDER_STUB=1: TrainingPage() 的 SampleTrainingContext（仅视觉基线，不代表真实运行）");
    }

    /// <summary>图库 / 模型 / 标注 / 训练 / 推理都要项目上下文；仪表盘与设置不需要。</summary>
    private static bool NeedsProject(AppRoute route) => route is
        AppRoute.Gallery or AppRoute.Models or AppRoute.Labeling or AppRoute.Training or AppRoute.Inference;

    /// <summary>
    /// 走主窗口自己的导航服务切页：页面由服务从容器解析并放进内容区，与用户点侧栏导航是同一条路径。
    /// 导航服务还没接这个路由时（现在 7 条都接了，留着给后续新增页面），把页面挂进同一个内容区——壳层照旧，只是没走路由缓存。
    /// </summary>
    private static void NavigateInShell(MainWindow window, AppRoute route, Type pageType)
    {
        var navigation = _services!.GetRequiredService<AvaloniaNavigationService>();
        navigation.Navigate(route);

        if (window.FindControl<ContentControl>("ContentHost")?.Content is Control { } hosted
            && hosted.GetType() == pageType)
        {
            Console.WriteLine($"[render] navigated to {route}: {hosted.GetType().Name}");
            return;
        }

        var page = (_services.GetService(pageType) as Control)
            ?? (Activator.CreateInstance(pageType) as Control)
            ?? throw new InvalidOperationException($"页面 {pageType.Name} 无法创建");

        ((IPageHost)window).Show(page, route);
        Console.WriteLine($"[render] route {route} not migrated yet: hosted {page.GetType().Name} into shell content host");
    }

    /// <summary>加载态从内容区页面的 DataContext 上取，避免和页面各自 New 出来的 ViewModel 实例对不上。</summary>
    private static Func<bool>? LoadingProbe(MainWindow window) => window.FindControl<ContentControl>("ContentHost")?.Content switch
    {
        DashboardPage { DataContext: DashboardViewModel viewModel } => () => viewModel.List.IsLoading,
        GalleryPage { DataContext: GalleryViewModel viewModel } => () => viewModel.List.IsLoading,
        ModelsPage { DataContext: ModelsViewModel viewModel } => () => viewModel.List.IsLoading,
        _ => null,
    };

    /// <summary>
    /// 进入一个项目，让图库/模型/标注/训练/推理页拿到项目上下文（只改内存会话，不写数据）。
    /// 选样本最多的那个：库里排在前面的可能是空项目，空项目会把整页渲成空壳，看不到真实内容。
    /// </summary>
    private static void EnterProject(IServiceProvider services)
    {
        try
        {
            var mediator = services.GetRequiredService<IMediator>();
            var overview = PumpFor(Task.Run(() => mediator.Send(new GetDashboardProjectsQuery())), TimeSpan.FromSeconds(20));
            var chosen = overview.Value?
                .OrderByDescending(project => project.SampleCount)
                .ThenBy(project => project.Name, StringComparer.Ordinal)
                .FirstOrDefault();
            if (chosen is null)
            {
                Console.WriteLine("[render] no project to enter");
                return;
            }

            var projectResult = PumpFor(Task.Run(() => mediator.Send(new GetProjectByIdQuery(chosen.Id))), TimeSpan.FromSeconds(20));
            if (projectResult.Value is not { } project)
            {
                Console.WriteLine($"[render] project {chosen.Id} not loaded: {projectResult.Error}");
                return;
            }

            services.GetRequiredService<WorkspaceSessionService>().EnterProject(project);
            Console.WriteLine($"[render] entered project {project.Name} ({project.Type}), {chosen.SampleCount} 样本");
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[render] enter project failed: {ex.Message}");
        }
    }

    private static T PumpFor<T>(Task<T> task, TimeSpan timeout)
    {
        var watch = Stopwatch.StartNew();
        while (!task.IsCompleted && watch.Elapsed < timeout)
        {
            Dispatcher.UIThread.RunJobs();
            Thread.Sleep(25);
        }

        return task.GetAwaiter().GetResult();
    }

    private static void AddResources()
    {
        if (Application.Current is not { } app)
        {
            return;
        }

        foreach (var source in ResourceSources)
        {
            var uri = new Uri(source);
            app.Resources.MergedDictionaries.Add(new ResourceInclude(uri) { Source = uri });
        }
    }

    private static bool WaitUntil(Func<bool> condition, TimeSpan timeout)
    {
        var watch = Stopwatch.StartNew();
        while (watch.Elapsed < timeout)
        {
            Dispatcher.UIThread.RunJobs();
            if (condition())
            {
                return true;
            }

            Thread.Sleep(25);
        }

        return condition();
    }

    private static void Pump(TimeSpan duration)
    {
        var watch = Stopwatch.StartNew();
        while (watch.Elapsed < duration)
        {
            Dispatcher.UIThread.RunJobs();
            Thread.Sleep(25);
        }
    }
}
