#requires -Version 7
[CmdletBinding()]
param([Parameter(Mandatory)][string]$ExistingProject)
$ErrorActionPreference = 'Stop'
$source = (Resolve-Path -LiteralPath $ExistingProject).Path
foreach ($name in 'Bridge/Endpoint.cs','app/Bridge.dll','Build.ps1','Start-Bridge.ps1') {
    if (-not (Test-Path -LiteralPath (Join-Path $source $name) -PathType Leaf)) { throw "Missing $name in the selected 0.2 project." }
}
$backup = Join-Path (Split-Path $source) ('Bridge-0.2-rollback-' + [guid]::NewGuid().ToString('N').Substring(0,8))
New-Item -ItemType Directory -Path $backup | Out-Null
New-Item -ItemType Directory -Path (Join-Path $backup Bridge) | Out-Null
foreach ($file in Get-ChildItem -LiteralPath (Join-Path $source Bridge) -File | Where-Object Extension -in '.cs','.csproj','.json') {
    Copy-Item -LiteralPath $file.FullName -Destination (Join-Path $backup Bridge)
}
foreach ($name in 'app','Build.ps1','Start-Bridge.ps1','Test-Local.ps1') {
    $path = Join-Path $source $name
    if (Test-Path -LiteralPath $path) { Copy-Item -LiteralPath $path -Destination $backup -Recurse }
}
foreach ($file in Get-ChildItem -LiteralPath $backup -File -Recurse) {
    $relative = [IO.Path]::GetRelativePath($backup,$file.FullName)
    if ((Get-FileHash -LiteralPath $file.FullName).Hash -ne (Get-FileHash -LiteralPath (Join-Path $source $relative)).Hash) { throw "Backup verification failed for $relative" }
}
Write-Host "Verified rollback copy: $backup"
