#requires -Version 7.0
[CmdletBinding()]
param(
    [ValidateSet('Fast', 'Slow', 'Pretrained')]
    [string]$Mode = 'Fast',
    [ValidateSet('Debug', 'Release')]
    [string]$Configuration = 'Debug'
)

$ErrorActionPreference = 'Stop'
$repo = Split-Path -Parent $PSScriptRoot
$solution = Join-Path $repo 'DeepVisionSharp.slnx'
$trainingTests = Join-Path $repo 'tests/DeepVisionSharp.TorchSharp.Tests/DeepVisionSharp.TorchSharp.Tests.csproj'

if ($Mode -eq 'Fast') {
    dotnet build $solution -c $Configuration --nologo
    if ($LASTEXITCODE -ne 0) { exit $LASTEXITCODE }

    dotnet test $solution -c $Configuration --no-build --nologo --filter 'Category!=Slow&Category!=Pretrained'
    exit $LASTEXITCODE
}

$previousOptIn = $env:DV_VERIFY_PRETRAINED
try {
    if ($Mode -eq 'Pretrained') {
        $env:DV_VERIFY_PRETRAINED = '1'
    }

    dotnet test $trainingTests -c $Configuration --nologo --filter "Category=$Mode"
    $exitCode = $LASTEXITCODE
}
finally {
    $env:DV_VERIFY_PRETRAINED = $previousOptIn
}
exit $exitCode
