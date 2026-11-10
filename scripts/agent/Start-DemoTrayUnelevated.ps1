<#
Purpose: Launch the demo tray through a same-user desktop Explorer folder broker.
Parameters: TrayExecutablePath, WorkingDirectory, ElevationProbeScript, WaitSeconds,
ShowStatus and DesktopExplorerProcessId control the executable, broker folder, token
probe, startup wait, UI and explicit desktop PID when multiple Explorer processes exist.
Modifies: starts one tray process; may briefly open/close its own Explorer folder window.
Does not modify repository files, stop existing processes, or close existing folder windows.
#>
[CmdletBinding()]
param(
    [string] $TrayExecutablePath = (Join-Path $PSScriptRoot '..\..\src\CopilotUsageMeter.Tray\bin\Debug\net10.0-windows\CopilotUsageMeter.Tray.exe'),
    [string] $WorkingDirectory = (Join-Path $PSScriptRoot '..\..'),
    [string] $ElevationProbeScript = (Join-Path $PSScriptRoot 'Get-ProcessElevation.ps1'),
    [ValidateRange(1, 30)]
    [int] $WaitSeconds = 3,
    [switch] $ShowStatus,
    [ValidateRange(0, 2147483647)]
    [int] $DesktopExplorerProcessId = 0
)

$ErrorActionPreference = 'Stop'
$TrayExecutablePath = (Resolve-Path -LiteralPath $TrayExecutablePath).ProviderPath
$WorkingDirectory = (Resolve-Path -LiteralPath $WorkingDirectory).ProviderPath
if ([IO.Path]::GetFileName($TrayExecutablePath) -ne 'CopilotUsageMeter.Tray.exe') {
    throw 'Only the CopilotUsageMeter demo tray executable is supported.'
}
if (@(Get-CimInstance Win32_Process -Filter "Name='CopilotUsageMeter.Tray.exe'").Count -ne 0) {
    throw 'A tray is already running; refusing to replace or activate it.'
}
$session = (Get-Process -Id $PID).SessionId
$filter = "Name='explorer.exe' AND SessionId=$session"
if ($DesktopExplorerProcessId -ne 0) { $filter += " AND ProcessId=$DesktopExplorerProcessId" }
$desktop = @(Get-CimInstance Win32_Process -Filter $filter)
if ($desktop.Count -ne 1) { throw 'Cannot uniquely identify desktop Explorer; supply the PID owning Shell_TrayWnd with -DesktopExplorerProcessId.' }
$owner = Invoke-CimMethod -InputObject $desktop[0] -MethodName GetOwnerSid
$identity = [Security.Principal.WindowsIdentity]::GetCurrent()
try {
    if ($owner.Sid -ne $identity.User.Value) { throw 'Desktop Explorer belongs to a different user.' }
}
finally { $identity.Dispose() }
if ((& $ElevationProbeScript -TargetProcessId $desktop[0].ProcessId).IsElevated) {
    throw 'The desktop Explorer is elevated; it cannot provide this verification.'
}

$shell = New-Object -ComObject Shell.Application
$matches = @($shell.Windows() | Where-Object {
    $_.FullName -eq (Join-Path $env:WINDIR 'explorer.exe') -and $_.Document.Folder.Self.Path -eq $WorkingDirectory
})
if ($matches.Count -gt 1) { throw 'More than one folder broker matches the working directory.' }
$opened = $matches.Count -eq 0
if ($opened) {
    $shell.Explore($WorkingDirectory)
    Start-Sleep -Seconds $WaitSeconds
    $matches = @($shell.Windows() | Where-Object {
        $_.FullName -eq (Join-Path $env:WINDIR 'explorer.exe') -and $_.Document.Folder.Self.Path -eq $WorkingDirectory
    })
}
if ($matches.Count -ne 1) { throw 'Cannot identify the desktop folder broker; no tray was launched.' }
$folder = $matches[0]
try {
    $arguments = ''
    if ($ShowStatus) { $arguments = '--show-status' }
    $folder.Document.Application.ShellExecute($TrayExecutablePath, $arguments, $WorkingDirectory, 'open', 1)
    Start-Sleep -Seconds $WaitSeconds
    $tray = @(Get-CimInstance Win32_Process -Filter "Name='CopilotUsageMeter.Tray.exe'")
    if ($tray.Count -ne 1 -or $tray[0].ExecutablePath -ne $TrayExecutablePath) {
        throw 'Cannot uniquely identify the started demo tray; inspect processes before retrying.'
    }
    $result = & $ElevationProbeScript -TargetProcessId $tray[0].ProcessId
    if ($result.IsElevated) { throw "Started tray PID $($tray[0].ProcessId) is elevated; interoperability is not verified." }
    $result
}
finally { if ($opened) { $folder.Quit() } }