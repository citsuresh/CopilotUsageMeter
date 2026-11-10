<#
.SYNOPSIS
Read-only: extract indexed UI element bounds without printing accessibility names or chat titles.
.PARAMETER Hwnd
Exact task-owned target window handle.
.PARAMETER AutomationId
Element ID to select from the filtered UI tree.
.PARAMETER NameContains
Optional static accessibility-name filter; never supply a chat title or text.
.PARAMETER ControlType
UIA type filter, default Button.
.PARAMETER Index
Zero-based match index; the returned MatchCount permits explicit ambiguity checks.
.PARAMETER MaxDepth
UIA traversal depth, default 40.
.PARAMETER ExePath
Path to the installed AgentDebugToolkit UI automation CLI.
#>
param(
    [Parameter(Mandatory=$true)][string]$Hwnd,
    [Parameter(Mandatory=$true)][string]$AutomationId,
    [string]$NameContains,
    [string]$ControlType = 'Button',
    [ValidateRange(0,10000)][int]$Index = 0,
    [ValidateRange(1,100)][int]$MaxDepth = 40,
    [string]$ExePath = 'C:\MyFiles\Git\AgentDebugToolkit\src\AgentDebugToolkit.UiAutomation.Cli\bin\Release\net8.0-windows\agentdebug-ui.exe'
)
$ErrorActionPreference = 'Stop'
Remove-Item Env:DOTNET_ROOT -ErrorAction SilentlyContinue
$arguments = @('inspect', '--hwnd', $Hwnd, '--controlType', $ControlType, '--maxDepth', $MaxDepth)
if ($NameContains) { $arguments += @('--nameContains', $NameContains) }
$tree = (& $ExePath @arguments) | ConvertFrom-Json
if (-not $tree.success) { throw 'UI bounds inspection failed.' }
function Find-Bounds($node) {
    if ($node.AutomationId -eq $AutomationId) { $node.BoundingRect }
    foreach ($child in $node.Children) { Find-Bounds $child }
}
$matches = @(Find-Bounds $tree.root)
if ($Index -ge $matches.Count) { throw 'Requested UI match is unavailable.' }
$bounds = $matches[$Index]
if ($null -eq $bounds -or $bounds.Width -le 0 -or $bounds.Height -le 0) { throw 'UI match has no visible bounds.' }
[pscustomobject]@{ MatchCount = $matches.Count; Index = $Index; Bounds = $bounds }