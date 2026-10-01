[CmdletBinding()]
param(
    [string]$OutputRoot,
    [string]$BuildRoot,
    [string]$BepInExRoot,
    [string]$DotNetLicenseRoot
)

$ErrorActionPreference = 'Stop'
$repoRoot = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot '..'))
if ([string]::IsNullOrWhiteSpace($OutputRoot)) { $OutputRoot = Join-Path $repoRoot '.artifacts\release\v1.0.1' }
if ([string]::IsNullOrWhiteSpace($BuildRoot)) { $BuildRoot = Join-Path $repoRoot '.artifacts\build' }
if ([string]::IsNullOrWhiteSpace($BepInExRoot)) { $BepInExRoot = Join-Path $repoRoot '.artifacts\cache\BepInEx-5.4.23.5' }
if ([string]::IsNullOrWhiteSpace($DotNetLicenseRoot)) { $DotNetLicenseRoot = Join-Path $repoRoot '.artifacts\dotnet-licenses' }
$OutputRoot = [IO.Path]::GetFullPath($OutputRoot)
$BuildRoot = [IO.Path]::GetFullPath($BuildRoot)
$portableStage = Join-Path $OutputRoot 'portable'
$payloadRoot = Join-Path $portableStage 'Payload'

$controller = Join-Path $BuildRoot 'controller\BunkerTidyUpTrainer.exe'
$pluginRoot = Join-Path $BuildRoot 'plugin'
$bepCore = Join-Path $BepInExRoot 'BepInEx\core'
foreach ($path in @($controller, (Join-Path $pluginRoot 'BunkerTidyUp.Mod.dll'), (Join-Path $pluginRoot 'Trainer.Shared.dll'),
        (Join-Path $BepInExRoot 'winhttp.dll'), (Join-Path $BepInExRoot 'doorstop_config.ini'),
        (Join-Path $BepInExRoot '.doorstop_version'), (Join-Path $bepCore 'BepInEx.dll'))) {
    if (-not (Test-Path -LiteralPath $path -PathType Leaf)) { throw ('缺少发行文件：' + $path) }
}

$outputPrefix = $OutputRoot.TrimEnd([IO.Path]::DirectorySeparatorChar) + [IO.Path]::DirectorySeparatorChar
$artifactsRoot = [IO.Path]::GetFullPath((Join-Path (Join-Path $repoRoot '.artifacts') '.'))
if (-not $OutputRoot.StartsWith($artifactsRoot.TrimEnd([IO.Path]::DirectorySeparatorChar) + [IO.Path]::DirectorySeparatorChar, [StringComparison]::OrdinalIgnoreCase)) {
    throw '为避免误删仓库外文件，发行输出必须位于仓库 .artifacts 目录下。'
}
if (Test-Path -LiteralPath $OutputRoot) { Remove-Item -LiteralPath $OutputRoot -Recurse -Force }
New-Item -ItemType Directory -Force -Path $portableStage, $payloadRoot | Out-Null
Copy-Item -LiteralPath $controller -Destination $portableStage
Copy-Item -LiteralPath (Join-Path $BepInExRoot 'winhttp.dll') -Destination $payloadRoot
Copy-Item -LiteralPath (Join-Path $BepInExRoot 'doorstop_config.ini') -Destination $payloadRoot
Copy-Item -LiteralPath (Join-Path $BepInExRoot '.doorstop_version') -Destination $payloadRoot
$payloadCore = Join-Path $payloadRoot 'BepInEx\core'
$pluginTarget = Join-Path $payloadRoot 'BepInEx\plugins\BunkerTidyUpTrainer'
New-Item -ItemType Directory -Force -Path $payloadCore, $pluginTarget | Out-Null
Get-ChildItem -LiteralPath $bepCore -Filter '*.dll' -File | Copy-Item -Destination $payloadCore
Copy-Item -LiteralPath (Join-Path $pluginRoot 'BunkerTidyUp.Mod.dll') -Destination $pluginTarget
Copy-Item -LiteralPath (Join-Path $pluginRoot 'Trainer.Shared.dll') -Destination $pluginTarget

Copy-Item -LiteralPath (Join-Path $repoRoot 'README.md') -Destination $portableStage
Copy-Item -LiteralPath (Join-Path $repoRoot 'LICENSE') -Destination $portableStage
Copy-Item -LiteralPath (Join-Path $repoRoot 'THIRD-PARTY-NOTICES.md') -Destination $portableStage
Copy-Item -LiteralPath (Join-Path $repoRoot 'docs\验证报告-1.0.1.md') -Destination (Join-Path $portableStage '验证报告.md')
Copy-Item -LiteralPath (Join-Path $repoRoot 'docs\release-notes-1.0.1.md') -Destination (Join-Path $portableStage '发行说明.md')
$packageLicenses = Join-Path $portableStage 'licenses'
$packageImage = Join-Path $portableStage 'docs\assets'
New-Item -ItemType Directory -Force -Path $packageLicenses, $packageImage | Out-Null
Get-ChildItem -LiteralPath (Join-Path $repoRoot 'licenses') -File | Copy-Item -Destination $packageLicenses
Copy-Item -LiteralPath (Join-Path $DotNetLicenseRoot 'Microsoft-DotNet-LICENSE.txt') -Destination $packageLicenses -Force
Copy-Item -LiteralPath (Join-Path $DotNetLicenseRoot 'Microsoft-DotNet-ThirdPartyNotices.txt') -Destination $packageLicenses -Force
Copy-Item -LiteralPath (Join-Path $DotNetLicenseRoot 'Microsoft-WindowsDesktop-ThirdPartyNotices.txt') -Destination $packageLicenses -Force
Copy-Item -LiteralPath (Join-Path $repoRoot 'docs\assets\trainer-ui.png') -Destination (Join-Path $packageImage 'trainer-ui.png')

$metadata = [ordered]@{
    product = 'Bunker Tidy Up Trainer'
    version = '1.0.1'
    platform = 'Windows x64'
    gameRuntime = 'Unity Mono 6000.0.59f2'
    loader = 'BepInEx 5.4.23.5'
    firstInstallFeaturesEnabled = $false
    controllerSelfContained = $true
}
$metadata | ConvertTo-Json -Depth 4 | Set-Content -LiteralPath (Join-Path $portableStage 'release-metadata.json') -Encoding UTF8

$archiveName = 'BunkerTidyUpTrainer-1.0.1-Portable.zip'
$archivePath = Join-Path $OutputRoot $archiveName
Add-Type -AssemblyName System.IO.Compression
$archiveStream = [IO.File]::Open($archivePath, [IO.FileMode]::CreateNew, [IO.FileAccess]::ReadWrite, [IO.FileShare]::None)
$zipArchive = $null
try {
    $zipArchive = [IO.Compression.ZipArchive]::new($archiveStream, [IO.Compression.ZipArchiveMode]::Create, $false)
    Get-ChildItem -LiteralPath $portableStage -File -Recurse -Force | ForEach-Object {
        $relativePath = [IO.Path]::GetRelativePath($portableStage, $_.FullName).Replace('\', '/')
        $entry = $zipArchive.CreateEntry($relativePath, [IO.Compression.CompressionLevel]::Optimal)
        $source = [IO.File]::OpenRead($_.FullName)
        $destination = $entry.Open()
        try { $source.CopyTo($destination) }
        finally { $destination.Dispose(); $source.Dispose() }
    }
}
finally {
    if ($null -ne $zipArchive) { $zipArchive.Dispose() }
    $archiveStream.Dispose()
}
Write-Host ('便携包已生成：' + $archivePath)
