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
# Every file setup.ps1 reads — when a file is added under claude/, add it here too (and in mac/install.sh)
$files = 'windows/setup.ps1', 'windows/hangul.ahk', 'projects.txt',
  'claude/CLAUDE.md', 'claude/statusline.sh', 'claude/agents/deep-reasoner.md', 'claude/agents/runner.md',
  'claude/commands/orchestra.md', 'claude/fable/fable.md'
foreach ($f in $files) {
  Write-Host "download $f"
  $out = Join-Path $t ($f -replace '/', '\')
  New-Item -ItemType Directory (Split-Path $out) -Force | Out-Null
  Invoke-WebRequest "$raw/$f" -OutFile $out -UseBasicParsing
}
$setup = Join-Path $t 'windows\setup.ps1'
Start-Process powershell -Verb RunAs -ArgumentList @('-NoProfile', '-ExecutionPolicy', 'Bypass', '-File', "`"$setup`"")
