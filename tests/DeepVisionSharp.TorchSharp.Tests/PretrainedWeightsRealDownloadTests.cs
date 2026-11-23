using Xunit;
using DeepVisionSharp.TorchSharp.Classification.Pretrained;
using DeepVisionSharp.TorchSharp.Models;

namespace DeepVisionSharp.TorchSharp.Tests;

[AttributeUsage(AttributeTargets.Method)]
public sealed class PretrainedTheoryAttribute : TheoryAttribute
{
    public PretrainedTheoryAttribute()
    {
        if (Environment.GetEnvironmentVariable("DV_VERIFY_PRETRAINED") != "1")
        {
            Skip = "Set DV_VERIFY_PRETRAINED=1 (needs network; the full set is about 500 MB).";
        }
    }
}

/// <summary>
/// 把目录里每一个预训练权重都真下载一遍，用纯 C# 转换：证明写死的下载地址和 SHA-256 都对，
/// 转出的包能被产品读回；本机默认目录里若有 Python 脚本转出的同名包，就逐字节对照。
/// 联网且整套约 500 MB，所以需要 <c>DV_VERIFY_PRETRAINED=1</c>。
/// </summary>
[Trait("Category", "Pretrained")]
public sealed class PretrainedWeightsRealDownloadTests
{
    public static TheoryData<string> AllArchs() => new(PretrainedWeightsCatalog.All.Select(source => source.Arch));

    [PretrainedTheory]
    [MemberData(nameof(AllArchs))]
    public async Task Catalog_entry_downloads_converts_and_matches_the_python_converter(string arch)
    {
        var root = Path.Combine(Path.GetTempPath(), "dv-real-download", Guid.NewGuid().ToString("N"));
        try
        {
            var source = PretrainedWeightsCatalog.All.Single(item => item.Arch == arch);
            var target = await new PretrainedWeightsDownloader(new HttpClient { Timeout = TimeSpan.FromMinutes(15) }, () => root)
                .DownloadAsync(source);

            using (var bundle = PretrainedWeightsBundle.Load(target))
            {
                Assert.Equal(arch, bundle.Arch);
                Assert.True(bundle.Tensors.Count > 100, $"{arch}: only {bundle.Tensors.Count} tensors");
            }

            // The Python converter's output is the reference, when the machine has one.
            var reference = Path.Combine(PretrainedWeightsLocator.RootDirectory, arch);
            if (!File.Exists(Path.Combine(reference, "index.json")))
            {
                return;
            }

            Assert.Equal(
                File.ReadAllText(Path.Combine(reference, "index.json")).Replace("\r\n", "\n"),
                File.ReadAllText(Path.Combine(target, "index.json")).Replace("\r\n", "\n"));
            foreach (var file in Directory.GetFiles(reference, "*.bin"))
            {
                Assert.True(
                    File.ReadAllBytes(file).AsSpan().SequenceEqual(File.ReadAllBytes(Path.Combine(target, Path.GetFileName(file)))),
                    $"{arch}/{Path.GetFileName(file)} differs from the Python converter's output");
            }
        }
        finally
        {
            if (Directory.Exists(root))
            {
                Directory.Delete(root, recursive: true);
            }
        }
    }
}
