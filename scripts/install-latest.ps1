#Requires -Version 5.1
param(
    [switch]$DownloadOnly,
    [string]$ReleaseTag = 'latest'
)
$ErrorActionPreference = 'Stop'
if (-not [Environment]::Is64BitOperatingSystem) { throw 'Windows x64 is required.' }
if (-not $DownloadOnly) {
    $principal = New-Object Security.Principal.WindowsPrincipal([Security.Principal.WindowsIdentity]::GetCurrent())
    if (-not $principal.IsInRole([Security.Principal.WindowsBuiltInRole]::Administrator)) {
        throw 'Open PowerShell as administrator, then run this script again.'
    }
}
if ($ReleaseTag -notmatch '^[a-zA-Z0-9._-]+$') { throw 'Invalid release tag.' }
[Net.ServicePointManager]::SecurityProtocol = [Net.SecurityProtocolType]::Tls12
$base = 'https://github.com/sergeychub/autof-agent/releases'
$url = if ($ReleaseTag -eq 'latest') { "$base/latest/download" } else { "$base/download/$ReleaseTag" }
$downloadDir = Join-Path ([IO.Path]::GetTempPath()) ('Avtoforward-Install-' + [guid]::NewGuid().ToString('N'))
New-Item -ItemType Directory -Path $downloadDir | Out-Null
$zip = Join-Path $downloadDir 'WorkstationAgent-win-x64.zip'
Write-Host 'Downloading Avtoforward Agent for Windows x64...'
Invoke-WebRequest -UseBasicParsing -Uri "$url/WorkstationAgent-win-x64.zip" -OutFile $zip
$manifest = Invoke-RestMethod -Uri "$url/manifest.json"
if ($manifest.runtime -ne 'win-x64' -or $manifest.channel -ne 'main' -or $manifest.sha256 -notmatch '^[a-fA-F0-9]{64}$') {
    throw 'Invalid release manifest.'
}
if ((Get-Item -LiteralPath $zip).Length -ne $manifest.sizeBytes -or
    (Get-FileHash -LiteralPath $zip -Algorithm SHA256).Hash -ine $manifest.sha256) {
    throw 'Package checksum/size mismatch. Installation stopped.'
}
$target = Join-Path $downloadDir 'package'
Add-Type -AssemblyName System.IO.Compression.FileSystem
$archive = [IO.Compression.ZipFile]::OpenRead($zip)
try {
    $prefix = [IO.Path]::GetFullPath($target) + [IO.Path]::DirectorySeparatorChar
    foreach ($entry in $archive.Entries) {
        $resolved = [IO.Path]::GetFullPath((Join-Path $target $entry.FullName))
        if (-not $resolved.StartsWith($prefix, [StringComparison]::OrdinalIgnoreCase)) { throw 'Unsafe archive path.' }
    }
} finally { $archive.Dispose() }
Expand-Archive -LiteralPath $zip -DestinationPath $target
foreach ($file in @('WorkstationAgent.exe','WorkstationAgent.Updater.exe','install-agent.ps1')) {
    if (-not (Test-Path -LiteralPath (Join-Path $target $file))) { throw "Missing package file: $file" }
}
Write-Host "Verified version: $($manifest.version)"
if ($DownloadOnly) { Write-Host "Verified package: $target"; return }
& (Join-Path $target 'install-agent.ps1') -SourcePath $target -StartAfterInstall
Write-Host 'Complete. Finish workstation registration in the agent setup window.'
Write-Host 'For FUJITSU fi-7160 install PaperStream IP TWAIN (32-bit).'
