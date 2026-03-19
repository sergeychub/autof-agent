param(
    [string]$SourcePath = "",
    [switch]$StartAfterInstall
)

$ErrorActionPreference = "Stop"
$root = Split-Path -Parent $PSScriptRoot
if ([string]::IsNullOrWhiteSpace($SourcePath)) {
    $SourcePath = Join-Path $root "artifacts\publish\win-x64"
}

if (-not (Test-Path $SourcePath)) {
    throw "Publish folder not found: $SourcePath"
}

$programFilesDir = Join-Path ${env:ProgramFiles} "Avtoforward\Agent"
$programDataDir = Join-Path ${env:ProgramData} "Avtoforward\Agent"
$startMenuDir = Join-Path ${env:ProgramData} "Microsoft\Windows\Start Menu\Programs\Avtoforward"
$exePath = Join-Path $programFilesDir "WorkstationAgent.exe"

New-Item -ItemType Directory -Force -Path $programFilesDir | Out-Null
New-Item -ItemType Directory -Force -Path $programDataDir | Out-Null
New-Item -ItemType Directory -Force -Path $startMenuDir | Out-Null

Copy-Item -Path (Join-Path $SourcePath "*") -Destination $programFilesDir -Recurse -Force

$wsh = New-Object -ComObject WScript.Shell
$shortcut = $wsh.CreateShortcut((Join-Path $startMenuDir "Avtoforward Agent.lnk"))
$shortcut.TargetPath = $exePath
$shortcut.WorkingDirectory = $programFilesDir
$shortcut.Save()

$runKey = "HKCU:\Software\Microsoft\Windows\CurrentVersion\Run"
New-Item -Path $runKey -Force | Out-Null
$runValue = '"' + $exePath + '"'
Set-ItemProperty -Path $runKey -Name "AvtoforwardAgent" -Value $runValue

if ($StartAfterInstall) {
    Start-Process -FilePath $exePath
}

Write-Host "Installed Avtoforward Agent to $programFilesDir"
Write-Host "Persistent config folder: $programDataDir"
