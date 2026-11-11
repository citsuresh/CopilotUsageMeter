<#
.SYNOPSIS
  Samples CPU, memory, handles and threads of an already running process, read-only.
.DESCRIPTION
  Used for the Phase 5 re-measurement of the real tray engine against the Phase 3 baseline (Measure-Resources.ps1 measured a demo engine).
  Prints aggregates only: no window titles, command lines, paths or arguments. Never starts, stops or changes any process.
  CPU is reported as a percentage of one core and of the whole machine, from the process's total processor time between samples.
.PARAMETER ProcessId
  The process to sample (required).
.PARAMETER DurationSeconds
  Total sampling time. Default 90.
.PARAMETER IntervalSeconds
  Time between samples. Default 1.
#>
[CmdletBinding()]
param(
    [Parameter(Mandatory)][int]$ProcessId,
    [int]$DurationSeconds = 90,
    [int]$IntervalSeconds = 1
)

$ErrorActionPreference = 'Stop'
$cores = [Environment]::ProcessorCount
$process = Get-Process -Id $ProcessId
$startTime = $process.StartTime
$samples = New-Object System.Collections.Generic.List[object]
$previousCpu = $process.TotalProcessorTime
$previousAt = [DateTime]::UtcNow
$end = $previousAt.AddSeconds($DurationSeconds)

while ([DateTime]::UtcNow -lt $end) {
    Start-Sleep -Seconds $IntervalSeconds
    try { $process.Refresh() } catch { break }
    if ($process.HasExited) { Write-Warning 'The process exited during sampling.'; break }
    $now = [DateTime]::UtcNow
    $cpu = $process.TotalProcessorTime
    $elapsed = ($now - $previousAt).TotalSeconds
    $coreShare = if ($elapsed -gt 0) { ($cpu - $previousCpu).TotalSeconds / $elapsed * 100 } else { 0 }
    $samples.Add([pscustomobject]@{
        WorkingSetMB = $process.WorkingSet64 / 1MB
        PrivateMB    = $process.PrivateMemorySize64 / 1MB
        Handles      = $process.HandleCount
        Threads      = $process.Threads.Count
        CorePercent  = $coreShare
    })
    $previousCpu = $cpu
    $previousAt = $now
}

if ($samples.Count -eq 0) { throw 'No samples were taken.' }

function Median($values) { $s = @($values | Sort-Object); $s[[int][Math]::Floor(($s.Count - 1) / 2)] }

$core = $samples | ForEach-Object CorePercent
[pscustomobject]@{
    Samples              = $samples.Count
    SecondsSinceStart    = [int]([DateTime]::Now - $startTime).TotalSeconds
    WorkingSetMB_Min     = [Math]::Round(($samples.WorkingSetMB | Measure-Object -Minimum).Minimum, 1)
    WorkingSetMB_Max     = [Math]::Round(($samples.WorkingSetMB | Measure-Object -Maximum).Maximum, 1)
    PrivateMB_Min        = [Math]::Round(($samples.PrivateMB | Measure-Object -Minimum).Minimum, 1)
    PrivateMB_Max        = [Math]::Round(($samples.PrivateMB | Measure-Object -Maximum).Maximum, 1)
    Handles_Min          = ($samples.Handles | Measure-Object -Minimum).Minimum
    Handles_Max          = ($samples.Handles | Measure-Object -Maximum).Maximum
    Threads_Max          = ($samples.Threads | Measure-Object -Maximum).Maximum
    CorePercent_Median   = [Math]::Round((Median $core), 2)
    CorePercent_Max      = [Math]::Round(($core | Measure-Object -Maximum).Maximum, 1)
    CorePercent_Average  = [Math]::Round(($core | Measure-Object -Average).Average, 3)
    MachinePercent_Avg   = [Math]::Round((($core | Measure-Object -Average).Average / $cores), 3)
    LogicalCores         = $cores
} | Format-List
