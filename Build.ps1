#requires -Version 7
[CmdletBinding()]
param([switch] $NoRestore)
$ErrorActionPreference = 'Stop'
$project = Join-Path $PSScriptRoot 'Bridge/Bridge.csproj'
if (-not $NoRestore) {
    & dotnet restore $project --locked-mode --runtime win-x64
    if ($LASTEXITCODE -ne 0) { throw "Restore failed ($LASTEXITCODE). Published app was not updated." }
}
& dotnet publish $project --no-restore --configuration Release --runtime win-x64 --self-contained false --output (Join-Path $PSScriptRoot app)
if ($LASTEXITCODE -ne 0) { throw "Publish failed ($LASTEXITCODE). Do not launch a partially updated app." }
$modules = Join-Path $PSScriptRoot 'app/runtimes/win/lib/net8.0/Modules'
Copy-Item -LiteralPath $modules -Destination (Join-Path $PSScriptRoot app) -Recurse -Force
if (-not (Test-Path (Join-Path $PSScriptRoot 'app/Modules/Microsoft.PowerShell.Utility/Microsoft.PowerShell.Utility.psd1'))) { throw 'PowerShell module publish incomplete.' }
Get-FileHash -LiteralPath (Join-Path $PSScriptRoot 'app/Bridge.dll') -Algorithm SHA256
