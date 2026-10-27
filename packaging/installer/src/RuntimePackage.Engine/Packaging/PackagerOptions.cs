using RuntimePackage.Engine.Models;

namespace RuntimePackage.Engine.Packaging;

public sealed class PackagerOptions
{
    public string RuntimeDirectory { get; set; } = string.Empty;
    public string AssetsDirectory { get; set; } = string.Empty;
    public string RequirementsDirectory { get; set; } = string.Empty;
    public string VersionDirectory { get; set; } = string.Empty;
    public string ProductName { get; set; } = "DeepVisionSharp";
    public string PackageId { get; set; } = "vision.deepvisionsharp";
    public string Version { get; set; } = string.Empty;
    public string Publisher { get; set; } = "ziyuhaokun";
    public string Architecture { get; set; } = "x64";
    public string RuntimeType { get; set; } = "app";
    public bool RequiresAdmin { get; set; }
    public string DefaultInstallDirectory { get; set; } = "%LocalAppData%\\DeepVisionSharp";
    public string OutputPackagePath { get; set; } = string.Empty;
    public List<EntryPointDescriptor>? EntryPoints { get; set; }
    public List<EnvironmentVariableDescriptor>? EnvironmentVariables { get; set; }
}
