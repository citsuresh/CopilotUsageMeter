<#
Purpose: Phase 3 partial resource baseline. Prints numeric aggregates only (no titles, text, paths or
local analytics); CLI output is discarded after an ok=true check.
Modes:
  Cli    - repeats the 3-launch legacy refresh (stats, usage-top --limit 100, activity) Iterations times and
           reports per-launch wall/CPU/peak-memory and per-refresh wall time.
  Tray   - starts the demo tray unelevated (via Start-DemoTrayUnelevated.ps1), samples its CPU, memory,
           handles and threads for DurationSeconds with no client window, then stops that tray PID only.
  Cycle  - as Tray, and additionally runs the 3 CLI calls every CycleIntervalSeconds to simulate refreshes
           at the planned throttle; reports tray and CLI CPU separately and combined.
Parameters: Mode, Iterations, DurationSeconds, SampleSeconds, CycleIntervalSeconds, TrayIntervalSeconds
(0 = tray default; otherwise a settings file is written for the run and removed afterwards; refuses to
run if one already exists), TrayExecutablePath, DesktopExplorerProcessId, CliPath.
Modifies: Tray/Cycle start and stop one tray process and may create then delete
%LOCALAPPDATA%\CopilotUsageMeter\settings.json. Cli mode modifies nothing. Never stops other processes.
Measures the demo engine only; not a real reader. One machine, one build; say so when reporting.
#>
[CmdletBinding()]
param(
    [Parameter(Mandatory)][ValidateSet('Cli', 'Tray', 'Cycle')][string] $Mode,
    [string] $CliPath = (Join-Path $PSScriptRoot '..\..\tools\chathistory\chathistory.exe'),
    [ValidateRange(1, 100)][int] $Iterations = 10,
    [ValidateRange(10, 1800)][int] $DurationSeconds = 90,
    [ValidateRange(1, 10)][int] $SampleSeconds = 1,
    [ValidateRange(5, 300)][int] $CycleIntervalSeconds = 5,
    [ValidateScript({ $_ -eq 0 -or ($_ -ge 5 -and $_ -le 300) })][int] $TrayIntervalSeconds = 0,
    [string] $TrayExecutablePath = (Join-Path $PSScriptRoot '..\..\src\CopilotUsageMeter.Tray\bin\Release\net10.0-windows\CopilotUsageMeter.Tray.exe'),
    [int] $DesktopExplorerProcessId = 0
)

$ErrorActionPreference = 'Stop'
$cores = [Environment]::ProcessorCount
$refreshVerbs = @(@('stats'), @('usage-top', '--limit', '100'), @('activity'))

function Get-Stat($values) {
    $v = @($values | Sort-Object)
    if ($v.Count -eq 0) { return [pscustomobject]@{ N = 0; Min = $null; Median = $null; Max = $null } }
    $mid = [math]::Floor($v.Count / 2)
    $median = if ($v.Count % 2) { $v[$mid] } else { ($v[$mid - 1] + $v[$mid]) / 2 }
    [pscustomobject]@{ N = $v.Count; Min = [math]::Round($v[0], 1); Median = [math]::Round($median, 1); Max = [math]::Round($v[-1], 1) }
}

function Invoke-Cli([string[]] $CliArgs) {
    $psi = New-Object System.Diagnostics.ProcessStartInfo
    $psi.FileName = $CliPath
    $psi.Arguments = $CliArgs -join ' '
    $psi.UseShellExecute = $false
    $psi.RedirectStandardOutput = $true
    $psi.RedirectStandardError = $true
    $psi.CreateNoWindow = $true
    if ($psi.EnvironmentVariables.ContainsKey('DOTNET_ROOT')) { $psi.EnvironmentVariables.Remove('DOTNET_ROOT') }
    $sw = [Diagnostics.Stopwatch]::StartNew()
    $p = [Diagnostics.Process]::Start($psi)
    $err = $p.StandardError.ReadToEndAsync()
    $out = $p.StandardOutput.ReadToEndAsync()
    $peak = 0L; $cpu = 0.0
    while (-not $p.WaitForExit(15)) {
        try { $p.Refresh(); if ($p.PeakWorkingSet64 -gt $peak) { $peak = $p.PeakWorkingSet64 }; $cpu = $p.TotalProcessorTime.TotalMilliseconds } catch { }
    }
    $p.WaitForExit(); $sw.Stop()
    try { $cpu = $p.TotalProcessorTime.TotalMilliseconds } catch { }
    $ok = $p.ExitCode -eq 0 -and $out.Result -match '"ok"\s*:\s*true'
    if (-not $ok) { throw "CLI call failed (exit $($p.ExitCode))" }
    [pscustomobject]@{ WallMs = $sw.ElapsedMilliseconds; CpuMs = $cpu; PeakMb = $peak / 1MB }
}

function Invoke-Refresh {
    $sw = [Diagnostics.Stopwatch]::StartNew()
    $calls = foreach ($verb in $refreshVerbs) { Invoke-Cli $verb }
    $sw.Stop()
    [pscustomobject]@{ WallMs = $sw.ElapsedMilliseconds; Calls = @($calls) }
}

function Start-Tray {
    $exe = (Resolve-Path -LiteralPath $TrayExecutablePath).ProviderPath
    $arguments = @{ TrayExecutablePath = $exe; WaitSeconds = 3 }
    if ($DesktopExplorerProcessId -ne 0) { $arguments.DesktopExplorerProcessId = $DesktopExplorerProcessId }
    & (Join-Path $PSScriptRoot 'Start-DemoTrayUnelevated.ps1') @arguments | Out-Null
    $tray = @(Get-CimInstance Win32_Process -Filter "Name='CopilotUsageMeter.Tray.exe'")
    if ($tray.Count -ne 1) { throw 'Cannot uniquely identify the tray started for measurement.' }
    $tray[0].ProcessId
}

function Measure-Tray([int] $TrayPid, [bool] $WithCycle) {
    $samples = New-Object System.Collections.Generic.List[object]
    $refreshes = New-Object System.Collections.Generic.List[object]
    $proc = Get-Process -Id $TrayPid
    $start = [DateTime]::UtcNow
    $prevAt = $start; $prevCpu = $proc.TotalProcessorTime.TotalMilliseconds
    $first = [pscustomobject]@{ WorkingSetMb = [math]::Round($proc.WorkingSet64 / 1MB, 1); PrivateMb = [math]::Round($proc.PrivateMemorySize64 / 1MB, 1); CpuSecSinceStart = [math]::Round($prevCpu / 1000, 2) }
    $nextRefresh = $start
    while (([DateTime]::UtcNow - $start).TotalSeconds -lt $DurationSeconds) {
        if ($WithCycle -and [DateTime]::UtcNow -ge $nextRefresh) {
            $refreshes.Add((Invoke-Refresh))
            $nextRefresh = [DateTime]::UtcNow.AddSeconds($CycleIntervalSeconds)
        }
        else { Start-Sleep -Seconds $SampleSeconds }
        $proc.Refresh()
        $now = [DateTime]::UtcNow; $cpu = $proc.TotalProcessorTime.TotalMilliseconds
        $samples.Add([pscustomobject]@{
            CorePct = ($cpu - $prevCpu) / (($now - $prevAt).TotalMilliseconds) * 100
            WorkingSetMb = $proc.WorkingSet64 / 1MB; PrivateMb = $proc.PrivateMemorySize64 / 1MB
            Handles = $proc.HandleCount; Threads = $proc.Threads.Count })
        $prevAt = $now; $prevCpu = $cpu
    }
    $wall = ($prevAt - $start).TotalSeconds
    $trayCpuSec = ($prevCpu - $first.CpuSecSinceStart * 1000) / 1000
    $result = [ordered]@{
        BuildAndScope = 'demo engine only (no real reader); one machine; one run'
        LogicalCores = $cores
        WindowSeconds = [math]::Round($wall, 1)
        Startup = $first
        TrayCpuPercentOfOneCore = Get-Stat ($samples | ForEach-Object CorePct)
        TrayCpuPercentOfMachine = [math]::Round($trayCpuSec / $wall / $cores * 100, 3)
        TrayWorkingSetMb = Get-Stat ($samples | ForEach-Object WorkingSetMb)
        TrayPrivateMb = Get-Stat ($samples | ForEach-Object PrivateMb)
        TrayHandlesMax = ($samples | Measure-Object Handles -Maximum).Maximum
        TrayThreadsMax = ($samples | Measure-Object Threads -Maximum).Maximum
    }
    if ($WithCycle) {
        $calls = @($refreshes | ForEach-Object { $_.Calls })
        $cliCpuSec = ($calls | Measure-Object CpuMs -Sum).Sum / 1000
        $result.Refreshes = $refreshes.Count
        $result.CycleIntervalSeconds = $CycleIntervalSeconds
        $result.ProcessLaunches = $calls.Count
        $result.RefreshWallMs = Get-Stat ($refreshes | ForEach-Object WallMs)
        $result.CliCpuSeconds = [math]::Round($cliCpuSec, 2)
        $result.CliCpuPercentOfMachine = [math]::Round($cliCpuSec / $wall / $cores * 100, 3)
        $result.TrayPlusCliCpuPercentOfMachine = [math]::Round(($trayCpuSec + $cliCpuSec) / $wall / $cores * 100, 3)
        $result.CliPeakMb = Get-Stat ($calls | ForEach-Object PeakMb)
    }
    [pscustomobject]$result
}

if ($Mode -eq 'Cli') {
    $CliPath = (Resolve-Path -LiteralPath $CliPath).ProviderPath
    $null = Invoke-Refresh # warm-up, not counted
    $runs = 1..$Iterations | ForEach-Object { Invoke-Refresh }
    $calls = @($runs | ForEach-Object { $_.Calls })
    [pscustomobject]@{
        BuildAndScope = 'toolkit CLI only; one machine'
        Iterations = $Iterations
        LaunchesPerRefresh = $refreshVerbs.Count
        RefreshWallMs = Get-Stat ($runs | ForEach-Object WallMs)
        LaunchWallMs = Get-Stat ($calls | ForEach-Object WallMs)
        LaunchCpuMs = Get-Stat ($calls | ForEach-Object CpuMs)
        LaunchPeakMb = Get-Stat ($calls | ForEach-Object PeakMb)
        RefreshCpuMs = Get-Stat ($runs | ForEach-Object { ($_.Calls | Measure-Object CpuMs -Sum).Sum })
    }
    return
}

if ($Mode -eq 'Cycle') { $CliPath = (Resolve-Path -LiteralPath $CliPath).ProviderPath }
$settingsPath = Join-Path $env:LOCALAPPDATA 'CopilotUsageMeter\settings.json'
$wroteSettings = $false
$trayPid = 0
$launching = $false
if (@(Get-CimInstance Win32_Process -Filter "Name='CopilotUsageMeter.Tray.exe'").Count -ne 0) { throw 'A tray is already running; refusing to measure or stop it.' }
try {
    if ($TrayIntervalSeconds -gt 0) {
        if (Test-Path -LiteralPath $settingsPath) { throw 'A settings file already exists; refusing to overwrite it.' }
        New-Item -ItemType Directory -Force -Path (Split-Path $settingsPath) | Out-Null
        [IO.File]::WriteAllText($settingsPath, "{`"DemoRefreshSeconds`":$TrayIntervalSeconds}", (New-Object Text.UTF8Encoding($false)))
        $wroteSettings = $true
    }
    $launching = $true
    $trayPid = Start-Tray
    Measure-Tray -TrayPid $trayPid -WithCycle ($Mode -eq 'Cycle')
}
finally {
    if ($trayPid -ne 0) { Stop-Process -Id $trayPid -ErrorAction SilentlyContinue }
    elseif ($launching) {
        # No tray existed before this run, so a single tray now is the one we launched before the PID check failed.
        $leftover = @(Get-CimInstance Win32_Process -Filter "Name='CopilotUsageMeter.Tray.exe'")
        if ($leftover.Count -eq 1) { Stop-Process -Id $leftover[0].ProcessId -ErrorAction SilentlyContinue }
    }
    if ($wroteSettings) { Remove-Item -LiteralPath $settingsPath -Force -ErrorAction SilentlyContinue }
}
