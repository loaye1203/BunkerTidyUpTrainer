[CmdletBinding()]
param(
    [string]$GamePath,
    [string]$UnityVersion,
    [string]$BepInExArchive
)

$ErrorActionPreference = 'Stop'
$repoRoot = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot '..'))
if ([string]::IsNullOrWhiteSpace($GamePath) -and [string]::IsNullOrWhiteSpace($UnityVersion)) {
    throw '请提供 -GamePath <游戏根目录> 或 -UnityVersion <例如 6000.0.59>。'
}
$buildArgs = @{}
if (-not [string]::IsNullOrWhiteSpace($GamePath)) { $buildArgs.GamePath = $GamePath }
if (-not [string]::IsNullOrWhiteSpace($UnityVersion)) { $buildArgs.UnityVersion = $UnityVersion }
if (-not [string]::IsNullOrWhiteSpace($BepInExArchive)) { $buildArgs.BepInExArchive = $BepInExArchive }
& (Join-Path $PSScriptRoot 'Build.ps1') @buildArgs
if ($LASTEXITCODE -ne 0) { throw '源代码构建失败。' }

$outputRoot = Join-Path $repoRoot '.artifacts\release\v1.0.3'
& (Join-Path $PSScriptRoot 'Package-Release.ps1') -OutputRoot $outputRoot
if ($LASTEXITCODE -ne 0) { throw '便携包打包失败。' }
& (Join-Path $PSScriptRoot 'Build-Installer.ps1') -OutputPath (Join-Path $outputRoot 'BunkerTidyUpTrainer-1.0.3-Setup.exe')
if ($LASTEXITCODE -ne 0) { throw '安装程序构建失败。' }

$assets = @('BunkerTidyUpTrainer-1.0.3-Setup.exe', 'BunkerTidyUpTrainer-1.0.3-Portable.zip')
$sumLines = foreach ($name in $assets) {
    $path = Join-Path $outputRoot $name
    if (-not (Test-Path -LiteralPath $path -PathType Leaf)) { throw ('发行资产缺失：' + $name) }
    $hash = (Get-FileHash -LiteralPath $path -Algorithm SHA256).Hash.ToLowerInvariant()
    $hash + '  ' + $name
}
$sumLines | Set-Content -LiteralPath (Join-Path $outputRoot 'SHA256SUMS.txt') -Encoding ASCII
$metadataPath = Join-Path $outputRoot 'release-metadata.json'
$manifest = [ordered]@{
    product = 'Bunker Tidy Up Trainer'
    version = '1.0.3'
    tag = 'v1.0.3'
    assets = @(
        foreach ($name in $assets) {
            $path = Join-Path $outputRoot $name
            [ordered]@{ name = $name; size = (Get-Item -LiteralPath $path).Length; sha256 = (Get-FileHash -LiteralPath $path -Algorithm SHA256).Hash.ToLowerInvariant() }
        }
    )
}
$manifest | ConvertTo-Json -Depth 6 | Set-Content -LiteralPath $metadataPath -Encoding UTF8
Write-Host ('公开版 1.0.3 已准备：' + $outputRoot)
