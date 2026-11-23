using System.IO.Compression;
using System.Net;
using System.Net.Http;
using System.Security.Cryptography;
using System.Text;
using Xunit;
using DeepVisionSharp.TorchSharp.Classification.Pretrained;

namespace DeepVisionSharp.TorchSharp.Tests;

/// <summary>
/// 预训练权重的纯 C# 转换与下载：用手写的最小 .pth（zip + pickle 协议 2）验证解析、写包、
/// 校验与落盘，全程不联网、不依赖 Python。
/// </summary>
public sealed class PretrainedWeightsDownloadTests : IDisposable
{
    private readonly string _root;

    public PretrainedWeightsDownloadTests()
    {
        _root = Path.Combine(Path.GetTempPath(), "dv-pretrained-tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_root);
    }

    public void Dispose()
    {
        try
        {
            Directory.Delete(_root, recursive: true);
        }
        catch
        {
            // A temp folder that survives a test run must not fail the test.
        }
    }

    [Fact]
    public void Reader_returns_contiguous_strided_and_integer_tensors()
    {
        var path = WriteCheckpoint();

        var tensors = TorchCheckpointReader.ReadStateDict(path).ToDictionary(t => t.Name);

        // 2x2 float32, contiguous.
        Assert.Equal(new long[] { 2, 2 }, tensors["layer.weight"].Shape);
        Assert.Equal("float32", tensors["layer.weight"].Dtype);
        Assert.Equal(new[] { 1f, 2f, 3f, 4f }, ToFloats(tensors["layer.weight"].Data));

        // Same storage seen through transposed strides: must come out as [[1,3],[2,4]].
        Assert.Equal(new[] { 1f, 3f, 2f, 4f }, ToFloats(tensors["layer.transposed"].Data));

        // Scalar int64 (BatchNorm's num_batches_tracked) is stored as shape [1], like the Python converter did.
        Assert.Equal(new long[] { 1 }, tensors["bn.num_batches_tracked"].Shape);
        Assert.Equal("int64", tensors["bn.num_batches_tracked"].Dtype);
        Assert.Equal(7L, BitConverter.ToInt64(tensors["bn.num_batches_tracked"].Data));
    }

    [Fact]
    public void Written_bundle_loads_back_through_the_product_reader()
    {
        var tensors = TorchCheckpointReader.ReadStateDict(WriteCheckpoint());
        var bundleDirectory = Path.Combine(_root, "bundle");

        PretrainedBundleWriter.Write(tensors, bundleDirectory, "resnet18");

        using var bundle = PretrainedWeightsBundle.Load(bundleDirectory);
        Assert.Equal("resnet18", bundle.Arch);
        Assert.Equal(3, bundle.Tensors.Count);
        Assert.Equal(new[] { 1f, 2f, 3f, 4f }, bundle.Tensors["layer.weight"].data<float>().ToArray());
        Assert.Equal(7L, bundle.Tensors["bn.num_batches_tracked"].item<long>());
    }

    [Fact]
    public void Reader_rejects_a_zip_that_is_not_a_checkpoint()
    {
        var path = Path.Combine(_root, "not-a-checkpoint.zip");
        using (var archive = ZipFile.Open(path, ZipArchiveMode.Create))
        {
            archive.CreateEntry("hello.txt");
        }

        Assert.Throws<InvalidDataException>(() => TorchCheckpointReader.ReadStateDict(path));
    }

    [Fact]
    public async Task Download_verifies_the_hash_converts_and_leaves_no_scratch_folder()
    {
        var bytes = File.ReadAllBytes(WriteCheckpoint());
        var source = SourceFor(Hash(bytes)[..8]);
        var root = Path.Combine(_root, "pretrained");
        var downloader = new PretrainedWeightsDownloader(FakeClient(bytes), () => root);
        var progress = new List<double>();

        var target = await downloader.DownloadAsync(source, new InlineProgress(progress.Add));

        Assert.Equal(Path.Combine(root, "resnet18"), target);
        Assert.True(File.Exists(Path.Combine(target, "index.json")));
        Assert.Equal(new[] { "resnet18" }, Directory.GetDirectories(root).Select(Path.GetFileName));
        Assert.Contains(1.0, progress);
    }

    [Fact]
    public async Task Local_conversion_preserves_the_checkpoint_and_an_existing_bundle()
    {
        var checkpoint = WriteCheckpoint();
        var source = SourceFor("00000000");
        var root = Path.Combine(_root, "pretrained");
        using var client = FakeClient([]);
        var downloader = new PretrainedWeightsDownloader(client, () => root);

        var target = await downloader.DownloadAsync(source, sourcePath: checkpoint);
        var existingFile = Path.Combine(target, "existing.txt");
        File.WriteAllText(existingFile, "keep");
        await downloader.DownloadAsync(source, sourcePath: checkpoint);

        Assert.True(File.Exists(checkpoint));
        Assert.Equal("keep", File.ReadAllText(existingFile));
        Assert.Equal(new[] { "resnet18" }, Directory.GetDirectories(root).Select(Path.GetFileName));
    }

    [Fact]
    public async Task Download_with_a_wrong_hash_fails_and_installs_nothing()
    {
        var bytes = File.ReadAllBytes(WriteCheckpoint());
        var source = SourceFor("00000000");
        var root = Path.Combine(_root, "pretrained");
        var downloader = new PretrainedWeightsDownloader(FakeClient(bytes), () => root);

        await Assert.ThrowsAsync<InvalidDataException>(() => downloader.DownloadAsync(source));

        Assert.Empty(Directory.GetDirectories(root));
    }

    [Fact]
    public void Catalog_maps_internal_arch_names()
    {
        Assert.Equal("resnet18", PretrainedWeightsCatalog.ForArch("resnet18_v2")!.Arch);
        Assert.Equal("resnet18", PretrainedWeightsCatalog.ForArch("resnet_tiny_v2")!.Arch);
        Assert.Equal("resnet34", PretrainedWeightsCatalog.ForArch("resnet34_v2")!.Arch);
        Assert.Null(PretrainedWeightsCatalog.ForArch("unknown-net"));
        Assert.Null(PretrainedWeightsCatalog.ForArch(null));
    }

    // ---- helpers ------------------------------------------------------------------------------

    private static PretrainedWeightsSource SourceFor(string hashPrefix) =>
        new("resnet18", "ResNet-18", "https://example.invalid/resnet18.pth", hashPrefix, 1);

    private static string Hash(byte[] bytes) => Convert.ToHexString(SHA256.HashData(bytes)).ToLowerInvariant();

    private static HttpClient FakeClient(byte[] payload) => new(new StubHandler(payload));

    private sealed class InlineProgress(Action<double> action) : IProgress<double>
    {
        public void Report(double value) => action(value);
    }

    private sealed class StubHandler(byte[] payload) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken) =>
            Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK) { Content = new ByteArrayContent(payload) });
    }

    private static float[] ToFloats(byte[] data)
    {
        var result = new float[data.Length / 4];
        Buffer.BlockCopy(data, 0, result, 0, data.Length);
        return result;
    }

    /// <summary>A minimal torch.save-style archive: three tensors over two storages.</summary>
    private string WriteCheckpoint()
    {
        var path = Path.Combine(_root, $"{Guid.NewGuid():N}.pth");
        using (var archive = ZipFile.Open(path, ZipArchiveMode.Create))
        {
            var pickle = new PickleWriter();
            pickle.Start();
            pickle.Tensor("layer.weight", "FloatStorage", "0", 4, size: [2, 2], stride: [2, 1]);
            pickle.Tensor("layer.transposed", "FloatStorage", "0", 4, size: [2, 2], stride: [1, 2]);
            pickle.Tensor("bn.num_batches_tracked", "LongStorage", "1", 1, size: [], stride: []);
            Write(archive, "archive/data.pkl", pickle.Finish());
            Write(
                archive,
                "archive/data/0",
                [.. BitConverter.GetBytes(1f), .. BitConverter.GetBytes(2f), .. BitConverter.GetBytes(3f), .. BitConverter.GetBytes(4f)]);
            Write(archive, "archive/data/1", BitConverter.GetBytes(7L));
        }

        return path;
    }

    private static void Write(ZipArchive archive, string name, byte[] data)
    {
        using var stream = archive.CreateEntry(name).Open();
        stream.Write(data);
    }

    /// <summary>Emits the protocol-2 opcodes torch.save uses for an OrderedDict of tensors.</summary>
    private sealed class PickleWriter
    {
        private readonly List<byte> _bytes = [];

        public void Start()
        {
            _bytes.AddRange([0x80, 0x02]);
            Global("collections", "OrderedDict");
            _bytes.Add((byte)')');
            _bytes.Add((byte)'R');
            _bytes.Add((byte)'(');
        }

        public void Tensor(string name, string storageType, string key, long numel, long[] size, long[] stride)
        {
            Str(name);
            Global("torch._utils", "_rebuild_tensor_v2");
            _bytes.Add((byte)'(');
            // persistent id: ('storage', <type>, key, 'cpu', numel)
            _bytes.Add((byte)'(');
            Str("storage");
            Global("torch", storageType);
            Str(key);
            Str("cpu");
            Int(numel);
            _bytes.Add((byte)'t');
            _bytes.Add((byte)'Q');
            Int(0); // storage offset
            LongTuple(size);
            LongTuple(stride);
            _bytes.Add(0x89); // requires_grad = False
            _bytes.Add((byte)')'); // backward hooks placeholder
            _bytes.Add((byte)'t');
            _bytes.Add((byte)'R');
        }

        public byte[] Finish()
        {
            _bytes.Add((byte)'u');
            _bytes.Add((byte)'.');
            return [.. _bytes];
        }

        private void Global(string module, string name) =>
            _bytes.AddRange(Encoding.ASCII.GetBytes($"c{module}\n{name}\n"));

        private void Str(string value)
        {
            var utf8 = Encoding.UTF8.GetBytes(value);
            _bytes.Add((byte)'X');
            _bytes.AddRange(BitConverter.GetBytes(utf8.Length));
            _bytes.AddRange(utf8);
        }

        private void Int(long value)
        {
            _bytes.Add((byte)'J');
            _bytes.AddRange(BitConverter.GetBytes((int)value));
        }

        private void LongTuple(long[] values)
        {
            _bytes.Add((byte)'(');
            foreach (var value in values)
            {
                Int(value);
            }

            _bytes.Add((byte)'t');
        }
    }
}
