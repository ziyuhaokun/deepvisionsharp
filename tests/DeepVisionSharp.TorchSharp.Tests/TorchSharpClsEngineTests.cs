using OpenCvSharp;
using TorchSharp;
using Xunit;
using DeepVisionSharp.TorchSharp.Classification;
using DeepVisionSharp.TorchSharp.Device;
using DeepVisionSharp.TorchSharp.Models;

namespace DeepVisionSharp.TorchSharp.Tests;

/// <summary>
/// Engine contract tests over the real inference path: a tiny model is written to a temp folder with
/// the same sidecar the trainer produces, then loaded through <see cref="TorchSharpClsEngine"/>.
/// </summary>
public sealed class TorchSharpClsEngineTests : IDisposable
{
    private const string ModelFileName = "best.torchsharp";
    private readonly string _root;

    public TorchSharpClsEngineTests()
    {
        _root = Path.Combine(Path.GetTempPath(), "dv-engine-tests", Guid.NewGuid().ToString("N"));
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
    public async Task Warmup_releases_the_gate_even_when_the_model_cannot_be_loaded()
    {
        using var engine = new TorchSharpClsEngine();
        var missing = Path.Combine(_root, "absent.torchsharp");

        Assert.Throws<FileNotFoundException>(() => engine.Warmup(missing, "cpu"));

        // The lease must not leak: a later training run has to be able to take the gate.
        Assert.Equal(0, engine.Gate.InFlightInference);
        using var training = await engine.Gate.BeginTrainingAsync(TimeSpan.FromSeconds(1));
        Assert.True(engine.Gate.IsTrainingActive);
    }

    [Fact]
    public async Task Infer_is_rejected_while_training_holds_the_gate_before_any_native_load()
    {
        var model = WriteModel(classes: ["OK", "NG"], preprocessing: "unit_scale_v1");
        using var engine = new TorchSharpClsEngine();
        using var training = await engine.Gate.BeginTrainingAsync(TimeSpan.FromSeconds(1));

        // The refusal must come from the gate, not from a model that failed to load afterwards.
        Assert.Throws<InferenceBlockedByTrainingException>(() =>
            engine.Infer(model, "cpu", WriteImage(), 0.5));
    }

    [Fact]
    public void Warmup_loads_the_real_model_and_reports_it_was_not_cached()
    {
        var model = WriteModel(classes: ["OK", "NG"], preprocessing: "unit_scale_v1");
        using var engine = new TorchSharpClsEngine();

        Assert.False(engine.Warmup(model, "cpu"));
        Assert.Equal(1, engine.LoadedModelCount);

        // Second call finds it in the cache, so it reports "already warm".
        Assert.True(engine.Warmup(model, "cpu"));
        Assert.Equal(1, engine.LoadedModelCount);
    }

    [Fact]
    public void Infer_runs_the_whole_pipeline_and_returns_a_class_from_the_sidecar()
    {
        var model = WriteModel(classes: ["alpha", "beta", "gamma"], preprocessing: "unit_scale_v1");
        using var engine = new TorchSharpClsEngine();

        var result = engine.Infer(model, "cpu", WriteImage(), 0.0);

        Assert.NotNull(result.Detection);
        Assert.Contains(result.Detection!.ClassName, new[] { "alpha", "beta", "gamma" });
        // An untrained head still has to produce a probability in range.
        Assert.InRange(result.Detection.Confidence, 0.0, 1.0);
        Assert.Equal(8, result.ImageWidth);
        Assert.Equal(8, result.ImageHeight);
    }

    [Fact]
    public async Task Infer_registers_with_the_gate_for_its_whole_duration()
    {
        var model = WriteModel(classes: ["OK", "NG"], preprocessing: "unit_scale_v1");
        using var engine = new TorchSharpClsEngine();
        var image = WriteImage();

        var running = 0;
        var observed = 0;
        var probe = Task.Run(() =>
        {
            while (Volatile.Read(ref running) == 1)
            {
                observed = Math.Max(observed, engine.Gate.InFlightInference);
                Thread.Sleep(1);
            }
        });

        Volatile.Write(ref running, 1);
        var inference = Task.Run(() => engine.Infer(model, "cpu", image, 0.0));
        while (!inference.IsCompleted)
        {
            observed = Math.Max(observed, engine.Gate.InFlightInference);
            Thread.Sleep(1);
        }

        Volatile.Write(ref running, 0);
        await inference;
        await probe;

        Assert.True(observed >= 1, $"in-flight inference was never observed (max {observed})");
        Assert.Equal(0, engine.Gate.InFlightInference);
    }

    [Fact]
    public async Task BeginTraining_waits_for_a_real_inference_to_finish()
    {
        var model = WriteModel(classes: ["OK", "NG"], preprocessing: "unit_scale_v1");
        using var engine = new TorchSharpClsEngine();
        var image = WriteImage();

        var inference = Task.Run(() => engine.Infer(model, "cpu", image, 0.0));
        while (engine.Gate.InFlightInference == 0)
        {
            await Task.Delay(1);
        }

        var training = engine.Gate.BeginTrainingAsync(TimeSpan.FromSeconds(5));
        await Task.Delay(20);
        Assert.False(training.IsCompleted);

        await inference;
        using var lease = await training.WaitAsync(TimeSpan.FromSeconds(5));
        Assert.True(engine.Gate.IsTrainingActive);
    }

    [Fact]
    public void ClearCache_after_dispose_does_not_throw()
    {
        using var engine = new TorchSharpClsEngine();
        engine.Dispose();

        engine.ClearCache();
        engine.ClearCache();
    }

    [Fact]
    public void Infer_after_dispose_throws_object_disposed()
    {
        var model = WriteModel(classes: ["OK", "NG"], preprocessing: "unit_scale_v1");
        using var engine = new TorchSharpClsEngine();
        engine.Dispose();

        Assert.Throws<ObjectDisposedException>(() =>
            engine.Infer(model, "cpu", WriteImage(), 0.5));
    }

    [Fact]
    public void Warmup_rejects_a_sidecar_with_only_one_class()
    {
        // Weights hold a 2-class head but the sidecar claims one class. A single-class model would
        // silently return "no result" for every image, so the metadata must be refused.
        var model = WriteModel(classes: ["OK", "NG"], preprocessing: "unit_scale_v1", sidecarClasses: ["only"]);
        using var engine = new TorchSharpClsEngine();

        Assert.Throws<InvalidDataException>(() => engine.Warmup(model, "cpu"));
    }

    [Fact]
    public void Warmup_rejects_a_sidecar_without_metadata()
    {
        var model = Path.Combine(_root, "bare.torchsharp");
        File.WriteAllBytes(model, [1, 2, 3]);
        using var engine = new TorchSharpClsEngine();

        Assert.Throws<InvalidDataException>(() => engine.Warmup(model, "cpu"));
    }

    [Fact]
    public void ClearCache_frees_the_slot_but_keeps_the_engine_usable()
    {
        var model = WriteModel(classes: ["OK", "NG"], preprocessing: "unit_scale_v1");
        using var engine = new TorchSharpClsEngine();

        engine.Warmup(model, "cpu");
        Assert.Equal(1, engine.LoadedModelCount);

        engine.ClearCache();
        Assert.Equal(0, engine.LoadedModelCount);

        // Inference must still work; the model is simply loaded again.
        var result = engine.Infer(model, "cpu", WriteImage(), 0.0);
        Assert.NotNull(result.Detection);
    }

    /// <summary>
    /// Writes a real Module.save checkpoint plus the sidecar the trainer writes, using a tiny
    /// resnet-tiny head so the file is a few kilobytes.
    /// </summary>
    private string WriteModel(
        IReadOnlyList<string> classes,
        string preprocessing,
        IReadOnlyList<string>? sidecarClasses = null)
    {
        var path = Path.Combine(_root, ModelFileName);
        using (var model = TorchSharpClassifierFactory.Create("resnet-tiny", inputChannels: 3, numClasses: classes.Count, imageSize: 8))
        {
            model.save(path);
        }

        var infoPath = path + ".info";
        var declared = sidecarClasses ?? classes;
        var sidecar = new Dictionary<string, object?>
        {
            ["schema_version"] = 2,
            ["artifact_kind"] = "trained_model",
            ["model_type"] = "resnet18",
            ["classes"] = declared.ToList(),
            ["num_classes"] = declared.Count,
            ["max_detections"] = 1,
            ["input_size"] = 8,
            ["input_width"] = 8,
            ["input_height"] = 8,
            ["input_channels"] = 3,
            ["weights_format"] = "torchsharp",
            ["preprocessing"] = preprocessing,
            ["arch"] = "resnet-tiny"
        };
        File.WriteAllText(infoPath, System.Text.Json.JsonSerializer.Serialize(sidecar));
        return path;
    }

    private string WriteImage()
    {
        var path = Path.Combine(_root, "sample.png");
        using (var mat = new Mat(8, 8, MatType.CV_8UC3, new OpenCvSharp.Scalar(90, 120, 160)))
        {
            Cv2.ImWrite(path, mat);
        }

        return path;
    }
}
