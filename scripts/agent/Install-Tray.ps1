<#
.SYNOPSIS
  Per-user install or uninstall of the Copilot Usage Meter tray. No administrator rights are needed.
.DESCRIPTION
  Install: copies the files next to this script to InstallRoot and creates a Start Menu shortcut.
  Uninstall: removes InstallRoot, the shortcut, the "Start with Windows" Run value and the notification identity key under HKCU.
  Left in place on purpose: %LOCALAPPDATA%\CopilotUsageMeter (settings, alert state, logs; contains no chat text) and any saved GitHub sign-in
  (use "Log out" in Full details before uninstalling to remove it). Delete that folder by hand for a complete removal.
  Refuses to run while a tray is running from InstallRoot and never stops a process: exit the tray from its menu first.
  Refuses an InstallRoot that is a drive root, relative, the settings folder, inside it, or %LOCALAPPDATA% or a parent; Uninstall only deletes a folder that contains the tray exe, and only removes the startup entry if it points into InstallRoot.
  Modifies: InstallRoot, the shortcut and (Uninstall only, unless -SkipRegistry) two HKCU values; nothing else.
.PARAMETER Action
  Install (default) or Uninstall.
.PARAMETER SourceDirectory
  Folder holding the published tray (default: this script's folder). Install only.
.PARAMETER InstallRoot
  Default: %LOCALAPPDATA%\Programs\CopilotUsageMeter.
.PARAMETER StartMenuDirectory
  Default: the current user's Start Menu Programs folder.
.PARAMETER SkipRegistry
  Do not read or change the registry (for tests in a temporary location).
#>
[CmdletBinding()]
param(
    [ValidateSet('Install', 'Uninstall')][string]$Action = 'Install',
    [string]$SourceDirectory,
    [string]$InstallRoot = (Join-Path $env:LOCALAPPDATA 'Programs\CopilotUsageMeter'),
    [string]$StartMenuDirectory = (Join-Path $env:APPDATA 'Microsoft\Windows\Start Menu\Programs'),
    [switch]$SkipRegistry
)

$ErrorActionPreference = 'Stop'
# Windows PowerShell 5.1 leaves $PSScriptRoot empty in parameter defaults when started with -File, so the default is resolved here.
if (-not $SourceDirectory) { $SourceDirectory = $PSScriptRoot }
$exeName = 'CopilotUsageMeter.Tray.exe'
$shortcut = Join-Path $StartMenuDirectory 'Copilot Usage Meter.lnk'
$installRootFull = [IO.Path]::GetFullPath($InstallRoot).TrimEnd('\')

function Assert-SafeInstallRoot {
    # Refuses a drive root, a relative path, the data folder, anything inside it, and %LOCALAPPDATA% or any of its parents.
    if (-not [IO.Path]::IsPathRooted($InstallRoot) -or $null -eq [IO.Path]::GetDirectoryName($installRootFull)) {
        throw "InstallRoot must be a full path to a folder, not a drive root: $InstallRoot"
    }
    $local = [IO.Path]::GetFullPath($env:LOCALAPPDATA).TrimEnd('\')
    $data = Join-Path $local 'CopilotUsageMeter'
    $isWithin = { param($child, $parent) $child -eq $parent -or $child.StartsWith($parent + '\', [StringComparison]::OrdinalIgnoreCase) }
    if ((& $isWithin $installRootFull $data) -or (& $isWithin $local $installRootFull)) {
        throw "InstallRoot must not be the settings folder, %LOCALAPPDATA% or one of its parents: $installRootFull"
    }
}

function Assert-NotRunning {
    $running = Get-Process -Name ([IO.Path]::GetFileNameWithoutExtension($exeName)) -ErrorAction SilentlyContinue |
        Where-Object { $_.Path -and $_.Path.StartsWith($installRootFull + '\', [StringComparison]::OrdinalIgnoreCase) }
    if ($running) { throw "The tray is running from $installRootFull (PID $($running.Id -join ', ')). Exit it from its tray menu first." }
}

if ($Action -eq 'Install') {
    $source = [IO.Path]::GetFullPath($SourceDirectory).TrimEnd('\')
    if (-not (Test-Path (Join-Path $source $exeName))) { throw "$exeName was not found in $source." }
    if ($source -eq $installRootFull) { throw 'The source and the install folder are the same.' }
    Assert-SafeInstallRoot
    Assert-NotRunning
    New-Item -ItemType Directory -Force $installRootFull | Out-Null
    Copy-Item (Join-Path $source '*') $installRootFull -Recurse -Force
    New-Item -ItemType Directory -Force $StartMenuDirectory | Out-Null
    $shell = New-Object -ComObject WScript.Shell
    $link = $shell.CreateShortcut($shortcut)
    $link.TargetPath = Join-Path $installRootFull $exeName
    $link.WorkingDirectory = $installRootFull
    $link.Description = 'Copilot Usage Meter'
    $link.Save()
    "Installed to $installRootFull. Start it from the Start Menu ('Copilot Usage Meter'). Needs the .NET 10 Desktop Runtime unless the package is self-contained."
}
else {
    Assert-SafeInstallRoot
    Assert-NotRunning
    if ((Test-Path -LiteralPath $installRootFull) -and -not (Test-Path -LiteralPath (Join-Path $installRootFull $exeName))) {
        throw "$installRootFull does not contain $exeName, so it is not removed."
    }
    if (Test-Path -LiteralPath $installRootFull) { Remove-Item -LiteralPath $installRootFull -Recurse -Force }
    if (Test-Path -LiteralPath $shortcut) { Remove-Item -LiteralPath $shortcut -Force }
    if (-not $SkipRegistry) {
        $runKey = 'HKCU:\Software\Microsoft\Windows\CurrentVersion\Run'
        $runValue = (Get-ItemProperty -Path $runKey -Name 'CopilotUsageMeter' -ErrorAction SilentlyContinue).CopilotUsageMeter
        # A startup entry that points at another copy (for example a development build) is left alone.
        if ($runValue -and $runValue.StartsWith('"' + $installRootFull + '\', [StringComparison]::OrdinalIgnoreCase)) {
            Remove-ItemProperty -Path $runKey -Name 'CopilotUsageMeter' -ErrorAction SilentlyContinue
        }
        Remove-Item -Path 'HKCU:\Software\Classes\AppUserModelId\CopilotUsageMeter.Tray' -Recurse -ErrorAction SilentlyContinue
    }
    "Uninstalled. Kept: $(Join-Path $env:LOCALAPPDATA 'CopilotUsageMeter') (settings, alert state, logs) and any saved GitHub sign-in (use Log out in Full details to remove it)."
}
