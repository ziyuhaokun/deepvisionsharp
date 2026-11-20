#requires -Version 7.0
<#
.SYNOPSIS
  用根目录 agent-render.json 的 AvaloniaRealShell 预设，把 7 条路由逐个导航进 MainWindow 离屏出图。
.DESCRIPTION
  渲染宿主在 %TEMP%\agent-render-cache（由 agent-render 工具管理）；本脚本只把本次取证产物收敛到
  .outputs/agent-render/<timestamp>/。DV_RENDER_PAGE / DV_RENDER_STUB 由本脚本临时设置并清理。
#>
[CmdletBinding()]
param(
    [string]$Pages = 'dashboard,gallery,models,labeling,training,inference,settings',
    [switch]$Build,
    # agent-render 工具仓库根（含 scripts/render.ps1）；省略时读环境变量 AGENT_RENDER_ROOT。
    [string]$AgentRenderRoot = $env:AGENT_RENDER_ROOT,
    # 取证产物根目录；省略时用 <RepoRoot>/.outputs/agent-render。
    [string]$OutRoot
)

$ErrorActionPreference = 'Stop'
if (-not $IsWindows) { throw '本脚本需要 Windows + PowerShell 7。' }

$RepoRoot = (Resolve-Path -LiteralPath (Join-Path (Join-Path $PSScriptRoot '..') '..')).ProviderPath

if (-not $AgentRenderRoot) {
    throw '未找到 agent-render 工具根：请设置环境变量 AGENT_RENDER_ROOT 或传 -AgentRenderRoot。'
}
$AgentRenderRoot = $ExecutionContext.SessionState.Path.GetUnresolvedProviderPathFromPSPath($AgentRenderRoot)
$renderScript = Join-Path $AgentRenderRoot 'scripts/render.ps1'
if (-not (Test-Path -LiteralPath $renderScript -PathType Leaf)) {
    throw "agent-render 工具根无效：$renderScript 不存在。"
}

if (-not $OutRoot) { $OutRoot = Join-Path $RepoRoot '.outputs/agent-render' }
$OutRoot = $ExecutionContext.SessionState.Path.GetUnresolvedProviderPathFromPSPath($OutRoot)
$runRoot = Join-Path $OutRoot (Get-Date -Format 'yyyyMMdd-HHmmss')
$shotsDir = Join-Path $runRoot 'shots'
$bindingDir = Join-Path $runRoot 'binding-errors'
$summaryDir = Join-Path $runRoot 'summary'
New-Item -ItemType Directory -Force -Path $shotsDir, $bindingDir, $summaryDir | Out-Null

$knownPages = @('dashboard', 'gallery', 'models', 'labeling', 'training', 'inference', 'settings')
$pageList = @($Pages -split ',' | ForEach-Object { $_.Trim() } | Where-Object { $_ } | Select-Object -Unique)
if ($pageList.Count -eq 0) { throw '-Pages 至少要给一个页面。' }
$unknownPages = @($pageList | Where-Object { $_ -notin $knownPages })
if ($unknownPages.Count -gt 0) {
    throw "未知页面：$($unknownPages -join ', ')。可用：$($knownPages -join ', ')"
}

$shellBin = Join-Path $RepoRoot 'src/DeepVisionSharp.Shell.Avalonia/bin/Debug/net10.0-windows'
$uiBin = Join-Path $RepoRoot 'src/DeepVisionSharp.UI.Avalonia/bin/Debug/net10.0'
if (-not (Test-Path -LiteralPath $shellBin -PathType Container)) {
    throw "找不到 Shell 输出目录：$shellBin（先构建 Avalonia Shell）。"
}
$uiDll = Join-Path $uiBin 'DeepVisionSharp.UI.Avalonia.dll'
if (-not (Test-Path -LiteralPath $uiDll -PathType Leaf)) {
    throw "找不到 DeepVisionSharp.UI.Avalonia 输出：$uiDll（先构建 DeepVisionSharp.UI.Avalonia）。"
}

# 业务程序集全部当引用传给宿主：FreeSql 用 Assembly.Load 反射加载 provider，
# 只有进了宿主 deps.json 的 DLL 才找得到。DeepVisionSharp.UI.Avalonia.dll 只取 DeepVisionSharp.UI.Avalonia 项目的 bin，
# 不重复带 Shell 输出下复制的同名 DLL；Avalonia* 由宿主自己提供。
$refs = @(Get-ChildItem -LiteralPath $shellBin -Filter '*.dll' -File |
    Where-Object { $_.Name -notlike 'Avalonia*' -and $_.Name -ne 'DeepVisionSharp.UI.Avalonia.dll' } |
    Select-Object -ExpandProperty FullName)
$refs += $uiDll
if ($refs.Count -eq 0) { throw "没有可传给渲染宿主的业务 DLL：$shellBin" }

function Invoke-Render([string]$Page, [switch]$Stub) {
    $env:DV_RENDER_PAGE = $Page
    if ($Stub) { $env:DV_RENDER_STUB = '1' } else { Remove-Item Env:DV_RENDER_STUB -ErrorAction SilentlyContinue }

    $renderArgs = @{
        Config         = $RepoRoot
        Preset         = 'AvaloniaRealShell'
        AssemblyPath   = $refs
        TimeoutSeconds = 180
    }
    if ($Build) { $renderArgs['Build'] = $true }

    try {
        $outDir = (& $renderScript @renderArgs | Select-Object -Last 1)
        $outDir = "$outDir".Trim()
        # 第一次渲染可能因为宿主 out/ 缺 CHANGELOG.md / SQLite 原生库而失败；
        # 只要有 summary.json，就把它当可用结果返回，让外层同步依赖后重渲。
        if (-not $outDir -or -not (Test-Path -LiteralPath (Join-Path $outDir 'summary.json') -PathType Leaf)) {
            throw "渲染 $Page 没有产出 summary.json（exit=$LASTEXITCODE）。"
        }
        return $outDir
    }
    finally {
        Remove-Item Env:DV_RENDER_PAGE, Env:DV_RENDER_STUB -ErrorAction SilentlyContinue
    }
}

# 离屏宿主只知道托管依赖与显式传入的程序集：SQLite 原生库和设置页「关于」读的
# CHANGELOG.md 都要手工同步进它的 out/（win-x64）。任一文件变化都重渲一次。
function Sync-HostRuntimeFiles([string]$outDir) {
    $native = Join-Path $shellBin 'runtimes/win-x64/native/SQLite.Interop.dll'
    if (-not (Test-Path -LiteralPath $native -PathType Leaf)) {
        Write-Warning "缺少 SQLite 原生库：$native"
    }

    $target = Join-Path (Split-Path -Parent $outDir) 'out'
    if (-not (Test-Path -LiteralPath $target -PathType Container)) { return $false }

    $files = @()
    if (Test-Path -LiteralPath $native -PathType Leaf) {
        $files += @{ Source = $native; Dest = 'SQLite.Interop.dll'; SubDir = 'runtimes/win-x64/native' }
    }
    $changeLog = Join-Path $RepoRoot 'CHANGELOG.md'
    if (Test-Path -LiteralPath $changeLog -PathType Leaf) {
        $files += @{ Source = $changeLog; Dest = 'CHANGELOG.md'; SubDir = '' }
    }

    $copied = $false
    foreach ($entry in $files) {
        foreach ($dest in @($target, (Join-Path $target $entry.SubDir))) {
            $dst = Join-Path $dest $entry.Dest
            $found = Get-Item -LiteralPath $dst -ErrorAction SilentlyContinue
            if ($found -and $found.Length -eq (Get-Item -LiteralPath $entry.Source).Length) { continue }
            New-Item -ItemType Directory -Force -Path $dest | Out-Null
            Copy-Item -LiteralPath $entry.Source -Destination $dst -Force
            $copied = $true
        }
    }
    return $copied
}

# stub 图必须在图上自证：截图上方压一条红底声明带（只改 PNG，不参与布局，不影响 layout 报告）。
function Add-StubBanner([string]$Path) {
    Add-Type -AssemblyName System.Drawing
    $banner = 38
    $source = [System.Drawing.Bitmap]::new($Path)
    $canvas = [System.Drawing.Bitmap]::new($source.Width, $source.Height + $banner)
    try {
        $graphics = [System.Drawing.Graphics]::FromImage($canvas)
        try {
            $graphics.Clear([System.Drawing.Color]::White)
            $fill = [System.Drawing.SolidBrush]::new([System.Drawing.Color]::FromArgb(255, 190, 26, 26))
            try { $graphics.FillRectangle($fill, 0, 0, $source.Width, $banner) }
            finally { $fill.Dispose() }
            $font = [System.Drawing.Font]::new('Microsoft YaHei', 13, [System.Drawing.FontStyle]::Bold)
            try {
                $graphics.DrawString('STUB 数据 · 仅视觉基线，不代表真实运行（DV_RENDER_STUB=1）',
                    $font, [System.Drawing.Brushes]::White, 12, 8)
            }
            finally { $font.Dispose() }
            $graphics.DrawImage($source, 0, $banner, $source.Width, $source.Height)
        }
        finally { $graphics.Dispose() }

        # 源图占着同一个路径，必须先释放句柄再写回去。
        $source.Dispose()
        $source = $null
        $canvas.Save($Path, [System.Drawing.Imaging.ImageFormat]::Png)
    }
    finally {
        if ($null -ne $source) { $source.Dispose() }
        $canvas.Dispose()
    }
}

$shots = @()
foreach ($page in $pageList) { $shots += [ordered]@{ page = $page; stub = $false; name = "inwin-$page" } }
# 数据库里 0 个训练任务，真 ViewModel 渲染不出指标区；指标卡片的视觉基线单独出一张 stub 图。
if ($pageList -contains 'training') {
    $shots += [ordered]@{ page = 'training'; stub = $true; name = 'inwin-training-stub' }
}

$rows = @()
foreach ($shot in $shots) {
    $started = [DateTime]::Now
    $outDir = Invoke-Render $shot.page -Stub:$shot.stub
    if (Sync-HostRuntimeFiles $outDir) {
        $outDir = Invoke-Render $shot.page -Stub:$shot.stub
    }
    $seconds = [int]([DateTime]::Now - $started).TotalSeconds

    $summarySource = Join-Path $outDir 'summary.json'
    $summaryTarget = Join-Path $summaryDir "$($shot.name).summary.json"
    Copy-Item -LiteralPath $summarySource -Destination $summaryTarget -Force
    $sum = Get-Content -LiteralPath $summarySource -Raw | ConvertFrom-Json
    if ($sum.status -ne 'ok' -or $sum.bindingIssueCount -gt 0) {
        throw "渲染 $($shot.name) 未通过：status=$($sum.status) binding=$($sum.bindingIssueCount)"
    }

    $bindingTargets = @()
    foreach ($name in @('binding-errors.json', 'binding-errors.txt')) {
        $source = Join-Path $outDir $name
        if (Test-Path -LiteralPath $source -PathType Leaf) {
            $target = Join-Path $bindingDir "$($shot.name).$name"
            Copy-Item -LiteralPath $source -Destination $target -Force
            $bindingTargets += $target
        }
    }

    $png = Join-Path $outDir 'snapshot.png'
    $target = Join-Path $shotsDir "$($shot.name).png"
    $state = if (Test-Path -LiteralPath $png -PathType Leaf) { Copy-Item -LiteralPath $png -Destination $target -Force; 'ok' } else { 'missing' }
    if ($shot.stub -and $state -eq 'ok') { Add-StubBanner $target }

    $rows += [ordered]@{
        name          = $shot.name
        page          = $shot.page
        data          = if ($shot.stub) { 'stub（SampleTrainingContext）' } else { 'real' }
        snapshot      = $target
        status        = $sum.status
        seconds       = $seconds
        layoutIssues  = $sum.layoutIssueCount
        bindingIssues = $sum.bindingIssueCount
        bindingErrors = $bindingTargets
        summary       = $summaryTarget
        outDir        = $outDir
    }
    Write-Host ("{0,-22} {1,-8} {2,3}s  status={3} layout={4} binding={5}" -f $shot.name, $state, $seconds, $sum.status, $sum.layoutIssueCount, $sum.bindingIssueCount)
    $sum.layoutIssues | ForEach-Object { Write-Host "    layout: $_" }
    $sum.bindingIssues | Select-Object -First 6 | ForEach-Object { Write-Host "    binding: $_" }
    $log = Join-Path $outDir 'render.stdout.log'
    if (Test-Path -LiteralPath $log -PathType Leaf) {
        Get-Content -LiteralPath $log |
            Where-Object { $_ -match '^\[render\]|^(crit|fail):' } |
            Select-Object -First 12 |
            ForEach-Object { Write-Host ('    ' + $_.Substring(0, [Math]::Min($_.Length, 260))) }
    }
}

$report = [ordered]@{
    generatedAt     = [DateTime]::Now.ToString('o')
    repoRoot        = $RepoRoot
    agentRenderRoot = $AgentRenderRoot
    outRoot         = $runRoot
    preset          = 'AvaloniaRealShell'
    pages           = $pageList
    shots           = $rows
}
$reportPath = Join-Path $runRoot 'report.json'
$report | ConvertTo-Json -Depth 6 | Set-Content -LiteralPath $reportPath -Encoding utf8
Write-Host ''
Write-Host "report -> $reportPath ; snapshots -> $shotsDir ; binding errors -> $bindingDir ; summaries -> $summaryDir"
