using DeepVisionSharp.TorchSharp.Device;
using Xunit;

namespace DeepVisionSharp.TorchSharp.Tests;

public sealed class InferenceTrainingGateTests
{
    [Fact]
    public async Task Training_timeout_releases_gate_and_does_not_overlap_inference()
    {
        var gate = new InferenceTrainingGate();
        using var inference = gate.EnterInference();

        await Assert.ThrowsAsync<TimeoutException>(() =>
            gate.BeginTrainingAsync(TimeSpan.FromMilliseconds(20)));

        Assert.False(gate.IsTrainingActive);
        Assert.Equal(1, gate.InFlightInference);

        inference.Dispose();
        using var training = await gate.BeginTrainingAsync(TimeSpan.FromSeconds(1));
        Assert.True(gate.IsTrainingActive);
    }

    [Fact]
    public async Task Training_waits_until_inference_lease_is_released()
    {
        var gate = new InferenceTrainingGate();
        using var inference = gate.EnterInference();
        // Generous timeout: this test checks waiting, so a starved thread pool must not time it out.
        var trainingTask = gate.BeginTrainingAsync(TimeSpan.FromSeconds(30));

        await Task.Delay(20);
        Assert.False(trainingTask.IsCompleted);
        Assert.True(gate.IsTrainingActive);

        inference.Dispose();
        using var training = await trainingTask;
        Assert.True(gate.IsTrainingActive);
    }
}
