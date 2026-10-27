using CommunityToolkit.Mvvm.ComponentModel;

namespace DeepVisionSharp.Shell.ViewModels.Training;

public sealed partial class TrainingJobRenameDialogState : ObservableObject
{
    [ObservableProperty]
    private string _name = string.Empty;

    [ObservableProperty]
    private string _dialogErrorMessage = string.Empty;

    public bool HasDialogErrorMessage => !string.IsNullOrWhiteSpace(DialogErrorMessage);

    public void Reset(string name)
    {
        Name = name;
        ClearDialogError();
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
}
