#requires -Version 7
[CmdletBinding()]
param([switch]$AllowElevatedGetDate)
$ErrorActionPreference = 'Stop'
$exe = Join-Path $PSScriptRoot 'app/Bridge.exe'
$previous = $env:PSBRIDGE_ALLOW_ELEVATED_GET_DATE
try {
    if ($AllowElevatedGetDate) { $env:PSBRIDGE_ALLOW_ELEVATED_GET_DATE = '1' }
    Write-Host 'UI-only test. No PowerShell command will execute. Test Reject, X and Approve in separate runs.'
    & $exe --ui-test
    if ($LASTEXITCODE -ne 0) { throw "UI test failed ($LASTEXITCODE). Inspect stderr." }
} finally { $env:PSBRIDGE_ALLOW_ELEVATED_GET_DATE = $previous }
