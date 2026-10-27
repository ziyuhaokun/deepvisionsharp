namespace DeepVisionSharp.Domain.ValueObjects;

public sealed class ProjectBusyException(string message) : InvalidOperationException(message);
