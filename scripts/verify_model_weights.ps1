<#
.SYNOPSIS
  Verify an explicitly supplied offline YOLO weights directory against manifest.json.
  Optional compatibility utility; not required by the DeepVisionSharp installer.

.PARAMETER WeightsDir
  Directory containing manifest.json and its weight files (family folders or legacy flat layout).

.PARAMETER Preset
  core | full

.EXAMPLE
  .\scripts\verify_model_weights.ps1 -WeightsDir C:\model-weights -Preset full
#>
[CmdletBinding()]
param(
    [ValidateSet("core", "full")]
    [string]$Preset = "core",

    [string]$WeightsDir = ""
)

$ErrorActionPreference = "Stop"

if ([string]::IsNullOrWhiteSpace($WeightsDir)) {
    throw "Specify -WeightsDir containing manifest.json. DeepVisionSharp no longer bundles Ultralytics initialization weights."
}

$manifestPath = Join-Path $WeightsDir "manifest.json"
if (-not (Test-Path $manifestPath)) {
    Write-Error "manifest.json not found: $manifestPath"
}

$manifest = Get-Content -Raw -Encoding UTF8 $manifestPath | ConvertFrom-Json
$entries = @($manifest.weights)
if ($Preset -eq "core") {
    $entries = @($entries | Where-Object { $_.tier -eq "core" })
}

$present = @()
$missing = @()
foreach ($entry in $entries) {
    $rel = ([string]$entry.file) -replace '/', [IO.Path]::DirectorySeparatorChar
    $path = Join-Path $WeightsDir $rel
    $legacy = Join-Path $WeightsDir ([IO.Path]::GetFileName($rel))
    if ((Test-Path $path) -and ((Get-Item $path).Length -gt 0)) {
        $mb = [math]::Round((Get-Item $path).Length / 1MB, 1)
        $present += [pscustomobject]@{ File = $rel; ProductKey = $entry.productKey; Family = $entry.family; MB = $mb }
    }
    elseif ((Test-Path $legacy) -and ((Get-Item $legacy).Length -gt 0)) {
        $mb = [math]::Round((Get-Item $legacy).Length / 1MB, 1)
        $present += [pscustomobject]@{ File = "$([IO.Path]::GetFileName($rel)) [legacy]"; ProductKey = $entry.productKey; Family = $entry.family; MB = $mb }
    }
    else {
        $missing += [pscustomobject]@{ File = $rel; ProductKey = $entry.productKey; Family = $entry.family; Tier = $entry.tier }
    }
}

Write-Host "Weights dir : $WeightsDir"
Write-Host "Preset      : $Preset"
Write-Host "Expected    : $($entries.Count)"
Write-Host "Present     : $($present.Count)"
Write-Host "Missing     : $($missing.Count)"
Write-Host ""

if ($present.Count -gt 0) {
    Write-Host "OK:"
    $present | Format-Table -AutoSize | Out-String | Write-Host
}

if ($missing.Count -gt 0) {
    Write-Host "MISSING:"
    $missing | Format-Table -AutoSize | Out-String | Write-Host
    Write-Host "Supply the missing files listed above in WeightsDir, then run this command again."
    exit 2
}

Write-Host "All $Preset weights are present (family-folder layout)."
exit 0
