param([string]$Executable = (Join-Path $PSScriptRoot 'switchTheme.exe'))
$ErrorActionPreference = 'Stop'
$executablePath = (Resolve-Path -LiteralPath $Executable).Path
$programs = [Environment]::GetFolderPath([Environment+SpecialFolder]::Programs)
New-Item -ItemType Directory -Path $programs -Force | Out-Null
$shortcutPath = Join-Path $programs 'switchTheme.lnk'
$shell = $null
$shortcut = $null
try {
    $shell = New-Object -ComObject WScript.Shell
    $shortcut = $shell.CreateShortcut($shortcutPath)
    $shortcut.TargetPath = $executablePath
    $shortcut.WorkingDirectory = Split-Path -Parent $executablePath
    $shortcut.IconLocation = $executablePath + ',0'
    $shortcut.Description = 'switchTheme - Windows theme scheduler'
    $shortcut.Save()
    Write-Output "Start menu shortcut: $shortcutPath"
} finally {
    if ($shortcut) { [Runtime.InteropServices.Marshal]::FinalReleaseComObject($shortcut) | Out-Null }
    if ($shell) { [Runtime.InteropServices.Marshal]::FinalReleaseComObject($shell) | Out-Null }
}
