namespace DeepVisionSharp.Shell.Services.Dialogs;

public interface IAppDialogService
{
    Task<AppDialogResult> ShowAsync(
        AppDialogKind kind,
        object dataContext,
        DialogOptions? options = null,
        CancellationToken cancellationToken = default);
}
