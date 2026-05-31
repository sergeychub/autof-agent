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
$runnerScriptPath = Join-Path $programDataDir "run-updater.ps1"
$updaterTaskName = "AvtoforwardAgentUpdater"

function Stop-AgentIfRunning {
    if (-not (Test-Path -LiteralPath $exePath)) {
        return
    }

    $expectedPath = [System.IO.Path]::GetFullPath($exePath)
    Get-Process -Name "WorkstationAgent" -ErrorAction SilentlyContinue | ForEach-Object {
        $process = $_
        try {
            $processPath = [System.IO.Path]::GetFullPath($process.MainModule.FileName)
            if ($processPath -ieq $expectedPath) {
                $process.CloseMainWindow() | Out-Null
                if (-not $process.WaitForExit(10000)) {
                    Stop-Process -Id $process.Id -Force
                }
            }
        }
        catch {
        }
    }
}

function Grant-ProgramDataAccess {
    $identity = [System.Security.Principal.WindowsIdentity]::GetCurrent().Name
    $grant = "${identity}:(OI)(CI)M"
    & icacls.exe $programDataDir /grant $grant /T /C | Out-Null
}

New-Item -ItemType Directory -Force -Path $programFilesDir | Out-Null
New-Item -ItemType Directory -Force -Path $programDataDir | Out-Null
New-Item -ItemType Directory -Force -Path $startMenuDir | Out-Null
Grant-ProgramDataAccess

Stop-AgentIfRunning
Get-ChildItem -LiteralPath $programFilesDir -Force | Remove-Item -Recurse -Force
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

$escapedInstallDir = $programFilesDir.Replace("'", "''")
$escapedProgramDataDir = $programDataDir.Replace("'", "''")
$runnerScript = @"
`$ErrorActionPreference = "Stop"
`$installDir = '$escapedInstallDir'
`$programDataDir = '$escapedProgramDataDir'
`$runnerDir = Join-Path `$programDataDir "updates\runner"

if (Test-Path -LiteralPath `$runnerDir) {
    Remove-Item -LiteralPath `$runnerDir -Recurse -Force
}

New-Item -ItemType Directory -Force -Path `$runnerDir | Out-Null
Copy-Item -Path (Join-Path `$installDir "*") -Destination `$runnerDir -Recurse -Force

`$updaterExe = Join-Path `$runnerDir "WorkstationAgent.Updater.exe"
if (-not (Test-Path -LiteralPath `$updaterExe)) {
    throw "Updater executable was not found: `$updaterExe"
}

`$process = Start-Process -FilePath `$updaterExe -ArgumentList @("--install-dir", "`$installDir") -Wait -PassThru -WindowStyle Hidden
exit `$process.ExitCode
"@

Set-Content -Path $runnerScriptPath -Value $runnerScript -Encoding UTF8
Grant-ProgramDataAccess

$taskAction = New-ScheduledTaskAction `
    -Execute "powershell.exe" `
    -Argument "-NoProfile -ExecutionPolicy Bypass -File `"$runnerScriptPath`""
$taskPrincipal = New-ScheduledTaskPrincipal `
    -UserId ([System.Security.Principal.WindowsIdentity]::GetCurrent().Name) `
    -LogonType Interactive `
    -RunLevel Highest
$taskSettings = New-ScheduledTaskSettingsSet `
    -AllowStartIfOnBatteries `
    -DontStopIfGoingOnBatteries `
    -MultipleInstances IgnoreNew `
    -ExecutionTimeLimit (New-TimeSpan -Minutes 30)

Register-ScheduledTask `
    -TaskName $updaterTaskName `
    -Action $taskAction `
    -Principal $taskPrincipal `
    -Settings $taskSettings `
    -Force | Out-Null

if ($StartAfterInstall) {
    Start-Process -FilePath $exePath -WorkingDirectory $programFilesDir
}

Write-Host "Installed Avtoforward Agent to $programFilesDir"
Write-Host "Persistent config folder: $programDataDir"
Write-Host "Updater scheduled task: $updaterTaskName"
