using System.Text.Json;

using TorchSharp;
using static TorchSharp.torch;

namespace DeepVisionSharp.TorchSharp.Classification.Pretrained;

/// <summary>
/// Reads the flat tensor bundle produced by <c>scripts/convert_torchvision_weights.py</c>.
/// </summary>
/// <remarks>
/// TorchSharp 0.107 cannot read a PyTorch state dict (<c>torch.load</c> returns a single tensor and
/// <c>Module.load</c> expects TorchSharp's own format), so the conversion happens offline and the
/// product only reads raw little-endian blobs plus an index.
/// </remarks>
public sealed class PretrainedWeightsBundle : IDisposable
{
    private PretrainedWeightsBundle(string arch, Dictionary<string, Tensor> tensors)
    {
        Arch = arch;
        Tensors = tensors;
    }

    public string Arch { get; }

    public IReadOnlyDictionary<string, Tensor> Tensors { get; }

    public static PretrainedWeightsBundle Load(string directory)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(directory);
        var indexPath = Path.Combine(directory, "index.json");
        if (!File.Exists(indexPath))
        {
            throw new FileNotFoundException("预训练权重缺少 index.json", indexPath);
        }

        // The index is written with snake_case field names; bind them case-insensitively.
        var index = JsonSerializer.Deserialize<IndexDocument>(
                File.ReadAllText(indexPath),
                new JsonSerializerOptions { PropertyNameCaseInsensitive = true })
            ?? throw new InvalidDataException($"预训练权重索引无效：{indexPath}");

        var tensors = new Dictionary<string, Tensor>(StringComparer.Ordinal);
        try
        {
            foreach (var entry in index.Tensors)
            {
                var blobPath = Path.Combine(directory, entry.File);
                if (!File.Exists(blobPath))
                {
                    throw new FileNotFoundException($"预训练权重缺少数据文件：{entry.File}", blobPath);
                }

                tensors[entry.Name] = ReadTensor(entry, blobPath);
            }
        }
        catch
        {
            // A bundle is ~45 MB of native tensors for resnet18; abandoning a half-read bundle
            // without releasing them keeps that memory until the finalizer runs (or forever, if the
            // caller swallows the exception and retries).
            foreach (var tensor in tensors.Values)
            {
                tensor.Dispose();
            }

            throw;
        }

        return new PretrainedWeightsBundle(index.Arch, tensors);
    }

    private static Tensor ReadTensor(TensorEntry entry, string blobPath)
    {
        var bytes = File.ReadAllBytes(blobPath);
        var shape = entry.Shape.Select(value => (long)value).ToArray();
        var count = shape.Length == 0 ? 1 : shape.Aggregate(1L, (current, value) => current * value);

        if (string.Equals(entry.Dtype, "float32", StringComparison.OrdinalIgnoreCase))
        {
            if (bytes.Length != count * sizeof(float))
            {
                throw new InvalidDataException($"{entry.Name}: float32 数据长度与形状不匹配");
            }

            var values = new float[count];
            Buffer.BlockCopy(bytes, 0, values, 0, bytes.Length);
            return tensor(values, shape);
        }

        if (string.Equals(entry.Dtype, "int64", StringComparison.OrdinalIgnoreCase))
        {
            if (bytes.Length != count * sizeof(long))
            {
                throw new InvalidDataException($"{entry.Name}: int64 数据长度与形状不匹配");
            }

            var values = new long[count];
            Buffer.BlockCopy(bytes, 0, values, 0, bytes.Length);
            return tensor(values, shape);
        }

        throw new InvalidDataException($"{entry.Name}: 不支持的预训练权重类型 {entry.Dtype}");
    }

    public void Dispose()
    {
        foreach (var tensor in Tensors.Values)
        {
            tensor.Dispose();
        }
    }

    private sealed class IndexDocument
    {
        public string Arch { get; set; } = string.Empty;

        public List<TensorEntry> Tensors { get; set; } = new();
    }

    private sealed class TensorEntry
    {
        public string Name { get; set; } = string.Empty;

        public List<int> Shape { get; set; } = new();

        public string Dtype { get; set; } = string.Empty;

        public string File { get; set; } = string.Empty;
    }
}
