$ErrorActionPreference = 'Stop'
$projectRoot = Split-Path -Parent $PSScriptRoot
$output = Join-Path $projectRoot 'dist\.local-tests'
New-Item -ItemType Directory -Path $output -Force | Out-Null
$compiler = Join-Path $env:WINDIR 'Microsoft.NET\Framework64\v4.0.30319\csc.exe'
$executable = Join-Path $output 'switchTheme.Tests.exe'
$sources = @('src\switchTheme\Settings.cs','src\switchTheme\NativeServices.cs','src\switchTheme\StartupTask.cs','src\switchTheme\ResumeState.cs','tests\switchTheme.Tests\Program.cs') | ForEach-Object { Join-Path $projectRoot $_ }
& $compiler /nologo /utf8output /warnaserror /target:exe /platform:x64 /optimize+ "/out:$executable" /r:System.dll /r:System.Core.dll /r:System.Drawing.dll /r:System.Windows.Forms.dll /r:System.Runtime.Serialization.dll $sources
if ($LASTEXITCODE -ne 0) { throw '本机版测试编译失败' }
& $executable
if ($LASTEXITCODE -ne 0) { throw '本机版测试失败' }
