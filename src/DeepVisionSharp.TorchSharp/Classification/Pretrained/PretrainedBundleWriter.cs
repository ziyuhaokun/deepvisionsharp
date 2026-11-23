using System.Text.Json;

namespace DeepVisionSharp.TorchSharp.Classification.Pretrained;

/// <summary>
/// Writes the flat bundle <see cref="PretrainedWeightsBundle"/> reads:
/// <c>index.json</c> plus one raw little-endian blob per tensor, in ordinal name order.
/// Keeps the same layout as earlier converted bundles.
/// </summary>
public static class PretrainedBundleWriter
{
    public static void Write(IEnumerable<RawTensor> tensors, string directory, string arch)
    {
        ArgumentNullException.ThrowIfNull(tensors);
        ArgumentException.ThrowIfNullOrWhiteSpace(directory);
        ArgumentException.ThrowIfNullOrWhiteSpace(arch);

        Directory.CreateDirectory(directory);
        var entries = new List<object>();
        var index = 0;
        foreach (var tensor in tensors.OrderBy(item => item.Name, StringComparer.Ordinal))
        {
            var fileName = $"{index:D4}.bin";
            File.WriteAllBytes(Path.Combine(directory, fileName), tensor.Data);
            entries.Add(new
            {
                name = tensor.Name,
                shape = tensor.Shape,
                dtype = tensor.Dtype,
                file = fileName,
            });
            index++;
        }

        File.WriteAllText(
            Path.Combine(directory, "index.json"),
            JsonSerializer.Serialize(new { arch, tensors = entries }, new JsonSerializerOptions { WriteIndented = true }));
    }
}
