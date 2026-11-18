#!/usr/bin/env bash
set -euo pipefail
cd -- "$(dirname -- "$0")"
dotnet restore Linux --locked-mode -r linux-x64
dotnet publish Linux --no-restore -c Release -r linux-x64 --self-contained false -o "$PWD/app-linux"
mkdir -p app-linux/Modules
cp -a app-linux/runtimes/unix/lib/net8.0/Modules/. app-linux/Modules/
(cd desktop && npm ci && npm run build && cd src-tauri && cargo build --release --locked --features tauri/custom-protocol)
install -m 755 desktop/src-tauri/target/release/bridge-supervisor app-linux/bridge-supervisor
