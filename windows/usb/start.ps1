# USB 시작하기 — 새 PC 에서 이 폴더의 '시작하기.cmd' 를 두 번 누른다
#  1) 인터넷이 없으면 '네트워크 드라이버' 폴더의 드라이버를 깐다(이 PC 에 맞는 것만 들어간다)
#  2) GitHub 에서 설치 프로그램 최신판을 받아 연다 — 받지 못하거나 깨져 있으면 USB 에 저장된 마지막 판으로 연다
#  3) 받은 판으로 설치 화면이 뜨면 바로 그 판을 USB 의 last-good 폴더에 저장하고, USB 의 시작하기 파일도 새 판으로 바꾼다(사람이 할 일 없음)
# 콘솔 창은 띄우지 않는다 — 작은 '시작하기' 창에 진행 상황을, 문제는 알림 창으로 보인다
$ErrorActionPreference = 'Continue'
$ProgressPreference = 'SilentlyContinue'
[Net.ServicePointManager]::SecurityProtocol = 'Tls12'
$kit = $PSScriptRoot

$isAdmin = ([Security.Principal.WindowsPrincipal][Security.Principal.WindowsIdentity]::GetCurrent()).IsInRole([Security.Principal.WindowsBuiltInRole]::Administrator)
if (-not $isAdmin) { Start-Process powershell -Verb RunAs -WindowStyle Hidden -ArgumentList '-NoProfile', '-WindowStyle', 'Hidden', '-ExecutionPolicy', 'Bypass', '-File', "`"$PSCommandPath`"", '-Relaunched'; return }

Add-Type -AssemblyName System.Windows.Forms, System.Drawing
[System.Windows.Forms.Application]::EnableVisualStyles()
$ui = New-Object System.Windows.Forms.Form
$ui.Text = 'PC 설치 — 시작하기'; $ui.ClientSize = New-Object System.Drawing.Size(500, 100); $ui.StartPosition = 'CenterScreen'
$ui.FormBorderStyle = 'FixedDialog'; $ui.ControlBox = $false; $ui.Font = New-Object System.Drawing.Font('Malgun Gothic', 10)
$lbl = New-Object System.Windows.Forms.Label
$lbl.Dock = 'Fill'; $lbl.TextAlign = 'MiddleCenter'
$ui.Controls.Add($lbl)
$ui.Show()
function Say($msg) { $lbl.Text = $msg; [System.Windows.Forms.Application]::DoEvents() }
function Wait-Sec($sec) { for ($i = 0; $i -lt $sec * 5; $i++) { Start-Sleep -Milliseconds 200; [System.Windows.Forms.Application]::DoEvents() } }
function Show-Msg($msg, $icon = 'Information') { [void][System.Windows.Forms.MessageBox]::Show($msg, 'PC 설치 — 시작하기', 'OK', $icon) }

$raw = 'https://raw.githubusercontent.com/devkhh002/dev-env/main'
function Online { try { Invoke-WebRequest "$raw/README.md" -Method Head -UseBasicParsing -TimeoutSec 8 | Out-Null; $true } catch { $false } }

Say '인터넷 연결을 확인합니다...'
if (-not (Online)) {
  Say '인터넷이 연결돼 있지 않아 USB 의 네트워크 드라이버를 설치합니다...'
  if (Get-ChildItem "$kit\네트워크 드라이버" -Recurse -Filter *.inf -EA 0) { pnputil /add-driver "$kit\네트워크 드라이버\*.inf" /subdirs /install | Out-Null }
  else { Show-Msg "'네트워크 드라이버' 폴더가 비어 있습니다. 랜선·Wi-Fi 를 직접 연결해 보세요." Warning }
  for ($i = 0; $i -lt 12 -and -not (Online); $i++) { Say "연결을 기다립니다 ($(60 - $i * 5)초)`r`n랜선을 확인하고, Wi-Fi 는 작업 표시줄에서 직접 연결하세요."; Wait-Sec 5 }
}

$t = Join-Path $env:TEMP 'dev-env'
$good = Join-Path $kit 'last-good'
$fresh = $false
$why = ''
if (Online) {
  try {
    Remove-Item $t -Recurse -Force -EA 0
    $list = (Invoke-WebRequest "$raw/windows/files.txt" -UseBasicParsing).Content -split "`r?`n" | Where-Object { $_ -match '\S' -and $_ -notmatch '^\s*#' }
    foreach ($f in $list) {
      $f = $f.Trim(); Say "GitHub 에서 설치 프로그램 최신판을 받는 중`r`n$f"
      $out = Join-Path $t ($f -replace '/', '\')
      New-Item -ItemType Directory (Split-Path $out) -Force | Out-Null
      Invoke-WebRequest "$raw/$f" -OutFile $out -UseBasicParsing -ErrorAction Stop
    }
    $err = $null; [void][Management.Automation.Language.Parser]::ParseFile("$t\windows\setup.ps1", [ref]$null, [ref]$err)
    $fresh = -not $err
  } catch { $why = "$_" }
}

function Ver($d) { $v = Get-Content "$d\windows\version.txt" -Encoding UTF8 -EA 0 | Select-Object -First 1; if ($v) { $v } else { '알 수 없음' } }
$dir = if ($fresh) { $t }
       elseif (Test-Path "$good\windows\setup.ps1") { Show-Msg ("GitHub 에서 받지 못해 USB 예비판(버전 $(Ver $good))으로 엽니다." + $(if ($why) { "`r`n`r`n$why" } else { '' })) Warning; $good }
       else { $null }
if (-not $dir) { $ui.Close(); Show-Msg '설치 프로그램을 열 수 없습니다. 인터넷 연결을 확인하세요.' Error; return }

Say "설치 화면을 엽니다 — 버전 $(Ver $dir)`r`n(상태 확인에 1~2분 걸릴 수 있습니다)"
Wait-Sec 1
$ui.Close()
$p = Start-Process powershell -PassThru -WindowStyle Hidden -ArgumentList '-NoProfile', '-ExecutionPolicy', 'Bypass', '-File', "`"$dir\windows\setup.ps1`"", '-Usb', "`"$kit`""
# 설치 화면(제목 'PC 설치 — 버전 …' — setup.ps1 이 이 제목을 바꾸면 여기도)이 뜨면 받은 판이 도는 것 — 바로 저장한다.
# 설치가 끝나기를 기다리지 않는다: 재부팅하면 저장을 못 하고, 설치가 띄운 앱(HWiNFO 등)이 꺼질 때까지 기다리게 되고, '앱 추가' 로 고친 목록이 섞인다
$up = $false
while (-not $p.HasExited -and -not $up) { Start-Sleep -Milliseconds 500; $p.Refresh(); $up = $p.MainWindowTitle -like 'PC 설치 — 버전*' }
if ($fresh -and $up) {
  robocopy $t $good /MIR /R:1 /W:1 /NFL /NDL /NJH /NJS /NP | Out-Null
  # USB 맨 위의 '버전 ….txt' 도 맞춘다 — 탐색기에서 파일 이름만 보면 이 USB 예비판의 버전을 안다
  $v = Ver $good
  Get-ChildItem -LiteralPath $kit -Filter '버전 *.txt' -EA 0 | Remove-Item -Force
  [IO.File]::WriteAllText((Join-Path $kit ('버전 ' + ($v -replace ':', '.') + '.txt')), "이 USB 에 들어 있는 설치 프로그램 버전: $v`r`n설치 화면 제목에도 같은 버전이 보입니다.`r`n", (New-Object Text.UTF8Encoding $true))
  # USB 의 시작하기 파일도 받은 판으로 — 다음에 이 USB 로 시작할 때 고친 시작하기가 돈다 (같으면 건드리지 않는다. 이름 대응은 setup.ps1 의 $UsbKitFiles 와 같게)
  foreach ($m in @(@('start.ps1', 'start.ps1'), @('start.cmd', '시작하기.cmd'), @('README.txt', '읽어보기.txt'))) {
    $src = Join-Path $t "windows\usb\$($m[0])"; $dst = Join-Path $kit $m[1]
    # 처음부터 관리자로 열렸으면(-Relaunched 없음) cmd.exe 가 아직 이 .cmd 를 읽는 중 — 지금 바꾸면 새 파일의 옛 자리부터 이어 읽는다.
    # 그래서 .cmd 는 관리자 권한을 다시 얻어 연 경우에만 바꾼다(늘 관리자로 여는 PC 에서는 설치 화면의 관리 > USB 항목으로 바꾼다)
    if ($m[1] -like '*.cmd' -and $args -notcontains '-Relaunched') { continue }
    if ((Test-Path -LiteralPath $src) -and (-not (Test-Path -LiteralPath $dst) -or (Get-FileHash -LiteralPath $src).Hash -ne (Get-FileHash -LiteralPath $dst).Hash)) { Copy-Item -LiteralPath $src $dst -Force -EA 0 }
  }
}
