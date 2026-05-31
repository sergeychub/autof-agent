param(
    [string]$Configuration = "Release",
    [string]$Runtime = "win-x64",
    [string]$Version = ""
)

$ErrorActionPreference = "Stop"
$root = Split-Path -Parent $PSScriptRoot
$agentProject = Join-Path $root "WorkstationAgent\WorkstationAgent.csproj"
$updaterProject = Join-Path $root "WorkstationAgent.Updater\WorkstationAgent.Updater.csproj"
$publishDir = Join-Path $root "artifacts\publish\$Runtime"

New-Item -ItemType Directory -Force -Path $publishDir | Out-Null
Get-ChildItem -LiteralPath $publishDir -Force | Remove-Item -Recurse -Force

$env:DOTNET_CLI_HOME = Join-Path $root ".dotnet"
$env:DOTNET_SKIP_FIRST_TIME_EXPERIENCE = "1"
$env:DOTNET_CLI_TELEMETRY_OPTOUT = "1"
$env:NUGET_PACKAGES = Join-Path $root ".nuget\packages"

function Get-VersionProperties {
    if ([string]::IsNullOrWhiteSpace($Version)) {
        return @()
    }

    $versionCore = ($Version -split "\+")[0] -replace "-.*$", ""
    $parts = New-Object System.Collections.Generic.List[string]
    foreach ($part in $versionCore.Split(".")) {
        if (-not [string]::IsNullOrWhiteSpace($part)) {
            $parts.Add($part)
        }
    }

    while ($parts.Count -lt 3) {
        $parts.Add("0")
    }

    $assemblyVersion = "$($parts[0]).$($parts[1]).$($parts[2]).0"
    return @(
        "/p:Version=$versionCore",
        "/p:AssemblyVersion=$assemblyVersion",
        "/p:FileVersion=$assemblyVersion",
        "/p:InformationalVersion=$Version"
    )
}

function Publish-Project {
    param(
        [string]$ProjectPath
    )

    $publishArgs = @(
        $ProjectPath,
        "--configuration", $Configuration,
        "--runtime", $Runtime,
        "--self-contained", "true",
        "/p:PublishSingleFile=false",
        "/p:IncludeNativeLibrariesForSelfExtract=true",
        "/p:PublishReadyToRun=false",
        "--output", $publishDir,
        "--configfile", (Join-Path $root "WorkstationAgent\NuGet.Config")
    ) + (Get-VersionProperties)

    dotnet publish @publishArgs
    if ($LASTEXITCODE -ne 0) {
        throw "dotnet publish failed for $ProjectPath with exit code $LASTEXITCODE"
    }
}

Publish-Project -ProjectPath $agentProject
Publish-Project -ProjectPath $updaterProject

Write-Host "Published Avtoforward Agent to $publishDir"
