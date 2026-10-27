param(
    [ValidateSet("src", "wpf")]
    [string]$Shell = "src",
    [Alias("PythonRepo")]
    [string]$PackagerRepo = "",
    [ValidateSet("Release", "Debug")]
    [string]$Configuration = "Release",
    [ValidateSet("win-x64")]
    [string]$RuntimeIdentifier = "win-x64"
)

$ErrorActionPreference = "Stop"

if ($Shell -eq "wpf") {
    Write-Warning "Parameter -Shell wpf is deprecated; use -Shell src instead."
    $Shell = "src"
}

$scriptDir = $PSScriptRoot
$repoRoot = (Resolve-Path (Join-Path $scriptDir "..")).Path

if ([string]::IsNullOrWhiteSpace($PackagerRepo)) {
    $PackagerRepo = Join-Path $repoRoot "packaging\installer"
}

if (-not (Test-Path -LiteralPath $PackagerRepo -PathType Container)) {
    throw "RuntimePackage tools not found: $PackagerRepo. Restore packaging\installer or specify -PackagerRepo."
}
$PackagerRepo = (Resolve-Path -LiteralPath $PackagerRepo).Path

$projectDir = Join-Path $repoRoot "src\DeepVisionSharp.Shell"
$csprojPath = Join-Path $projectDir "DeepVisionSharp.Shell.csproj"
$publishDir = Join-Path $projectDir "bin\$Configuration\net10.0-windows\$RuntimeIdentifier\publish"
$outputDir = Join-Path $scriptDir "Output"
$assetsDir = Join-Path $repoRoot "packaging\assets"
$versionDir = Join-Path $repoRoot "packaging\version"
$packagerProject = Join-Path $PackagerRepo "src\RuntimePackage.Packager.Cli\RuntimePackage.Packager.Cli.csproj"
$publishSetupHostScript = Join-Path $PackagerRepo "scripts\publish_setup_host.ps1"
$setupHostPath = Join-Path $PackagerRepo "artifacts\setuphost\$RuntimeIdentifier\RuntimePackage.SetupHost.Wpf.exe"

Write-Host "Building installer for shell: $Shell"
Write-Host "Project: $csprojPath"
Write-Host "Installer tools: $PackagerRepo"
Write-Host "Configuration: $Configuration; Runtime: $RuntimeIdentifier"

if (-not (Test-Path $projectDir)) {
    Write-Error "Project directory not found: $projectDir"
}

if (-not (Test-Path $csprojPath)) {
    Write-Error "Project file not found: $csprojPath"
}

if (-not (Test-Path $packagerProject)) {
    Write-Error "Packager CLI not found: $packagerProject"
}

if (-not (Test-Path -LiteralPath $publishSetupHostScript -PathType Leaf)) {
    throw "SetupHost publish script not found: $publishSetupHostScript"
}

if (-not (Test-Path (Join-Path $assetsDir "license.txt"))) {
    Write-Error "Missing packaging asset: $assetsDir\license.txt"
}

if (-not (Test-Path (Join-Path $assetsDir "logo.ico"))) {
    Write-Error "Missing packaging asset: $assetsDir\logo.ico"
}

Write-Host "--------------------------------------------------"
Write-Host "Step 1: Publishing DeepVisionSharp..."
Write-Host "--------------------------------------------------"

if (Test-Path $publishDir) {
    $resolvedPublishDir = (Resolve-Path -LiteralPath $publishDir).ProviderPath
    $expectedPublishDir = [IO.Path]::GetFullPath(
        (Join-Path $projectDir "bin\$Configuration\net10.0-windows\$RuntimeIdentifier\publish"))
    if (-not [string]::Equals($resolvedPublishDir, $expectedPublishDir, [StringComparison]::OrdinalIgnoreCase) `
        -or ((Get-Item -LiteralPath $resolvedPublishDir -Force).Attributes -band [IO.FileAttributes]::ReparsePoint)) {
        throw "Refusing to clean unexpected publish directory: $resolvedPublishDir"
    }
    Write-Host "Cleaning old publish directory..."
    Remove-Item -LiteralPath $resolvedPublishDir -Recurse -Force
}

$publishArgs = @(
    "publish",
    $csprojPath,
    "-c", $Configuration,
    "-r", $RuntimeIdentifier,
    "--self-contained", "true",
    "-o", $publishDir
)

Write-Host "Running: dotnet $($publishArgs -join ' ')"
& dotnet @publishArgs

if ($LASTEXITCODE -ne 0) {
    Write-Error "dotnet publish failed with exit code $LASTEXITCODE"
}

Write-Host "Publish completed successfully."

foreach ($file in @("DeepVisionSharp.exe", "DeepVisionSharp.dll", "CHANGELOG.md", "license.txt")) {
    if (-not (Test-Path -LiteralPath (Join-Path $publishDir $file) -PathType Leaf)) {
        throw "Required application file missing from publish output: $file"
    }
}

$version = "0.0.1"
if (Test-Path $csprojPath) {
    try {
        [xml]$projectXml = Get-Content $csprojPath -Encoding UTF8
        $propertyGroup = $projectXml.Project.PropertyGroup | Select-Object -First 1

        if ($null -ne $propertyGroup) {
            if (-not [string]::IsNullOrWhiteSpace($propertyGroup.Version)) {
                $version = $propertyGroup.Version.Trim()
            }
            elseif (-not [string]::IsNullOrWhiteSpace($propertyGroup.AssemblyVersion)) {
                $version = $propertyGroup.AssemblyVersion.Trim()
            }
            elseif (-not [string]::IsNullOrWhiteSpace($propertyGroup.FileVersion)) {
                $version = $propertyGroup.FileVersion.Trim()
            }
        }
    }
    catch {
        Write-Warning "Failed to parse version from csproj, using fallback version $version"
    }
}

Write-Host "Using Version: $version"
New-Item -ItemType Directory -Path $versionDir -Force | Out-Null
[IO.File]::WriteAllText((Join-Path $versionDir "version.txt"), $version, [Text.UTF8Encoding]::new($false))
New-Item -ItemType Directory -Path $outputDir -Force | Out-Null

Write-Host "--------------------------------------------------"
Write-Host "Step 2: Publishing SetupHost..."
Write-Host "--------------------------------------------------"

$setupHostIcon = Join-Path $assetsDir "logo.ico"
& $publishSetupHostScript `
    -Configuration $Configuration `
    -RuntimeIdentifier $RuntimeIdentifier `
    -ApplicationIcon $setupHostIcon
if ($LASTEXITCODE -ne 0) {
    Write-Error "SetupHost publish failed with exit code $LASTEXITCODE"
}

if (-not (Test-Path -LiteralPath $setupHostPath)) {
    Write-Error "SetupHost executable not found: $setupHostPath"
}

$setupPath = Join-Path $outputDir "DeepVisionSharp-$version-x64.exe"

Write-Host "--------------------------------------------------"
Write-Host "Step 3: Building single-file installer..."
Write-Host "--------------------------------------------------"

& dotnet run --project $packagerProject -c $Configuration -- `
    --runtime $publishDir `
    --assets $assetsDir `
    --version-dir $versionDir `
    --version $version `
    --runtime-type app `
    --package-id vision.deepvisionsharp `
    --product-name "DeepVisionSharp" `
    --publisher ziyuhaokun `
    --default-install-dir "%LocalAppData%\DeepVisionSharp" `
    --entry-point "DeepVisionSharp.exe" `
    --setup-host $setupHostPath `
    --output $setupPath

if ($LASTEXITCODE -ne 0) {
    Write-Error "Self-developed packager failed with exit code $LASTEXITCODE"
}

Write-Host "--------------------------------------------------"
Write-Host "Installer created successfully!"
Write-Host "Shell: $Shell"
Write-Host "Output: $setupPath"
Write-Host "Default install: %LocalAppData%\DeepVisionSharp"
Write-Host "--------------------------------------------------"

