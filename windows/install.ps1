# One-line bootstrap (PowerShell):
#   irm https://raw.githubusercontent.com/devkhh002/dev-env/main/windows/install.ps1 | iex
# Fetches only the files setup needs (not the whole repo — the manual captures are large)
# and opens the setup checklist as administrator.
$ErrorActionPreference = 'Stop'
$ProgressPreference = 'SilentlyContinue'
[Net.ServicePointManager]::SecurityProtocol = 'Tls12'
$raw = 'https://raw.githubusercontent.com/devkhh002/dev-env/main'
$t = Join-Path $env:TEMP 'dev-env'
Remove-Item $t -Recurse -Force -ErrorAction SilentlyContinue
New-Item -ItemType Directory "$t\windows" -Force | Out-Null
foreach ($f in 'windows/setup.ps1', 'windows/hangul.ahk', 'projects.txt') {
  Write-Host "download $f"
  Invoke-WebRequest "$raw/$f" -OutFile (Join-Path $t ($f -replace '/', '\')) -UseBasicParsing
}
$setup = Join-Path $t 'windows\setup.ps1'
Start-Process powershell -Verb RunAs -ArgumentList @('-NoProfile', '-ExecutionPolicy', 'Bypass', '-File', "`"$setup`"")
