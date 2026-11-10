<#
.SYNOPSIS
Publishes the local toolkit CLI beside the tray's build input; modifies build output only.
.PARAMETER ToolkitRoot
Toolkit source checkout, defaulting to the repository's submodule working tree.
.PARAMETER OutputDirectory
Portable output directory. Existing files are not deleted.
.PARAMETER Configuration
Build configuration, default Release.
.PARAMETER Runtime
Self-contained target runtime, default win-x64.
.PARAMETER NuGetSource
Package source used for runtime/dependency restore.
#>
param(
    [string]$ToolkitRoot = (Join-Path $PSScriptRoot '..\..\external\VsChatHistoryToolkit'),
    [string]$OutputDirectory = (Join-Path $PSScriptRoot '..\..\tools\chathistory'),
    [string]$Configuration = 'Release',
    [string]$Runtime = 'win-x64',
    [string]$NuGetSource = 'https://api.nuget.org/v3/index.json'
)
$ErrorActionPreference = 'Stop'
Remove-Item Env:DOTNET_ROOT -ErrorAction SilentlyContinue
$project = Join-Path $ToolkitRoot 'src\VsChatHistoryToolkit.Cli\VsChatHistoryToolkit.Cli.csproj'
if (-not (Test-Path -LiteralPath $project)) { throw 'Toolkit source is absent; initialize the submodule first.' }
& dotnet publish $project --configuration $Configuration --runtime $Runtime --self-contained true '-p:PublishSingleFile=true' --output $OutputDirectory --source $NuGetSource
if ($LASTEXITCODE -ne 0) { throw 'Toolkit publish failed.' }