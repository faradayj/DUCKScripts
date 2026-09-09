# Verification script for CoreDUCK skillsets
param (
    [string]$ScriptPath = "DUCKScripts/UltrasDUCK/TestClassDUCK.cs"
)

$ErrorActionPreference = "Stop"
$CurrentDir = Split-Path -Parent $MyInvocation.MyCommand.Path
$WorkspaceRoot = (Resolve-Path "$CurrentDir/../../../..").Path

Write-Host "=== Running Skua Compilation Check ===" -ForegroundColor Cyan
Write-Host "Target Script: $ScriptPath" -ForegroundColor Gray
Write-Host "Workspace: $WorkspaceRoot" -ForegroundColor Gray

$TesterProj = Join-Path $WorkspaceRoot "DUCKScripts\SkuaCompileTester\SkuaCompileTester.csproj"
$TargetFullPath = Join-Path $WorkspaceRoot $ScriptPath

if (-not (Test-Path $TesterProj)) {
    Write-Error "SkuaCompileTester project not found at: $TesterProj"
}

if (-not (Test-Path $TargetFullPath)) {
    Write-Error "Target script not found at: $TargetFullPath"
}

$proc = Start-Process -FilePath "dotnet" -ArgumentList "run", "--project", "`"$TesterProj`"", "`"$TargetFullPath`"" -NoNewWindow -PassThru -Wait

if ($proc.ExitCode -eq 0) {
    Write-Host "`n[SUCCESS] Compilation passed cleanly with 0 errors!" -ForegroundColor Green
} else {
    Write-Host "`n[FAILURE] Compilation failed with exit code $($proc.ExitCode)." -ForegroundColor Red
    exit $proc.ExitCode
}
