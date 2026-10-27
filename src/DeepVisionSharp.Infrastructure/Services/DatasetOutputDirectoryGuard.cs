namespace DeepVisionSharp.Infrastructure.Services;

internal static class DatasetOutputDirectoryGuard
{
    public static void PrepareEmpty(string outputRoot)
    {
        if (string.IsNullOrWhiteSpace(outputRoot))
        {
            throw new ArgumentException("数据集输出目录不能为空。", nameof(outputRoot));
        }

        var fullPath = Path.GetFullPath(outputRoot);
        if (File.Exists(fullPath))
        {
            throw new IOException($"数据集输出路径已被文件占用：{fullPath}");
        }

        if (Directory.Exists(fullPath))
        {
            var attributes = File.GetAttributes(fullPath);
            if ((attributes & FileAttributes.ReparsePoint) != 0)
            {
                throw new IOException($"数据集输出目录不能是重解析点：{fullPath}");
            }

            if (Directory.EnumerateFileSystemEntries(fullPath).Any())
            {
                throw new IOException($"数据集输出目录已存在且非空，拒绝覆盖：{fullPath}");
            }

            return;
        }

        Directory.CreateDirectory(fullPath);
    }
}
