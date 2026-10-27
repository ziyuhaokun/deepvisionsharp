using CommunityToolkit.Mvvm.ComponentModel;

namespace DeepVisionSharp.Shell.ViewModels;

public sealed partial class SettingsDebugAccessDialogState : ObservableObject
{
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasPassword))]
    private string _password = string.Empty;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasErrorMessage))]
    private string _errorMessage = string.Empty;

    public bool HasPassword => !string.IsNullOrEmpty(Password);

    public bool HasErrorMessage => !string.IsNullOrEmpty(ErrorMessage);

    public void RejectPassword()
    {
        Password = string.Empty;
        ErrorMessage = "密码错误，请重试。";
    }

    public void Reset()
    {
        Password = string.Empty;
        ErrorMessage = string.Empty;
    }
}
