using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using MediatR;
using Microsoft.Extensions.Logging;
using DeepVisionSharp.Application.Galleries;
using DeepVisionSharp.Application.Models;
using DeepVisionSharp.Application.Ports;
using DeepVisionSharp.Shell.Services;
using DeepVisionSharp.Shell.Services.Dialogs;
using DeepVisionSharp.Shell.Services.Navigation;
using DeepVisionSharp.Shell.ViewModels.Gallery;

namespace DeepVisionSharp.Shell.ViewModels;

/// <summary>
/// Gallery page coordinator. Feature work lives in List / Editor / Transfer / Reorganize / BatchInference.
/// </summary>
public sealed partial class GalleryViewModel : ObservableObject, IDisposable
{
    public GalleryViewModel(
        IMediator mediator,
        ILocalFileDialogService fileDialog,
        IGalleryTransferService transferService,
        IProjectFileStore fileStore,
        ISampleThumbnailService sampleThumbnails,
        IImageSourceFactory imageSources,
        ILogger<GalleryViewModel> logger,
        ILogger<GalleryListViewModel> listLogger,
        ILogger<GalleryEditorViewModel> editorLogger,
        ILogger<GalleryTransferViewModel> transferLogger,
        ILogger<GalleryReorganizeViewModel> reorganizeLogger,
        ILogger<GalleryBatchInferenceViewModel> batchInferenceLogger,
        WorkspaceSessionService workspaceSession,
        IAppNavigationService navigation,
        ShellSnackbarNotifier snackbar,
        IAppDialogService appDialogService,
        IProcessLauncher processLauncher,
        ITaskCapabilityResolver taskCapabilities,
        ShellTaskCenterViewModel taskCenter)
    {
        _ = logger;

        // isBusy closes over this.Transfer after construction completes (invoked only at runtime).
        List = new GalleryListViewModel(
            mediator,
            transferService,
            fileStore,
            sampleThumbnails,
            imageSources,
            workspaceSession,
            navigation,
            snackbar,
            appDialogService,
            processLauncher,
            listLogger,
            isBusy: () => Transfer is { IsImporting: true }
                || Editor is { IsSavingGallery: true }
                || BatchInference is { IsRunning: true });

        Transfer = new GalleryTransferViewModel(
            fileDialog,
            transferService,
            List,
            snackbar,
            transferLogger);

        Editor = new GalleryEditorViewModel(
            mediator,
            List,
            workspaceSession,
            snackbar,
            appDialogService,
            editorLogger);

        Reorganize = new GalleryReorganizeViewModel(
            mediator,
            List,
            snackbar,
            appDialogService,
            reorganizeLogger);

        BatchInference = new GalleryBatchInferenceViewModel(
            mediator,
            taskCapabilities,
            appDialogService,
            snackbar,
            taskCenter,
            List,
            batchInferenceLogger);
    }

    public GalleryListViewModel List { get; }

    public GalleryEditorViewModel Editor { get; }

    public GalleryTransferViewModel Transfer { get; }

    public GalleryReorganizeViewModel Reorganize { get; }

    public GalleryBatchInferenceViewModel BatchInference { get; }

    [ObservableProperty]
    private int _cardColumns = 3;

    [RelayCommand]
    private Task LoadAsync() => List.LoadCommand.ExecuteAsync(null);

    /// <summary>Cancels page work without disposing; the page instance may be shown again.</summary>
    public void Deactivate() => List.LoadCommand.Cancel();

    public void Dispose() => List.LoadCommand.Cancel();
}
