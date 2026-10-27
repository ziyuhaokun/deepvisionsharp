namespace RuntimePackage.Engine.Installation;

public sealed record ValidationResult(bool IsValid, IReadOnlyList<string> Errors);
