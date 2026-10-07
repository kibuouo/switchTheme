param(
    [Parameter(Mandatory=$true)][string]$Phase,
    [string]$Executable = 'dist\switchTheme\switchTheme.exe',
    [string]$OutputDirectory = 'dist\measurements',
    [int]$Samples = 8,
    [int]$IntervalSeconds = 3
)
$ErrorActionPreference = 'Stop'
$root = Split-Path -Parent $PSScriptRoot
$exe = if ([IO.Path]::IsPathRooted($Executable)) { $Executable } else { Join-Path $root $Executable }
$out = if ([IO.Path]::IsPathRooted($OutputDirectory)) { $OutputDirectory } else { Join-Path $root $OutputDirectory }
New-Item -ItemType Directory -Path $out -Force | Out-Null
$watch = [Diagnostics.Stopwatch]::StartNew()
$rows = @()
for ($sample = 1; $sample -le $Samples; $sample++) {
    if ($sample -gt 1) { Start-Sleep -Seconds $IntervalSeconds }
    $process = @(Get-CimInstance Win32_Process | Where-Object ExecutablePath -eq $exe)
    if ($process.Count -ne 1) { throw "Expected one instance of $exe, found $($process.Count)" }
    $counter = Get-CimInstance Win32_PerfFormattedData_PerfProc_Process | Where-Object IDProcess -eq $process[0].ProcessId
    if ($null -eq $counter) { throw 'Performance counter missing' }
    $rows += [pscustomobject]@{
        Phase = $Phase; Sample = $sample; ElapsedSeconds = $watch.Elapsed.TotalSeconds; Pid = $process[0].ProcessId
        PrivateWSMiB = $counter.WorkingSetPrivate / 1MB; WorkingSetMiB = $counter.WorkingSet / 1MB
        PrivateCommittedMiB = $counter.PrivateBytes / 1MB
        CpuSeconds = ([double]$process[0].UserModeTime + [double]$process[0].KernelModeTime) / 10000000
        Threads = $process[0].ThreadCount; Handles = $process[0].HandleCount
    }
}
if (@($rows.Pid | Select-Object -Unique).Count -ne 1) { throw 'Process changed during sample' }
$duration = $rows[-1].ElapsedSeconds - $rows[0].ElapsedSeconds
$cpu = $rows[-1].CpuSeconds - $rows[0].CpuSeconds
$sorted = @($rows.PrivateWSMiB | Sort-Object)
$summary = [pscustomobject]@{
    Phase = $Phase; Samples = $Samples; DurationSeconds = [Math]::Round($duration, 3)
    PrivateWSMedianMiB = [Math]::Round(($sorted[[int][Math]::Floor(($Samples-1)/2)] + $sorted[[int][Math]::Ceiling(($Samples-1)/2)]) / 2, 3)
    WorkingSetMeanMiB = [Math]::Round(($rows.WorkingSetMiB | Measure-Object -Average).Average, 3)
    PrivateCommittedMeanMiB = [Math]::Round(($rows.PrivateCommittedMiB | Measure-Object -Average).Average, 3)
    CpuDeltaSeconds = $cpu; CpuMachineMeanPercent = [Math]::Round(100 * $cpu / $duration / [Environment]::ProcessorCount, 5)
    Threads = $rows[-1].Threads; Handles = $rows[-1].Handles
}
$rows | Export-Csv -LiteralPath (Join-Path $out "$Phase.csv") -NoTypeInformation -Encoding utf8
$summary | ConvertTo-Json | Set-Content -LiteralPath (Join-Path $out "$Phase-summary.json") -Encoding utf8
$summary | ConvertTo-Json
