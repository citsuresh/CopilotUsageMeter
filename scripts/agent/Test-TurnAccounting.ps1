<#
.SYNOPSIS
  Checks per-turn and main/subagent accounting consistency from the toolkit CLI. Read-only.
.DESCRIPTION
  For each session starting on/after -From (max 100 by CLI clamp), runs `usage <id> --by-turn` and verifies:
  main+subagents == total, sum(by_turn) == total, turn_index unique and contiguous, and by_turn count vs the
  session's turn_count. Prints counts and numeric differences only - never titles, text, repositories or ids.
.PARAMETER From
  ISO date (inclusive) for the sessions query.
.PARAMETER Limit
  Max sessions (CLI clamps to 100).
.PARAMETER Cli
  Path to chathistory.exe.
#>
param(
  [string]$From = '2026-09-01',
  [int]$Limit = 100,
  [string]$Cli = (Join-Path $PSScriptRoot '..\..\tools\chathistory\chathistory.exe')
)
Remove-Item Env:DOTNET_ROOT -ErrorAction SilentlyContinue
function Sum($rows, $field) { $t = 0; foreach ($r in $rows) { $t += $r.totals.$field }; $t }

$s = (& $Cli sessions --from $From --limit $Limit 2>$null) -join '' | ConvertFrom-Json
if ($LASTEXITCODE -ne 0 -or -not $s.ok) { throw "sessions call failed (exit $LASTEXITCODE)" }
"sessions fetched: $($s.count), rowCapHit=$($s.count -ge $Limit)"
$c = @{ cliError = 0; checked = 0; notRecorded = 0; usageTruncated = 0; splitBad = 0; withSub = 0; turnSumBad = 0; dupIdx = 0; idxGap = 0; countDiff = 0; countDiffByOne = 0 }
$examples = @()
foreach ($x in $s.sessions) {
  $u = (& $Cli usage $x.id --by-turn 2>$null) -join '' | ConvertFrom-Json
  $c.checked++
  if ($LASTEXITCODE -ne 0 -or -not $u.ok) { $c.cliError++; continue }
  if (-not $u.recorded) { $c.notRecorded++; continue }
  if ($u.truncated) { $c.usageTruncated++ }
  $t = $u.total
  if (($u.main.requests + $u.subagents.requests) -ne $t.requests -or ($u.main.nano_aiu + $u.subagents.nano_aiu) -ne $t.nano_aiu -or ($u.main.credits + $u.subagents.credits) -ne $t.credits) { $c.splitBad++ }
  if ($u.subagents.requests -gt 0) { $c.withSub++ }
  $rows = @($u.by_turn)
  $sumReq = Sum $rows 'requests'; $sumAiu = Sum $rows 'nano_aiu'
  if ($sumReq -ne $t.requests -or $sumAiu -ne $t.nano_aiu) { $c.turnSumBad++; if ($examples.Count -lt 5) { $examples += ("requests {0} vs {1}; nano_aiu diff {2}" -f $sumReq, $t.requests, ($t.nano_aiu - $sumAiu)) } }
  $idx = @($rows | ForEach-Object { $_.turn_index })
  if (@($idx | Select-Object -Unique).Count -ne $idx.Count) { $c.dupIdx++ }
  if ($idx.Count -gt 0 -and (($idx | Measure-Object -Maximum).Maximum -ne $idx.Count - 1)) { $c.idxGap++ }
  if ($rows.Count -ne $x.turn_count) { $c.countDiff++; if ($rows.Count - $x.turn_count -eq 1) { $c.countDiffByOne++ } }
}
$c.GetEnumerator() | Sort-Object Name | ForEach-Object { "{0}: {1}" -f $_.Name, $_.Value }
$examples
