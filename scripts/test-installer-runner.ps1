param([string]$InstallerPath = (Join-Path $PSScriptRoot 'install-agent.ps1'))

$ErrorActionPreference = 'Stop'
$tokens = $null
$errors = $null
$installer = [System.Management.Automation.Language.Parser]::ParseFile($InstallerPath, [ref]$tokens, [ref]$errors)
if ($errors.Count) { throw "Installer syntax errors: $($errors.Message -join '; ')" }
$assignments = @($installer.FindAll({
    param($node)
    $node -is [System.Management.Automation.Language.AssignmentStatementAst] -and
    $node.Left -is [System.Management.Automation.Language.VariableExpressionAst] -and
    $node.Left.VariablePath.UserPath -eq 'runnerScript'
}, $true))
if ($assignments.Count -ne 1) { throw 'Expected exactly one runnerScript assignment.' }
$expression = $assignments[0].Right.Expression
if ($expression -isnot [System.Management.Automation.Language.ExpandableStringExpressionAst] -or
    $expression.StringConstantType -ne 'DoubleQuotedHereString' -or
    @($expression.NestedExpressions | Where-Object { $_ -isnot [System.Management.Automation.Language.VariableExpressionAst] }).Count) {
    throw 'Runner generation must be a here-string with variable interpolation only.'
}

function New-TestRunner([string]$Assignment, [string]$InstallDir, [string]$DataDir) {
    # Evaluate only the validated here-string assignment, never the installer or runner.
    $principal = 'INSTALLER_PRINCIPAL_MUST_NOT_LEAK'
    $escapedInstallDir = $InstallDir.Replace("'", "''")
    $escapedProgramDataDir = $DataDir.Replace("'", "''")
    & ([scriptblock]::Create($Assignment + "`n`$runnerScript"))
}

function Assert-Runner([string]$Runner, [string]$InstallDir, [string]$DataDir) {
    $runnerTokens = $null
    $runnerErrors = $null
    $ast = [System.Management.Automation.Language.Parser]::ParseInput($Runner, [ref]$runnerTokens, [ref]$runnerErrors)
    if ($runnerErrors.Count) { throw "Generated runner syntax errors: $($runnerErrors.Message -join '; ')" }
    if ($Runner.Contains('INSTALLER_PRINCIPAL_MUST_NOT_LEAK') -or
        $Runner -notmatch '(?m)^\$principal = New-Object Security\.Principal\.WindowsPrincipal\(' -or
        $Runner -notmatch 'if \(-not \$principal\.IsInRole\(\[Security\.Principal\.WindowsBuiltInRole\]::Administrator\)\)') {
        throw 'Generated runner must construct and check its own administrator principal.'
    }
    foreach ($expected in @(@('installDir', $InstallDir), @('programDataDir', $DataDir))) {
        $name = $expected[0]
        $assignment = @($ast.FindAll({
            param($node)
            $node -is [System.Management.Automation.Language.AssignmentStatementAst] -and
            $node.Left -is [System.Management.Automation.Language.VariableExpressionAst] -and
            $node.Left.VariablePath.UserPath -eq $name
        }, $true))
        if ($assignment.Count -ne 1 -or $assignment[0].Right.Expression.Value -cne $expected[1]) {
            throw "Generated runner changed the literal $name path."
        }
    }
}

$assignmentText = $assignments[0].Extent.Text
foreach ($paths in @(
    @('C:\Program Files\Avtoforward\Agent', 'C:\ProgramData\Avtoforward\Agent'),
    @('C:\Test O''Brien\$agent`name', 'C:\Test Data O''Brien\$state`name')
)) {
    $runner = New-TestRunner $assignmentText $paths[0] $paths[1]
    Assert-Runner $runner $paths[0] $paths[1]
}
# Regression check: the old unescaped interpolation must fail this same validation.
$oldAssignment = $assignmentText.Replace('`$principal', '$principal')
$rejected = $false
try { Assert-Runner (New-TestRunner $oldAssignment 'C:\Install' 'C:\Data') 'C:\Install' 'C:\Data' }
catch { $rejected = $true }
if (-not $rejected) { throw 'The regression fixture unexpectedly passed.' }

Write-Host "PASS: runner generation, administrator guard and literal paths (PowerShell $($PSVersionTable.PSVersion)); no installer/runner execution."
