param(
    [string]$Configuration = "Release",
    [string]$Runtime = "win-x64"
)

$ErrorActionPreference = "Stop"
$root = Split-Path -Parent $PSScriptRoot
$project = Join-Path $root "WorkstationAgent\WorkstationAgent.csproj"
$publishDir = Join-Path $root "artifacts\publish\$Runtime"

New-Item -ItemType Directory -Force -Path $publishDir | Out-Null

$env:DOTNET_CLI_HOME = Join-Path $root ".dotnet"
$env:DOTNET_SKIP_FIRST_TIME_EXPERIENCE = "1"
$env:DOTNET_CLI_TELEMETRY_OPTOUT = "1"
$env:NUGET_PACKAGES = Join-Path $root ".nuget\packages"

dotnet publish $project `
  --configuration $Configuration `
  --runtime $Runtime `
  --self-contained true `
  /p:PublishSingleFile=false `
  /p:IncludeNativeLibrariesForSelfExtract=true `
  /p:PublishReadyToRun=false `
  --output $publishDir `
  --configfile (Join-Path $root "WorkstationAgent\NuGet.Config")

if ($LASTEXITCODE -ne 0) {
    throw "dotnet publish failed with exit code $LASTEXITCODE"
}

Write-Host "Published Avtoforward Agent to $publishDir"
