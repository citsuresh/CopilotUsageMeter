<#
.SYNOPSIS
  Publishes the tray (Release, framework-dependent unless -SelfContained) and zips it with the install scripts.
.DESCRIPTION
  Writes <OutputDirectory>\CopilotUsageMeter.Tray-<version>.zip. Needs the portable chathistory CLI in tools\chathistory (see Publish-ChatHistoryCli.ps1);
  stops with an error if it is absent so a package never ships without it. The zip holds the published files plus Install-Tray.ps1.
  Modifies: creates the staging folder under $env:TEMP\cum\package and the zip; never touches a running tray or any installed copy.
.PARAMETER OutputDirectory
  Where the zip is written (created if missing). Default: artifacts under the repository root (git-ignored).
.PARAMETER SelfContained
  Bundle the .NET runtime (much larger; no runtime needed on the target machine).
.PARAMETER KeepSymbols
  Keep .pdb files in the package (removed by default).
#>
[CmdletBinding()]
param(
    [string]$OutputDirectory,
    [switch]$SelfContained,
    [switch]$KeepSymbols
)

$ErrorActionPreference = 'Stop'
$root = (Resolve-Path (Join-Path $PSScriptRoot '..\..')).Path
if (-not $OutputDirectory) { $OutputDirectory = Join-Path $root 'artifacts' }
if (-not (Test-Path (Join-Path $root 'tools\chathistory\chathistory.exe'))) {
    throw 'tools\chathistory\chathistory.exe is missing. Run scripts\agent\Publish-ChatHistoryCli.ps1 first.'
}

$props = Get-Content (Join-Path $root 'Directory.Build.props') -Raw
if ($props -notmatch '<Version>([^<]+)</Version>') { throw 'Version not found in Directory.Build.props.' }
$version = $Matches[1]

Remove-Item Env:DOTNET_ROOT -ErrorAction SilentlyContinue
$stage = Join-Path $env:TEMP 'cum\package'
if (Test-Path $stage) { Remove-Item $stage -Recurse -Force }
$sc = if ($SelfContained) { '--self-contained' } else { '--no-self-contained' }
dotnet publish (Join-Path $root 'src\CopilotUsageMeter.Tray\CopilotUsageMeter.Tray.csproj') -c Release -o $stage $sc -v q
if ($LASTEXITCODE -ne 0) { throw "dotnet publish failed with exit code $LASTEXITCODE." }

if (-not $KeepSymbols) { Get-ChildItem $stage -Recurse -Filter *.pdb | Remove-Item -Force }
Copy-Item (Join-Path $PSScriptRoot 'Install-Tray.ps1') $stage

New-Item -ItemType Directory -Force $OutputDirectory | Out-Null
$zip = Join-Path $OutputDirectory "CopilotUsageMeter.Tray-$version.zip"
if (Test-Path $zip) { Remove-Item $zip -Force }
Compress-Archive -Path (Join-Path $stage '*') -DestinationPath $zip
$item = Get-Item $zip
"Package: $($item.FullName) ({0:N1} MB), version $version, self-contained: $($SelfContained.IsPresent)" -f ($item.Length / 1MB)
