using Xunit;

namespace DeepVisionSharp.TorchSharp.Tests;

/// <summary>
/// The generated training tests share TorchSharp's process-global RNG.
/// They run one at a time, and not beside the rest of the suite, so a seed
/// placed just before model creation is not consumed by another test.
/// </summary>
[CollectionDefinition(TorchSharpSlowCollection.Name, DisableParallelization = true)]
public sealed class TorchSharpSlowCollection
{
    public const string Name = "TorchSharp slow training";
}
