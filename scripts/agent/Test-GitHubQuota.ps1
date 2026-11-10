<#
.SYNOPSIS
  Read-only probe of the UNSUPPORTED GitHub copilot_internal/user endpoint using the user's own `gh` sign-in.
.DESCRIPTION
  Reads the token with `gh auth token` into a local variable only (never written to disk, logs, the
  console, the command line or the environment), makes one GET request, and prints only status,
  field names, booleans and numbers. Identity strings, endpoints and any other text are never printed.
  If `gh` is not signed in, it reports that and exits; it never starts a login or stores anything.
  Sign in/out yourself with: gh auth login --web  /  gh auth logout --hostname github.com
.PARAMETER Hostname
  GitHub host to use. Default github.com.
.NOTES
  Modifies nothing. The endpoint is undocumented and may change or be blocked without notice.
  Account-reported numbers; never add them to local analytics or treat them as the same units.
#>
[CmdletBinding()]
param([string]$Hostname = 'github.com')

$ErrorActionPreference = 'Stop'
[Net.ServicePointManager]::SecurityProtocol = [Net.SecurityProtocolType]::Tls12

$token = $null
try {
    $token = (& gh auth token --hostname $Hostname 2>$null | Out-String).Trim()
} catch { $token = $null }
if ([string]::IsNullOrWhiteSpace($token)) {
    Write-Output 'status: not-signed-in (run: gh auth login --web)'
    return
}

$status = $null
$json = $null
try {
    $headers = @{ Authorization = "Bearer $token"; Accept = 'application/json'; 'User-Agent' = 'CopilotUsageMeter-probe' }
    $json = Invoke-RestMethod -Uri "https://api.$Hostname/copilot_internal/user" -Headers $headers -Method Get
    $status = 200
} catch {
    if ($_.Exception.Response) { $status = [int]$_.Exception.Response.StatusCode } else { $status = 'network-error' }
} finally {
    $token = $null
    $headers = $null
}

Write-Output "status: $status"
if ($status -ne 200) { Write-Output 'result: unavailable'; return }

# Strings are shown only for these non-identifying keys; every other string is masked.
$allowedStrings = '^(copilot_plan|access_type_sku|quota_reset_date|quota_reset_date_utc)$'
function Show-Node($node, $prefix) {
    foreach ($p in $node.PSObject.Properties) {
        $name = "$prefix$($p.Name)"; $v = $p.Value
        if ($v -is [System.Management.Automation.PSCustomObject]) { Show-Node $v "$name." }
        elseif ($v -is [System.Array]) { Write-Output "$name = array[$($v.Count)]" }
        elseif ($v -is [bool] -or $v -is [int] -or $v -is [long] -or $v -is [double] -or $v -is [decimal]) { Write-Output "$name = $v" }
        elseif ($v -is [string] -and $p.Name -match $allowedStrings) { Write-Output "$name = $v" }
        else { Write-Output "$name = <masked>" }
    }
}
Show-Node $json ''
