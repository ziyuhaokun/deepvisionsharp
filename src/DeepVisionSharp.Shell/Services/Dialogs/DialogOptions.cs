namespace DeepVisionSharp.Shell.Services.Dialogs;

public sealed record DialogOptions(
    string? Title = null,
    string? PrimaryButtonText = null,
    string? SecondaryButtonText = null,
    string? CloseButtonText = null,
    AppDialogDefaultButton DefaultButton = AppDialogDefaultButton.Primary,
    string? PrimaryButtonIsEnabledBindingPath = null);
