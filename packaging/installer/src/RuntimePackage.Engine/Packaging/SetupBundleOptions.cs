namespace RuntimePackage.Engine.Packaging;

public sealed class SetupBundleOptions
{
    public PackagerOptions PackageOptions { get; set; } = new();
    public string SetupHostExecutablePath { get; set; } = string.Empty;
    public string BootstrapperProjectPath { get; set; } = string.Empty;
    public string OutputSetupPath { get; set; } = string.Empty;
    public bool KeepIntermediatePackage { get; set; }
}
