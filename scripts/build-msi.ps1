param(
    [string]$Configuration = "Release",
    [string]$Runtime = "win-x64"
)

$ErrorActionPreference = "Stop"
$wix = Get-Command wix.exe -ErrorAction SilentlyContinue

if (-not $wix) {
    throw "WiX CLI is not installed. Production runtime and install scripts are ready, but MSI build requires WiX Toolset."
}

Write-Host "WiX detected at $($wix.Source)."
Write-Host "Use publish-production.ps1 first, then wire a WiX package against artifacts\publish\$Runtime."
