using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using System.Collections.ObjectModel;

namespace DeepVisionSharp.Shell.ViewModels;

public sealed partial class GalleryReorganizeDialogState : ObservableObject
{
    public const string TargetModeNew = "New";
    public const string TargetModeExisting = "Existing";

    [ObservableProperty]
    private string _targetMode = TargetModeNew;

    [ObservableProperty]
    private string _newGalleryName = string.Empty;

    [ObservableProperty]
    private Guid? _selectedTargetGalleryId;

    [ObservableProperty]
    private string _dialogErrorMessage = string.Empty;

    [ObservableProperty]
    private string _summaryText = string.Empty;

    [ObservableProperty]
    private bool _deleteEmptySources = true;

    [ObservableProperty]
    private bool _showDeleteEmptySourcesOption;

    public ObservableCollection<GalleryTargetOption> AvailableTargets { get; } = new();

    public bool HasDialogErrorMessage => !string.IsNullOrWhiteSpace(DialogErrorMessage);

    public bool UseNewGallery => string.Equals(TargetMode, TargetModeNew, StringComparison.Ordinal);

    public bool UseExistingGallery => string.Equals(TargetMode, TargetModeExisting, StringComparison.Ordinal);

    public void Reset(string defaultNewGalleryName, string summaryText, bool showDeleteEmptySourcesOption = false)
    {
        TargetMode = TargetModeNew;
        NewGalleryName = defaultNewGalleryName;
        SelectedTargetGalleryId = AvailableTargets.FirstOrDefault()?.Id;
        DialogErrorMessage = string.Empty;
        SummaryText = summaryText;
        DeleteEmptySources = true;
        ShowDeleteEmptySourcesOption = showDeleteEmptySourcesOption;
        OnPropertyChanged(nameof(UseNewGallery));
        OnPropertyChanged(nameof(UseExistingGallery));
        OnPropertyChanged(nameof(HasDialogErrorMessage));
    }

    public void SetDialogError(string message)
    {
        DialogErrorMessage = message;
        OnPropertyChanged(nameof(HasDialogErrorMessage));
    }

    public void ClearDialogError()
    {
        DialogErrorMessage = string.Empty;
        OnPropertyChanged(nameof(HasDialogErrorMessage));
    }

    partial void OnTargetModeChanged(string value)
    {
        OnPropertyChanged(nameof(UseNewGallery));
        OnPropertyChanged(nameof(UseExistingGallery));
    }

    [RelayCommand]
    private void SelectNewTarget()
    {
        TargetMode = TargetModeNew;
    }

    [RelayCommand]
    private void SelectExistingTarget()
    {
        TargetMode = TargetModeExisting;
    }
}

public sealed record GalleryTargetOption(Guid Id, string Name, int SampleCount)
{
    public string DisplayText => $"{Name} ({SampleCount} 样本)";

    public override string ToString() => DisplayText;
}
