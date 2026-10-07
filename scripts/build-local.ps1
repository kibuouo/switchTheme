param([string]$OutputDirectory = 'dist\switchTheme.Local-stage', [switch]$Profile)
$ErrorActionPreference = 'Stop'
$projectRoot = Split-Path -Parent $PSScriptRoot
$sourceDirectory = Join-Path $projectRoot 'src\switchTheme'
$outputPath = if ([IO.Path]::IsPathRooted($OutputDirectory)) { $OutputDirectory } else { Join-Path $projectRoot $OutputDirectory }
New-Item -ItemType Directory -Path $outputPath -Force | Out-Null
$compiler = Join-Path $env:WINDIR 'Microsoft.NET\Framework64\v4.0.30319\csc.exe'
if (-not (Test-Path -LiteralPath $compiler)) { throw '本机缺少 .NET Framework 4.8 编译器。' }
$icon = Join-Path $projectRoot 'src\switchTheme\Assets\app.ico'
$manifest = Join-Path $projectRoot 'src\switchTheme\app.manifest'
$executable = Join-Path $outputPath 'switchTheme.exe'
$sourceFiles = @(Get-ChildItem -LiteralPath $sourceDirectory -Filter '*.cs' | ForEach-Object FullName)
$profileFlag = if($Profile){'/define:PROFILE'}else{'/define:RELEASE'}
& $compiler /nologo /utf8output /warnaserror /target:winexe /platform:x64 /optimize+ /debug- $profileFlag "/out:$executable" "/win32icon:$icon" "/win32manifest:$manifest" "/resource:$icon,app.ico" /r:System.dll /r:System.Core.dll /r:System.Drawing.dll /r:System.Windows.Forms.dll /r:System.Runtime.Serialization.dll $sourceFiles
if ($LASTEXITCODE -ne 0) { throw '本机轻量版编译失败。' }
Copy-Item -LiteralPath (Join-Path $sourceDirectory 'switchTheme.exe.config') -Destination (Join-Path $outputPath 'switchTheme.exe.config') -Force
Write-Output "本机轻量版已编译：$executable"
