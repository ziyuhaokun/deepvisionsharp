using System.IO;
using Avalonia.Controls;
using Avalonia.Platform.Storage;
using DeepVisionSharp.Application.Galleries;

namespace DeepVisionSharp.Shell.Avalonia.Services;

/// <summary>
/// Avalonia 文件/目录选择器（<see cref="TopLevel.StorageProvider"/>）。
/// 过滤器沿用 WPF 的 <c>名称|*.ext;*.ext2|名称2|*.*</c> 写法，在这里翻译成 Avalonia 的文件类型。
/// </summary>
public sealed class AvaloniaLocalFileDialogService : ILocalFileDialogService
{
    public async Task<string[]?> SelectFilesAsync(string filter = "所有文件|*.*", bool allowMultiSelect = true)
    {
        var storage = GetStorageProvider();
        if (storage is null)
        {
            return null;
        }

        var files = await storage.OpenFilePickerAsync(new FilePickerOpenOptions
        {
            Title = "选择图像文件",
            AllowMultiple = allowMultiSelect,
            FileTypeFilter = ParseFilter(filter),
        }).ConfigureAwait(true);

        return files.Count == 0 ? null : files.Select(ToLocalPath).Where(path => path.Length > 0).ToArray();
    }

    public async Task<string?> SaveFileAsync(string filter, string defaultFileName)
    {
        var storage = GetStorageProvider();
        if (storage is null)
        {
            return null;
        }

        var file = await storage.SaveFilePickerAsync(new FilePickerSaveOptions
        {
            Title = "保存文件",
            SuggestedFileName = defaultFileName,
            FileTypeChoices = ParseFilter(filter),
        }).ConfigureAwait(true);

        return file is null ? null : ToLocalPath(file);
    }

    public async Task<string?> SelectFolderAsync()
    {
        var storage = GetStorageProvider();
        if (storage is null)
        {
            return null;
        }

        var folders = await storage.OpenFolderPickerAsync(new FolderPickerOpenOptions
        {
            Title = "选择图像目录",
            AllowMultiple = false,
        }).ConfigureAwait(true);

        return folders.Count == 0 ? null : ToLocalPath(folders[0]);
    }

    private static IStorageProvider? GetStorageProvider()
        => TopLevel.GetTopLevel(ShellWindowHost.Window)?.StorageProvider;

    private static string ToLocalPath(IStorageItem item)
        => item.TryGetLocalPath() ?? string.Empty;

    /// <summary>把 WPF 过滤串（成对的 名称|模式）转成 Avalonia 的 <see cref="FilePickerFileType"/> 列表。</summary>
    private static FilePickerFileType[] ParseFilter(string filter)
    {
        var parts = filter.Split('|', StringSplitOptions.TrimEntries);
        var types = new List<FilePickerFileType>();
        for (var i = 0; i + 1 < parts.Length; i += 2)
        {
            var name = parts[i];
            var patterns = parts[i + 1]
                .Split(';', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
                .Where(pattern => pattern != "*.*")
                .ToArray();

            types.Add(patterns.Length == 0
                ? new FilePickerFileType(string.IsNullOrWhiteSpace(name) ? "所有文件" : name)
                : new FilePickerFileType(string.IsNullOrWhiteSpace(name) ? "所有文件" : name) { Patterns = patterns });
        }

        return types.Count > 0 ? types.ToArray() : [FilePickerFileTypes.All];
    }
}
