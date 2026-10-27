[CmdletBinding()]
param(
    # Product architecture to fetch: resnet18 (s tier) or resnet34 (m/l/x tier).
    [ValidateSet('resnet18', 'resnet34')][string]$Arch = 'resnet18',
    # Where the converted bundle is written. The loader reads <Destination>/<Arch>/index.json.
    [string]$Destination = (Join-Path $env:LOCALAPPDATA 'DeepVisionSharp/Models/pretrained'),
    # Optional already-downloaded .pth; skips the download.
    [string]$SourcePath,
    # Keep the downloaded .pth after conversion.
    [switch]$KeepSource
)
$ErrorActionPreference = 'Stop'

$urls = @{
    resnet18 = 'https://download.pytorch.org/models/resnet18-f37072fd.pth'
    resnet34 = 'https://download.pytorch.org/models/resnet34-b627a593.pth'
}

$repo = Split-Path -Parent $PSScriptRoot
$converter = Join-Path $repo 'scripts/convert_torchvision_weights.py'
if (-not (Test-Path -LiteralPath $converter)) {
    throw "Converter not found: $converter"
}

$python = (Get-Command python -ErrorAction SilentlyContinue)?.Source
if ([string]::IsNullOrWhiteSpace($python)) {
    throw 'Python 3 is required to convert torchvision weights (numpy only; torch is not needed).'
}
& $python -c 'import numpy' 2>&1 | Out-Null
if ($LASTEXITCODE -ne 0) {
    throw 'numpy is required: pip install numpy'
}

$temporarySource = $null
try {
    if ([string]::IsNullOrWhiteSpace($SourcePath)) {
        $temporarySource = Join-Path ([IO.Path]::GetTempPath()) ("dv-{0}-{1}.pth" -f $Arch, [guid]::NewGuid().ToString('N'))
        Write-Host "Downloading $Arch weights -> $temporarySource"
        Invoke-WebRequest -Uri $urls[$Arch] -OutFile $temporarySource -UseBasicParsing
        $SourcePath = $temporarySource
    }

    if (-not (Test-Path -LiteralPath $SourcePath)) {
        throw "Weights file not found: $SourcePath"
    }

    $target = Join-Path $Destination $Arch
    New-Item -ItemType Directory -Force -Path $target | Out-Null
    Write-Host "Converting $SourcePath -> $target"
    & $python $converter $SourcePath $target --arch $Arch
    if ($LASTEXITCODE -ne 0) {
        throw "Conversion failed (exit $LASTEXITCODE)"
    }

    Write-Host ''
    Write-Host "Pretrained bundle ready: $target" -ForegroundColor Green
    Write-Host 'Training picks it up automatically when the v2 architecture is selected.'
}
finally {
    if ($temporarySource -and (Test-Path -LiteralPath $temporarySource) -and -not $KeepSource) {
        Remove-Item -LiteralPath $temporarySource -Force -ErrorAction SilentlyContinue
    }
}
