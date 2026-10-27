using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using MediatR;
using Microsoft.Extensions.Logging;
using DeepVisionSharp.Application.Galleries;
using DeepVisionSharp.Application.Ports;
using DeepVisionSharp.Application.Projects;
using DeepVisionSharp.Shell.Services;
using DeepVisionSharp.Shell.Services.Dialogs;
using DeepVisionSharp.Shell.Services.Navigation;

namespace DeepVisionSharp.Shell.ViewModels.Dashboard;

public sealed partial class DashboardViewModel : ObservableObject
{
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
        ILogger<DashboardProjectTransferViewModel> transferLogger)
    {
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
}
