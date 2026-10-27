namespace RuntimePackage.Engine.Installation;

public sealed class InstallPreflight
{
    public Task<InstallPreflightResult> ValidateAsync(InstallPlan plan, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(plan);
        cancellationToken.ThrowIfCancellationRequested();

        var errors = new List<string>();
        var notes = new List<string>();
        var requiresOverwriteConfirmation = false;

        var pathRoot = Path.GetPathRoot(plan.ResolvedInstallDirectory);
        if (!string.IsNullOrWhiteSpace(pathRoot))
        {
            var normalizedRoot = Path.TrimEndingDirectorySeparator(pathRoot);
            var normalizedDirectory = Path.TrimEndingDirectorySeparator(plan.ResolvedInstallDirectory);
            if (string.Equals(normalizedRoot, normalizedDirectory, StringComparison.OrdinalIgnoreCase))
            {
                errors.Add("不支持安装到磁盘根目录。");
            }
        }

        var extractedRoot = Path.GetFullPath(plan.Package.ExtractedRoot);
        if (plan.ResolvedInstallDirectory.StartsWith(extractedRoot, StringComparison.OrdinalIgnoreCase))
        {
            errors.Add("安装目录与安装包解压目录重叠。");
        }

        if (Directory.Exists(plan.ResolvedInstallDirectory))
        {
            var installationRecord = Path.Combine(plan.ResolvedInstallDirectory, "installation.json");
            if (File.Exists(installationRecord))
            {
                requiresOverwriteConfirmation = true;
                notes.Add("该目录中已存在安装记录，继续安装将覆盖原有安装。");
            }
            else if (Directory.EnumerateFileSystemEntries(plan.ResolvedInstallDirectory).Any())
            {
                requiresOverwriteConfirmation = true;
                notes.Add("安装目录包含已有内容，继续安装将先清空该目录。");
            }
        }

        notes.Add($"解析后的安装目录：{plan.ResolvedInstallDirectory}");

        return Task.FromResult(new InstallPreflightResult(
            errors.Count == 0,
            plan.ResolvedInstallDirectory,
            plan.PrimaryEntryPointRelativePath,
            requiresOverwriteConfirmation,
            errors,
            notes));
    }
}
