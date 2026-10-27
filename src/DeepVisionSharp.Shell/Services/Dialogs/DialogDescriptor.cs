namespace DeepVisionSharp.Shell.Services.Dialogs;

public sealed record DialogDescriptor(
    Type ContentViewType,
    string Title,
    string PrimaryButtonText,
    string CloseButtonText = "取消",
    string? SecondaryButtonText = null);
