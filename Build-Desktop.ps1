#requires -Version 7
$ErrorActionPreference = 'Stop'
if (-not $IsWindows) { throw 'Use Build-Linux.sh on Ubuntu.' }
Push-Location $PSScriptRoot
try {
    dotnet restore WindowsDesktop --locked-mode -r win-x64
    if ($LASTEXITCODE) { throw 'Desktop restore failed.' }
    dotnet publish WindowsDesktop --no-restore -r win-x64 -c Release -o app-desktop
    if ($LASTEXITCODE) { throw 'Desktop publish failed.' }
    Copy-Item -LiteralPath app-desktop/runtimes/win/lib/net8.0/Modules -Destination app-desktop -Recurse -Force
    Push-Location desktop
    try {
        npm.cmd ci
        if ($LASTEXITCODE) { throw 'npm ci failed.' }
        npm.cmd run build
        if ($LASTEXITCODE) { throw 'React build failed.' }
        cargo build --manifest-path src-tauri/Cargo.toml --locked --release --features tauri/custom-protocol
        if ($LASTEXITCODE) { throw 'Tauri build failed.' }
        Copy-Item src-tauri/target/release/bridge-supervisor.exe ../app-desktop -Force
    } finally { Pop-Location }
} finally { Pop-Location }
