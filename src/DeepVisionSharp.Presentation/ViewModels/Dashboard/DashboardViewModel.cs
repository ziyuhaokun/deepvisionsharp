using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using MediatR;
using Microsoft.Extensions.Logging;
using DeepVisionSharp.Application.Galleries;
using DeepVisionSharp.Application.Ports;
using DeepVisionSharp.Application.Projects;
using DeepVisionSharp.Presentation.Services;
using DeepVisionSharp.Presentation.Services.Dialogs;
using DeepVisionSharp.Presentation.Services.Navigation;
using DeepVisionSharp.Presentation.Ports;

namespace DeepVisionSharp.Presentation.ViewModels.Dashboard;

public sealed partial class DashboardViewModel : ObservableObject, ICardColumnsHost
{
    private readonly ShellSnackbarNotifier _snackbar;
    private readonly ILogger<DashboardViewModel> _logger;

    public DashboardViewModel(
        IMediator mediator,
        ILocalFileDialogService fileDialog,
        WorkspaceSessionService workspaceSession,
        IAppNavigationService navigation,
        ShellSnackbarNotifier snackbar,
        IAppDialogService appDialogService,
        ISampleThumbnailService sampleThumbnails,
        IImageSourceFactory imageSources,
        ILogger<DashboardProjectListViewModel> listLogger,
        ILogger<DashboardProjectEditorViewModel> editorLogger,
        ILogger<DashboardProjectTransferViewModel> transferLogger,
        ILogger<DashboardViewModel> logger)
    {
        _snackbar = snackbar;
        _logger = logger;
        List = new DashboardProjectListViewModel(
            mediator,
            workspaceSession,
            navigation,
            snackbar,
            appDialogService,
            sampleThumbnails,
            imageSources,
            listLogger);

        Editor = new DashboardProjectEditorViewModel(
            mediator,
            List,
            workspaceSession,
            snackbar,
            appDialogService,
            editorLogger);

        Transfer = new DashboardProjectTransferViewModel(
            mediator,
            fileDialog,
            snackbar,
            transferLogger,
            () => List.RefreshProjectsAsync());

        // 导入/导出进行中，禁用删除/进入等并发项目操作。
        Transfer.IsTransferringChanged += blocked => List.SetBlockedByTransfer(blocked);
    }

    public DashboardProjectListViewModel List { get; }

    public DashboardProjectEditorViewModel Editor { get; }

    public DashboardProjectTransferViewModel Transfer { get; }

    /// <summary>卡片墙响应式列数（1–4），由页面按可用宽度计算。</summary>
    [ObservableProperty]
    private int _cardColumns = 2;

    [RelayCommand]
    private Task LoadAsync() => List.LoadCommand.ExecuteAsync(null);

    /// <summary>
    /// 页面首次进入时调用：已有数据静默刷新，否则走完整加载。
    /// 异常在此收敛为 snackbar，避免页面的 async void 事件处理器把异常漏到 UI 线程。
    /// </summary>
    public async Task LoadForNavigationAsync()
    {
        try
        {
            if (List.HasProjects)
            {
                await List.RefreshProjectsAsync();
            }
            else
            {
                await LoadCommand.ExecuteAsync(null);
            }
        }
        catch (OperationCanceledException)
        {
            // 导航走了，下一次进入会重新加载。
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to load dashboard projects on navigation");
            _snackbar.ShowError("加载项目列表失败，详细信息已写入日志。", "项目");
        }
    }
}
