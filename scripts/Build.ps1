[CmdletBinding()]
param(
    [string]$GamePath,
    [string]$UnityVersion,
    [string]$BepInExArchive
)

$ErrorActionPreference = 'Stop'
$repoRoot = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot '..'))
$artifactRoot = Join-Path $repoRoot '.artifacts'
$cacheRoot = Join-Path $artifactRoot 'cache'
$bepArchiveName = 'BepInEx_win_x64_5.4.23.5.zip'
$bepArchiveUrl = 'https://github.com/BepInEx/BepInEx/releases/download/v5.4.23.5/BepInEx_win_x64_5.4.23.5.zip'
$bepArchiveSha256 = '82F9878551030F54657792C0740D9D51A09500EEAE1FBA21106B0C441E6732C4'

if ([string]::IsNullOrWhiteSpace($GamePath) -eq [string]::IsNullOrWhiteSpace($UnityVersion)) {
    throw '请且仅请提供 -GamePath（读取本机游戏 Unity 引用）或 -UnityVersion（从 BepInEx Unity 库下载引用）。'
}

New-Item -ItemType Directory -Force -Path $cacheRoot | Out-Null
$unityManagedDir = ''
if (-not [string]::IsNullOrWhiteSpace($GamePath)) {
    $gameRoot = [IO.Path]::GetFullPath($GamePath)
    if (-not (Test-Path -LiteralPath (Join-Path $gameRoot 'Bunker.exe'))) {
        throw 'GamePath 必须指向包含 Bunker.exe 的游戏根目录。'
    }
    $unityManagedDir = Join-Path $gameRoot 'Bunker_Data\Managed'
}
else {
    $unityArchive = Join-Path $cacheRoot ('UnityReferences-' + $UnityVersion + '.zip')
    $unityUrl = 'https://unity.bepinex.dev/libraries/' + [Uri]::EscapeDataString($UnityVersion) + '.zip'
    if (-not (Test-Path -LiteralPath $unityArchive)) {
        Write-Host ('下载 Unity ' + $UnityVersion + ' 编译引用：' + $unityUrl)
        Invoke-WebRequest -Uri $unityUrl -OutFile $unityArchive
    }
    $unityExtractRoot = Join-Path $cacheRoot ('UnityReferences-' + $UnityVersion)
    if (-not (Test-Path -LiteralPath $unityExtractRoot)) {
        New-Item -ItemType Directory -Force -Path $unityExtractRoot | Out-Null
        Expand-Archive -LiteralPath $unityArchive -DestinationPath $unityExtractRoot -Force
    }
    $coreModule = Get-ChildItem -LiteralPath $unityExtractRoot -Filter 'UnityEngine.CoreModule.dll' -File -Recurse | Select-Object -First 1
    if ($null -eq $coreModule) { throw 'Unity 引用包中找不到 UnityEngine.CoreModule.dll。' }
    $unityManagedDir = $coreModule.DirectoryName
}

foreach ($file in @('UnityEngine.CoreModule.dll', 'UnityEngine.dll', 'UnityEngine.PhysicsModule.dll')) {
    if (-not (Test-Path -LiteralPath (Join-Path $unityManagedDir $file))) {
        throw ('Unity 引用目录缺少 ' + $file + '：' + $unityManagedDir)
    }
}

if ([string]::IsNullOrWhiteSpace($BepInExArchive)) {
    $BepInExArchive = Join-Path $cacheRoot $bepArchiveName
    if (-not (Test-Path -LiteralPath $BepInExArchive)) {
        Write-Host ('下载 BepInEx 5.4.23.5：' + $bepArchiveUrl)
        Invoke-WebRequest -Uri $bepArchiveUrl -OutFile $BepInExArchive
    }
}
$BepInExArchive = [IO.Path]::GetFullPath($BepInExArchive)
$actualBepHash = (Get-FileHash -LiteralPath $BepInExArchive -Algorithm SHA256).Hash
if (-not $actualBepHash.Equals($bepArchiveSha256, [StringComparison]::OrdinalIgnoreCase)) {
    throw ('BepInEx 压缩包 SHA-256 不匹配。期望 ' + $bepArchiveSha256 + '，实际 ' + $actualBepHash)
}

$bepRoot = Join-Path $cacheRoot 'BepInEx-5.4.23.5'
$bepCore = Join-Path $bepRoot 'BepInEx\core'
if (-not (Test-Path -LiteralPath (Join-Path $bepCore 'BepInEx.dll'))) {
    New-Item -ItemType Directory -Force -Path $bepRoot | Out-Null
    Expand-Archive -LiteralPath $BepInExArchive -DestinationPath $bepRoot -Force
}
$bepAssembly = [Reflection.AssemblyName]::GetAssemblyName((Join-Path $bepCore 'BepInEx.dll'))
if ($bepAssembly.Version.ToString() -ne '5.4.23.5') {
    throw ('BepInEx 版本不符：' + $bepAssembly.Version)
}
foreach ($file in @('0Harmony.dll', 'BepInEx.dll')) {
    if (-not (Test-Path -LiteralPath (Join-Path $bepCore $file))) { throw ('BepInEx 缺少 ' + $file) }
}

$dotnetCommand = Get-Command dotnet -ErrorAction Stop
$dotnetPath = $dotnetCommand.Source
$dotnetRoot = Split-Path -Parent $dotnetPath
$dotnetHome = Join-Path $artifactRoot 'dotnet-home'
$nugetPackages = Join-Path $artifactRoot 'nuget'
New-Item -ItemType Directory -Force -Path $dotnetHome, $nugetPackages | Out-Null
$previousCliHome = $env:DOTNET_CLI_HOME
$previousNugetPackages = $env:NUGET_PACKAGES
Push-Location $repoRoot
try {
    $env:DOTNET_CLI_HOME = $dotnetHome
    $env:NUGET_PACKAGES = $nugetPackages
    $sdkVersion = (& $dotnetPath --version).Trim()
    if (-not $sdkVersion.StartsWith('10.0.', [StringComparison]::Ordinal)) {
        throw ('需要 .NET 10 SDK；当前 dotnet 版本为 ' + $sdkVersion)
    }

    $buildRoot = Join-Path $artifactRoot 'build'
    $controllerOutput = Join-Path $buildRoot 'controller'
    $pluginOutput = Join-Path $buildRoot 'plugin'
    New-Item -ItemType Directory -Force -Path $controllerOutput, $pluginOutput | Out-Null
    $controllerProject = Join-Path $repoRoot 'src\Trainer.Controller\Trainer.Controller.csproj'
    $pluginProject = Join-Path $repoRoot 'src\BunkerTidyUp.Mod\BunkerTidyUp.Mod.csproj'

    & $dotnetPath restore $controllerProject --runtime win-x64 -p:Version=1.0.1
    if ($LASTEXITCODE -ne 0) { throw 'Controller NuGet restore 失败。' }
    & $dotnetPath publish $controllerProject --no-restore --configuration Release --runtime win-x64 --self-contained true `
        -p:PublishSingleFile=true -p:IncludeNativeLibrariesForSelfExtract=true -p:EnableCompressionInSingleFile=true `
        -p:PublishTrimmed=false -p:Version=1.0.1 -p:AssemblyVersion=1.0.1.0 -p:FileVersion=1.0.1.0 `
        --output $controllerOutput
    if ($LASTEXITCODE -ne 0) { throw 'Windows x64 控制器发布失败。' }

    & $dotnetPath restore $pluginProject (('-p:BepInExCoreDir=' + $bepCore)) (('-p:UnityManagedDir=' + $unityManagedDir)) -p:Version=1.0.1
    if ($LASTEXITCODE -ne 0) { throw '游戏插件 NuGet restore 失败。' }
    & $dotnetPath build $pluginProject --no-restore --configuration Release (('-p:BepInExCoreDir=' + $bepCore)) `
        (('-p:UnityManagedDir=' + $unityManagedDir)) -p:Version=1.0.1 -p:AssemblyVersion=1.0.1.0 -p:FileVersion=1.0.1.0 `
        --output $pluginOutput
    if ($LASTEXITCODE -ne 0) { throw '游戏插件构建失败。' }
    foreach ($file in @('BunkerTidyUp.Mod.dll', 'Trainer.Shared.dll')) {
        if (-not (Test-Path -LiteralPath (Join-Path $pluginOutput $file))) { throw ('构建输出缺少 ' + $file) }
    }
    if (-not (Test-Path -LiteralPath (Join-Path $controllerOutput 'BunkerTidyUpTrainer.exe'))) {
        throw '构建输出缺少自包含控制器 EXE。'
    }

    $licensesRoot = Join-Path $artifactRoot 'dotnet-licenses'
    New-Item -ItemType Directory -Force -Path $licensesRoot | Out-Null
    foreach ($item in @(
        @{ Source = (Join-Path $dotnetRoot 'LICENSE.txt'); Destination = (Join-Path $licensesRoot 'Microsoft-DotNet-LICENSE.txt') },
        @{ Source = (Join-Path $dotnetRoot 'ThirdPartyNotices.txt'); Destination = (Join-Path $licensesRoot 'Microsoft-DotNet-ThirdPartyNotices.txt') },
        @{ Source = (Join-Path $dotnetRoot ('sdk\' + $sdkVersion + '\Sdks\Microsoft.NET.Sdk.WindowsDesktop\THIRD-PARTY-NOTICES.TXT')); Destination = (Join-Path $licensesRoot 'Microsoft-WindowsDesktop-ThirdPartyNotices.txt') }
    )) {
        if (-not (Test-Path -LiteralPath $item.Source)) { throw ('缺少 .NET 发行许可文件：' + $item.Source) }
        Copy-Item -LiteralPath $item.Source -Destination $item.Destination -Force
    }
}
finally {
    $env:DOTNET_CLI_HOME = $previousCliHome
    $env:NUGET_PACKAGES = $previousNugetPackages
    Pop-Location
}

Write-Host ('构建成功：' + $buildRoot)
Write-Host ('BepInEx 依赖：' + $bepRoot)
Write-Host ('Unity 引用：' + $unityManagedDir)
