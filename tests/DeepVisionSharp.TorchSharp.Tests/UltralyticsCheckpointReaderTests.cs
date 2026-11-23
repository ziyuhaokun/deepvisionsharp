using System.IO.Compression;
using System.Net;
using System.Net.Http;
using System.Security.Cryptography;
using System.Text;
using Xunit;
using DeepVisionSharp.TorchSharp.Classification.Pretrained;

namespace DeepVisionSharp.TorchSharp.Tests;

/// <summary>
/// Ultralytics 的 .pt 是整棵 nn.Module 的 pickle：类对象不能也不该被还原，只保留属性，
/// 再按 _parameters / _buffers / _modules 走一遍。这里用手写的最小 .pt 验证这条路径，不联网。
/// </summary>
public sealed class UltralyticsCheckpointReaderTests : IDisposable
{
    private readonly string _root;

    public UltralyticsCheckpointReaderTests()
    {
        _root = Path.Combine(Path.GetTempPath(), "dv-ultralytics-tests", Guid.NewGuid().ToString("N"));
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
    public void Module_tree_is_flattened_like_state_dict_with_buffers_and_skipping_missing_parameters()
    {
        var path = WriteCheckpoint(emaValue: null, modelValue: 1f);

        var tensors = TorchCheckpointReader.ReadModuleStateDict(path).ToDictionary(t => t.Name);

        Assert.Equal(
            new[] { "model.0.bn.num_batches_tracked", "model.0.conv.weight" },
            tensors.Keys.OrderBy(name => name, StringComparer.Ordinal));
        Assert.Equal(new[] { 1f, 1f, 1f, 1f }, ToFloats(tensors["model.0.conv.weight"].Data));
        Assert.Equal("int64", tensors["model.0.bn.num_batches_tracked"].Dtype);
        // The conv's `bias` is None in the pickle and must not show up.
        Assert.DoesNotContain("model.0.conv.bias", tensors.Keys);
    }

    [Fact]
    public void Ema_copy_wins_over_model_when_both_exist()
    {
        var path = WriteCheckpoint(emaValue: 9f, modelValue: 1f);

        var tensors = TorchCheckpointReader.ReadModuleStateDict(path).ToDictionary(t => t.Name);

        Assert.Equal(new[] { 9f, 9f, 9f, 9f }, ToFloats(tensors["model.0.conv.weight"].Data));
    }

    [Fact]
    public void Checkpoint_without_a_module_is_rejected()
    {
        var path = Path.Combine(_root, "empty.pt");
        using (var archive = ZipFile.Open(path, ZipArchiveMode.Create))
        {
            var pickle = new Pickle();
            pickle.Start();
            pickle.Dict(() => pickle.Str("epoch"), () => pickle.Int(3));
            Write(archive, "empty/data.pkl", pickle.Finish());
        }

        Assert.Throws<InvalidDataException>(() => TorchCheckpointReader.ReadModuleStateDict(path));
    }

    [Fact]
    public async Task Ultralytics_source_downloads_verifies_and_converts()
    {
        var bytes = File.ReadAllBytes(WriteCheckpoint(emaValue: null, modelValue: 1f));
        var hash = Convert.ToHexString(SHA256.HashData(bytes)).ToLowerInvariant();
        var source = new PretrainedWeightsSource(
            "yolo26n", "YOLO26-N 检测", "https://example.invalid/yolo26n.pt", hash, 1, PretrainedCheckpointFormat.UltralyticsCheckpoint);
        var root = Path.Combine(_root, "pretrained");
        var downloader = new PretrainedWeightsDownloader(
            new HttpClient(new StubHandler(bytes)), () => root);

        var target = await downloader.DownloadAsync(source);

        Assert.Equal(Path.Combine(root, "yolo26n"), target);
        using var bundle = PretrainedWeightsBundle.Load(target);
        Assert.Equal(2, bundle.Tensors.Count);
        Assert.True(bundle.Tensors.ContainsKey("model.0.conv.weight"));
    }

    [Fact]
    public void Catalog_knows_every_yolo26_scale_for_detection_and_classification()
    {
        foreach (var scale in new[] { "n", "s", "m", "l", "x" })
        {
            var detection = PretrainedWeightsCatalog.ForArch($"yolo26-{scale}-det");
            var detectionAlias = PretrainedWeightsCatalog.ForArch($"yolo26{scale}-det");
            var classification = PretrainedWeightsCatalog.ForArch($"yolo26{scale}-cls");

            Assert.Equal($"yolo26{scale}", detection!.Arch);
            Assert.Equal(detection, detectionAlias);
            Assert.Equal($"yolo26{scale}-cls", classification!.Arch);
            Assert.All(new[] { detection, classification }, item =>
            {
                Assert.Equal(PretrainedCheckpointFormat.UltralyticsCheckpoint, item.Format);
                Assert.Equal(64, item.Sha256Prefix.Length); // the full hash, not a prefix
                Assert.StartsWith("https://github.com/ultralytics/assets/releases/download/v8.4.0/", item.Url);
            });
        }

        Assert.Null(PretrainedWeightsCatalog.ForArch("yolo26q-det"));
        Assert.Equal(12, PretrainedWeightsCatalog.All.Count);
    }

    // ---- helpers ------------------------------------------------------------------------------

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

    private static void Write(ZipArchive archive, string name, byte[] data)
    {
        using var stream = archive.CreateEntry(name).Open();
        stream.Write(data);
    }

    /// <summary>
    /// {"model": Net, "ema": Net | None} where Net.model.0 = Layer(conv.weight, conv.bias=None, bn.num_batches_tracked).
    /// Storages 0 (model weights), 1 (ema weights), 2 (the int64 counter).
    /// </summary>
    private string WriteCheckpoint(float? emaValue, float modelValue)
    {
        var path = Path.Combine(_root, $"{Guid.NewGuid():N}.pt");
        using (var archive = ZipFile.Open(path, ZipArchiveMode.Create))
        {
            var pickle = new Pickle();
            pickle.Start();
            pickle.Dict(
                () => pickle.Str("model"),
                () => Net(pickle, "0"),
                () => pickle.Str("ema"),
                () => { if (emaValue is null) pickle.None(); else Net(pickle, "1"); });
            Write(archive, "yolo/data.pkl", pickle.Finish());
            Write(archive, "yolo/data/0", Floats(modelValue));
            Write(archive, "yolo/data/1", Floats(emaValue ?? 0f));
            Write(archive, "yolo/data/2", BitConverter.GetBytes(5L));
        }

        return path;
    }

    private static byte[] Floats(float value) => [.. Enumerable.Repeat(value, 4).SelectMany(BitConverter.GetBytes)];

    private static void Net(Pickle pickle, string weightStorage)
    {
        // ClassificationModel { _parameters: {}, _buffers: {}, _modules: { model: Sequential { _modules: { "0": Layer } } } }
        pickle.Object(
            "ultralytics.nn.tasks",
            "ClassificationModel",
            () => pickle.Dict(
                () => pickle.Str("_parameters"), () => pickle.Dict(),
                () => pickle.Str("_buffers"), () => pickle.Dict(),
                () => pickle.Str("_modules"),
                () => pickle.Dict(
                    () => pickle.Str("model"),
                    () => pickle.Object(
                        "torch.nn.modules.container",
                        "Sequential",
                        () => pickle.Dict(
                            () => pickle.Str("_modules"),
                            () => pickle.Dict(
                                () => pickle.Str("0"),
                                () => Layer(pickle, weightStorage)))))));
    }

    private static void Layer(Pickle pickle, string weightStorage)
    {
        // Layer { _modules: { conv: Holder{_parameters{weight, bias=None}}, bn: Holder{_buffers{num_batches_tracked}} } }
        pickle.Object(
            "ultralytics.nn.modules.conv",
            "Conv",
            () => pickle.Dict(
                () => pickle.Str("_modules"),
                () => pickle.Dict(
                    () => pickle.Str("conv"),
                    () => pickle.Object(
                        "torch.nn.modules.conv",
                        "Conv2d",
                        () => pickle.Dict(
                            () => pickle.Str("_parameters"),
                            () => pickle.Dict(
                                () => pickle.Str("weight"),
                                () => pickle.Parameter(weightStorage, "FloatStorage", 4, [2, 2], [2, 1]),
                                () => pickle.Str("bias"),
                                () => pickle.None()))),
                    () => pickle.Str("bn"),
                    () => pickle.Object(
                        "torch.nn.modules.batchnorm",
                        "BatchNorm2d",
                        () => pickle.Dict(
                            () => pickle.Str("_buffers"),
                            () => pickle.Dict(
                                () => pickle.Str("num_batches_tracked"),
                                () => pickle.Tensor("2", "LongStorage", 1, [], [])))))));
    }

    /// <summary>Protocol-2 opcodes, enough for objects, dicts and tensors.</summary>
    private sealed class Pickle
    {
        private readonly List<byte> _bytes = [];

        public void Start() => _bytes.AddRange([0x80, 0x02]);

        public byte[] Finish()
        {
            _bytes.Add((byte)'.');
            return [.. _bytes];
        }

        public void None() => _bytes.Add((byte)'N');

        /// <summary>A dict from alternating key / value writers.</summary>
        public void Dict(params Action[] keyValueWriters)
        {
            _bytes.Add((byte)'}');
            if (keyValueWriters.Length == 0)
            {
                return;
            }

            _bytes.Add((byte)'(');
            foreach (var write in keyValueWriters)
            {
                write();
            }

            _bytes.Add((byte)'u');
        }

        /// <summary>cls.__new__(cls) then BUILD with the attribute dict, as pickle does for plain objects.</summary>
        public void Object(string module, string name, Action writeState)
        {
            Global(module, name);
            _bytes.Add((byte)')');
            _bytes.Add(0x81); // NEWOBJ
            writeState();
            _bytes.Add((byte)'b'); // BUILD
        }

        /// <summary>torch._utils._rebuild_parameter(tensor, requires_grad, hooks)</summary>
        public void Parameter(string key, string storageType, long numel, long[] size, long[] stride)
        {
            Global("torch._utils", "_rebuild_parameter");
            _bytes.Add((byte)'(');
            Tensor(key, storageType, numel, size, stride);
            _bytes.Add(0x88); // requires_grad = True
            _bytes.Add((byte)')'); // hooks
            _bytes.Add((byte)'t');
            _bytes.Add((byte)'R');
        }

        public void Tensor(string key, string storageType, long numel, long[] size, long[] stride)
        {
            Global("torch._utils", "_rebuild_tensor_v2");
            _bytes.Add((byte)'(');
            _bytes.Add((byte)'(');
            Str("storage");
            Global("torch", storageType);
            Str(key);
            Str("cpu");
            Int(numel);
            _bytes.Add((byte)'t');
            _bytes.Add((byte)'Q');
            Int(0);
            LongTuple(size);
            LongTuple(stride);
            _bytes.Add(0x89);
            _bytes.Add((byte)')');
            _bytes.Add((byte)'t');
            _bytes.Add((byte)'R');
        }

        public void Str(string value)
        {
            var utf8 = Encoding.UTF8.GetBytes(value);
            _bytes.Add((byte)'X');
            _bytes.AddRange(BitConverter.GetBytes(utf8.Length));
            _bytes.AddRange(utf8);
        }

        public void Int(long value)
        {
            _bytes.Add((byte)'J');
            _bytes.AddRange(BitConverter.GetBytes((int)value));
        }

        private void Global(string module, string name) =>
            _bytes.AddRange(Encoding.ASCII.GetBytes($"c{module}\n{name}\n"));

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
