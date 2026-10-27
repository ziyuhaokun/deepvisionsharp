using RuntimePackage.Engine.Models;

namespace RuntimePackage.Engine.Installation;

public sealed record PackageReadResult(string PackagePath, string ExtractedRoot, PackageManifest Manifest);
