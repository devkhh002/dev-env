# USB 시작하기 — 새 PC 에서 이 폴더의 '시작하기.cmd' 를 두 번 누른다
#  1) 인터넷이 없으면 '네트워크 드라이버' 폴더의 드라이버를 깐다(이 PC 에 맞는 것만 들어간다)
#  2) GitHub 에서 설치 프로그램 최신판을 받아 연다 — 받지 못하거나 깨져 있으면 USB 에 저장된 마지막 판으로 연다
#  3) 잘 돌았으면 그 판을 USB 의 last-good 폴더에 저장해 둔다(다음에 대비, 사람이 할 일 없음)
$ErrorActionPreference = 'Continue'
$ProgressPreference = 'SilentlyContinue'
[Net.ServicePointManager]::SecurityProtocol = 'Tls12'
$kit = $PSScriptRoot

$isAdmin = ([Security.Principal.WindowsPrincipal][Security.Principal.WindowsIdentity]::GetCurrent()).IsInRole([Security.Principal.WindowsBuiltInRole]::Administrator)
if (-not $isAdmin) { Start-Process powershell -Verb RunAs -ArgumentList '-NoProfile', '-ExecutionPolicy', 'Bypass', '-File', "`"$PSCommandPath`""; return }
try { $Host.UI.RawUI.WindowTitle = '개발 PC 설치 — 시작하기' } catch {}

$raw = 'https://raw.githubusercontent.com/devkhh002/dev-env/main'
function Online { try { Invoke-WebRequest "$raw/README.md" -Method Head -UseBasicParsing -TimeoutSec 8 | Out-Null; $true } catch { $false } }

if (-not (Online)) {
  Write-Host '인터넷이 연결돼 있지 않아 USB 의 네트워크 드라이버를 설치합니다...' -ForegroundColor Cyan
  if (Get-ChildItem "$kit\네트워크 드라이버" -Recurse -Filter *.inf -EA 0) { pnputil /add-driver "$kit\네트워크 드라이버\*.inf" /subdirs /install | Out-Host }
  else { Write-Host "  '네트워크 드라이버' 폴더가 비어 있습니다." -ForegroundColor Yellow }
  Write-Host '연결을 기다립니다(최대 1분). 랜선을 확인하고, Wi-Fi 는 작업 표시줄에서 직접 연결하세요.'
  for ($i = 0; $i -lt 12 -and -not (Online); $i++) { Start-Sleep -Seconds 5 }
}

$t = Join-Path $env:TEMP 'dev-env'
$good = Join-Path $kit 'last-good'
$fresh = $false
if (Online) {
  try {
    Remove-Item $t -Recurse -Force -EA 0
    $list = (Invoke-WebRequest "$raw/windows/files.txt" -UseBasicParsing).Content -split "`r?`n" | Where-Object { $_ -match '\S' -and $_ -notmatch '^\s*#' }
    foreach ($f in $list) {
      $f = $f.Trim(); Write-Host "받는 중: $f"
      $out = Join-Path $t ($f -replace '/', '\')
      New-Item -ItemType Directory (Split-Path $out) -Force | Out-Null
      Invoke-WebRequest "$raw/$f" -OutFile $out -UseBasicParsing -ErrorAction Stop
    }
    $err = $null; [void][Management.Automation.Language.Parser]::ParseFile("$t\windows\setup.ps1", [ref]$null, [ref]$err)
    $fresh = -not $err
  } catch { Write-Host "GitHub 에서 받지 못했습니다: $_" -ForegroundColor Yellow }
}

$dir = if ($fresh) { $t } elseif (Test-Path "$good\windows\setup.ps1") { Write-Host 'USB 에 저장된 마지막 판으로 엽니다.' -ForegroundColor Yellow; $good } else { $null }
if (-not $dir) { Write-Host '설치 프로그램을 열 수 없습니다. 인터넷 연결을 확인하세요.' -ForegroundColor Red; Read-Host 'Enter 를 누르면 닫습니다'; return }

Write-Host '설치 화면을 엽니다...'
$p = Start-Process powershell -Wait -PassThru -ArgumentList '-NoProfile', '-ExecutionPolicy', 'Bypass', '-File', "`"$dir\windows\setup.ps1`"", '-Usb', "`"$kit`""
if ($fresh -and $p.ExitCode -eq 0) { robocopy $t $good /MIR /R:1 /W:1 /NFL /NDL /NJH /NJS /NP | Out-Null; Write-Host 'USB 의 마지막 판을 갱신했습니다.' }
