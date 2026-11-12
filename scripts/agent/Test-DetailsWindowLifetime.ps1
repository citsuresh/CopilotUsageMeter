<#
Purpose: Verify live Full details recreation and normal-bounds restoration through Exp.
Parameters: exact tray/Exp PIDs, optional physical-pixel test bounds (left, top, width,
height), static control labels and toolkit/cursor script paths.
Modifies: the selected windows' geometry/lifetime and foreground focus only. Restores
the initial normal bounds on success. Does not stop processes or modify files/accounts.
#>
[CmdletBinding()]
param(
    [Parameter(Mandatory=$true)][ValidateRange(1,2147483647)][int]$TrayProcessId,
    [Parameter(Mandatory=$true)][ValidateRange(1,2147483647)][int]$VsProcessId,
    [ValidateCount(4,4)][double[]]$TestBounds,
    [string]$DetailsTitle = 'Copilot Usage Meter - Full details',
    [string]$ChevronName = 'Show Copilot usage details',
    [string]$DetailsButtonName = 'Full details',
    [string]$UiExePath = 'C:\MyFiles\Git\AgentDebugToolkit\src\AgentDebugToolkit.UiAutomation.Cli\bin\Release\net8.0-windows\win-x64\agentdebug-ui.exe',
    [string]$CursorScriptPath = (Join-Path $env:USERPROFILE '.copilot\skills\ui-interaction\scripts\AgentCursor.ps1')
)

$ErrorActionPreference = 'Stop'
Remove-Item Env:DOTNET_ROOT -ErrorAction SilentlyContinue
Add-Type -AssemblyName UIAutomationClient
Add-Type -AssemblyName UIAutomationTypes
. $CursorScriptPath

function Invoke-Ui([string[]]$Arguments) {
    $result = & $UiExePath @Arguments | ConvertFrom-Json
    if ($LASTEXITCODE -ne 0 -or -not $result.success) {
        throw "UI command failed: $($Arguments -join ' ') ($($result.error))."
    }
    $result
}

function Get-Details {
    $windows = Invoke-Ui @('list-windows', '--pid', "$TrayProcessId")
    $matches = @($windows.windows | Where-Object { $_.Title -eq $DetailsTitle })
    if ($matches.Count -ne 1) { throw 'Expected exactly one Full details window.' }
    $matches[0]
}

function Get-Element($Window) {
    [System.Windows.Automation.AutomationElement]::FromHandle([IntPtr][Convert]::ToInt64($Window.Hwnd,16))
}

function Open-Details {
    $windows = Invoke-Ui @('list-windows', '--pid', "$VsProcessId")
    $main = @($windows.windows | Where-Object { -not $_.OwnerHwnd })
    if ($main.Count -ne 1) { throw 'Cannot uniquely identify the Exp main window.' }
    $handle = $main[0].Hwnd
    if (-not (Set-AgentForegroundWindow -Handle ([IntPtr][Convert]::ToInt64($handle,16)))) {
        throw 'Cannot activate Exp.'
    }
    $null = Invoke-Ui @('wait-for-element', '--hwnd', $handle, '--strategy', 'Name',
        '--value', $ChevronName, '--controlType', 'Button', '--state', 'enabled', '--timeoutMs', '5000')
    $null = Invoke-Ui @('click', '--hwnd', $handle, '--scopeHwnd', $handle,
        '--strategy', 'Name', '--value', $ChevronName, '--controlType', 'Button')
    $buttonCondition = New-Object System.Windows.Automation.AndCondition(
        (New-Object System.Windows.Automation.PropertyCondition([System.Windows.Automation.AutomationElement]::NameProperty, $DetailsButtonName)),
        (New-Object System.Windows.Automation.PropertyCondition([System.Windows.Automation.AutomationElement]::ControlTypeProperty, [System.Windows.Automation.ControlType]::Button)))
    $deadline = [DateTime]::UtcNow.AddSeconds(5)
    do {
        $windows = Invoke-Ui @('list-windows', '--pid', "$VsProcessId")
        $popup = @($windows.windows | Where-Object {
            if ($_.OwnerHwnd -ne $handle) { return $false }
            $element = Get-Element $_
            $buttons = $element.FindAll([System.Windows.Automation.TreeScope]::Descendants, $buttonCondition)
            if ($buttons.Count -gt 1) { throw 'Full details button is not unique in the owned window.' }
            $buttons.Count -eq 1
        })
        if ($popup.Count -gt 0) { break }
        Start-Sleep -Milliseconds 100
    } while ([DateTime]::UtcNow -lt $deadline)
    if ($popup.Count -ne 1) { throw 'Cannot uniquely identify the usage popup.' }
    $null = Invoke-Ui @('wait-for-element', '--hwnd', $popup[0].Hwnd, '--strategy', 'Name',
        '--value', $DetailsButtonName, '--controlType', 'Button', '--state', 'enabled', '--timeoutMs', '5000')
    $null = Invoke-Ui @('click', '--hwnd', $handle, '--scopeHwnd', $popup[0].Hwnd,
        '--strategy', 'Name', '--value', $DetailsButtonName, '--controlType', 'Button')
}

$tray = Get-CimInstance Win32_Process -Filter "ProcessId=$TrayProcessId"
$vs = Get-CimInstance Win32_Process -Filter "ProcessId=$VsProcessId"
if (-not $tray -or [IO.Path]::GetFileName($tray.ExecutablePath) -ne 'CopilotUsageMeter.Tray.exe') {
    throw 'The target PID is not the tray.'
}
if (-not $vs -or [IO.Path]::GetFileName($vs.ExecutablePath) -ne 'devenv.exe' -or
    $vs.CommandLine -notmatch '(?i)[/-]RootSuffix\s+"?Exp"?(?:\s|$)') {
    throw 'Only an explicitly identified experimental Visual Studio is supported.'
}

$initial = Get-Details
$element = Get-Element $initial
$pattern = [System.Windows.Automation.WindowPattern]$element.GetCurrentPattern([System.Windows.Automation.WindowPattern]::Pattern)
if ($pattern.Current.WindowVisualState -ne [System.Windows.Automation.WindowVisualState]::Normal) {
    throw 'Start with Full details in normal window state.'
}
$original = $initial.BoundingRect
if (-not $TestBounds) { $TestBounds = @($original.X,$original.Y,$original.Width,$original.Height) }
if ($TestBounds[2] -le 0 -or $TestBounds[3] -le 0) { throw 'Test dimensions must be positive.' }

$null = Invoke-Ui @('cursor-session', 'start', '--kind', 'UiAutomation')
Enable-AgentCursor
try {
    $transform = [System.Windows.Automation.TransformPattern]$element.GetCurrentPattern([System.Windows.Automation.TransformPattern]::Pattern)
    $transform.Move($TestBounds[0],$TestBounds[1])
    $transform.Resize($TestBounds[2],$TestBounds[3])
    $expected = (Get-Details).BoundingRect
    $properties = @('X','Y','Width','Height')
    for ($index = 0; $index -lt $properties.Count; $index++) {
        if ([Math]::Abs($expected.($properties[$index]) - $TestBounds[$index]) -gt 2) {
            throw 'The requested test bounds were not applied.'
        }
    }
    foreach ($state in @('Normal','Maximized','Minimized')) {
        $before = Get-Details
        $element = Get-Element $before
        $pattern = [System.Windows.Automation.WindowPattern]$element.GetCurrentPattern([System.Windows.Automation.WindowPattern]::Pattern)
        $pattern.SetWindowVisualState([System.Windows.Automation.WindowVisualState]::$state)
        if ($pattern.Current.WindowVisualState.ToString() -ne $state) {
            throw "The requested window state $state was not applied."
        }
        $pattern.Close()
        $null = Invoke-Ui @('wait-for-window-change', '--pid', "$TrayProcessId", '--until', 'settle',
            '--timeoutMs', '5000', '--settleMs', '200')
        $windows = Invoke-Ui @('list-windows', '--pid', "$TrayProcessId")
        if (@($windows.windows | Where-Object { $_.Title -eq $DetailsTitle }).Count -ne 0) {
            throw "Details did not close from state $state."
        }
        if (-not (Get-Process -Id $TrayProcessId -ErrorAction SilentlyContinue)) {
            throw 'Closing details stopped the tray.'
        }
        Open-Details
        $null = Invoke-Ui @('wait-for-window-change', '--pid', "$TrayProcessId", '--titleContains', $DetailsTitle,
            '--until', 'match', '--timeoutMs', '5000', '--settleMs', '200')
        $after = Get-Details
        if ($after.Hwnd -eq $before.Hwnd) { throw 'Expected a recreated window handle.' }
        foreach ($property in @('X','Y','Width','Height')) {
            if ([Math]::Abs($after.BoundingRect.$property - $expected.$property) -gt 2) {
                throw "Saved $property was not restored after closing from $state."
            }
        }
        $element = Get-Element $after
        $pattern = [System.Windows.Automation.WindowPattern]$element.GetCurrentPattern([System.Windows.Automation.WindowPattern]::Pattern)
        if ($pattern.Current.WindowVisualState -ne [System.Windows.Automation.WindowVisualState]::Normal) {
            throw 'Recreated details must open in normal state.'
        }
        [pscustomobject]@{ State = $state; Passed = $true; Recreated = $true; Bounds = $after.BoundingRect }
    }
    $transform = [System.Windows.Automation.TransformPattern]$element.GetCurrentPattern([System.Windows.Automation.TransformPattern]::Pattern)
    $transform.Move($original.X,$original.Y)
    $transform.Resize($original.Width,$original.Height)
}
finally {
    Restore-AgentCursor
    $null = Invoke-Ui @('cursor-session', 'stop', '--kind', 'UiAutomation')
}