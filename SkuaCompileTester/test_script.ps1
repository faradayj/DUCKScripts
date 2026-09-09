param (
    [Parameter(Mandatory=$true)]
    [string]$ScriptPath
)

$TesterDir = $PSScriptRoot
Push-Location $TesterDir

try {
    dotnet run -- "$ScriptPath"
}
finally {
    Pop-Location
}
