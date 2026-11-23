using DeepVisionSharp.Domain.Datasets;

namespace DeepVisionSharp.TorchSharp.Training;

/// <summary>
/// 读取分类导出目录（train/val + 数字类目录 + dataset-manifest.json）。
/// </summary>
public sealed class ClassificationFolderDataset
{
    private static readonly string[] ImageExtensions =
    [
        ".jpg", ".jpeg", ".png", ".bmp", ".tif", ".tiff", ".webp"
    ];

    public required string Root { get; init; }

    public required IReadOnlyList<string> Classes { get; init; }

    public required string Fingerprint { get; init; }

    public required IReadOnlyList<Sample> TrainSamples { get; init; }

    public required IReadOnlyList<Sample> ValSamples { get; init; }

    public sealed record Sample(string ImagePath, int ClassIndex);

    public static ClassificationFolderDataset Load(string datasetRoot)
    {
        if (string.IsNullOrWhiteSpace(datasetRoot) || !Directory.Exists(datasetRoot))
        {
            throw new DirectoryNotFoundException($"分类数据集目录不存在：{datasetRoot}");
        }

        var manifestPath = Path.Combine(datasetRoot, DatasetManifest.FileName);
        if (!File.Exists(manifestPath))
        {
            throw new FileNotFoundException("缺少 dataset-manifest.json", manifestPath);
        }

        var manifest = DatasetManifest.Read(manifestPath, DatasetManifest.Classification);
        var classes = (manifest.Classes ?? [])
            .Select(name => name?.Trim() ?? string.Empty)
            .Where(name => !string.IsNullOrWhiteSpace(name))
            .ToList();
        if (classes.Count < 2)
        {
            throw new InvalidOperationException("分类数据集至少需要 2 个类别");
        }

        // 导出布局：train/0000, train/0001 … 与 classes 顺序一一对应
        var train = LoadSplit(datasetRoot, "train", classes.Count);
        var val = LoadSplit(datasetRoot, "val", classes.Count);
        if (train.Count == 0)
        {
            throw new InvalidOperationException("分类训练集为空");
        }

        return new ClassificationFolderDataset
        {
            Root = Path.GetFullPath(datasetRoot),
            Classes = classes,
            Fingerprint = manifest.DatasetFingerprint ?? string.Empty,
            TrainSamples = train,
            ValSamples = val
        };
    }

    private static List<Sample> LoadSplit(string datasetRoot, string split, int classCount)
    {
        var samples = new List<Sample>();
        for (var classIndex = 0; classIndex < classCount; classIndex++)
        {
            var dir = Path.Combine(datasetRoot, split, classIndex.ToString("D4"));
            if (!Directory.Exists(dir))
            {
                continue;
            }

            foreach (var file in Directory.EnumerateFiles(dir))
            {
                var ext = Path.GetExtension(file);
                if (!ImageExtensions.Contains(ext, StringComparer.OrdinalIgnoreCase))
                {
                    continue;
                }

                samples.Add(new Sample(file, classIndex));
            }
        }

        return samples;
    }
}
