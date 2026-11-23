using Xunit;
using DeepVisionSharp.TorchSharp.Classification.Pretrained;
using DeepVisionSharp.TorchSharp.Models;

namespace DeepVisionSharp.TorchSharp.Tests;

/// <summary>
/// 真下载官方 ResNet-18 权重，用纯 C# 转换，并和 Python 脚本转出的包逐字节对照。
/// 联网且约 45 MB，所以和其他预训练测试一样需要 <c>DV_VERIFY_PRETRAINED=1</c>。
/// </summary>
[Trait("Category", "Pretrained")]
public sealed class PretrainedWeightsRealDownloadTests
{
    [PretrainedFact]
    public async Task ResNet18_converted_in_csharp_matches_the_python_converter_byte_for_byte()
    {
        var root = Path.Combine(Path.GetTempPath(), "dv-real-download", Guid.NewGuid().ToString("N"));
        try
        {
            var source = PretrainedWeightsCatalog.ForArch("resnet18")!;
            var target = await new PretrainedWeightsDownloader(new HttpClient { Timeout = TimeSpan.FromMinutes(10) }, () => root)
                .DownloadAsync(source);

            using (var bundle = PretrainedWeightsBundle.Load(target))
            {
                Assert.Equal("resnet18", bundle.Arch);
                Assert.True(bundle.Tensors.ContainsKey("conv1.weight"));
                Assert.True(bundle.Tensors.ContainsKey("fc.weight"));
            }

            // The Python converter's output is the reference, when the machine has one.
            var reference = Path.Combine(PretrainedWeightsLocator.RootDirectory, "resnet18");
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
                    $"{Path.GetFileName(file)} differs from the Python converter's output");
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
