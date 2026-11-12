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
.PARAMETER IncludeGuiResources
  Include read-only GDI and USER object counts from GetGuiResources.
.PARAMETER PassThru
  Return the aggregate object with timing metadata instead of the legacy formatted display.
#>
[CmdletBinding()]
param(
    [Parameter(Mandatory)][int]$ProcessId,
    [int]$DurationSeconds = 90,
    [int]$IntervalSeconds = 1,
    [switch]$IncludeGuiResources,
    [switch]$PassThru
)

$ErrorActionPreference = 'Stop'
if ($IncludeGuiResources -and -not ('ProcessResourceGuiCounters' -as [type])) {
    Add-Type -TypeDefinition 'using System; using System.Runtime.InteropServices; public static class ProcessResourceGuiCounters { [DllImport("user32.dll")] public static extern uint GetGuiResources(IntPtr process, uint flags); }'
}
$cores = [Environment]::ProcessorCount
$process = Get-Process -Id $ProcessId
$startTime = $process.StartTime
$samples = New-Object System.Collections.Generic.List[object]
$previousCpu = $process.TotalProcessorTime
$previousAt = [DateTime]::UtcNow
$samplingStart = $previousAt
$firstGdi = if ($IncludeGuiResources) { [ProcessResourceGuiCounters]::GetGuiResources($process.Handle, 0) }
$firstUser = if ($IncludeGuiResources) { [ProcessResourceGuiCounters]::GetGuiResources($process.Handle, 1) }
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
        GdiObjects   = $(if ($IncludeGuiResources) { [ProcessResourceGuiCounters]::GetGuiResources($process.Handle, 0) })
        UserObjects  = $(if ($IncludeGuiResources) { [ProcessResourceGuiCounters]::GetGuiResources($process.Handle, 1) })
    })
    $previousCpu = $cpu
    $previousAt = $now
}

if ($samples.Count -eq 0) { throw 'No samples were taken.' }

function Median($values) { $s = @($values | Sort-Object); $s[[int][Math]::Floor(($s.Count - 1) / 2)] }

$core = $samples | ForEach-Object CorePercent
$result = [ordered]@{
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
}
if ($PassThru) {
    $result.ProcessId = $ProcessId
    $result.SamplingStartUtc = $samplingStart.ToString('o')
    $result.SamplingEndUtc = $previousAt.ToString('o')
    $result.DurationSeconds = [Math]::Round(($previousAt - $samplingStart).TotalSeconds, 3)
    $result.Threads_Min = ($samples.Threads | Measure-Object -Minimum).Minimum
    $result.MachinePercent_Median = [Math]::Round((Median $core) / $cores, 3)
    $result.MachinePercent_Max = [Math]::Round(($core | Measure-Object -Maximum).Maximum / $cores, 3)
}
if ($IncludeGuiResources) {
    $result.GdiObjects_Before = $firstGdi
    $result.GdiObjects_Min = ($samples.GdiObjects | Measure-Object -Minimum).Minimum
    $result.GdiObjects_Max = ($samples.GdiObjects | Measure-Object -Maximum).Maximum
    $result.GdiObjects_After = $samples[$samples.Count - 1].GdiObjects
    $result.UserObjects_Before = $firstUser
    $result.UserObjects_Min = ($samples.UserObjects | Measure-Object -Minimum).Minimum
    $result.UserObjects_Max = ($samples.UserObjects | Measure-Object -Maximum).Maximum
    $result.UserObjects_After = $samples[$samples.Count - 1].UserObjects
}
if ($PassThru) { [pscustomobject]$result } else { [pscustomobject]$result | Format-List }
