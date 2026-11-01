#requires -Version 5.1
[CmdletBinding()]
param(
    [string]$YakPath = (Join-Path $env:ProgramFiles 'Rhino 8\System\Yak.exe'),
    [switch]$NoBuild
)
$ErrorActionPreference = 'Stop'
if ($env:OS -ne 'Windows_NT') { throw 'Use build-package.sh on macOS.' }
if (-not (Test-Path -LiteralPath $YakPath -PathType Leaf)) { throw 'Provide the Rhino 8 Yak.exe path with -YakPath.' }
$projectRoot = Split-Path -Parent $PSScriptRoot
function Invoke-Checked([string]$Program, [string[]]$Arguments) {
    & $Program @Arguments
    if ($LASTEXITCODE -ne 0) { throw "Command failed with exit code $LASTEXITCODE." }
}
Push-Location $projectRoot
try {
    $checks = 'not-rerun'
    if (-not $NoBuild) {
        Invoke-Checked 'dotnet' @('build', 'Aholo.sln', '-c', 'Release')
        Invoke-Checked 'dotnet' @('test', 'tests/Aholo.Tests/Aholo.Tests.csproj', '-c', 'Release', '--no-build', '--no-restore')
        $checks = 'passed'
    }
    $python = Get-Command py.exe -ErrorAction SilentlyContinue
    if ($null -ne $python) {
        Invoke-Checked $python.Source @('-3', 'scripts/package.py', '--platform', 'win', '--yak', $YakPath, '--checks', $checks)
    } else {
        Invoke-Checked 'python' @('scripts/package.py', '--platform', 'win', '--yak', $YakPath, '--checks', $checks)
    }
} finally { Pop-Location }
