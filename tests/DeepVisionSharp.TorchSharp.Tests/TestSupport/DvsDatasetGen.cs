using System.Buffers.Binary;
using System.IO.Compression;
using System.Text;
using System.Text.Json;

namespace DvsDatasetGen;

/// <summary>测试夹具参数；字段顺序与既有 Generated*Tests 的调用保持一致。</summary>
public sealed record Options(
    string Root,
    int Seed,
    int Size,
    int TrainPerClass,
    int ValPerClass);

/// <summary>
/// 仓库自带的确定性合成数据集生成器。
/// 原实现是 gitignore 的本地工具，导致克隆仓库后 TorchSharp 测试工程无法编译；
/// 这里只实现测试实际使用的分类目录 + YOLO 标签契约，不引入产品代码或新 NuGet。
/// </summary>
public static class DatasetGenerator
{
    public static readonly string[] Classes = ["ok", "scratch", "dent"];

    public static void Generate(Options options)
    {
        ArgumentNullException.ThrowIfNull(options);
        if (string.IsNullOrWhiteSpace(options.Root)) throw new ArgumentException("Root 不能为空。", nameof(options));
        if (options.Size < 8) throw new ArgumentOutOfRangeException(nameof(options), "Size 至少为 8。");
        if (options.TrainPerClass < 1 || options.ValPerClass < 0)
            throw new ArgumentOutOfRangeException(nameof(options), "每类训练样本至少 1 张，验证样本不能为负。");

        Directory.CreateDirectory(options.Root);
        foreach (var (split, count) in new[] { ("train", options.TrainPerClass), ("val", options.ValPerClass) })
        {
            for (var classIndex = 0; classIndex < Classes.Length; classIndex++)
            {
                var classDir = Path.Combine(options.Root, split, classIndex.ToString("D4"));
                Directory.CreateDirectory(classDir);
                for (var sample = 0; sample < count; sample++)
                {
                    var stem = sample.ToString("D4");
                    var pngPath = Path.Combine(classDir, stem + ".png");
                    File.WriteAllBytes(pngPath, CreatePng(options.Size, classIndex, OptionsSeed(options.Seed, classIndex, sample)));
                    File.WriteAllText(
                        Path.Combine(classDir, stem + ".txt"),
                        CreateYoloLabel(classIndex),
                        Encoding.ASCII);
                }
            }
        }

        var manifest = new
        {
            schema_version = 1,
            task = "classification",
            classes = Classes,
            dataset_fingerprint = $"generated-fixture-v1-seed-{options.Seed}",
            split_method = "generated-fixture-v1"
        };
        File.WriteAllText(
            Path.Combine(options.Root, "dataset-manifest.json"),
            JsonSerializer.Serialize(manifest),
            Encoding.UTF8);
    }

    /// <summary>
    /// Process-stable mix. <see cref="HashCode.Combine"/> is salted per process, so the same seed
    /// used to draw different pictures on every test host and the slow detection run could not repeat.
    /// </summary>
    internal static int Variant(int seed, int classIndex, int sample)
    {
        unchecked
        {
            uint hash = 2166136261;
            hash = (hash ^ (uint)seed) * 16777619;
            hash = (hash ^ (uint)classIndex) * 16777619;
            hash = (hash ^ (uint)sample) * 16777619;
            return (int)hash;
        }
    }

    private static int OptionsSeed(int seed, int classIndex, int sample) =>
        Variant(seed, classIndex, sample);

    /// <summary>三类使用三种高对比几何，既便于真实训练超过随机水平，也保持完全确定。</summary>
    private static byte[] CreatePng(int size, int classIndex, int variant)
    {
        var raw = new byte[size * (size + 1)];
        var jitter = Math.Abs(variant) % 7;
        for (var y = 0; y < size; y++)
        {
            var row = y * (size + 1);
            raw[row] = 0; // PNG filter: None
            for (var x = 0; x < size; x++)
            {
                var value = classIndex switch
                {
                    0 => x < size / 2 + jitter ? 232 : 24,
                    1 => y < size / 2 + jitter ? 232 : 24,
                    _ => Math.Abs(x - size / 2) < size / 4 && Math.Abs(y - size / 2) < size / 4 ? 232 : 24,
                };
                raw[row + 1 + x] = (byte)value;
            }
        }

        using var compressed = new MemoryStream();
        using (var zlib = new ZLibStream(compressed, CompressionLevel.Fastest, leaveOpen: true))
        {
            zlib.Write(raw);
        }

        using var png = new MemoryStream();
        png.Write([137, 80, 78, 71, 13, 10, 26, 10]);

        Span<byte> ihdr = stackalloc byte[13];
        BinaryPrimitives.WriteInt32BigEndian(ihdr[..4], size);
        BinaryPrimitives.WriteInt32BigEndian(ihdr.Slice(4, 4), size);
        ihdr[8] = 8;  // bit depth
        ihdr[9] = 0;  // grayscale
        ihdr[10] = 0; // compression method
        ihdr[11] = 0; // filter method
        ihdr[12] = 0; // no interlace
        WriteChunk(png, "IHDR", ihdr);
        WriteChunk(png, "IDAT", compressed.ToArray());
        WriteChunk(png, "IEND", []);
        return png.ToArray();
    }

    /// <summary>前两类带固定缺陷框，第三类是无目标；class index 与两分类检测慢测试的 numClasses=2 对齐。</summary>
    private static string CreateYoloLabel(int classIndex) => classIndex switch
    {
        0 => "0 0.250 0.500 0.400 0.700\n",
        1 => "1 0.500 0.250 0.700 0.400\n",
        _ => string.Empty,
    };

    private static void WriteChunk(Stream stream, string type, ReadOnlySpan<byte> data)
    {
        Span<byte> length = stackalloc byte[4];
        BinaryPrimitives.WriteUInt32BigEndian(length, (uint)data.Length);
        stream.Write(length);

        var typeBytes = Encoding.ASCII.GetBytes(type);
        stream.Write(typeBytes);
        stream.Write(data);

        var crc = 0xFFFFFFFFu;
        crc = UpdateCrc(crc, typeBytes);
        crc = UpdateCrc(crc, data);
        crc ^= 0xFFFFFFFFu;
        BinaryPrimitives.WriteUInt32BigEndian(length, crc);
        stream.Write(length);
    }

    private static uint UpdateCrc(uint crc, ReadOnlySpan<byte> data)
    {
        foreach (var value in data)
        {
            crc ^= value;
            for (var bit = 0; bit < 8; bit++)
            {
                crc = (crc & 1) != 0 ? (crc >> 1) ^ 0xEDB88320u : crc >> 1;
            }
        }

        return crc;
    }
}
