#requires -Version 7.0
[CmdletBinding()]
param(
    # Product architecture to fetch:
    #   resnet18 / resnet34          torchvision ImageNet classification weights
    #   yolo26n|s|m|l|x              Ultralytics COCO detection checkpoint
    #   yolo26n|s|m|l|x-cls          Ultralytics ImageNet classification checkpoint
    [ValidateSet(
        'resnet18', 'resnet34',
        'yolo26n', 'yolo26s', 'yolo26m', 'yolo26l', 'yolo26x',
        'yolo26n-cls', 'yolo26s-cls', 'yolo26m-cls', 'yolo26l-cls', 'yolo26x-cls')]
    [string]$Arch = 'resnet18',
    # Where the converted bundle is written. The loader reads <Destination>/<Arch>/index.json.
    [string]$Destination = $(if (-not [string]::IsNullOrWhiteSpace($env:DV_PRETRAINED_ROOT)) { $env:DV_PRETRAINED_ROOT } else { Join-Path $env:LOCALAPPDATA 'DeepVisionSharp/Models/pretrained' }),
    # Optional already-downloaded .pth/.pt; skips the download.
    [string]$SourcePath,
    # Keep the downloaded checkpoint after conversion.
    [switch]$KeepSource
)
$ErrorActionPreference = 'Stop'
if ([string]::IsNullOrWhiteSpace($Destination)) {
    $Destination = if (-not [string]::IsNullOrWhiteSpace($env:DV_PRETRAINED_ROOT)) {
        $env:DV_PRETRAINED_ROOT
    } else {
        Join-Path $env:LOCALAPPDATA 'DeepVisionSharp/Models/pretrained'
    }
}

$urls = @{
    resnet18 = 'https://download.pytorch.org/models/resnet18-f37072fd.pth'
    resnet34 = 'https://download.pytorch.org/models/resnet34-b627a593.pth'
}
# Ultralytics publishes every scale under one release; -cls is the ImageNet variant.
foreach ($scale in 'n', 's', 'm', 'l', 'x') {
    $urls["yolo26$scale"] = "https://github.com/ultralytics/assets/releases/download/v8.4.0/yolo26$scale.pt"
    $urls["yolo26$scale-cls"] = "https://github.com/ultralytics/assets/releases/download/v8.4.0/yolo26$scale-cls.pt"
}

$repo = Split-Path -Parent $PSScriptRoot
# A torchvision .pth is a plain state dict; an Ultralytics .pt is a pickled training result.
$isUltralytics = $Arch.StartsWith('yolo26', [StringComparison]::Ordinal)
$converter = Join-Path $repo ($(if ($isUltralytics) {
    'scripts/convert_ultralytics_weights.py'
} else {
    'scripts/convert_torchvision_weights.py'
}))
if (-not (Test-Path -LiteralPath $converter)) {
    throw "Converter not found: $converter"
}

$python = (Get-Command python -ErrorAction SilentlyContinue)?.Source
if ([string]::IsNullOrWhiteSpace($python)) {
    throw 'Python 3 is required to convert pretrained weights (numpy only; torch is not needed).'
}
& $python -c 'import numpy' 2>&1 | Out-Null
if ($LASTEXITCODE -ne 0) {
    throw 'numpy is required: pip install numpy'
}

$temporarySource = $null
try {
    if ([string]::IsNullOrWhiteSpace($SourcePath)) {
        $temporarySource = Join-Path ([IO.Path]::GetTempPath()) ("dv-{0}-{1}.pt" -f $Arch, [guid]::NewGuid().ToString('N'))
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
    Write-Host 'Training picks it up automatically when the matching architecture is selected.'
}
finally {
    if ($temporarySource -and (Test-Path -LiteralPath $temporarySource) -and -not $KeepSource) {
        Remove-Item -LiteralPath $temporarySource -Force -ErrorAction SilentlyContinue
    }
}
