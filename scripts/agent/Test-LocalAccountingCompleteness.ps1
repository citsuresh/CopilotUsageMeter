<#
.SYNOPSIS
  Read-only Phase 3 probe of local-accounting completeness and chathistory CLI cost.
.DESCRIPTION
  Calls the chathistory CLI (never the database) and prints ONLY numeric/boolean aggregates.
  Titles, summaries, repository names and paths returned by the CLI are used in memory for
  joins and counts and are never printed, logged or written to disk. Session ids are not printed.
  Checks: activity vs stats vs usage-top agreement; half-open ISO window boundaries; weekly/daily
  window splitting against the 100-row cap; sessions started before the window; chats that cannot be
  mapped to a repository; chats spanning several days or models (joint-filter ambiguity); and the
  wall time, CPU time and peak memory of each CLI process.
.PARAMETER Month
  UTC month to test, yyyy-MM. Default: current UTC month.
.PARAMETER CliPath
  Path to chathistory.exe.
.PARAMETER ChunkDays
  Window length in days for the splitting check (default 7).
.PARAMETER MaxSessionProbes
  Maximum number of per-chat `usage` calls used for the multi-model / multi-day check (default 40).
.PARAMETER SkipSessionProbes
  Skip the per-chat `usage` calls.
Modifies nothing.
#>
[CmdletBinding()]
param(
    [string]$Month = ([DateTime]::UtcNow.ToString('yyyy-MM')),
    [string]$CliPath = (Join-Path $PSScriptRoot '..\..\tools\chathistory\chathistory.exe'),
    [int]$ChunkDays = 7,
    [int]$MaxSessionProbes = 40,
    [switch]$SkipSessionProbes
)

$ErrorActionPreference = 'Stop'
$script:metrics = New-Object System.Collections.Generic.List[object]

function Invoke-Cli {
    param([string[]]$CliArgs, [string]$Label)
    $psi = New-Object System.Diagnostics.ProcessStartInfo
    $psi.FileName = $CliPath
    $psi.Arguments = ($CliArgs | ForEach-Object { if ($_ -match '\s') { '"' + $_ + '"' } else { $_ } }) -join ' '
    $psi.UseShellExecute = $false
    $psi.RedirectStandardOutput = $true
    $psi.RedirectStandardError = $true
    $psi.CreateNoWindow = $true
    $psi.StandardOutputEncoding = [Text.Encoding]::UTF8
    if ($psi.EnvironmentVariables.ContainsKey('DOTNET_ROOT')) { $psi.EnvironmentVariables.Remove('DOTNET_ROOT') }
    $sw = [Diagnostics.Stopwatch]::StartNew()
    $p = [Diagnostics.Process]::Start($psi)
    $errTask = $p.StandardError.ReadToEndAsync()
    $outTask = $p.StandardOutput.ReadToEndAsync()
    $peak = 0L; $cpu = 0.0
    while (-not $p.WaitForExit(15)) {
        try { $p.Refresh(); if ($p.PeakWorkingSet64 -gt $peak) { $peak = $p.PeakWorkingSet64 }; $cpu = $p.TotalProcessorTime.TotalMilliseconds } catch { }
    }
    $p.WaitForExit()
    $sw.Stop()
    $out = $outTask.Result
    try { $cpu = $p.TotalProcessorTime.TotalMilliseconds } catch { }
    $script:metrics.Add([pscustomobject]@{
        Label = $Label
        WallMs = $sw.ElapsedMilliseconds
        CpuMs = [int]$cpu
        PeakMb = [math]::Round($peak / 1MB, 1)
        Exit = $p.ExitCode
    })
    if ($p.ExitCode -ne 0) { throw "CLI '$Label' exit $($p.ExitCode)" }
    $json = $out | ConvertFrom-Json
    if (-not $json.ok) { throw "CLI '$Label' returned ok=false" }
    return $json
}

function Iso([DateTime]$d) { return $d.ToString("yyyy-MM-dd'T'HH:mm:ss'Z'") }

function Sum-Chats($usageTop) {
    $req = 0L; $cr = 0.0
    foreach ($s in @($usageTop.sessions)) { $req += [long]$s.requests; $cr += [double]$s.credits }
    return [pscustomobject]@{ Requests = $req; Credits = [math]::Round($cr, 2); Chats = @($usageTop.sessions).Count }
}

$m1 = [DateTime]::SpecifyKind([DateTime]::ParseExact($Month, 'yyyy-MM', $null), [DateTimeKind]::Utc)
$m2 = $m1.AddMonths(1)
"month=$Month"

# 1. Whole-month figures from three verbs.
$act = Invoke-Cli @('activity', '--from', (Iso $m1), '--to', (Iso $m2), '--limit', '100') 'activity'
$actCredits = 0.0; $actReq = 0L
foreach ($d in @($act.days)) { $actReq += [long]$d.requests; $actCredits += [double]$d.credits }
"activity.days=$(@($act.days).Count) activity.truncated=$($act.truncated) activity.requests=$actReq activity.credits=$([math]::Round($actCredits,2))"

$stats = Invoke-Cli @('stats') 'stats'
$sm = @($stats.usage.by_month | Where-Object { $_.month -eq $Month })
if ($sm.Count -eq 1) { "stats.month.requests=$($sm[0].totals.requests) stats.month.credits=$($sm[0].totals.credits)" } else { 'stats.month=unavailable' }

$top = Invoke-Cli @('usage-top', '--from', (Iso $m1), '--to', (Iso $m2), '--by', 'credits', '--limit', '100') 'usage-top-month'
$topSum = Sum-Chats $top
"usagetop.month.chats=$($topSum.Chats) usagetop.month.envelopeTruncated=$($top.truncated) usagetop.month.requests=$($topSum.Requests) usagetop.month.credits=$($topSum.Credits) rowCapHit=$($topSum.Chats -ge 100)"

# 2. Half-open boundary check at a mid-day instant.
$mid = $m1.AddDays([math]::Floor(($m2 - $m1).TotalDays / 2)).AddHours(12).AddMinutes(37).AddSeconds(11)
$a = Sum-Chats (Invoke-Cli @('usage-top', '--from', (Iso $m1), '--to', (Iso $mid), '--by', 'credits', '--limit', '100') 'usage-top-half-a')
$b = Sum-Chats (Invoke-Cli @('usage-top', '--from', (Iso $mid), '--to', (Iso $m2), '--by', 'credits', '--limit', '100') 'usage-top-half-b')
"boundary.requests.split=$($a.Requests + $b.Requests) boundary.requests.whole=$($topSum.Requests) boundary.requests.equal=$(($a.Requests + $b.Requests) -eq $topSum.Requests)"
"boundary.credits.split=$([math]::Round($a.Credits + $b.Credits,2)) boundary.credits.whole=$($topSum.Credits) chats.a=$($a.Chats) chats.b=$($b.Chats)"

# 3. Chunked windows (the plan for months that exceed the 100-chat cap).
$chunkReq = 0L; $chunkCr = 0.0; $chunkTrunc = 0; $chunkCapHit = 0; $maxChunkChats = 0; $chunks = 0
$allIds = New-Object System.Collections.Generic.HashSet[string]
$byId = @{}; $sessionsCapHit = 0; $sessionsSeen = 0
$cursor = $m1
while ($cursor -lt $m2) {
    $next = $cursor.AddDays($ChunkDays); if ($next -gt $m2) { $next = $m2 }
    $r = Invoke-Cli @('usage-top', '--from', (Iso $cursor), '--to', (Iso $next), '--by', 'credits', '--limit', '100') 'usage-top-chunk'
    $s = Sum-Chats $r
    $chunkReq += $s.Requests; $chunkCr += $s.Credits; $chunks++
    if ($r.truncated) { $chunkTrunc++ }
    if ($s.Chats -ge 100) { $chunkCapHit++ }
    if ($s.Chats -gt $maxChunkChats) { $maxChunkChats = $s.Chats }
    foreach ($x in @($r.sessions)) { [void]$allIds.Add([string]$x.session_id) }
    $sr = Invoke-Cli @('sessions', '--from', (Iso $cursor), '--to', (Iso $next), '--limit', '100') 'sessions-chunk'
    if (@($sr.sessions).Count -ge 100) { $sessionsCapHit++ }
    foreach ($x in @($sr.sessions)) { $byId[[string]$x.id] = $x }
    $cursor = $next
}
"chunks.count=$chunks chunks.envelopeTruncated=$chunkTrunc chunks.rowCapHit=$chunkCapHit sessionsChunks.rowCapHit=$sessionsCapHit chunks.maxChats=$maxChunkChats chunks.requests=$chunkReq chunks.credits=$([math]::Round($chunkCr,2)) chunks.distinctChats=$($allIds.Count)"
"chunks.requests.equalActivity=$($chunkReq -eq $actReq) chunks.credits.deltaVsActivity=$([math]::Round($chunkCr - $actCredits,2))"

# 4. Sessions started before the window, and mapping chats to repositories (in memory only).
$startedBefore = 0; $unmapped = 0; $noRepo = 0
foreach ($id in $allIds) {
    if (-not $byId.ContainsKey($id)) { $unmapped++; continue }
    $s = $byId[$id]
    if (-not $s.repository -and -not $s.cwd) { $noRepo++ }
    if ($s.start -and ([DateTime]::Parse($s.start, [Globalization.CultureInfo]::InvariantCulture, [Globalization.DateTimeStyles]::AssumeUniversal -bor [Globalization.DateTimeStyles]::AdjustToUniversal) -lt $m1)) { $startedBefore++ }
}
"sessions.mapped=$($byId.Count) chatsWithUsage=$($allIds.Count) startedBeforeWindow=$startedBefore unmappedToSession=$unmapped withoutRepoOrCwd=$noRepo"

# 5. Joint-filter ambiguity: chats using several models or spanning several UTC days.
if (-not $SkipSessionProbes) {
    $probed = 0; $multiModel = 0; $multiDay = 0; $subagentChats = 0
    foreach ($id in $allIds) {
        if ($probed -ge $MaxSessionProbes) { break }
        $u = Invoke-Cli @('usage', $id) 'usage-session'
        $probed++
        if (@($u.by_model).Count -gt 1) { $multiModel++ }
        if ($u.first_at -and $u.last_at) {
            $f = ([string]$u.first_at).Substring(0, 10); $l = ([string]$u.last_at).Substring(0, 10)
            if ($f -ne $l) { $multiDay++ }
        }
        if ($u.subagents -and [long]$u.subagents.requests -gt 0) { $subagentChats++ }
    }
    "jointFilter.probed=$probed multiModel=$multiModel multiDay=$multiDay withSubagents=$subagentChats"
}

# 6. CLI process cost.
$groups = $script:metrics | Group-Object Label
foreach ($g in $groups) {
    $w = ($g.Group | Measure-Object WallMs -Average -Maximum)
    $c = ($g.Group | Measure-Object CpuMs -Average)
    $pk = ($g.Group | Measure-Object PeakMb -Maximum)
    "cost.$($g.Name) calls=$($g.Count) wallAvgMs=$([int]$w.Average) wallMaxMs=$([int]$w.Maximum) cpuAvgMs=$([int]$c.Average) peakMb=$($pk.Maximum)"
}
"cost.total processLaunches=$($script:metrics.Count) wallMs=$(($script:metrics | Measure-Object WallMs -Sum).Sum) cpuMs=$(($script:metrics | Measure-Object CpuMs -Sum).Sum)"
