<#
.SYNOPSIS
  Builds the Panel VSIX, installs it into the Visual Studio experimental instance only, and optionally launches a new Exp instance.
.DESCRIPTION
  Optional version bump of source.extension.vsixmanifest, MSBuild (no restore), uninstall + install via VSIXInstaller
  with /rootSuffix, `devenv /updateconfiguration`, verification of the installed identity version, then an optional
  launch. Never touches the main instance and never stops any process. Run from any directory.
.PARAMETER VsRoot            Visual Studio install root (contains Common7 and MSBuild).
.PARAMETER InstanceId        VS instance id of the Exp hive (the folder 18.0_<id>Exp).
.PARAMETER RootSuffix        Hive suffix (default Exp). Anything else is refused to protect the main instance.
.PARAMETER Version           Set the manifest identity version and the shared Directory.Build.props Version first (for example 0.1.12). Omit to keep them.
.PARAMETER Configuration     Build configuration (default Debug).
.PARAMETER SkipBuild         Reuse the existing .vsix in bin.
.PARAMETER NoLaunch          Install only; do not start a new Exp instance.
.PARAMETER SolutionPath      Solution opened by the launched instance (default CopilotUsageMeter.slnx at the repo root).
.PARAMETER ProjectPath       VSIX project (default src\CopilotUsageMeter.Panel.Vsix\CopilotUsageMeter.Panel.Vsix.csproj).
.PARAMETER ExtensionId       Identity Id used for the uninstall.
.PARAMETER UpdateTimeoutSeconds  Wait limit for /updateconfiguration.
.NOTES
  Modifies: the manifest and Directory.Build.props (only when -Version is given), build output, the Exp extension hive. Starts one devenv.
  Example: .\scripts\agent\Deploy-ExpVsix.ps1 -Version 0.1.12
#>
[CmdletBinding()]
param(
    [string]$VsRoot = 'C:\Program Files\Microsoft Visual Studio\18\Insiders',
    [string]$InstanceId = '063a2394',
    [string]$RootSuffix = 'Exp',
    [ValidatePattern('^\d+\.\d+\.\d+(\.\d+)?$')]
    [string]$Version,
    [string]$Configuration = 'Debug',
    [switch]$SkipBuild,
    [switch]$NoLaunch,
    [string]$SolutionPath,
    [string]$ProjectPath,
    [string]$ExtensionId = 'CopilotUsageMeter.Panel.citsuresh',
    [int]$UpdateTimeoutSeconds = 120
)
$ErrorActionPreference = 'Stop'
Remove-Item Env:DOTNET_ROOT -ErrorAction SilentlyContinue

if ($RootSuffix -ne 'Exp') { throw "Refusing RootSuffix '$RootSuffix': only the experimental instance is allowed." }

$repo = (Resolve-Path (Join-Path $PSScriptRoot '..\..')).Path
if (-not $SolutionPath) { $SolutionPath = Join-Path $repo 'CopilotUsageMeter.slnx' }
if (-not $ProjectPath) { $ProjectPath = Join-Path $repo 'src\CopilotUsageMeter.Panel.Vsix\CopilotUsageMeter.Panel.Vsix.csproj' }
$projDir = Split-Path $ProjectPath
$manifest = Join-Path $projDir 'source.extension.vsixmanifest'
$vsix = Join-Path $projDir "bin\$Configuration\net48\CopilotUsageMeter.Panel.vsix"
$installer = Join-Path $VsRoot 'Common7\IDE\VSIXInstaller.exe'
$devenv = Join-Path $VsRoot 'Common7\IDE\devenv.exe'
$msbuild = Join-Path $VsRoot 'MSBuild\Current\Bin\MSBuild.exe'
foreach ($f in $installer, $devenv, $msbuild, $manifest) { if (-not (Test-Path $f)) { throw "Not found: $f" } }

if ($Version) {
    $text = [IO.File]::ReadAllText($manifest)
    $updated = [regex]::Replace($text, '(<Identity[^>]*\sVersion=")[^"]*(")', "`${1}$Version`${2}", 1)
    if ($updated -ne $text) { [IO.File]::WriteAllText($manifest, $updated, (New-Object Text.UTF8Encoding($true))) }
    # The tray and the extension row/popup show the assembly version from this shared property.
    $props = Join-Path $repo 'Directory.Build.props'
    $propsText = [IO.File]::ReadAllText($props)
    $propsNew = [regex]::Replace($propsText, '(<Version>)[^<]*(</Version>)', "`${1}$Version`${2}", 1)
    if ($propsNew -ne $propsText) { [IO.File]::WriteAllText($props, $propsNew, (New-Object Text.UTF8Encoding($false))) }
    Write-Host "Manifest and Directory.Build.props version: $Version"
}

if (-not $SkipBuild) {
    & $msbuild $ProjectPath -restore:false -m "-p:Configuration=$Configuration" -p:DeployExtension=false -p:VsixDeployOnDebug=false -verbosity:quiet -nologo
    if ($LASTEXITCODE -ne 0) { throw "Build failed ($LASTEXITCODE)" }
}
if (-not (Test-Path $vsix)) { throw "VSIX not found: $vsix" }

$common = @('/quiet', "/rootSuffix:$RootSuffix", "/instanceIds:$InstanceId")
# The uninstall may legitimately fail when the extension is not installed yet, so only the install exit code is checked.
Start-Process $installer -ArgumentList ($common + "/uninstall:$ExtensionId") -Wait | Out-Null
$install = Start-Process $installer -ArgumentList ($common + "`"$vsix`"") -Wait -PassThru
if ($install.ExitCode -ne 0) { throw "VSIXInstaller failed with exit code $($install.ExitCode). Close any running Exp instance and retry." }

$update = Start-Process $devenv -ArgumentList '/RootSuffix', $RootSuffix, '/updateconfiguration' -PassThru
if (-not $update.WaitForExit($UpdateTimeoutSeconds * 1000)) { Write-Warning '/updateconfiguration did not finish in time.' }

$hive = Join-Path $env:LOCALAPPDATA "Microsoft\VisualStudio\18.0_${InstanceId}$RootSuffix\Extensions"
$installed = Get-ChildItem $hive -Recurse -Filter extension.vsixmanifest -ErrorAction SilentlyContinue |
    ForEach-Object { [regex]::Match((Get-Content $_.FullName -Raw), "<Identity[^>]*Id=`"$([regex]::Escape($ExtensionId))`"[^>]*>").Value } |
    Where-Object { $_ }
if (-not $installed) { throw 'Install verification failed: extension not found in the Exp hive.' }
Write-Host "Installed: $installed"
$expected = [regex]::Match([IO.File]::ReadAllText($manifest), '<Identity[^>]*\sVersion="([^"]*)"').Groups[1].Value
if (-not ($installed | Where-Object { $_ -match "\sVersion=`"$([regex]::Escape($expected))`"" })) {
    throw "Install verification failed: the Exp hive does not contain version $expected (stale or failed install)."
}

if (-not $NoLaunch) {
    $p = Start-Process $devenv -ArgumentList '/RootSuffix', $RootSuffix, "`"$SolutionPath`"" -PassThru
    Write-Host "Launched Exp devenv PID $($p.Id)"
}
