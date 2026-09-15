# One-line bootstrap (PowerShell):
#   irm https://raw.githubusercontent.com/devkhh002/dev-env/main/windows/install.ps1 | iex
# Downloads this repository and opens the setup checklist (as administrator).
$ErrorActionPreference = 'Stop'
$ProgressPreference = 'SilentlyContinue'
[Net.ServicePointManager]::SecurityProtocol = 'Tls12'
$t = Join-Path $env:TEMP 'dev-env'
Remove-Item $t -Recurse -Force -ErrorAction SilentlyContinue
New-Item -ItemType Directory $t | Out-Null
Invoke-WebRequest 'https://github.com/devkhh002/dev-env/archive/refs/heads/main.zip' -OutFile "$t\main.zip" -UseBasicParsing
Expand-Archive "$t\main.zip" $t -Force
$setup = Join-Path $t 'dev-env-main\windows\setup.ps1'
Start-Process powershell -Verb RunAs -ArgumentList @('-NoProfile', '-ExecutionPolicy', 'Bypass', '-File', "`"$setup`"")
