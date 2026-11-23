#requires -Version 7.0
[CmdletBinding()]
param(
    [ValidateSet(
        'resnet18', 'resnet34',
        'yolo26n', 'yolo26s', 'yolo26m', 'yolo26l', 'yolo26x',
        'yolo26n-cls', 'yolo26s-cls', 'yolo26m-cls', 'yolo26l-cls', 'yolo26x-cls')]
    [string]$Arch = 'resnet18',
    [string]$Destination = '',
    [string]$SourcePath = '',
    [switch]$KeepSource
)

$ErrorActionPreference = 'Stop'
$project = Join-Path $PSScriptRoot 'PretrainedWeights/PretrainedWeights.csproj'
$keep = $KeepSource.IsPresent.ToString().ToLowerInvariant()
dotnet run --project $project --configuration Release -- $Arch $Destination $SourcePath $keep
exit $LASTEXITCODE
