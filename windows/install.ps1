# One-line bootstrap (PowerShell):
#   irm https://raw.githubusercontent.com/devkhh002/dev-env/main/windows/install.ps1 | iex
# Fetches only the files listed in windows/files.txt (not the whole repo — the manual captures are large)
# and opens the setup window as administrator.
$ErrorActionPreference = 'Stop'
$ProgressPreference = 'SilentlyContinue'
[Net.ServicePointManager]::SecurityProtocol = 'Tls12'
$raw = 'https://raw.githubusercontent.com/devkhh002/dev-env/main'
$t = Join-Path $env:TEMP 'dev-env'
Remove-Item $t -Recurse -Force -ErrorAction SilentlyContinue
$files = (Invoke-WebRequest "$raw/windows/files.txt" -UseBasicParsing).Content -split "`r?`n" | Where-Object { $_ -match '\S' -and $_ -notmatch '^\s*#' }
foreach ($f in $files) {
  $f = $f.Trim()
  Write-Host "download $f"
  $out = Join-Path $t ($f -replace '/', '\')
  New-Item -ItemType Directory (Split-Path $out) -Force | Out-Null
  Invoke-WebRequest "$raw/$f" -OutFile $out -UseBasicParsing
}
$setup = Join-Path $t 'windows\setup.ps1'
Start-Process powershell -Verb RunAs -ArgumentList @('-NoProfile', '-ExecutionPolicy', 'Bypass', '-File', "`"$setup`"")
