using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using MediatR;
using Microsoft.Extensions.Logging;
using DeepVisionSharp.Application.Galleries;
using DeepVisionSharp.Application.Models;
using DeepVisionSharp.Application.Ports;
using DeepVisionSharp.Shell.Services;
using DeepVisionSharp.Shell.Services.Dialogs;

namespace DeepVisionSharp.Shell.ViewModels.Models;

public sealed partial class ModelsViewModel : ObservableObject, IDisposable
{
    public ModelsViewModel(
        IMediator mediator,
        ILocalFileDialogService fileDialog,
        WorkspaceSessionService workspaceSession,
        IModelCheckpointFileService checkpointFiles,
        IProcessLauncher processLauncher,
        ShellSnackbarNotifier snackbar,
        IAppDialogService appDialogService,
        ITaskCapabilityResolver taskCapabilities,
        IUiDispatcher uiDispatcher,
        ILogger<ModelsListViewModel> listLogger)
    {
        List = new ModelsListViewModel(
            mediator,
            fileDialog,
            workspaceSession,
            checkpointFiles,
            processLauncher,
            snackbar,
            appDialogService,
            taskCapabilities,
            uiDispatcher,
            listLogger);

        Editor = new ModelsEditorViewModel(
            mediator,
            List,
            snackbar,
            appDialogService);
    }

    public ModelsListViewModel List { get; }

    public ModelsEditorViewModel Editor { get; }

    [RelayCommand]
    private Task LoadAsync() => List.LoadCommand.ExecuteAsync(null);

    public Task OnPageLoadedAsync() => LoadCommand.ExecuteAsync(null);

    /// <summary>
    /// Cancels page work without tearing down subscriptions. The page instance may be shown again,
    /// and unsubscribing <c>Models.CollectionChanged</c> here would leave a re-activated page with a
    /// list that never updates.
    /// </summary>
    public void Deactivate() => List.LoadCommand.Cancel();

    public void Dispose()
    {
        List.LoadCommand.Cancel();
        List.Dispose();
        Editor.Dispose();
    }
}
