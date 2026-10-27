using DeepVisionSharp.Application.Galleries;

namespace DeepVisionSharp.Shell.Services;

/// <summary>
/// WPF-only file/folder pickers via <c>Microsoft.Win32</c> (no WinForms dependency).
/// </summary>
public sealed class LocalFileDialogService : ILocalFileDialogService
{
    public Task<string[]?> SelectFilesAsync(string filter = "所有文件|*.*", bool allowMultiSelect = true)
    {
        var dialog = new Microsoft.Win32.OpenFileDialog
        {
            Filter = filter,
            Multiselect = allowMultiSelect,
            CheckFileExists = true,
            CheckPathExists = true
        };

        var result = dialog.ShowDialog();
        return Task.FromResult(result == true ? dialog.FileNames : null);
    }

    public Task<string?> SaveFileAsync(string filter, string defaultFileName)
    {
        var dialog = new Microsoft.Win32.SaveFileDialog
        {
            Filter = filter,
            FileName = defaultFileName,
            AddExtension = true,
            OverwritePrompt = true,
            CheckPathExists = true
        };

        var result = dialog.ShowDialog();
        return Task.FromResult(result == true ? dialog.FileName : null);
    }

    public Task<string?> SelectFolderAsync()
    {
        var dialog = new Microsoft.Win32.OpenFolderDialog
        {
            Title = "选择图像目录",
            Multiselect = false
        };

        var result = dialog.ShowDialog();
        return Task.FromResult(result == true ? dialog.FolderName : null);
    }
}
