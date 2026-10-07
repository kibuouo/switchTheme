$ErrorActionPreference = 'Stop'
$projectRoot = Split-Path -Parent $PSScriptRoot
$publishDirectory = Join-Path $projectRoot 'dist\switchTheme'
$executable = Join-Path $publishDirectory 'switchTheme.exe'
$resultFile = Join-Path $publishDirectory 'install-result.json'
try {
    if (!(Test-Path -LiteralPath $executable)) { throw 'Build the release before installing.' }
    $repair = Start-Process -FilePath $executable -ArgumentList '--repair-startup' -WindowStyle Hidden -PassThru -Wait
    if ($repair.ExitCode -ne 0) { throw 'Startup registration failed; see data\startup.log.' }
    $settingsPath = Join-Path $publishDirectory 'data\settings.json'
    $settings = if (Test-Path -LiteralPath $settingsPath) { Get-Content -LiteralPath $settingsPath -Raw | ConvertFrom-Json } else { [pscustomobject]@{ StartWithWindows = $false } }
    if ($settings.StartWithWindows) {
        $task = Get-ScheduledTask -TaskName 'switchTheme'
        if ($task.Actions.Execute -ne $executable) { throw 'Startup executable does not match the release.' }
    }
    $programs = [Environment]::GetFolderPath([Environment+SpecialFolder]::Programs)
    New-Item -ItemType Directory -Path $programs -Force | Out-Null
    $shortcutPath = Join-Path $programs 'switchTheme.lnk'
    $shell = New-Object -ComObject WScript.Shell
    $shortcut = $shell.CreateShortcut($shortcutPath)
    $shortcut.TargetPath = $executable
    $shortcut.WorkingDirectory = $publishDirectory
    $shortcut.IconLocation = $executable + ',0'
    $shortcut.Description = 'switchTheme - Windows theme scheduler'
    $shortcut.Save()
    [Runtime.InteropServices.Marshal]::FinalReleaseComObject($shortcut) | Out-Null
    [Runtime.InteropServices.Marshal]::FinalReleaseComObject($shell) | Out-Null
    $oldTask = Get-ScheduledTask -TaskName 'ThemeShift' -ErrorAction SilentlyContinue
    $oldExecutable = Join-Path $projectRoot 'dist\ThemeShift\ThemeShift.exe'
    if ($oldTask -and $oldTask.Actions.Execute -eq $oldExecutable) {
        Stop-ScheduledTask -TaskName 'ThemeShift' -ErrorAction SilentlyContinue
        Unregister-ScheduledTask -TaskName 'ThemeShift' -Confirm:$false
    }
    Get-CimInstance Win32_Process -Filter "Name='ThemeShift.exe'" | Where-Object { $_.ExecutablePath -eq $oldExecutable } | ForEach-Object {
        Stop-Process -Id $_.ProcessId -Force
    }
    if ($settings.StartWithWindows) { Start-ScheduledTask -TaskName 'switchTheme' }
    else { Start-Process -FilePath $executable -ArgumentList '--background' -WindowStyle Hidden }
    @{ Installed = $true; Executable = $executable; Shortcut = $shortcutPath; StartupEnabled = $settings.StartWithWindows; InstalledAt = (Get-Date).ToString('o') } |
        ConvertTo-Json | Set-Content -LiteralPath $resultFile -Encoding UTF8
    Write-Output "Installed: $shortcutPath"
} catch {
    @{ Installed = $false; Error = $_.Exception.ToString() } | ConvertTo-Json | Set-Content -LiteralPath $resultFile -Encoding UTF8
    throw
}
