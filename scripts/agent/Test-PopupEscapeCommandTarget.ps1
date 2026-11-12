<#
.SYNOPSIS
  Tests the built VSIX Escape target against its real restored Visual Studio SDK assemblies.
.PARAMETER AssemblyPath
  Path to the built CopilotUsageMeter.Panel.Vsix.dll.
.PARAMETER InteropAssemblyPath
  Path to the matching restored Microsoft.VisualStudio.Interop.dll (net472).
.PARAMETER FrameworkAssemblyPath
  Path to the matching restored Microsoft.VisualStudio.Shell.Framework.dll (net472).
.NOTES
  Read-only. Run in a fresh Windows PowerShell process. No Visual Studio UI or files are changed.
#>
[CmdletBinding()]
param(
    [Parameter(Mandatory)][string]$AssemblyPath,
    [Parameter(Mandatory)][string]$InteropAssemblyPath,
    [Parameter(Mandatory)][string]$FrameworkAssemblyPath
)
$ErrorActionPreference = 'Stop'
Set-StrictMode -Version Latest

$interop = [Reflection.Assembly]::LoadFrom((Resolve-Path $InteropAssemblyPath).Path)
$framework = [Reflection.Assembly]::LoadFrom((Resolve-Path $FrameworkAssemblyPath).Path)
$assembly = [Reflection.Assembly]::LoadFrom((Resolve-Path $AssemblyPath).Path)
$targetType = $assembly.GetType('CopilotUsageMeter.Panel.Vsix.CompactUsageView+EscapeCommandTarget', $true)
$commandType = $interop.GetType('Microsoft.VisualStudio.OLE.Interop.OLECMD', $true)
$constants = $framework.GetType('Microsoft.VisualStudio.VSConstants', $true)
$notSupported = [int]$interop.GetType('Microsoft.VisualStudio.OLE.Interop.Constants', $true).
    GetField('OLECMDERR_E_NOTSUPPORTED').GetRawConstantValue()
$enabled = [uint32]3
$state = @{ Open = $false; Dismissals = 0 }
$isOpen = [Func[bool]]{ $state.Open }
$dismiss = [Action]{ $state.Open = $false; $state.Dismissals++ }
$target = [Activator]::CreateInstance($targetType, [object[]]@($isOpen, $dismiss))
$query = $targetType.GetMethod('QueryStatus')
$exec = $targetType.GetMethod('Exec')
$cases = 0

function Test-Command {
    param([Guid]$Group, [uint32]$Id, [bool]$Open, [bool]$ShouldHandle)
    $state.Open = $Open
    $before = $state.Dismissals
    $commands = [Array]::CreateInstance($commandType, 1)
    $command = [Activator]::CreateInstance($commandType)
    $command.cmdID = $Id
    $commands.SetValue($command, 0)
    $status = [int]$query.Invoke($target, [object[]]@($Group, [uint32]1, $commands, [IntPtr]::Zero))
    $expectedResult = $notSupported
    $expectedFlags = [uint32]0
    if ($ShouldHandle) { $expectedResult = 0; $expectedFlags = $enabled }
    if ($status -ne $expectedResult -or $commands.GetValue(0).cmdf -ne $expectedFlags) {
        throw "QueryStatus mismatch: group=$Group id=$Id open=$Open result=$status flags=$($commands.GetValue(0).cmdf)."
    }
    $result = [int]$exec.Invoke($target, [object[]]@($Group, $Id, [uint32]0, [IntPtr]::Zero, [IntPtr]::Zero))
    $expectedDismissals = $before
    if ($ShouldHandle) { $expectedDismissals++ }
    if ($result -ne $expectedResult -or $state.Dismissals -ne $expectedDismissals -or
        $state.Open -ne ($Open -and -not $ShouldHandle)) {
        throw "Exec mismatch: group=$Group id=$Id open=$Open result=$result dismissals=$($state.Dismissals - $before)."
    }
    $script:cases++
}

$groups = @(
    @{ Field = 'GUID_VSStandardCommandSet97'; Enum = 'VSStd97CmdID'; Escape = 'Escape' },
    @{ Field = 'VSStd2K'; Enum = 'VSStd2KCmdID'; Escape = 'CANCEL' }
)
foreach ($group in $groups) {
    $guid = [Guid]$constants.GetField($group.Field).GetValue($null)
    $enumType = $constants.GetNestedType($group.Enum)
    $escapeId = [uint32][Enum]::Parse($enumType, $group.Escape)
    $ids = @([Enum]::GetValues($enumType) | ForEach-Object { [uint32]$_ }) +
        @([uint32]103, [uint32]154, [uint32]743, [uint32]::MaxValue) | Sort-Object -Unique
    foreach ($id in $ids) {
        Test-Command $guid $id $false $false
        Test-Command $guid $id $true ($id -eq $escapeId)
    }
}

$unknownGroup = [Guid]'0ab11476-75cd-46c9-b85e-a1a0d647d150'
foreach ($id in [uint32]103, [uint32]154, [uint32]743) {
    Test-Command $unknownGroup $id $false $false
    Test-Command $unknownGroup $id $true $false
}
$state.Open = $true
$commands = [Array]::CreateInstance($commandType, 2)
$escape = [Activator]::CreateInstance($commandType)
$escape.cmdID = [uint32]743
$commands.SetValue($escape, 0)
$other = [Activator]::CreateInstance($commandType)
$other.cmdID = [uint32]154
$commands.SetValue($other, 1)
$group97 = [Guid]$constants.GetField('GUID_VSStandardCommandSet97').GetValue($null)
$result = [int]$query.Invoke($target, [object[]]@($group97, [uint32]2, $commands, [IntPtr]::Zero))
if ($result -ne 0 -or $commands.GetValue(0).cmdf -ne $enabled -or $commands.GetValue(1).cmdf -ne 0) {
    throw 'Mixed QueryStatus must enable only Escape, not the unrelated Cancel command.'
}
$cases++
Write-Output "PASS: $cases command cases (open/closed, all SDK command IDs, unknown group, mixed status); $($state.Dismissals) dismissals."