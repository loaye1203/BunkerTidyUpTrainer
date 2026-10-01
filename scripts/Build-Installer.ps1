[CmdletBinding()]
param(
    [string]$PortableArchive,
    [string]$OutputPath
)

$ErrorActionPreference = 'Stop'
$repoRoot = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot '..'))
if ([string]::IsNullOrWhiteSpace($PortableArchive)) { $PortableArchive = Join-Path $repoRoot '.artifacts\release\v1.0.2\BunkerTidyUpTrainer-1.0.2-Portable.zip' }
if ([string]::IsNullOrWhiteSpace($OutputPath)) { $OutputPath = Join-Path $repoRoot '.artifacts\release\v1.0.2\BunkerTidyUpTrainer-1.0.2-Setup.exe' }
$PortableArchive = [IO.Path]::GetFullPath($PortableArchive)
$OutputPath = [IO.Path]::GetFullPath($OutputPath)
if (-not (Test-Path -LiteralPath $PortableArchive -PathType Leaf)) { throw ('找不到便携发行包：' + $PortableArchive) }
$frameworkDirectory = Join-Path $env:WINDIR 'Microsoft.NET\Framework64\v4.0.30319'
$compiler = Join-Path $frameworkDirectory 'csc.exe'
if (-not (Test-Path -LiteralPath $compiler -PathType Leaf)) { throw '找不到 Windows .NET Framework C# 编译器 csc.exe。此编译器只用于构建安装器，不要求游戏玩家另装 .NET 10。' }
New-Item -ItemType Directory -Force -Path (Split-Path -Parent $OutputPath) | Out-Null
$arguments = @(
    '/nologo', '/target:winexe', '/optimize+',
    ('/out:' + $OutputPath),
    '/reference:System.dll', '/reference:System.Core.dll', '/reference:System.Drawing.dll', '/reference:System.Windows.Forms.dll',
    '/reference:System.IO.Compression.dll', '/reference:System.IO.Compression.FileSystem.dll',
    '/reference:System.Runtime.Serialization.dll',
    ('/resource:' + $PortableArchive + ',PortablePackage.zip'),
    (Join-Path $repoRoot 'installer\Bootstrapper.cs')
)
& $compiler @arguments
if ($LASTEXITCODE -ne 0) { throw 'Windows 自解压安装器编译失败。' }
Write-Host ('安装程序已生成：' + $OutputPath)
