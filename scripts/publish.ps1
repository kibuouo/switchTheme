$ErrorActionPreference = 'Stop'
$projectRoot = Split-Path -Parent $PSScriptRoot
$publishDirectory = Join-Path $projectRoot 'dist\switchTheme'
$stageDirectory = Join-Path $projectRoot 'dist\.publish-stage'
& (Join-Path $PSScriptRoot 'build-local.ps1') -OutputDirectory $stageDirectory
New-Item -ItemType Directory -Path $publishDirectory -Force | Out-Null
$stageExe = Join-Path $stageDirectory 'switchTheme.exe'
$publishedExe = Join-Path $publishDirectory 'switchTheme.exe'
for ($copyAttempt = 0; $copyAttempt -lt 5; $copyAttempt++) {
    try {
        Copy-Item -LiteralPath $stageExe -Destination $publishedExe -Force -ErrorAction Stop
        break
    } catch {
        if ($copyAttempt -eq 4) { throw '目标 exe 仍被占用，请从托盘退出 switchTheme 后重试。' }
        Start-Sleep -Milliseconds 500
    }
}
Copy-Item -LiteralPath (Join-Path $stageDirectory 'switchTheme.exe.config') -Destination $publishDirectory -Force
$previousData = Join-Path $projectRoot 'dist\ThemeShift\data'
$publishedData = Join-Path $publishDirectory 'data'
if (!(Test-Path -LiteralPath $publishedData) -and (Test-Path -LiteralPath $previousData)) {
    Copy-Item -LiteralPath $previousData -Destination $publishedData -Recurse
}
Copy-Item -LiteralPath (Join-Path $projectRoot 'README.md') -Destination (Join-Path $publishDirectory '使用说明.md') -Force
Copy-Item -LiteralPath (Join-Path $PSScriptRoot 'add-start-menu.ps1') -Destination (Join-Path $publishDirectory 'Add-StartMenu.ps1') -Force
Compress-Archive -LiteralPath (Join-Path $publishDirectory 'switchTheme.exe'), (Join-Path $publishDirectory 'switchTheme.exe.config'), (Join-Path $publishDirectory '使用说明.md'), (Join-Path $publishDirectory 'Add-StartMenu.ps1') -DestinationPath (Join-Path $projectRoot 'dist\switchTheme-win-x64.zip') -Force
Write-Output "发布完成：$publishDirectory"
