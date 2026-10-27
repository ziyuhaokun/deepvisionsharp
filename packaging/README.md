# DeepVisionSharp installer

From the project root, build the Windows x64 single-file installer:

```powershell
.\scripts\build_installer.ps1
```

The build publishes the application as self-contained, publishes the RuntimePackage SetupHost, and bundles the application payload into `scripts/Output/DeepVisionSharp-{version}-x64.exe`. It does not install or launch the application.

## Build prerequisites

- Windows x64 and the .NET 10 SDK, with access to the required NuGet packages.
- The RuntimePackage source is included in this repository under `packaging/installer/` (Engine, CLI and SetupHost).

The default build uses `packaging/installer/`. Missing local tools fail before application publishing. To use a different tool source tree:

```powershell
.\scripts\build_installer.ps1 -PackagerRepo C:\tools\RuntimePackage
```

This is a .NET application package (`runtime-type=app`), not a Python runtime package. Python, Conda and Ultralytics initialization weights are not build prerequisites or bundled application content.

## Parameters and compatibility

- `-Configuration Release|Debug`: applies to the application, SetupHost and packager CLI; default `Release`.
- `-RuntimeIdentifier win-x64`: only x64 is supported by the current native dependencies.
- `-PackagerRepo`: optional tool source directory; defaults to `packaging/installer/`. `-PythonRepo` is kept as an alias.
- `-Shell src`: the WPF application; `-Shell wpf` remains a deprecated alias. The removed Blazor shell is not accepted.

Application publish output is `src/DeepVisionSharp.Shell/bin/{Configuration}/net10.0-windows/win-x64/publish`. The script cleans only that selected publish directory before publishing. The default SetupHost output is `packaging/installer/artifacts/setuphost/win-x64` (ignored by Git).

## Local tool source and verification

`packaging/installer/Installer.slnx` contains the three tool projects and package regression tests. It is separate from the six-project product solution; no application project references these tools. The projects inherit the repository's language, warnings-as-errors and NuGet audit settings.

```powershell
dotnet build packaging/installer/Installer.slnx -c Release
dotnet test packaging/installer/tests/RuntimePackage.Engine.Tests/RuntimePackage.Engine.Tests.csproj -c Release
```

The installer tool source lives in this repository. The existing `.vrp` format and install/uninstall implementation are retained. Python runtime builder UI and SetupBootstrapper projects are not included.

## Product identity and inputs

- Product name: `DeepVisionSharp`.
- Package ID: `vision.deepvisionsharp`.
- Entry point: `DeepVisionSharp.exe`.
- Default install directory: `%LocalAppData%\DeepVisionSharp` (per-user, no administrator required).
- Version: read from the Shell project; written to `packaging/version/version.txt` during packaging.
- `packaging/assets/license.txt`: installer license text.
- `packaging/assets/logo.ico`: installer executable icon.
- `packaging/assets/logo.png`: wizard sidebar banner.
- Root `CHANGELOG.md` and Shell `license.txt`: copied into the application publish directory. `CHANGELOG.Developer.md` stays in the source checkout.

The self-contained payload includes .NET and native TorchSharp/LibTorch and OpenCV dependencies. NVIDIA drivers remain a machine prerequisite for GPU execution, not installer content.

## Optional external weight verification

`scripts/verify_model_weights.ps1` is retained as a standalone compatibility utility for an external Ultralytics weight collection. It is not part of this application's build or packaging flow. Supply a directory containing `manifest.json` and its declared weight files:

```powershell
.\scripts\verify_model_weights.ps1 -WeightsDir C:\model-weights -Preset core
```

`-Preset full` checks all manifest entries. Family subdirectories and legacy flat filenames remain supported. Missing files produce exit code `2`; a complete collection produces `0`. An omitted directory or missing manifest is an error. The utility does not download files.
