namespace RuntimePackage.Engine.Packaging.Build;

public enum BuildStage
{
    None = 0,
    InputValidation,
    RuntimeScan,
    CopyRuntimeToStaging,
    CreateRuntimeZip,
    CopyAssetsAndMetadata,
    GenerateManifest,
    AssemblePackage,
    CreateSetupBundle,
    CleanupAndFinalize
}
