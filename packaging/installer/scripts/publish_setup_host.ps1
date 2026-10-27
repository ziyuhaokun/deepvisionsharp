param(
    [string]$Configuration = "Release",
    [string]$RuntimeIdentifier = "win-x64",
    [string]$PackagePath = "",
    [string]$OutputDirectory = "",
    [string]$SetupFileNamePrefix = "DeepVisionSharp",
    [string]$ApplicationIcon = ""
)

$ErrorActionPreference = "Stop"

function Read-VrpManifestVersion {
    param(
        [Parameter(Mandatory = $true)]
        [string]$Path
    )

    Add-Type -AssemblyName System.IO.Compression.FileSystem

    $resolvedPath = (Resolve-Path -LiteralPath $Path).ProviderPath
    $archive = [System.IO.Compression.ZipFile]::OpenRead($resolvedPath)
    try {
        $entry = $archive.GetEntry("manifest.json")
        if ($null -eq $entry) {
            throw "manifest.json not found in package: $resolvedPath"
        }

        $reader = [System.IO.StreamReader]::new($entry.Open())
        try {
            $manifest = $reader.ReadToEnd() | ConvertFrom-Json
        } finally {
            $reader.Dispose()
        }

        $version = [string]$manifest.version
        if ([string]::IsNullOrWhiteSpace($version)) {
            throw "manifest.version is empty in package: $resolvedPath"
        }

        return $version.Trim()
    } finally {
        $archive.Dispose()
    }
}

function ConvertTo-SafeFileNamePart {
    param(
        [Parameter(Mandatory = $true)]
        [string]$Value
    )

    $invalidChars = [System.IO.Path]::GetInvalidFileNameChars()
    $safeValue = $Value
    foreach ($char in $invalidChars) {
        $safeValue = $safeValue.Replace($char, "_")
    }

    $safeValue = $safeValue.Trim()
    if ([string]::IsNullOrWhiteSpace($safeValue)) {
        return "unknown"
    }

    return $safeValue
}

$repoRoot = Resolve-Path (Join-Path $PSScriptRoot "..")
$projectPath = Join-Path $repoRoot "src\RuntimePackage.SetupHost.Wpf\RuntimePackage.SetupHost.Wpf.csproj"
$outputPath = Join-Path $repoRoot "artifacts\setuphost\$RuntimeIdentifier"

$publishArgs = @(
    "publish", $projectPath,
    "-c", $Configuration,
    "-r", $RuntimeIdentifier,
    "--self-contained", "true",
    "-p:PublishSingleFile=true",
    "-p:EnableCompressionInSingleFile=true",
    "-p:IncludeNativeLibrariesForSelfExtract=true",
    "-o", $outputPath
)

if (-not [string]::IsNullOrWhiteSpace($ApplicationIcon)) {
    if (-not (Test-Path -LiteralPath $ApplicationIcon)) {
        throw "ApplicationIcon not found: $ApplicationIcon"
    }
    $resolvedIcon = (Resolve-Path -LiteralPath $ApplicationIcon).ProviderPath
    $publishArgs += "-p:ApplicationIcon=$resolvedIcon"
    Write-Host "Using ApplicationIcon: $resolvedIcon"
}

dotnet @publishArgs
if ($LASTEXITCODE -ne 0) {
    throw "SetupHost publish failed with exit code $LASTEXITCODE"
}

Write-Host "SetupHost published to $outputPath"

if (-not [string]::IsNullOrWhiteSpace($PackagePath)) {
    if (-not (Test-Path -LiteralPath $PackagePath)) {
        throw "Package not found: $PackagePath"
    }

    if ([string]::IsNullOrWhiteSpace($OutputDirectory)) {
        $OutputDirectory = Join-Path $repoRoot "output"
    }

    New-Item -ItemType Directory -Path $OutputDirectory -Force | Out-Null

    $setupHostExecutablePath = Join-Path $outputPath "RuntimePackage.SetupHost.Wpf.exe"
    if (-not (Test-Path -LiteralPath $setupHostExecutablePath)) {
        throw "Published SetupHost not found: $setupHostExecutablePath"
    }

    $version = Read-VrpManifestVersion -Path $PackagePath
    $safeVersion = ConvertTo-SafeFileNamePart -Value $version
    $baseFileName = "$SetupFileNamePrefix`_$safeVersion"
    $setupOutputPath = Join-Path $OutputDirectory "$baseFileName.exe"
    $packageOutputPath = Join-Path $OutputDirectory "$baseFileName.vrp"

    Copy-Item -LiteralPath $setupHostExecutablePath -Destination $setupOutputPath -Force

    $sourcePackagePath = (Resolve-Path -LiteralPath $PackagePath).ProviderPath
    $targetPackagePath = [System.IO.Path]::GetFullPath($packageOutputPath)
    if (-not [string]::Equals($sourcePackagePath, $targetPackagePath, [System.StringComparison]::OrdinalIgnoreCase)) {
        Copy-Item -LiteralPath $sourcePackagePath -Destination $packageOutputPath -Force
    }

    Write-Host "Setup generated from package manifest version $version"
    Write-Host "Setup: $setupOutputPath"
    Write-Host "Package: $packageOutputPath"
}
