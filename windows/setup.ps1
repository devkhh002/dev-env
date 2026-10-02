# PC 설치 — Windows
#   실행하면 단계별 설치 화면이 뜬다. 고른 것만 설치하고, 이미 된 것은 건너뛴다(여러 번 실행해도 안전).
#   powershell -ExecutionPolicy Bypass -File setup.ps1                (설치 화면)
#   powershell -ExecutionPolicy Bypass -File setup.ps1 -List          (상태만 보기)
#   powershell -ExecutionPolicy Bypass -File setup.ps1 -Only git,node (화면 없이 이것만)
#   powershell -ExecutionPolicy Bypass -File setup.ps1 -All           (화면 없이 전부)
# 앱·도구 목록은 저장소의 catalog.txt, 프로젝트는 projects.txt — 그 파일만 고치면 모든 PC 의 설치 화면에 반영된다.
# -Usb  : USB 의 PC설치 폴더(시작하기가 넘겨준다. 없으면 드라이브를 찾아본다)
# -Progress·-NoPause : 설치 화면이 설치 창(일꾼)에 넘기는 것.  -Snapshot : 화면을 그림으로 저장(시험용)
# -Upgrade : 설치된 앱을 모두 최신으로 (설치 화면의 '모두 최신으로' 가 일꾼에 넘긴다)
param([switch]$All, [string[]]$Only, [string]$OnlyFile, [switch]$List, [switch]$NoPause, [string]$Progress, [string]$Usb, [string]$Snapshot, [switch]$Upgrade)

# ── 버전(올릴 때는 여기만) ───────────────────────────────────────────
$V = @{
  Git      = '2.55.0.windows.5'; GitFile = 'Git-2.55.0.5-64-bit.exe'
  Node     = '24.21.0'
  Python   = '3.13.15'
  Gh       = '2.101.0'
  Pwsh     = '7.6.6'
  Terminal = '1.24.11911.0'
  Sunshine = 'v2026.914.233613'
  Clasp    = '3.3.0'
  Firebase = '15.24.0'
}
$GitName  = 'Hyunhyo Kim'
$GitEmail = 'devkhh002@gmail.com'
$DevRoot  = 'C:\dev'
$ClaudeModel = 'opus[1m]'   # Claude Code 기본 모델 — 별칭이라 새 Opus가 나오면 자동으로 따라간다 (빼려면 '' 로)
$OldClaudeModels = @('claude-opus-5-5[1m]')   # 예전에 이 설치가 넣던 값 — 이것만 새 기본값으로 바꾸고, 사람이 고른 모델은 그대로 둔다
$UpgradeSkip = @('Google.ChromeRemoteDesktopHost')   # '모두 최신으로' 에서 빼는 winget 아이디 — 올리는 동안 원격 접속이 끊긴다
# '모두 최신으로' 에서 빼는 ⑤ 개발 환경 — 위 $V 로 버전을 고정한다(올릴 때는 $V 를 고친다). * 가능
$UpgradeFixed = @('Git.Git', 'OpenJS.NodeJS*', 'Python.Python.3.13', 'Python.Launcher', 'GitHub.cli', 'Microsoft.PowerShell', 'Microsoft.WindowsTerminal')
# ────────────────────────────────────────────────────────────────────

$ErrorActionPreference = 'Continue'
$ProgressPreference = 'SilentlyContinue'
[Net.ServicePointManager]::SecurityProtocol = 'Tls12'
[Console]::OutputEncoding = [Text.Encoding]::UTF8
$Repo = Split-Path $PSScriptRoot -Parent
# 버전 — windows/version.txt 한 줄(커밋할 때 자동으로 그 시각이 된다). 설치 화면 제목·USB 의 '버전 ….txt' 에 보인다
$Version = Get-Content "$PSScriptRoot\version.txt" -Encoding UTF8 -EA 0 | Select-Object -First 1
if (-not $Version) { $Version = '알 수 없음' }

# 설치 화면으로 뜨는가(명령줄로 쓰는 -List·-Only·-All·-Upgrade·-Snapshot 이 아닐 때)
$Gui = -not ($All -or $Only -or $OnlyFile -or $List -or $Snapshot -or $Upgrade)

# 콘솔(파란 PowerShell 창)을 숨기고 보이기 — 설치 화면만 보이게. 일꾼 창은 '진행 창 보기' 로 꺼내 본다
Add-Type -Name Con -Namespace DevEnv -MemberDefinition @'
[DllImport("kernel32.dll")] public static extern System.IntPtr GetConsoleWindow();
[DllImport("kernel32.dll")] public static extern uint GetConsoleProcessList([Out] uint[] list, uint count);
[DllImport("user32.dll")] public static extern bool ShowWindow(System.IntPtr hWnd, int nCmdShow);
[DllImport("user32.dll")] public static extern bool IsWindowVisible(System.IntPtr hWnd);
[DllImport("user32.dll")] public static extern bool SetForegroundWindow(System.IntPtr hWnd);
'@
# 이 창을 이 프로세스 혼자 쓸 때만 숨긴다 — 사람이 쓰던 터미널에서 실행했으면 그 터미널을 숨기지 않는다
function Hide-OwnConsole { $l = New-Object UInt32[] 4; if ([DevEnv.Con]::GetConsoleProcessList($l, 4) -eq 1) { [void][DevEnv.Con]::ShowWindow([DevEnv.Con]::GetConsoleWindow(), 0) } }
function Show-OwnConsole { $h = [DevEnv.Con]::GetConsoleWindow(); if ($h -ne [IntPtr]::Zero) { [void][DevEnv.Con]::ShowWindow($h, 5); [void][DevEnv.Con]::SetForegroundWindow($h) } }

$isAdmin = ([Security.Principal.WindowsPrincipal][Security.Principal.WindowsIdentity]::GetCurrent()).IsInRole([Security.Principal.WindowsBuiltInRole]::Administrator)
if (-not $isAdmin -and -not $List -and -not $Snapshot) {
  $a = @('-NoProfile', '-ExecutionPolicy', 'Bypass', '-File', "`"$PSCommandPath`"")
  if ($All) { $a += '-All' }
  if ($Only) { $a += '-Only'; $a += "`"$($Only -join ',')`"" }
  if ($NoPause) { $a += '-NoPause' }
  if ($Progress) { $a += '-Progress'; $a += "`"$Progress`"" }
  if ($Usb) { $a += '-Usb'; $a += "`"$Usb`"" }
  if ($OnlyFile) { $a += '-OnlyFile'; $a += "`"$OnlyFile`"" }
  if ($Upgrade) { $a += '-Upgrade' }
  $sp = @{}; if ($Gui) { $sp.WindowStyle = 'Hidden' }   # 설치 화면이면 관리자 창도 콘솔 없이
  Start-Process powershell -Verb RunAs -ArgumentList $a @sp
  return
}

# 설치 화면이 오류로 멈추면 숨은 콘솔 대신 알림 창으로 알린다
trap { if ($Gui -and ('System.Windows.Forms.MessageBox' -as [type])) { [void][System.Windows.Forms.MessageBox]::Show("설치 화면 오류:`r`n$_", 'PC 설치', 'OK', 'Error') }; break }

# 설치 화면: 콘솔은 숨기고, 상태를 확인하는 동안(최대 2분)은 작은 '준비 중' 창을 띄운다
$script:Pump = $false   # 화면이 떠 있는 동안 기다리는 곳에서 화면을 멈추지 않게(DoEvents)
if ($Gui -or $Snapshot) {
  Add-Type -AssemblyName System.Windows.Forms, System.Drawing
  [System.Windows.Forms.Application]::EnableVisualStyles()
}
$script:Splash = $null
if ($Gui) {
  Hide-OwnConsole
  $script:Pump = $true
  $script:Splash = New-Object System.Windows.Forms.Form
  $script:Splash.Text = 'PC 설치'; $script:Splash.ClientSize = New-Object System.Drawing.Size(460, 90); $script:Splash.StartPosition = 'CenterScreen'
  $script:Splash.FormBorderStyle = 'FixedDialog'; $script:Splash.ControlBox = $false; $script:Splash.Font = New-Object System.Drawing.Font('Malgun Gothic', 10)
  $script:SplashText = New-Object System.Windows.Forms.Label
  $script:SplashText.Dock = 'Fill'; $script:SplashText.TextAlign = 'MiddleCenter'; $script:SplashText.Text = '설치 화면을 준비합니다...'
  $script:Splash.Controls.Add($script:SplashText)
  $script:Splash.Show(); [System.Windows.Forms.Application]::DoEvents()
}
# 준비 상황 — 설치 화면이면 '준비 중' 창에, 아니면 콘솔에
function Show-Prep($msg) { if ($script:Splash) { $script:SplashText.Text = $msg; [System.Windows.Forms.Application]::DoEvents() } elseif (-not $Progress) { Write-Host $msg } }
# 선택 항목은 명령줄 대신 파일로 받는다 — 한글·공백이 든 Id 가 프로세스 사이에서 깨지지 않게
if ($OnlyFile -and (Test-Path -LiteralPath $OnlyFile)) { $Only = @(Get-Content -LiteralPath $OnlyFile -Encoding UTF8 | Where-Object { $_ -match '\S' }) }

# 예전 판(dev-env-dl)이 멈추며 남긴 받다 만 파일을 보지 않도록 폴더 이름을 바꿨다
$Tmp = "$env:TEMP\dev-env-cache"; New-Item -ItemType Directory $Tmp -Force | Out-Null
function Refresh-Path { $env:Path = [Environment]::GetEnvironmentVariable('Path', 'Machine') + ';' + [Environment]::GetEnvironmentVariable('Path', 'User') + ";$env:APPDATA\npm;$env:USERPROFILE\.local\bin" }
# 큰 파일도 빨리 받게 Windows 내장 curl.exe 를 쓴다(PowerShell 5.1 의 Invoke-WebRequest 는 큰 파일에서 매우 느려 멈춘 것처럼 보인다).
# .part 로 받다가 다 받으면 이름을 바꾼다 — 중간에 끊긴 파일을 다 받은 것으로 착각하지 않게. 진행률은 '진행 창 보기' 로 꺼내 본다
function Fetch($url, $name, [long]$size = 0) {
  $p = "$Tmp\$name"
  # 크기를 아는 파일(GitHub 릴리스)은 대조 — 받다 만 파일이면 지우고 다시 받는다
  if ((Test-Path $p) -and $size -gt 0 -and (Get-Item $p).Length -ne $size) { Say "   받다 만 파일이라 다시 받습니다: $name"; Remove-Item $p -Force }
  if (Test-Path $p) { return $p }
  $part = "$p.part"; Remove-Item $part -EA 0
  Say "   받는 중: $name  (진행률은 '진행 창 보기')"
  $curl = "$env:SystemRoot\System32\curl.exe"
  if (Test-Path $curl) { & $curl -L --fail --retry 3 --retry-delay 2 -o $part $url; $ok = ($LASTEXITCODE -eq 0) }
  else { try { (New-Object Net.WebClient).DownloadFile($url, $part); $ok = $true } catch { $ok = $false } }
  if ($ok -and (Test-Path $part)) { Move-Item $part $p -Force; Say ('   받음: {0} ({1:N0}MB)' -f $name, ((Get-Item $p).Length / 1MB)) }
  else { Remove-Item $part -EA 0; throw "다운로드 실패: $url" }
  return $p
}
function Msi($p) { Start-Process msiexec -Wait -ArgumentList "/i `"$p`" /qn /norestart" }
# 설치 프로그램을 실행하고 '그 프로그램만' 끝나기를 기다린다 — Start-Process -Wait 는 설치가 끝나며 띄운 앱(HWiNFO 등)이 꺼질 때까지 기다려 멈춘 것처럼 된다
function Start-Wait($file, [string]$argList) { $a = @{ FilePath = $file; PassThru = $true }; if ($argList) { $a.ArgumentList = $argList }; (Start-Process @a).WaitForExit() }
function Has($cmd) { Refresh-Path; return [bool](Get-Command $cmd -EA 0) }
function Q($s) { "'" + ([string]$s -replace "'", "''") + "'" }
# 진행 기록: 설치 화면이 이 파일을 읽어 보여 준다(@@ 줄은 화면용 표시)
function Mark($line) { if ($Progress) { try { [IO.File]::AppendAllText($Progress, "$line`r`n", (New-Object Text.UTF8Encoding $false)) } catch {} } }
function Say($msg, $color = 'Gray') { Write-Host $msg -ForegroundColor $color; Mark $msg }
# 바깥 프로그램을 시간 제한을 두고 실행 — 응답이 없으면 끝내고 $null (winget 이 반쯤 깔린 PC 에서 상태 확인이 영원히 멈추던 것)
# (화면이 떠 있으면 기다리는 동안에도 화면이 움직인다. 출력은 UTF-8 로 읽는다 — winget·git 의 한글)
function Invoke-Timed($exe, [string[]]$argList, [int]$sec) {
  $out = [IO.Path]::GetTempFileName(); $err = [IO.Path]::GetTempFileName()
  try {
    $p = Start-Process -FilePath $exe -ArgumentList $argList -NoNewWindow -PassThru -RedirectStandardOutput $out -RedirectStandardError $err -EA Stop
    $null = $p.Handle   # PS 5.1 에서 ExitCode 를 받으려면 필요
    $sw = [Diagnostics.Stopwatch]::StartNew()
    while (-not $p.WaitForExit(100)) {
      if ($script:Pump) { [System.Windows.Forms.Application]::DoEvents() }
      if ($sw.Elapsed.TotalSeconds -gt $sec) { try { $p.Kill() } catch {}; return $null }
    }
    $p.WaitForExit()
    return @{ Code = $p.ExitCode; Out = [IO.File]::ReadAllText($out); Err = [IO.File]::ReadAllText($err) }
  } catch { return $null } finally { Remove-Item $out, $err -EA 0 }
}
# 명령줄 인자 하나를 따옴표로 감싼다(빈칸·한글 경로) — Start-Process 는 인자를 그냥 이어 붙인다
function Quote-Arg([string]$s) { if ($s -and $s -notmatch '[\s"]') { return $s }; '"' + (($s -replace '(\\*)"', '$1$1\"') -replace '(\\+)$', '$1$1') + '"' }
# winget 의 표(검색 결과·올릴 것 목록)를 줄마다 칸 배열로 — 한글은 화면에서 두 칸이라 글자 수가 아닌 화면 칸으로 열을 나눈다.
# 열 시작 = 머리줄 낱말이 시작하는 칸 중 모든 줄에서 바로 앞 칸이 비어 있는 곳 (한글 머리줄 '장치 ID' 처럼 띄어 쓴 열 이름도 된다)
function ConvertTo-Cells([string]$s) {
  $l = [Collections.Generic.List[string]]::new()
  foreach ($ch in $s.ToCharArray()) {
    $n = [int]$ch; $l.Add([string]$ch)
    if (($n -ge 0x1100 -and $n -le 0x115F) -or ($n -ge 0x2E80 -and $n -le 0xA4CF) -or ($n -ge 0xAC00 -and $n -le 0xD7A3) -or ($n -ge 0xF900 -and $n -le 0xFAFF) -or ($n -ge 0xFF00 -and $n -le 0xFF60) -or ($n -ge 0xFFE0 -and $n -le 0xFFE6)) { $l.Add('') }
  }
  , $l
}
function ConvertFrom-WingetTable([string]$text) {
  $lines = @($text -split "`n" | ForEach-Object { ($_.TrimEnd("`r") -split "`r")[-1].TrimEnd() })   # 진행 표시가 \r 로 덮어쓴 줄은 마지막 것만
  $dash = -1; for ($i = 1; $i -lt $lines.Count; $i++) { if ($lines[$i] -match '^-{10,}$') { $dash = $i; break } }
  if ($dash -lt 1) { return }
  $head = ConvertTo-Cells $lines[$dash - 1]
  # 열 후보 = 머리줄 낱말 자리 중 첫 표 줄에서도 바로 앞 칸이 빈 곳. 표 줄 = 늘 채워지는 아이디·버전 열(후보 2·3번째)이 그 자리에서 시작하는 줄.
  # 표 밑의 '8 업그레이드를 사용할 수 있습니다.'·'1 패키지에 … 핀이 있습니다' 같은 줄에서 표가 끝난다
  if ($dash + 1 -ge $lines.Count -or -not $lines[$dash + 1]) { return }
  $first = ConvertTo-Cells $lines[$dash + 1]
  $tok = @(for ($p = 0; $p -lt $head.Count; $p++) { if ($head[$p] -match '\S' -and ($p -eq 0 -or ($head[$p - 1] -eq ' ' -and ($p - 1 -ge $first.Count -or $first[$p - 1] -eq ' ')))) { $p } })
  if ($tok.Count -lt 3) { return }
  $rows = @()
  for ($i = $dash + 1; $i -lt $lines.Count -and $lines[$i]; $i++) {
    $c = ConvertTo-Cells $lines[$i]
    $ok = $true
    foreach ($p in $tok[1], $tok[2]) { if ($c.Count -le $p -or $c[$p - 1] -ne ' ' -or $c[$p] -notmatch '\S') { $ok = $false } }
    if (-not $ok) { break }
    $rows += , $c
  }
  $starts = @(0)
  for ($p = 1; $p -lt $head.Count; $p++) {
    if ($head[$p] -notmatch '\S' -or $head[$p - 1] -ne ' ') { continue }
    $ok = $true; foreach ($r in $rows) { if ($p - 1 -lt $r.Count -and $r[$p - 1] -ne ' ') { $ok = $false; break } }
    if ($ok) { $starts += $p }
  }
  foreach ($r in $rows) {
    $cols = for ($k = 0; $k -lt $starts.Count; $k++) {
      $a = $starts[$k]; $b = if ($k + 1 -lt $starts.Count) { [Math]::Min($starts[$k + 1], $r.Count) } else { $r.Count }
      if ($a -ge $b) { '' } else { (-join $r[$a..($b - 1)]).Trim() }
    }
    , @($cols)
  }
}
function Test-Online { try { Invoke-WebRequest 'https://raw.githubusercontent.com/devkhh002/dev-env/main/README.md' -Method Head -UseBasicParsing -TimeoutSec 8 | Out-Null; $true } catch { $false } }
# 이동식(2)·로컬(3) 드라이브만 본다 — 연결이 끊긴 네트워크 드라이브(NAS 등)는 찾는 데 한참 멈춘다
function Find-UsbKit { foreach ($d in Get-CimInstance Win32_LogicalDisk -Filter 'DriveType=2 OR DriveType=3' -EA 0) { $k = Join-Path "$($d.DeviceID)\" 'PC설치'; if (Test-Path -LiteralPath (Join-Path $k 'start.ps1')) { return $k } }; return $null }
Show-Prep '인터넷·USB 를 확인합니다...'
if (-not $Usb) { $Usb = Find-UsbKit }
$script:Online = Test-Online

# Windows Terminal 은 처음 뜰 때 만드는 설정에 Ctrl+C(선택한 글자 복사)·Ctrl+V(붙여넣기)를 넣는다.
# 우리가 settings.json 을 먼저 만들면 그게 빠져 Ctrl+V 가 Claude Code 로 그냥 넘어간다(붙여넣기 안 됨) — 직접 넣는다
$WtSettings = "$env:LOCALAPPDATA\Packages\Microsoft.WindowsTerminal_8wekyb3d8bbwe\LocalState\settings.json"
function Test-WtKeys { (Test-Path $WtSettings) -and ((Get-Content $WtSettings -Raw -Encoding UTF8) -match '"keys"\s*:\s*"ctrl\+v"') }
function Set-WtKeys {
  New-Item -ItemType Directory (Split-Path $WtSettings) -Force | Out-Null
  if (Test-Path $WtSettings) {
    try { $s = Get-Content $WtSettings -Raw -Encoding UTF8 | ConvertFrom-Json } catch { return }   # 주석이 든 파일 = WT 가 직접 만든 것이라 이미 들어 있다
  } else {
    $s = [pscustomobject]@{ defaultProfile = '{574e775e-4f2a-5b96-ac1e-a2962a402336}'; profiles = [pscustomobject]@{ defaults = [pscustomobject]@{}; list = @() } }
  }
  $acts = @(@($s.actions) | Where-Object { $_ -and $_.id -notin 'User.copy.644BA8F2', 'User.paste' }) + @(
    [pscustomobject]@{ command = [pscustomobject]@{ action = 'copy'; singleLine = $false }; id = 'User.copy.644BA8F2' },
    [pscustomobject]@{ command = 'paste'; id = 'User.paste' })
  $keys = @(@($s.keybindings) | Where-Object { $_ -and $_.keys -notin 'ctrl+c', 'ctrl+v' }) + @(
    [pscustomobject]@{ id = 'User.copy.644BA8F2'; keys = 'ctrl+c' },
    [pscustomobject]@{ id = 'User.paste'; keys = 'ctrl+v' })
  $s | Add-Member actions $acts -Force
  $s | Add-Member keybindings $keys -Force
  [IO.File]::WriteAllText($WtSettings, ($s | ConvertTo-Json -Depth 20), (New-Object Text.UTF8Encoding $false))
}

# winget — LTSC 에는 없어서 GitHub 릴리스(앱 설치 관리자 + 의존 패키지)로 직접 깐다
# winget 이 있고 '살아 있는지'(20초 안에 --version 응답) — 반쯤 깔려 응답이 없으면 없는 것으로 보고 다시 깔게 한다
$script:WingetOk = $null
function Test-Winget {
  Refresh-Path
  $c = Get-Command winget -EA 0
  if (-not $c) { return $false }
  if ($null -eq $script:WingetOk) { $r = Invoke-Timed $c.Source @('--version') 20; $script:WingetOk = [bool]($r -and $r.Code -eq 0) }
  return $script:WingetOk
}
function Install-Winget {
  Say '   winget 설치 1/3: GitHub 에서 설치 파일을 받습니다(합쳐서 약 300MB — 몇 분 걸릴 수 있다)'
  $rel = Invoke-RestMethod 'https://api.github.com/repos/microsoft/winget-cli/releases/latest' -Headers @{ 'User-Agent' = 'dev-env' }
  $get = { param($like) $f = $rel.assets | Where-Object { $_.name -like $like } | Select-Object -First 1; Fetch $f.browser_download_url $f.name $f.size }
  $bundle = & $get 'Microsoft.DesktopAppInstaller_8wekyb3d8bbwe.msixbundle'
  $depZip = & $get 'DesktopAppInstaller_Dependencies.zip'
  $lic = & $get '*License1.xml'
  # Expand-Archive 는 PS 5.1 에서 매우 느려 멈춘 것처럼 보인다 — .NET 으로 x64 파일만 꺼낸다
  Say '   winget 설치 2/3: 필요한 64비트 파일만 꺼냅니다'
  Add-Type -AssemblyName System.IO.Compression.FileSystem
  $out = "$Tmp\wgdeps"; New-Item -ItemType Directory $out -Force | Out-Null
  $zip = [IO.Compression.ZipFile]::OpenRead($depZip)
  try {
    foreach ($en in $zip.Entries) {
      if ($en.FullName -match 'x64' -and $en.Name -match '\.(appx|msix)$') { [IO.Compression.ZipFileExtensions]::ExtractToFile($en, (Join-Path $out $en.Name), $true) }
    }
  } finally { $zip.Dispose() }
  $deps = @(Get-ChildItem $out -Recurse -Include *.appx, *.msix | ForEach-Object FullName)
  Say ('   winget 설치 3/3: 등록합니다(1~2분, 의존 패키지 {0}개)' -f $deps.Count)
  Add-AppxProvisionedPackage -Online -PackagePath $bundle -DependencyPackagePath $deps -LicensePath $lic | Out-Null
  Add-AppxPackage -Path $bundle -DependencyPath $deps -ForceUpdateFromAnyVersion -EA SilentlyContinue   # 반쯤 깔린 것도 다시 등록
  $script:WingetOk = $null   # 다시 살아 있는지 본다
}
# 설치 상태는 winget export 한 번으로 모아 본다(앱마다 물으면 느리다)
$script:WG = $null
function Get-WingetMap {
  $m = @{}
  if (-not (Test-Winget)) { return $m }
  New-Item -ItemType Directory $Tmp -Force | Out-Null
  $f = "$Tmp\winget-export.json"; Remove-Item $f -EA 0
  # 스토어(msstore) 목록은 빼고 winget 기본 목록만 — 스토어가 없는 PC 에서 거기서 멈출 수 있다. 처음엔 목록을 받느라 느릴 수 있어 2분까지
  $r = Invoke-Timed (Get-Command winget).Source @('export', '-o', "`"$f`"", '--source', 'winget', '--include-versions', '--accept-source-agreements', '--disable-interactivity') 120
  if (-not $r) { Write-Host '   winget 목록을 2분 안에 못 읽어 건너뜁니다 — 앱 상태는 제어판 기준으로 봅니다' -ForegroundColor Yellow }
  if (Test-Path $f) {
    try { foreach ($src in (Get-Content $f -Raw -Encoding UTF8 | ConvertFrom-Json).Sources) { foreach ($p in $src.Packages) { $m[$p.PackageIdentifier] = [string]$p.Version } } } catch {}
  }
  return $m
}
function Test-WingetPkg($id, $ver) {
  if ($null -eq $script:WG) { $script:WG = Get-WingetMap }
  if (-not $script:WG.ContainsKey($id)) { return $false }
  return (-not $ver) -or ($script:WG[$id] -eq $ver)
}
function Install-WingetPkg($id, $ver, $source) {
  if (-not (Test-Winget)) { Install-Winget }
  if (-not (Test-Winget)) { Say '   winget 이 없어 설치하지 못했습니다' Yellow; return }
  $a = @('install', '--id', $id, '--exact', '--silent', '--accept-package-agreements', '--accept-source-agreements', '--disable-interactivity')
  if ($ver) { $a += @('--version', $ver) }
  if ($source) { $a += @('--source', $source) }
  & winget @a
  $code = $LASTEXITCODE
  if ($code -in 0, -1978335189, -1978335135) {   # 성공 · 올릴 것 없음 · 이미 설치됨
    if ($ver) { & winget pin add --id $id --version $ver --accept-source-agreements --disable-interactivity *> $null }   # 모두 최신으로 에서도 안 올라가게
    if ($null -eq $script:WG) { $script:WG = @{} }
    $script:WG[$id] = $(if ($ver) { $ver } else { 'installed' })
    return
  }
  $hex = '0x{0:X8}' -f $code
  $why = switch ($hex) {
    '0x80190194' { '다운로드 주소가 없어졌다(404) — winget 목록이 아직 안 고쳐졌다. 며칠 뒤 다시 하거나 catalog.txt 에서 공식 주소(url:·latest:)로 바꾼다' }
    '0x8A150014' { 'winget 에서 그 아이디를 찾지 못했다 — catalog.txt 의 아이디 확인 (winget search 이름)' }
    default { '''진행 창 보기'' 로 winget 메시지를 본다' }
  }
  Say "   winget 실패 $hex — $why" Yellow
}
# USB 도구(catalog 의 usb:) — .zip 은 C:\Tools\이름 에 풀고, .exe·.msi 는 실행(설치 창은 사람이 넘긴다)
$ArpKeys = 'HKLM:\SOFTWARE\Microsoft\Windows\CurrentVersion\Uninstall\*', 'HKLM:\SOFTWARE\WOW6432Node\Microsoft\Windows\CurrentVersion\Uninstall\*', 'HKCU:\Software\Microsoft\Windows\CurrentVersion\Uninstall\*'
function Test-Arp($name) { [bool](Get-ItemProperty $ArpKeys -EA 0 | Where-Object { $_.DisplayName -and $_.DisplayName -like "*$name*" }) }
function Test-ArpExact($name) { [bool](Get-ItemProperty $ArpKeys -EA 0 | Where-Object { $_.DisplayName -eq $name }) }
# 링크에서 받아 바로 설치(늘 최신판) — 조용히 설치하는 옵션은 설치본 종류에 맞춘다: Inno Setup(HWiNFO 등) /VERYSILENT · 그 밖(NSIS: PotPlayer 등) /S
# 서명이 아예 없거나 깨진 파일은 실행하지 않는다(받는 곳에서 바꿔치기됐을 때)
function Install-UrlApp($url, $name) {
  $leaf = ($url -split '[/?#]' | Where-Object { $_ }) | Select-Object -Last 1
  if ($leaf -notmatch '\.(exe|msi)$') { $leaf = "$name.exe" }
  $p = Fetch $url $leaf
  $sig = (Get-AuthenticodeSignature $p).Status
  if ($sig -in 'NotSigned', 'HashMismatch') { Remove-Item $p -Force; throw "서명이 없거나 깨진 설치 파일이라 실행하지 않았습니다($sig): $url" }
  if ($leaf -match '\.msi$') { Start-Wait msiexec "/i `"$p`" /qn /norestart"; return }
  $inno = [Diagnostics.FileVersionInfo]::GetVersionInfo($p).Comments -like '*Inno Setup*'
  Start-Wait $p $(if ($inno) { '/VERYSILENT /SUPPRESSMSGBOXES /NORESTART /SP-' } else { '/S' })
}
# 공식 페이지에서 가장 새 파일을 찾아 설치 — 파일 이름이 패턴(*)에 맞는 링크 중 번호가 가장 큰 것
# (새 판이 나오면 예전 파일을 바로 지우는 곳은 winget 목록이 따라올 때까지 404 가 난다: HWiNFO)
function Find-LatestLink($page, $pattern) {
  $html = (Invoke-WebRequest $page -UseBasicParsing -TimeoutSec 30 -UserAgent 'Mozilla/5.0 (Windows NT 10.0; Win64; x64)').Content
  $links = foreach ($m in [regex]::Matches($html, 'href\s*=\s*["'']([^"''>]+)')) {
    try { $u = [Uri]::new([Uri]$page, [Net.WebUtility]::HtmlDecode($m.Groups[1].Value)) } catch { continue }
    if ([IO.Path]::GetFileName($u.AbsolutePath) -like $pattern) { $u.AbsoluteUri }
  }
  $links | Sort-Object { [regex]::Replace($_, '\d+', { param($d) $d.Value.PadLeft(12, '0') }) } | Select-Object -Last 1
}
function Install-LatestApp($spec, $name) {
  $page, $pattern = $spec -split '\s+', 2
  $url = Find-LatestLink $page $pattern
  if (-not $url) { throw "공식 페이지에서 '$pattern' 파일을 찾지 못했습니다 — 페이지가 바뀌었으면 catalog.txt 를 고친다: $page" }
  Say "   가장 새 파일: $url"
  Install-UrlApp $url $name
}
# winget 밖에서 깐 앱은 winget 목록에 안 잡힐 수 있다(예: Chrome) — 제어판 이름이나 스토어 앱 이름으로 한 번 더 본다
function Test-Hint($hint, $name) {
  if ($hint -like 'appx:*') { return [bool](Get-AppxPackage -Name $hint.Substring(5) -EA 0) }
  if ($hint -like 'file:*') { return Test-Path -LiteralPath $hint.Substring(5) }   # 설치 파일 경로로 확인 (제어판에 안 올라오는 앱: 팟플 등)
  if ($hint -like 'arp=*') { return Test-ArpExact $hint.Substring(4) }   # 이름이 정확히 같을 때만 (예: 32/64 구분)
  if ($hint -like 'arp:*') { return Test-Arp $hint.Substring(4) }
  return Test-Arp $name
}
function Install-UsbTool($name, $file) {
  if (-not $Usb) { Say '   USB(PC설치 폴더)를 찾지 못했습니다' Yellow; return }
  $p = Join-Path (Join-Path $Usb '도구') $file
  if (-not (Test-Path -LiteralPath $p)) { Say "   USB 에 파일이 없습니다: $p" Yellow; return }
  Unblock-File -LiteralPath $p -EA 0   # 내려받은 키트의 '인터넷에서 받은 파일' 표시 — 남아 있으면 SmartScreen 이 막는다
  switch ([IO.Path]::GetExtension($p).ToLower()) {
    '.zip' { Expand-Archive -LiteralPath $p "C:\Tools\$name" -Force }
    '.msi' { Start-Wait msiexec "/i `"$p`"" }
    default { Start-Wait $p }
  }
}
# 전원 설정의 현재 AC 값(초) — powercfg 출력의 마지막 두 16진수가 AC·DC
function Get-AcIndex($sub, $setting) {
  $h = @(powercfg /query SCHEME_CURRENT $sub $setting 2>$null | Select-String '0x[0-9a-fA-F]{8}' -AllMatches | ForEach-Object { $_.Matches } | Select-Object -Last 2)
  if ($h.Count -eq 2) { [Convert]::ToInt32($h[0].Value, 16) } else { -1 }
}

# ── 설치 항목 ───────────────────────────────────────────────────────
$Items = [System.Collections.Generic.List[object]]::new()
function Add-Item($group, $id, $name, $check, $install, [switch]$Off, [switch]$Reboot, [string]$OkText = '설치됨', [string]$Kind = '') {
  $Items.Add([pscustomobject]@{ Group = $group; Id = $id; Name = $name; Check = $check; Install = $install; Default = -not $Off; Reboot = [bool]$Reboot; OkText = $OkText; Kind = $Kind; Installed = $false })
}
$G1 = '① 인터넷'
$G2 = '② Windows 설정'
$G3 = '③ 드라이버'
$G4 = '④ 도구·앱'
$G5 = '⑤ 개발 환경'
$G6 = '⑥ 개발 소스 (GitHub 로그인)'
$G9 = '관리'

# ① 인터넷 — 연결돼 있으면 건드리지 않는다(지금 원격으로 쓰는 연결이 끊기지 않게)
Add-Item $G1 netdriver '네트워크 드라이버 — USB 의 PC설치\네트워크 드라이버 (이 PC 에 맞는 것만 들어간다)' { $script:Online } {
  if ($script:Online) { Say '   인터넷이 이미 연결돼 있어 건너뜁니다(지금 쓰는 연결을 끊지 않게)'; return }
  if (-not $Usb) { Say '   USB(PC설치 폴더)를 찾지 못했습니다' Yellow; return }
  pnputil /add-driver "$Usb\네트워크 드라이버\*.inf" /subdirs /install | Out-Host
  for ($i = 0; $i -lt 12 -and -not (Test-Online); $i++) { Start-Sleep -Seconds 5 }
  $script:Online = Test-Online
} -OkText '연결됨'

# ② Windows 설정 (WSH 에서 가져온 것 중 보안 장치를 끄지 않는 것만)
Add-Item $G2 power '전원: 고성능 · 절전 안 함 (원격 PC 가 잠들지 않게)' { (Get-AcIndex SUB_SLEEP STANDBYIDLE) -eq 0 } {
  powercfg /setactive 8c5e7fda-e8bf-4a96-9a85-a6e23a8c635c 2>$null
  powercfg /change standby-timeout-ac 0
  powercfg /change disk-timeout-ac 0
}
Add-Item $G2 hibernate '최대 절전 끄기 (C 드라이브 용량 확보)' { -not (Test-Path 'C:\hiberfil.sys') } { powercfg /hibernate off }
$toastKey = 'HKCU:\Software\Microsoft\Windows\CurrentVersion\PushNotifications'
Add-Item $G2 toast '알림 팝업 끄기' { (Get-ItemProperty $toastKey -EA 0).ToastEnabled -eq 0 } {
  New-Item $toastKey -Force | Out-Null
  Set-ItemProperty $toastKey ToastEnabled 0 -Type DWord
}
Add-Item $G2 ssd 'SSD 최적화: SysMain·Windows 검색 서비스 끄기 (시작 메뉴 파일 검색이 느려진다)' { @(Get-Service SysMain, WSearch -EA 0 | Where-Object StartType -ne 'Disabled').Count -eq 0 } {
  foreach ($n in 'SysMain', 'WSearch') { Stop-Service $n -Force -EA 0; Set-Service $n -StartupType Disabled -EA 0 }
}
$dcKey = 'HKLM:\SOFTWARE\Policies\Microsoft\Windows\DataCollection'
Add-Item $G2 telemetry '진단 데이터 보내기 최소화' { (Get-ItemProperty $dcKey -EA 0).AllowTelemetry -eq 0 } {
  New-Item $dcKey -Force | Out-Null
  Set-ItemProperty $dcKey AllowTelemetry 0 -Type DWord
}
$kbKey = 'HKLM:\SYSTEM\CurrentControlSet\Services\i8042prt\Parameters'
Add-Item $G2 kbtype3 '키보드 종류 유형 3 (Shift+Space 로 한/영)' { $p = Get-ItemProperty $kbKey -EA 0; ($p.'LayerDriver KOR' -eq 'kbd101c.dll') -and ($p.OverrideKeyboardSubtype -eq 5) } {
  Set-ItemProperty $kbKey 'LayerDriver KOR' 'kbd101c.dll'
  Set-ItemProperty $kbKey OverrideKeyboardIdentifier 'PCAT_101CKEY'
  Set-ItemProperty $kbKey OverrideKeyboardType 8 -Type DWord
  Set-ItemProperty $kbKey OverrideKeyboardSubtype 5 -Type DWord
} -Reboot
$ctxKey = 'HKCU:\Software\Classes\CLSID\{86ca1aa0-34aa-4e8b-a509-50c905bae2a2}\InprocServer32'
Add-Item $G2 classicmenu '윈11 우클릭 메뉴를 예전 방식으로' { Test-Path $ctxKey } { New-Item $ctxKey -Value '' -Force | Out-Null } -Off -Reboot

# ③ 드라이버 — 그래픽은 NVIDIA 공식 조회로 늘 최신판. 랜(네트워크) 드라이버는 지금 인터넷을 쓰는 어댑터라 건드리지 않는다(원격이 끊긴다) — ① 의 USB '네트워크 드라이버' 로만.
$NvCacheDir = "$env:LOCALAPPDATA\dev-env"
function Get-NvGpu { Get-CimInstance Win32_VideoController -EA 0 | Where-Object { $_.PNPDeviceID -like 'PCI\VEN_10DE*' -and $_.Name -match 'GeForce' } | Select-Object -First 1 }
# 윈도우 드라이버 버전(예: 27.21.14.5751) → NVIDIA 표기 버전(457.51): 끝 두 묶음을 붙여 뒤 5자리에 점 하나
function Convert-NvVer($v) { $d = ("$v".Split('.')[-2..-1] -join '') -replace '\D'; if ($d.Length -lt 5) { return $null }; [double]($d.Substring($d.Length - 5).Insert(3, '.')) }
# 카드 이름 → NVIDIA 조회 ID(계열 psid·카드 pfid) → 최신 드라이버(@{Version;Url}). 못 찾으면 $null. 상태 확인이 느려지지 않게 12시간 캐시(설치할 때는 -Fresh 로 새로 조회).
function Get-NvLatest($gpu, [switch]$Fresh) {
  if (-not $gpu) { return $null }
  $cache = Join-Path $NvCacheDir ('nv-' + ($gpu.PNPDeviceID -replace '[^A-Za-z0-9]', '_') + '.json')
  if (-not $Fresh -and (Test-Path $cache) -and (Get-Item $cache).LastWriteTime -gt (Get-Date).AddHours(-12)) {
    try { $c = Get-Content $cache -Raw | ConvertFrom-Json; return @{ Version = [double]$c.Version; Url = $c.Url } } catch {}
  }
  try {
    $ch = (Get-CimInstance Win32_SystemEnclosure -EA 0).ChassisTypes
    $laptop = @(8, 9, 10, 14) | Where-Object { $ch -contains $_ }   # 노트북이면 '(Notebooks)' 계열을 쓴다
    $name = ($gpu.Name -replace '^NVIDIA\s+', '').Trim()
    $series = (Invoke-RestMethod 'https://www.nvidia.com/Download/API/lookupValueSearch.aspx?TypeID=2' -TimeoutSec 20).LookupValueSearch.LookupValues.LookupValue |
      Where-Object { [bool]($_.Name -match 'Notebooks') -eq [bool]$laptop } | Sort-Object { [int]$_.Value } -Descending
    $psid = $null; $pfid = $null
    foreach ($s in $series) {
      $cards = (Invoke-RestMethod "https://www.nvidia.com/Download/API/lookupValueSearch.aspx?TypeID=3&ParentID=$($s.Value)" -TimeoutSec 20).LookupValueSearch.LookupValues.LookupValue
      $hit = $cards | Where-Object { $_.Name -eq $name } | Select-Object -First 1
      if ($hit) { $psid = $s.Value; $pfid = $hit.Value; break }
    }
    if (-not $pfid) { return $null }
    $osid = if ([Environment]::OSVersion.Version.Build -ge 22000) { 135 } else { 57 }   # Win11 / Win10
    $u = "https://gfwsl.geforce.com/services_toolkit/services/com/nvidia/services/AjaxDriverService.php?func=DriverManualLookup&psid=$psid&pfid=$pfid&osID=$osid&languageCode=1033&isWHQL=1&dch=1&sort1=0&numberOfResults=1"
    $d = (Invoke-RestMethod $u -TimeoutSec 20).IDS[0].downloadInfo
    if ($d.DownloadURL) {
      $r = @{ Version = [double]$d.Version; Url = $d.DownloadURL }
      try { New-Item -ItemType Directory $NvCacheDir -Force | Out-Null; $r | ConvertTo-Json | Set-Content $cache } catch {}
      return $r
    }
  } catch {}
  return $null
}
# NVIDIA GeForce 카드가 있을 때만 항목이 뜬다 (없는 PC 에선 목록에 안 나온다)
if (Get-NvGpu) {
  Add-Item $G3 gpudriver 'NVIDIA 그래픽 드라이버 — 공식 조회로 최신판 (설치 중 화면이 잠깐 깜빡인다 · 원격 중엔 기본 꺼짐)' {
    $g = Get-NvGpu; $inst = Convert-NvVer $g.DriverVersion; $lat = Get-NvLatest $g
    (-not $lat) -or (-not $inst) -or ($inst -ge $lat.Version)   # 최신을 못 알아내면 들볶지 않는다
  } {
    $g = Get-NvGpu; $lat = Get-NvLatest $g -Fresh
    if (-not $lat) { Say '   NVIDIA 최신 드라이버를 못 찾았습니다 (인터넷·카드 이름 확인)' Yellow; return }
    Say "   지금 $(Convert-NvVer $g.DriverVersion) → 최신 $($lat.Version)"
    $p = Fetch $lat.Url "nvidia-$($lat.Version).exe"
    if ((Get-AuthenticodeSignature $p).Status -ne 'Valid') { Remove-Item $p -Force; throw '서명이 올바르지 않은 NVIDIA 설치 파일 — 실행하지 않음' }
    Say '   설치 중 — 화면이 잠깐 깜빡입니다 (원격이면 잠시 끊겨 보일 수 있음)'
    Start-Wait $p '-s -noreboot'
  } -Off
}

# ④ 도구·앱 — winget 먼저, 그다음 catalog.txt 의 앱들
Add-Item $G4 winget 'winget — 아래 앱들을 늘 최신판으로 설치하는 도구' { Test-Winget } { Install-Winget }
$Catalog = @(Get-Content "$Repo\catalog.txt" -Encoding UTF8 -EA 0 | Where-Object { $_ -match '\S' -and $_ -notmatch '^\s*#' } | ForEach-Object {
  $c = @($_.Split('|') | ForEach-Object { $_.Trim() })
  if ($c.Count -ge 3) { [pscustomobject]@{ Group = $c[0]; Name = $c[1]; How = $c[2]; On = ($c.Count -lt 4) -or ($c[3] -ne 'off'); Hint = $(if ($c.Count -ge 5) { $c[4] } else { '' }) } }
})
function Add-CatalogItem($e) {
  $grp = if ($e.Group -eq '개발 환경') { $G5 } else { "$G4/$($e.Group)" }
  $kind, $spec = $e.How.Split(':', 2)
  switch ($kind) {
    'winget' {
      $id, $ver = $spec.Split('@', 2)
      $chk = "(Test-WingetPkg $(Q $id) $(Q $ver))" + $(if (-not $ver) { " -or (Test-Hint $(Q $e.Hint) $(Q $e.Name))" } else { '' })   # 버전 고정은 버전까지 맞아야 한다
      $ins = "Install-WingetPkg $(Q $id) $(Q $ver) ''"
    }
    'msstore' { $chk = "(Test-WingetPkg $(Q $spec) '') -or (Test-Hint $(Q $e.Hint) $(Q $e.Name))"; $ins = "Install-WingetPkg $(Q $spec) '' 'msstore'" }
    'usb' {
      $chk = if ($spec -like '*.zip') { "Test-Path -LiteralPath $(Q "C:\Tools\$($e.Name)")" } else { "Test-Arp $(Q $e.Name)" }
      $ins = "Install-UsbTool $(Q $e.Name) $(Q $spec)"
    }
    'url' { $chk = "Test-Hint $(Q $e.Hint) $(Q $e.Name)"; $ins = "Install-UrlApp $(Q $spec) $(Q $e.Name)" }
    'latest' { $chk = "Test-Hint $(Q $e.Hint) $(Q $e.Name)"; $ins = "Install-LatestApp $(Q $spec) $(Q $e.Name)" }
    default { return }
  }
  # winget·스토어 앱만 winget 이 먼저 있어야 한다 (url·latest 는 winget 없이 받는다)
  Add-Item $grp "app:$($e.Name)" $e.Name ([scriptblock]::Create($chk)) ([scriptblock]::Create($ins)) -Off:(-not $e.On) -Kind $(if ($kind -eq 'usb') { 'usb' } elseif ($kind -in 'winget', 'msstore') { 'winget' } else { '' })
}
foreach ($e in $Catalog | Where-Object Group -ne '개발 환경') { Add-CatalogItem $e }

# TrafficMonitor — 공식 GitHub 최신판을 C:\Tools\TrafficMonitor 에 깔고, 지금 쓰는 설정(작업 표시줄에 속도 표시)을 넣고, 로그인할 때 자동 실행
# (winget 판은 설정 없이 기본값이라 작업 표시줄에 안 나오고 자동 실행도 안 된다)
# 이 프로그램은 실행 파일이 '관리자로만 실행'(requireAdministrator) 이라 시작 프로그램 폴더·Run 으로는 로그인 때 막히거나 묻는다
# → TrafficMonitor 가 스스로 쓰는 것과 같은 예약 작업(\TrafficMonitor\Autorun for 사용자, 가장 높은 권한)으로 띄운다. 옵션 창의 '자동 실행' 도 켜진 것으로 보인다
$TmDir = 'C:\Tools\TrafficMonitor'
$TmTask = "Autorun for $env:USERNAME"
function Test-TmTask { $t = Get-ScheduledTask -TaskPath '\TrafficMonitor\' -TaskName $TmTask -EA 0; [bool]$t -and $t.Actions[0].Execute -eq "$TmDir\TrafficMonitor.exe" -and $t.Principal.RunLevel -eq 'Highest' }
Add-Item "$G4/도구" trafficmonitor 'TrafficMonitor — 작업 표시줄에 속도 표시 · 로그인할 때 자동 실행 (공식 최신판)' {
  (Test-Path "$TmDir\TrafficMonitor.exe") -and (Test-TmTask) -and [bool]((Get-Content "$TmDir\config.ini" -EA 0) -match '^\s*show_task_bar_wnd\s*=\s*true')
} {
  $rel = Invoke-RestMethod 'https://api.github.com/repos/zhongyang219/TrafficMonitor/releases/latest' -Headers @{ 'User-Agent' = 'dev-env' }
  $a = $rel.assets | Where-Object { $_.name -match '_x64\.zip$' } | Select-Object -First 1   # Lite 가 아닌 전체판
  $zip = Fetch $a.browser_download_url $a.name $a.size
  # 설정·사용 기록: 이미 깐 곳 → 예전에 쓰던 다운로드 폴더 → 저장소 기본 설정 순으로 가져온다
  $from = @($TmDir, "$env:USERPROFILE\Downloads\TrafficMonitor") | Where-Object { Test-Path "$_\config.ini" } | Select-Object -First 1
  $keep = "$Tmp\tm-keep"; Remove-Item $keep -Recurse -Force -EA 0; New-Item -ItemType Directory $keep -Force | Out-Null
  if ($from) { Copy-Item "$from\config.ini", "$from\history_traffic.dat" $keep -Force -EA 0; Say "   설정을 가져옵니다: $from" }
  else { Copy-Item "$PSScriptRoot\trafficmonitor\config.ini" $keep -Force; Say '   저장소의 기본 설정(작업 표시줄 표시)을 넣습니다' }
  Get-Process TrafficMonitor -EA 0 | Stop-Process -Force; Start-Sleep 1   # 파일을 바꾸려면 꺼야 한다
  # 예전 설치 목록이 winget 으로 깐 TrafficMonitor(설정 없는 판)는 지운다 — 두 벌이 되지 않게
  # (winget 은 '사용자 범위로 깐 것은 관리자 창에서 못 지운다' 며 거절한다 → winget 이 깔 때 만든 폴더·PATH·제어판 항목을 직접 지운다)
  foreach ($k in Get-ChildItem 'HKCU:\Software\Microsoft\Windows\CurrentVersion\Uninstall' -EA 0 | Where-Object PSChildName -like 'zhongyang219.TrafficMonitor*') {
    $loc = (Get-ItemProperty $k.PSPath).InstallLocation
    if ($loc -like "$env:LOCALAPPDATA\Microsoft\WinGet\Packages\*") {
      Remove-Item $loc -Recurse -Force -EA 0
      $up = [Environment]::GetEnvironmentVariable('Path', 'User')
      if ($up -and $up.IndexOf($loc, [StringComparison]::OrdinalIgnoreCase) -ge 0) {
        [Environment]::SetEnvironmentVariable('Path', (($up -split ';' | Where-Object { $_ -and -not $_.StartsWith($loc, [StringComparison]::OrdinalIgnoreCase) }) -join ';'), 'User')
      }
    }
    Remove-Item $k.PSPath -Recurse -Force
    Say '   예전에 winget 으로 깐 TrafficMonitor 를 지웠습니다'
  }
  $x = "$Tmp\tm-zip"; Remove-Item $x -Recurse -Force -EA 0
  Add-Type -AssemblyName System.IO.Compression.FileSystem
  [IO.Compression.ZipFile]::ExtractToDirectory($zip, $x)
  $src = (Get-ChildItem $x -Recurse -Filter TrafficMonitor.exe | Select-Object -First 1).DirectoryName
  New-Item -ItemType Directory $TmDir -Force | Out-Null
  Copy-Item "$src\*" $TmDir -Recurse -Force
  Copy-Item "$keep\*" $TmDir -Force
  [IO.File]::WriteAllText("$TmDir\global_cfg.ini", "[config]`r`nportable_mode = true`r`n", [Text.Encoding]::ASCII)
  # 옛 자동 실행(시작 프로그램 바로가기·Run)은 지운다 — 예약 작업과 겹치면 두 번 떠서 '이미 실행 중' 창이 뜬다
  $st = [Environment]::GetFolderPath('Startup'); $ws = New-Object -ComObject WScript.Shell
  Get-ChildItem $st -Filter '*.lnk' -EA 0 | Where-Object { $ws.CreateShortcut($_.FullName).TargetPath -like '*\TrafficMonitor.exe' } | Remove-Item -Force
  Remove-ItemProperty 'HKCU:\Software\Microsoft\Windows\CurrentVersion\Run' -Name TrafficMonitor -EA 0
  # 로그인할 때 자동 실행 — TrafficMonitor 자기 설정과 같게(3초 뒤, 시간 제한 없음, 배터리여도 실행)
  $user = "$env:USERDOMAIN\$env:USERNAME"
  $act = New-ScheduledTaskAction -Execute "$TmDir\TrafficMonitor.exe"
  $tr = New-ScheduledTaskTrigger -AtLogOn -User $user; $tr.Delay = 'PT3S'
  $pr = New-ScheduledTaskPrincipal -UserId $user -LogonType Interactive -RunLevel Highest
  $set = New-ScheduledTaskSettingsSet -AllowStartIfOnBatteries -DontStopIfGoingOnBatteries -MultipleInstances IgnoreNew -ExecutionTimeLimit ([TimeSpan]::Zero)
  Register-ScheduledTask -TaskPath '\TrafficMonitor\' -TaskName $TmTask -Action $act -Trigger $tr -Principal $pr -Settings $set -Force | Out-Null
  # 바로 띄운다 — 로그인 때와 똑같이 그 작업으로
  Start-ScheduledTask -TaskPath '\TrafficMonitor\' -TaskName $TmTask
  Start-Sleep 3
}

# 반디집 무료판의 광고·분석·알림·업데이트 확인을 hosts 로 막는다 — 방화벽이 꺼져 있어도 동작한다
# (주소는 반디집 파일 안에 적힌 것에서 뽑았다. 반디소프트 자기 주소만 — 구글 광고 주소는 브라우저까지 망가뜨려 두지 않는다)
$HostsFile = "$env:SystemRoot\System32\drivers\etc\hosts"
$BandiBlock = 'adv.bandi.so', 'ana.bandi.so', 'log.bandi.so', 'ver.bandi.so', 'notice.bandisoft.com', 'go.bandisoft.com'
function Get-BandiMissing { $h = @(Get-Content $HostsFile -EA 0); @($BandiBlock | Where-Object { $d = [regex]::Escape($_); -not ($h -match "^\s*0\.0\.0\.0\s+$d\s*$") }) }
Add-Item "$G4/기본 앱" bandiad '반디집 광고 막기 (광고·분석·알림·업데이트 확인 주소를 hosts 로 — 방화벽이 꺼져 있어도 된다)' { (Get-BandiMissing).Count -eq 0 } {
  $add = Get-BandiMissing
  if ($add) { [IO.File]::AppendAllText($HostsFile, "`r`n# dev-env: Bandizip ads/telemetry/notice/update-check block`r`n" + (($add | ForEach-Object { "0.0.0.0 $_" }) -join "`r`n") + "`r`n", [Text.Encoding]::ASCII) }
  ipconfig /flushdns | Out-Null
}

Add-Item "$G4/원격" hangul '한/영 전환 (AutoHotkey 관리자 권한 + 10분마다 자동 확인·복구)' {
  $t = Get-ScheduledTask -TaskName 'hangul-ahk' -EA 0
  [bool]$t -and ($t.Principal.RunLevel -eq 'Highest') -and (Test-Path "$env:ProgramData\hangul.ahk") -and [bool](Get-Process AutoHotkey64 -EA 0)
} {
  $exe = "$env:ProgramFiles\AutoHotkey\v2\AutoHotkey64.exe"
  if (-not (Test-Path $exe)) { Start-Process (Fetch 'https://www.autohotkey.com/download/ahk-v2.exe' 'ahk-v2.exe') -Wait -ArgumentList '/silent' }
  $ahk = "$env:ProgramData\hangul.ahk"
  Copy-Item "$PSScriptRoot\hangul.ahk" $ahk -Force
  # 옛 방식(시작 폴더) 정리 — 예약 작업이 대신한다
  $s = [Environment]::GetFolderPath('CommonStartup')
  Remove-Item "$s\hangul.ahk", "$s\hangul.lnk" -Force -EA 0
  # 떠 있는 것(예전 일반 권한 실행분 포함)은 끈다 — #SingleInstance Ignore 라 그대로 두면 새로 띄운 쪽이 빠진다
  Stop-ScheduledTask -TaskName 'hangul-ahk' -EA 0
  Get-CimInstance Win32_Process -Filter "Name LIKE 'AutoHotkey%'" | Where-Object CommandLine -like '*hangul.ahk*' | ForEach-Object { Stop-Process -Id $_.ProcessId -Force -EA 0 }

  # 로그인할 때 + 10분마다: 스크립트가 죽어 있으면 다시 띄운다 (#SingleInstance Ignore 라 중복 안 뜬다)
  # 관리자 권한(Highest)으로 띄운다 — 일반 권한이면 관리자 창(관리자 PowerShell·VirtualBox 등)에 한/영 키를 못 보낸다(UIPI)
  $user = "$env:USERDOMAIN\$env:USERNAME"
  Unregister-ScheduledTask -TaskName 'hangul-ahk' -Confirm:$false -EA 0
  $act = New-ScheduledTaskAction -Execute $exe -Argument "`"$ahk`""
  $t1 = New-ScheduledTaskTrigger -AtLogOn -User $user
  $t2 = New-ScheduledTaskTrigger -Once -At (Get-Date).AddMinutes(1) -RepetitionInterval (New-TimeSpan -Minutes 10) -RepetitionDuration (New-TimeSpan -Days 3650)
  $pr = New-ScheduledTaskPrincipal -UserId $user -LogonType Interactive -RunLevel Highest
  $st = New-ScheduledTaskSettingsSet -AllowStartIfOnBatteries -DontStopIfGoingOnBatteries -StartWhenAvailable -MultipleInstances IgnoreNew -ExecutionTimeLimit ([TimeSpan]::Zero)
  Register-ScheduledTask -TaskName 'hangul-ahk' -Action $act -Trigger $t1, $t2 -Principal $pr -Settings $st -Description '원격 한/영 전환 스크립트 실행·감시' | Out-Null
  Start-ScheduledTask -TaskName 'hangul-ahk'

  # 바탕화면: 이상할 때 두 번 누르면 즉시 복구 — 예약 작업을 껐다 켠다(직접 띄우면 일반 권한이 돼서 관리자 창에서 안 된다)
  $ws = New-Object -ComObject WScript.Shell
  $lnk = $ws.CreateShortcut(([Environment]::GetFolderPath('Desktop')) + '\한영 다시 시작.lnk')
  $lnk.TargetPath = "$env:SystemRoot\System32\cmd.exe"
  $lnk.Arguments = '/c schtasks /end /tn hangul-ahk & schtasks /run /tn hangul-ahk'
  $lnk.WorkingDirectory = $env:ProgramData
  $lnk.IconLocation = "$exe,0"
  $lnk.WindowStyle = 7
  $lnk.Save()
}
Add-Item "$G4/원격" tailscale 'Tailscale (원격 접속 VPN)' { Test-Path "$env:ProgramFiles\Tailscale\tailscale.exe" } {
  Start-Process (Fetch 'https://pkgs.tailscale.com/stable/tailscale-setup-latest.exe' 'tailscale.exe') -Wait -ArgumentList '/quiet'
}
Add-Item "$G4/원격" sunshine "Sunshine $($V.Sunshine) (Moonlight 원격 호스트)" { Test-Path "$env:ProgramFiles\Sunshine\sunshine.exe" } {
  Msi (Fetch "https://github.com/LizardByte/Sunshine/releases/download/$($V.Sunshine)/Sunshine-Windows-AMD64-installer.msi" "sunshine-$($V.Sunshine).msi")
}

# ⑤ 개발 환경 — 버전 고정(맨 위 $V)
Add-Item $G5 git "Git $($V.Git) (Git Bash 포함)" { Test-Path 'C:\Program Files\Git\cmd\git.exe' } {
  $p = Fetch "https://github.com/git-for-windows/git/releases/download/v$($V.Git)/$($V.GitFile)" $V.GitFile
  Start-Process $p -Wait -ArgumentList '/VERYSILENT', '/NORESTART', '/NOCANCEL', '/SP-', '/o:PathOption=Cmd', '/o:CRLFOption=CRLFCommitAsIs'
}
Add-Item $G5 node "Node.js $($V.Node)" { Test-Path 'C:\Program Files\nodejs\node.exe' } {
  Msi (Fetch "https://nodejs.org/dist/v$($V.Node)/node-v$($V.Node)-x64.msi" "node-$($V.Node).msi")
}
Add-Item $G5 python "Python $($V.Python)" { [bool](Get-ChildItem 'C:\Program Files\Python3*\python.exe' -EA 0) } {
  $p = Fetch "https://www.python.org/ftp/python/$($V.Python)/python-$($V.Python)-amd64.exe" "python-$($V.Python).exe"
  Start-Process $p -Wait -ArgumentList '/quiet', 'InstallAllUsers=1', 'PrependPath=1', 'Include_test=0'
}
Add-Item $G5 gh "GitHub CLI $($V.Gh)" { Test-Path 'C:\Program Files\GitHub CLI\gh.exe' } {
  Msi (Fetch "https://github.com/cli/cli/releases/download/v$($V.Gh)/gh_$($V.Gh)_windows_amd64.msi" "gh-$($V.Gh).msi")
}
Add-Item $G5 pwsh "PowerShell $($V.Pwsh)" { Test-Path 'C:\Program Files\PowerShell\7\pwsh.exe' } {
  $p = Fetch "https://github.com/PowerShell/PowerShell/releases/download/v$($V.Pwsh)/PowerShell-$($V.Pwsh)-win-x64.msi" "pwsh-$($V.Pwsh).msi"
  Start-Process msiexec -Wait -ArgumentList "/i `"$p`" /qn /norestart ADD_PATH=1 ENABLE_PSREMOTING=0 REGISTER_MANIFEST=1"
}
Add-Item $G5 terminal "Windows Terminal $($V.Terminal) (PowerShell을 탭으로 열기·Ctrl+C/V 복사·붙여넣기)" { [bool](Get-AppxPackage Microsoft.WindowsTerminal) -and (Test-WtKeys) } {
  if (-not (Get-AppxPackage Microsoft.WindowsTerminal)) {
    $base = "https://github.com/microsoft/terminal/releases/download/v$($V.Terminal)/Microsoft.WindowsTerminal_$($V.Terminal)_8wekyb3d8bbwe.msixbundle"
    $bundle = Fetch $base "wt-$($V.Terminal).msixbundle"
    $kit = Fetch "$($base)_Windows10_PreinstallKit.zip" "wt-$($V.Terminal)-kit.zip"
    Expand-Archive $kit "$Tmp\wtkit" -Force
    $deps = Get-ChildItem "$Tmp\wtkit" -Recurse -Filter *.appx | Where-Object { $_.Name -match 'x64' } | ForEach-Object FullName
    Add-AppxPackage -Path $bundle -DependencyPath $deps
  }
  $k = 'HKCU:\Console\%%Startup'; New-Item $k -Force | Out-Null
  Set-ItemProperty $k DelegationConsole '{2EACA947-7F5F-4CFA-BA87-8F7FBEEFBE69}'
  Set-ItemProperty $k DelegationTerminal '{E12CFF52-A866-4C77-9A90-F570A7AA2C6B}'
  Set-WtKeys
}
Add-Item $G5 npmtools "clasp $($V.Clasp) · firebase-tools $($V.Firebase) (Node 필요)" { (Has 'clasp.cmd') -and (Has 'firebase.cmd') } {
  Refresh-Path; & npm.cmd i -g "@google/clasp@$($V.Clasp)" "firebase-tools@$($V.Firebase)" 2>&1 | Select-Object -Last 2
}
Add-Item $G5 claude 'Claude Code' { Test-Path "$env:USERPROFILE\.local\bin\claude.exe" } {
  Invoke-RestMethod https://claude.ai/install.ps1 | Invoke-Expression
  $up = [Environment]::GetEnvironmentVariable('Path', 'User')
  foreach ($d in @("$env:USERPROFILE\.local\bin", "$env:APPDATA\npm")) { if ($up -notlike "*$d*") { $up = ($up.TrimEnd(';') + ';' + $d).TrimStart(';') } }
  [Environment]::SetEnvironmentVariable('Path', $up, 'User')
}
Add-Item $G5 claudeconfig "Claude 설정 — 오케스트라 모드(말로 켜고 끄기·/orchestra)·상태 표시줄·기본 모델 $ClaudeModel(직접 고른 모델은 그대로)" {
  $c = "$env:USERPROFILE\.claude"
  $m = try { (Get-Content "$c\settings.json" -Raw -EA Stop | ConvertFrom-Json) } catch { $null }
  (Test-Path "$c\commands\orchestra.md") -and (Test-Path "$c\statusline.sh") -and ($m.statusLine.command -match 'statusline\.sh') -and
    ((-not $ClaudeModel) -or ($m.model -and $m.model -notin $OldClaudeModels))
} {
  $c = "$env:USERPROFILE\.claude"
  foreach ($d in 'agents', 'fable', 'commands') { New-Item -ItemType Directory "$c\$d" -Force | Out-Null }
  Copy-Item "$Repo\claude\agents\*" "$c\agents\" -Force
  Copy-Item "$Repo\claude\fable\fable.md" "$c\fable\fable.md" -Force
  Copy-Item "$Repo\claude\commands\*" "$c\commands\" -Force
  Copy-Item "$Repo\claude\statusline.sh" "$c\statusline.sh" -Force
  Copy-Item "$Repo\claude\CLAUDE.md" "$c\CLAUDE.md" -Force
  # settings.json 은 덮지 않고 statusLine 을 넣는다. 기본 모델은 비어 있거나 예전 기본값일 때만 넣는다(직접 고른 모델은 그대로)
  $sf = "$c\settings.json"
  $s = if (Test-Path $sf) { Get-Content $sf -Raw | ConvertFrom-Json } else { [pscustomobject]@{} }
  $s | Add-Member statusLine ([pscustomobject]@{ type = 'command'; command = 'bash ~/.claude/statusline.sh' }) -Force
  if ($ClaudeModel -and (-not $s.model -or $s.model -in $OldClaudeModels)) { $s | Add-Member model $ClaudeModel -Force }
  [IO.File]::WriteAllText($sf, ($s | ConvertTo-Json -Depth 20), (New-Object Text.UTF8Encoding $false))
}
Add-Item $G5 gitconfig 'git 기본 설정(줄바꿈 유지·한글 파일명·이름)' { (& git config --global core.autocrlf 2>$null) -eq 'false' } {
  Refresh-Path
  git config --global core.autocrlf false
  git config --global core.quotepath false
  git config --global init.defaultBranch main
  git config --global user.name $GitName
  git config --global user.email $GitEmail
}
foreach ($e in $Catalog | Where-Object Group -eq '개발 환경') { Add-CatalogItem $e }

# ⑥ 개발 소스: projects.txt 한 줄 = 항목 하나 (GitHub 로그인이 필요해 기본 체크 해제)
$projLines = Get-Content "$Repo\projects.txt" -Encoding UTF8 | Where-Object { $_ -match '\S' -and $_ -notmatch '^\s*#' }
foreach ($line in $projLines) {
  $c = $line.Split('|') | ForEach-Object { $_.Trim() }
  $folder = Join-Path $DevRoot ($c[0] -replace '/', '\'); $url = $c[1]; $lnk = $c[2]
  $id = 'proj:' + $c[0]
  $label = if ($url) { "$lnk — GitHub 에서 받기 + 바로가기" } else { "$lnk — 바로가기만 (폴더는 백업에서 직접 복원)" }
  $check = [scriptblock]::Create("(Test-Path -LiteralPath $(Q $folder)) -and (Test-Path -LiteralPath $(Q "$([Environment]::GetFolderPath('Desktop'))\$lnk.lnk"))")
  $install = {
    param($folder, $url, $lnk)
    New-Item -ItemType Directory (Split-Path $folder -Parent) -Force | Out-Null
    if ($url -and -not (Test-Path -LiteralPath $folder)) {
      Refresh-Path
      if ($url -match 'github\.com') {
        & gh auth status 2>$null | Out-Null
        if ($LASTEXITCODE -ne 0) {
          # 로그인은 진행 창에서 8자리 코드를 보고 Enter 를 눌러야 한다 — 숨어 있던 진행 창을 앞으로 꺼낸다
          Say '   GitHub 로그인이 필요합니다 — 앞에 뜬 진행 창의 8자리 코드를 확인하고 Enter (브라우저가 열립니다)' Yellow
          Show-OwnConsole
          & gh auth login -h github.com -p https -w; & gh auth setup-git
        }
      }
      & git clone $url $folder
    }
    if (-not (Test-Path -LiteralPath $folder)) { Write-Host "   폴더가 없습니다: $folder — 백업을 풀어 넣은 뒤 이 항목만 다시 실행하세요"; New-Item -ItemType Directory $folder -Force | Out-Null }
    $ws = New-Object -ComObject WScript.Shell
    foreach ($p in @([Environment]::GetFolderPath('Desktop'), "$env:APPDATA\Microsoft\Windows\Start Menu\Programs")) {
      $s = $ws.CreateShortcut("$p\$lnk.lnk")
      $s.TargetPath = "$env:LOCALAPPDATA\Microsoft\WindowsApps\wt.exe"
      $s.Arguments = "--title `"$lnk`" -d . `"C:\Program Files\PowerShell\7\pwsh.exe`" -NoLogo -NoExit -Command `"& '$env:USERPROFILE\.local\bin\claude.exe'`""
      $s.WorkingDirectory = $folder
      $s.IconLocation = "$env:USERPROFILE\.local\bin\claude.exe,0"
      $s.Save()
    }
  }
  Add-Item $G6 $id $label $check ([scriptblock]::Create("& {$install} $(Q $folder) $(Q $url) $(Q $lnk)")) -Off
}

# 관리
$UsbKitFiles = [ordered]@{ 'start.ps1' = 'start.ps1'; 'start.cmd' = '시작하기.cmd'; 'README.txt' = '읽어보기.txt' }   # 저장소 usb\ 이름 → USB 이름
function Get-UsbVersion($k) { Get-Content -LiteralPath "$k\last-good\windows\version.txt" -Encoding UTF8 -EA 0 | Select-Object -First 1 }
# USB 맨 위의 '버전 ….txt' — 탐색기에서 파일 이름만 보면 이 USB 의 설치 프로그램 버전을 안다
function Set-UsbMarker($k, $v) {
  Get-ChildItem -LiteralPath $k -Filter '버전 *.txt' -EA 0 | Remove-Item -Force
  [IO.File]::WriteAllText((Join-Path $k ('버전 ' + ($v -replace ':', '.') + '.txt')), "이 USB 에 들어 있는 설치 프로그램 버전: $v`r`n설치 화면 제목에도 같은 버전이 보입니다.`r`n", (New-Object Text.UTF8Encoding $true))
}
Add-Item $G9 usbkit "USB 시작하기·예비판을 이 버전($Version)으로 만들기·갱신 (Ventoy USB 의 PC설치)" {
  $k = Find-UsbKit
  [bool]$k -and ((Get-UsbVersion $k) -eq $Version) -and -not ($UsbKitFiles.Keys | Where-Object { -not (Test-Path -LiteralPath "$k\$($UsbKitFiles[$_])") -or (Get-FileHash -LiteralPath "$k\$($UsbKitFiles[$_])").Hash -ne (Get-FileHash "$PSScriptRoot\usb\$_").Hash })
} {
  $k = Find-UsbKit
  if (-not $k) {
    $v = Get-Volume -EA 0 | Where-Object { $_.FileSystemLabel -eq 'Ventoy' -and $_.DriveLetter } | Select-Object -First 1
    if (-not $v) { Say '   Ventoy USB 를 찾지 못했습니다' Yellow; return }
    $k = "$($v.DriveLetter):\PC설치"
  }
  New-Item -ItemType Directory "$k\네트워크 드라이버", "$k\도구" -Force | Out-Null
  foreach ($src in $UsbKitFiles.Keys) { Copy-Item "$PSScriptRoot\usb\$src" "$k\$($UsbKitFiles[$src])" -Force }
  # 예비판(last-good) = 지금 돌고 있는 이 설치 프로그램 그대로(files.txt 목록)
  foreach ($f in Get-Content "$PSScriptRoot\files.txt" -Encoding UTF8 | Where-Object { $_ -match '\S' -and $_ -notmatch '^\s*#' }) {
    $f = $f.Trim(); $dst = Join-Path "$k\last-good" ($f -replace '/', '\')
    New-Item -ItemType Directory (Split-Path $dst) -Force | Out-Null
    Copy-Item -LiteralPath (Join-Path $Repo ($f -replace '/', '\')) $dst -Force
  }
  Set-UsbMarker $k $Version
  Say "   $k 를 버전 $Version 으로 맞췄습니다 — 기종마다 랜 드라이버 폴더를 '네트워크 드라이버' 에 넣어 두세요"
} -Off

# ── 모두 최신으로(일꾼) ─────────────────────────────────────────────
# winget 으로 올릴 수 있는 앱을 하나씩 올린다. 버전 고정(catalog 의 @버전)과 $UpgradeSkip(원격 호스트)은 건드리지 않는다.
# url·latest 앱(winget 밖)은 공식 최신 설치본으로 덮어 깐다 — 이미 깐 것만.
function Run-Upgrade {
  Start-Transcript "$env:USERPROFILE\dev-env-setup.log" -Append | Out-Null
  try { $Host.UI.RawUI.WindowTitle = "PC 설치 버전 $Version — 모두 최신으로 (이 창을 닫지 마세요)" } catch {}
  Mark "@@HWND $([DevEnv.Con]::GetConsoleWindow())"
  Say "모두 최신으로 — 설치 프로그램 버전 $Version" Cyan
  $done = 0; $fail = 0
  if (-not (Test-Winget)) { Say '   winget 이 없어 winget 앱은 건너뜁니다 — ④ 의 winget 항목으로 먼저 설치' Yellow }
  else {
    $wg = (Get-Command winget).Source
    # 버전 고정은 winget 핀으로도 묶는다(핀 없이 예전에 깐 PC 가 있다) — winget 이 스스로도 건너뛰게
    $pins = @(foreach ($e in $Catalog) { if ($e.How -match '^winget:([^@]+)@(.+)$') { $Matches[1]; Invoke-Timed $wg @('pin', 'add', '--id', $Matches[1], '--version', $Matches[2], '--accept-source-agreements', '--disable-interactivity') 60 | Out-Null } })
    # url·latest 로 까는 앱은 아래에서 따로 — winget 목록에 같은 앱이 보여도 넘긴다(HWiNFO: winget 주소가 404)
    $own = @(foreach ($e in $Catalog) { if ($e.How -match '^(url|latest):' -and $e.Hint -match '^arp[:=](.+)$') { $Matches[1] } })
    Say '   새 판이 있는 앱을 찾습니다 (1~2분)'
    $r = Invoke-Timed $wg @('upgrade', '--source', 'winget', '--accept-source-agreements', '--disable-interactivity') 180
    if (-not $r) { Say '   winget 이 3분 안에 답하지 않아 winget 앱은 건너뜁니다' Yellow }
    $rows = @(if ($r) { ConvertFrom-WingetTable $r.Out })
    $map = $null
    $todo = @(foreach ($row in $rows) {
      if ($row.Count -lt 4 -or -not $row[1]) { continue }
      $name, $id, $cur, $new = $row[0..3]
      # 칸이 좁아 아이디가 '…' 로 잘렸으면 설치 목록(winget export)에서 앞부분이 같은 것 하나를 찾는다
      if ($id -match '…$') {
        if ($null -eq $map) { $map = Get-WingetMap }
        $hit = @($map.Keys | Where-Object { $_.StartsWith($id.TrimEnd('…')) })
        if ($hit.Count -ne 1) { Say "   아이디가 잘려 건너뜀: $name ($id)" DarkGray; continue }
        $id = $hit[0]
      }
      if ($id -in $UpgradeSkip) { Say "   건너뜀(원격 접속이 끊긴다): $name" DarkGray; continue }
      if ($id -in $pins -or ($UpgradeFixed | Where-Object { $id -like $_ })) { Say "   건너뜀(버전 고정): $name $cur" DarkGray; continue }
      if ($own | Where-Object { $name -like "*$_*" }) { continue }
      [pscustomobject]@{ Name = $name; Id = $id; Cur = $cur; New = $new }
    })
    if (-not $todo) { Say '   winget 앱은 모두 최신입니다' Green }
    $n = 0
    foreach ($t in $todo) {
      $n++; Say "[$n/$($todo.Count)] $($t.Name)  $($t.Cur) → $($t.New)" Cyan
      & $wg upgrade --id $t.Id --exact --silent --accept-package-agreements --accept-source-agreements --disable-interactivity
      $code = $LASTEXITCODE
      if ($code -in 0, -1978335189) { Say '   완료' Green; $done++ } else { Say ("   실패 0x{0:X8} — '진행 창 보기' 로 winget 메시지를 본다" -f $code) Yellow; $fail++ }
    }
  }
  foreach ($e in $Catalog | Where-Object { $_.How -match '^(url|latest):' }) {
    if (-not (Test-Hint $e.Hint $e.Name)) { continue }   # 이 PC 에 깐 것만
    Say "[winget 밖] $($e.Name)" Cyan
    try {
      $kind, $spec = $e.How.Split(':', 2)
      if ($kind -eq 'latest') {
        $page, $pattern = $spec -split '\s+', 2
        $url = Find-LatestLink $page $pattern
        if (-not $url) { throw "공식 페이지에서 '$pattern' 파일을 찾지 못했습니다: $page" }
        # 파일 이름의 숫자에 지금 판 번호(제어판 버전에서 숫자만)가 들어 있으면 이미 최신 — 예: hwi_852x.exe ↔ 8.52
        $dv = if ($e.Hint -match '^arp[:=](.+)$') { $h = $Matches[1]; (Get-ItemProperty $ArpKeys -EA 0 | Where-Object { $_.DisplayName -like "*$h*" } | Select-Object -First 1).DisplayVersion -replace '\D' }
        if ($dv -and ([IO.Path]::GetFileName($url) -replace '\D') -like "*$dv*") { Say '   이미 최신' DarkGray; continue }
        Say "   가장 새 파일: $url"
        Install-UrlApp $url $e.Name
      } else { Install-UrlApp $spec $e.Name }
      Say '   완료' Green; $done++
    } catch { Say "   실패: $_" Red; $fail++ }
  }
  Say ("끝 — 올림 {0} · 실패 {1}" -f $done, $fail) $(if ($fail) { 'Yellow' } else { 'Green' })
  Stop-Transcript | Out-Null
  Remove-Item $Tmp -Recurse -Force -EA 0
  if (-not $NoPause) { Read-Host 'Enter를 누르면 닫습니다' }
}
if ($Upgrade) { Run-Upgrade; return }

# ── 상태 ────────────────────────────────────────────────────────────
function Invoke-Check($it) { try { [bool](& $it.Check) } catch { $false } }
Show-Prep '상태 확인 1/2: winget 이 살아 있는지 보고 설치된 앱 목록을 읽습니다 (처음엔 최대 2분)'
if (Test-Winget) { $script:WG = Get-WingetMap }
else { $script:WG = @{}; if (-not $Progress -and -not $Gui) { Write-Host '   winget 이 없거나 응답하지 않습니다 — 설치 화면의 winget 항목으로 (다시) 깔 수 있습니다' -ForegroundColor Yellow } }
$n = 0
foreach ($it in $Items) { $n++; if ($n % 5 -eq 1) { Show-Prep "상태 확인 2/2: 나머지 항목 ($n/$($Items.Count))" }; $it.Installed = Invoke-Check $it }
if ($List) { "버전 $Version"; foreach ($it in $Items) { '{0,-4} {1,-26} {2,-30} {3}' -f $(if ($it.Installed) { 'OK' } else { '--' }), ($it.Group -replace '/', ' > '), $it.Id, $it.Name }; return }

# ── 설치(일꾼) ──────────────────────────────────────────────────────
function Run-Install($Pick) {
  Start-Transcript "$env:USERPROFILE\dev-env-setup.log" -Append | Out-Null
  try { $Host.UI.RawUI.WindowTitle = "PC 설치 버전 $Version — 진행 중 (이 창을 닫지 마세요)" } catch {}
  Mark "@@HWND $([DevEnv.Con]::GetConsoleWindow())"   # 설치 화면의 '진행 창 보기' 가 이 창을 꺼낸다
  Say "설치 프로그램 버전 $Version" Cyan
  $reboot = @(); $n = 0
  foreach ($it in $Pick) {
    $n++
    if (Invoke-Check $it) { Say "[$n/$($Pick.Count)] $($it.Name) — 이미 $($it.OkText), 건너뜀" DarkGray; continue }
    Say "[$n/$($Pick.Count)] $($it.Name)" Cyan
    try {
      & $it.Install; Refresh-Path
      if (Invoke-Check $it) {
        Say '   완료' Green
        if ($it.Reboot) { $reboot += $it.Name; Mark "@@REBOOT $($it.Name)" }
      } else { Say '   확인 필요 — 로그 참고' Yellow }
    } catch { Say "   실패: $_" Red }
  }
  if ($Progress) { $script:WG = $null; foreach ($it in $Items) { Mark ("@@STATUS {0}`t{1}" -f $it.Id, [int](Invoke-Check $it)) } }
  Stop-Transcript | Out-Null
  Remove-Item $Tmp -Recurse -Force -EA 0

  Write-Host ''
  if ($reboot) { Say "재부팅해야 적용되는 것: $($reboot -join ', ')" Yellow }
  Write-Host '──────── 사람이 해야 하는 일 ────────' -ForegroundColor Green
  Write-Host ' • 새 터미널을 열고 claude 실행 → 브라우저 로그인'
  Write-Host ' • clasp login / firebase login (Apps Script·Firebase 쓰는 프로젝트만)'
  Write-Host ' • Tailscale 트레이 아이콘 → 로그인 / Sunshine: https://localhost:47990 관리자 계정·PIN'
  Write-Host ' • 디펜더·방화벽·업데이트 차단 같은 보안 설정은 이 목록에 없다 — 필요하면 직접(WSH 등)'
  Write-Host ' • git이 없는 프로젝트·Claude 기억은 구글 드라이브 백업에서 복원 (README 참고)'
  Write-Host ' 로그: %USERPROFILE%\dev-env-setup.log'
  if (-not $NoPause) { Read-Host 'Enter를 누르면 닫습니다' }
}
if ($All) { Run-Install $Items; return }
if ($Only) { $ids = $Only -join ',' -split ','; Run-Install @($Items | Where-Object { $ids -contains $_.Id }); return }

# ── 설치 화면 ───────────────────────────────────────────────────────
try {
  Add-Type -ReferencedAssemblies System.Windows.Forms -WarningAction SilentlyContinue -TypeDefinition @'
public class DevEnvTree : System.Windows.Forms.TreeView {
  // 체크 상자를 빠르게 두 번 누르면 화면과 실제 체크가 어긋나는 TreeView 버그를 막는다
  protected override void WndProc(ref System.Windows.Forms.Message m) {
    if (m.Msg == 0x0203) { m.Result = System.IntPtr.Zero; return; }
    base.WndProc(ref m);
  }
}
'@
  $tv = New-Object DevEnvTree
} catch { $tv = New-Object System.Windows.Forms.TreeView }
function Pt($x, $y) { New-Object System.Drawing.Point($x, $y) }
function Sz($w, $h) { New-Object System.Drawing.Size($w, $h) }
function New-Ctl($type, $x, $y, $w, $h, $text = '') { $c = New-Object "System.Windows.Forms.$type"; $c.Location = Pt $x $y; $c.Size = Sz $w $h; if ($text) { $c.Text = $text }; $c }
function Show-Msg($text, $buttons = 'OK', $icon = 'None') { [System.Windows.Forms.MessageBox]::Show($text, 'PC 설치', $buttons, $icon) }
$font = New-Object System.Drawing.Font('Malgun Gothic', 10)

$form = New-Object System.Windows.Forms.Form
$form.Text = "PC 설치 — 버전 $Version"; $form.ClientSize = Sz 864 800; $form.StartPosition = 'CenterScreen'; $form.Font = $font
try { $form.Icon = [System.Drawing.Icon]::ExtractAssociatedIcon("$PSHOME\powershell.exe") } catch {}   # 작업 표시줄 아이콘
$board = (Get-CimInstance Win32_BaseBoard -EA 0).Product
$head = New-Ctl Label 12 10 714 24
$head.Text = "버전 $Version · $env:COMPUTERNAME · $board · " + $(if ($script:Online) { '인터넷 연결됨' } else { '인터넷 없음 — 먼저 ① 네트워크 드라이버' }) + $(if ($Usb) { " · USB $Usb" } else { '' })
# 진행 창 = 설치를 실제로 하는 숨은 콘솔(일꾼). 받는 진행률·winget 메시지를 보고 싶을 때만 꺼낸다
$bCon = New-Ctl Button 732 6 120 28 '진행 창 보기'
$bCon.Enabled = $false
$tv.CheckBoxes = $true; $tv.Location = Pt 12 40; $tv.Size = Sz 840 560; $tv.Font = $font
$log = New-Ctl TextBox 12 610 840 130
$log.Multiline = $true; $log.ReadOnly = $true; $log.ScrollBars = 'Vertical'; $log.BackColor = [System.Drawing.Color]::White
$form.Controls.AddRange(@($head, $bCon, $tv, $log))

$GroupNodes = @{}
function Get-GroupNode($path) {
  if ($GroupNodes.ContainsKey($path)) { return $GroupNodes[$path] }
  $parts = @($path -split '/')
  $node = New-Object System.Windows.Forms.TreeNode($parts[-1])
  $node.ForeColor = [System.Drawing.Color]::Navy
  if ($parts.Count -gt 1) { [void](Get-GroupNode (($parts[0..($parts.Count - 2)]) -join '/')).Nodes.Add($node) } else { [void]$tv.Nodes.Add($node) }
  $GroupNodes[$path] = $node
  return $node
}
function Set-NodeLook($node) {
  $it = $node.Tag
  $node.Text = $it.Name + $(if ($it.Installed) { "   — $($it.OkText)" } elseif ($it.Reboot) { '   (재부팅 필요)' } else { '' })
  $node.ForeColor = $(if ($it.Installed) { [System.Drawing.Color]::Gray } else { [System.Drawing.Color]::Black })
}
function Set-Down($node, $state) { foreach ($c in $node.Nodes) { $c.Checked = $state; Set-Down $c $state } }
function Sync-Up($node) { while ($node) { $all = $node.Nodes.Count -gt 0; foreach ($c in $node.Nodes) { if (-not $c.Checked) { $all = $false } }; $node.Checked = $all; $node = $node.Parent } }

foreach ($g in @($G1, $G2, $G3, $G4, "$G4/기본 앱", "$G4/원격", "$G4/도구", "$G4/Claude", $G5, $G6, $G9)) { [void](Get-GroupNode $g) }
$NodeById = @{}
function Add-ItemNode($it) {
  $node = New-Object System.Windows.Forms.TreeNode($it.Name)
  $node.Tag = $it
  Set-NodeLook $node
  $node.Checked = (-not $it.Installed) -and $it.Default
  [void](Get-GroupNode $it.Group).Nodes.Add($node)
  $NodeById[$it.Id] = $node
  $node
}
foreach ($it in $Items) { [void](Add-ItemNode $it) }
foreach ($k in @($GroupNodes.Keys)) { if ($GroupNodes[$k].Nodes.Count -eq 0) { $GroupNodes[$k].Remove(); $GroupNodes.Remove($k) } }   # 빈 묶음은 숨긴다(나중에 앱 추가로 생기면 다시 만든다)
foreach ($n in $NodeById.Values) { Sync-Up $n.Parent }
$tv.ExpandAll()
if ($tv.Nodes.Count) { $tv.Nodes[0].EnsureVisible() }

$script:Busy = $false
$tv.Add_AfterCheck({
  param($s, $ev)
  if ($script:Busy) { return }
  $script:Busy = $true
  try { Set-Down $ev.Node $ev.Node.Checked; Sync-Up $ev.Node.Parent } finally { $script:Busy = $false }
})

function New-Btn($text, $x, $w, $onClick) { $b = New-Ctl Button $x 754 $w 34 $text; $b.Add_Click($onClick); $form.Controls.Add($b); return $b }
function Set-AllChecked($state) { $script:Busy = $true; foreach ($n in $tv.Nodes) { $n.Checked = $state; Set-Down $n $state }; $script:Busy = $false }
$bAll = New-Btn '전체 선택' 12 96 { Set-AllChecked $true }
$bNone = New-Btn '전체 해제' 112 96 { Set-AllChecked $false }
$bAdd = New-Btn '앱 추가…' 222 100 { Show-AddApp }
$bUp = New-Btn '모두 최신으로' 326 120 { Start-Upgrade }
$bSrc = New-Btn '소스 올리기…' 450 116 { Show-Upload }
$bGo = New-Btn '선택한 것 설치' 604 140 { Start-Install }
$bClose = New-Btn '닫기' 752 100 { $form.Close() }
function Set-Busy($on) { foreach ($b in $bAll, $bNone, $bAdd, $bUp, $bSrc, $bGo) { $b.Enabled = -not $on } }

# 설치는 숨은 콘솔(일꾼)에서 돌리고, 이 화면은 그 진행 기록만 읽는다 — 그래서 화면이 멈추지 않는다
$timer = New-Object System.Windows.Forms.Timer; $timer.Interval = 500
$script:WorkerHwnd = [IntPtr]::Zero
function Start-Worker([string[]]$ModeArgs, [string]$StartMsg) {
  $script:Prog = Join-Path $env:TEMP "dev-env-progress-$PID.log"
  [IO.File]::WriteAllText($script:Prog, '')
  $script:Pos = 0; $script:RebootList = @(); $script:WorkerHwnd = [IntPtr]::Zero
  $a = @('-NoProfile', '-ExecutionPolicy', 'Bypass', '-File', "`"$PSCommandPath`"") + $ModeArgs + @('-NoPause', '-Progress', "`"$script:Prog`"")
  if ($Usb) { $a += @('-Usb', "`"$Usb`"") }
  $log.AppendText("$StartMsg`r`n")
  $script:Worker = Start-Process powershell -ArgumentList $a -PassThru -WindowStyle Hidden
  Set-Busy $true
  $timer.Start()
}
function Start-Install {
  $sel = @($Items | Where-Object { $NodeById[$_.Id].Checked })
  if (-not $sel) { [void](Show-Msg '설치할 것을 체크하세요.'); return }
  if (($sel | Where-Object Kind -eq 'winget') -and -not ($sel | Where-Object Id -eq 'winget') -and -not (Test-Winget)) { $sel = @($Items | Where-Object Id -eq 'winget') + $sel }
  $onlyFile = Join-Path $env:TEMP "dev-env-only-$PID.txt"
  [IO.File]::WriteAllLines($onlyFile, @($sel | ForEach-Object Id), (New-Object Text.UTF8Encoding $false))
  Start-Worker @('-OnlyFile', "`"$onlyFile`"") "설치를 시작합니다 — 받는 진행률·자세한 메시지는 오른쪽 위 '진행 창 보기'."
}
function Start-Upgrade {
  $msg = "설치된 앱을 모두 최신판으로 올립니다.`r`n`r`n - winget 앱: 새 판이 있는 것만`r`n - 팟플레이어·HWiNFO 처럼 winget 밖의 앱: 공식 최신 설치본으로`r`n - 빼는 것: 버전 고정(VirtualBox 등) · Chrome 원격 데스크톱 호스트(올리는 동안 원격이 끊긴다)`r`n`r`n앱이 켜져 있으면 잠깐 꺼질 수 있습니다. 진행할까요?"
  if ((Show-Msg $msg 'YesNo' 'Question') -ne 'Yes') { return }
  Start-Worker @('-Upgrade') "모두 최신으로 — 시작합니다 (자세한 메시지는 '진행 창 보기')."
}
function Set-WorkerShown($show) {
  if ($script:WorkerHwnd -eq [IntPtr]::Zero) { return }
  [void][DevEnv.Con]::ShowWindow($script:WorkerHwnd, $(if ($show) { 5 } else { 0 }))
  if ($show) { [void][DevEnv.Con]::SetForegroundWindow($script:WorkerHwnd) }
  $bCon.Text = $(if ($show) { '진행 창 숨기기' } else { '진행 창 보기' })
}
$bCon.Add_Click({ Set-WorkerShown (-not [DevEnv.Con]::IsWindowVisible($script:WorkerHwnd)) })
function Show-Line($line) {
  if ($line -match "^@@STATUS (.+)`t([01])$") {
    $n = $NodeById[$Matches[1]]
    if ($n) {
      $n.Tag.Installed = ($Matches[2] -eq '1'); Set-NodeLook $n
      if ($n.Tag.Installed) { $script:Busy = $true; $n.Checked = $false; Sync-Up $n.Parent; $script:Busy = $false }
    }
  } elseif ($line -match '^@@REBOOT (.+)$') { $script:RebootList += $Matches[1] }
  elseif ($line -match '^@@HWND (\d+)$') { $script:WorkerHwnd = [IntPtr][long]$Matches[1]; $bCon.Enabled = $script:WorkerHwnd -ne [IntPtr]::Zero }
  else { $log.AppendText("$line`r`n") }
}
function Read-Progress {
  if (-not $script:Prog -or -not (Test-Path -LiteralPath $script:Prog)) { return }
  try {
    $fs = [IO.File]::Open($script:Prog, [IO.FileMode]::Open, [IO.FileAccess]::Read, [IO.FileShare]::ReadWrite)
    try {
      $len = $fs.Length
      if ($len -le $script:Pos) { return }
      $buf = New-Object byte[] ([int]($len - $script:Pos))
      [void]$fs.Seek($script:Pos, [IO.SeekOrigin]::Begin)
      $got = $fs.Read($buf, 0, $buf.Length)
      if ($got -le 0) { return }
      $end = [Array]::LastIndexOf($buf, [byte]10, $got - 1)   # 다 쓴 줄까지만 읽는다
      if ($end -lt 0) { return }
      $script:Pos += $end + 1
      foreach ($line in ([Text.Encoding]::UTF8.GetString($buf, 0, $end + 1) -split "`r?`n")) { if ($line) { Show-Line $line } }
    } finally { $fs.Dispose() }
  } catch {}
}
function Finish-Worker {
  Set-Busy $false
  $script:WorkerHwnd = [IntPtr]::Zero; $bCon.Enabled = $false; $bCon.Text = '진행 창 보기'
  $log.AppendText("끝났습니다. (기록: %USERPROFILE%\dev-env-setup.log)`r`n")
  if ($script:RebootList.Count) {
    $msg = "재부팅해야 적용되는 것이 있습니다:`r`n - " + ($script:RebootList -join "`r`n - ") + "`r`n`r`n원격으로 접속 중이면 재부팅하는 동안 연결이 끊깁니다.`r`n지금 재부팅할까요?"
    if ((Show-Msg $msg 'YesNo' 'Question') -eq 'Yes') { Restart-Computer -Force }
  }
}
$timer.Add_Tick({ Read-Progress; if ($script:Worker -and $script:Worker.HasExited) { $timer.Stop(); Read-Progress; Finish-Worker } })
$form.Add_FormClosing({
  param($s, $ev)
  if ($script:Worker -and -not $script:Worker.HasExited) {
    if ((Show-Msg '아직 진행 중입니다. 이 화면을 닫으면 진행 창이 나타나 거기서 계속됩니다. 닫을까요?' 'YesNo') -ne 'Yes') { $ev.Cancel = $true }
    else { Set-WorkerShown $true }
  }
})

# ── git (앱 추가·소스 올리기) ───────────────────────────────────────
function Invoke-Git($dir, [string[]]$argList, [int]$sec = 60) {
  Refresh-Path
  $exe = (Get-Command git.exe -EA 0).Source
  if (-not $exe) { return @{ Code = -1; Out = ''; Err = 'git 이 없습니다 — ⑤ 의 Git 을 먼저 설치하세요' } }
  $env:GIT_TERMINAL_PROMPT = '0'   # 콘솔에서 비밀번호를 묻다 멈추지 않게 — GitHub 로그인은 Git 자격 증명 창이 띄운다
  $r = Invoke-Timed $exe (@('-C', (Quote-Arg $dir)) + @($argList | ForEach-Object { Quote-Arg $_ })) $sec
  if (-not $r) { return @{ Code = -1; Out = ''; Err = "git 이 ${sec}초 안에 끝나지 않았습니다" } }
  return $r
}
function Get-GitText($r) { (($r.Err, $r.Out) -join "`n").Trim() }
# 커밋 메시지는 파일로 넘긴다 — 한글이 명령줄에서 깨지지 않게
function Invoke-GitCommit($dir, $message) {
  $mf = Join-Path $env:TEMP "dev-env-commit-$PID.txt"
  [IO.File]::WriteAllText($mf, "$message`n", (New-Object Text.UTF8Encoding $false))
  try { Invoke-Git $dir @('commit', '-q', '-F', $mf) 120 } finally { Remove-Item $mf -EA 0 }
}
# 올리기 — 다른 PC 에서 먼저 올린 것이 있어 거절되면 받아서(rebase) 한 번 더. 받다가 충돌하면 되돌리고 알린다
function Invoke-GitPush($dir, [bool]$hasUpstream) {
  $push = if ($hasUpstream) { @('push', '-q') } else { @('push', '-q', '-u', 'origin', 'HEAD') }
  $p = Invoke-Git $dir $push 180
  if ($p.Code -eq 0) { return @{ Ok = $true; Text = '' } }
  if ($hasUpstream -and (Get-GitText $p) -match 'rejected|fetch first|non-fast-forward') {
    $pl = Invoke-Git $dir @('pull', '--rebase', '-q') 180
    if ($pl.Code -ne 0) { [void](Invoke-Git $dir @('rebase', '--abort')); return @{ Ok = $false; Text = "GitHub 에 먼저 올라간 것과 같은 곳을 고쳐 합치지 못했습니다 — 직접 정리가 필요합니다:`r`n$(Get-GitText $pl)" } }
    $p = Invoke-Git $dir $push 180
    if ($p.Code -eq 0) { return @{ Ok = $true; Text = '(GitHub 에 먼저 올라간 것을 받아 합친 뒤 올림)' } }
  }
  return @{ Ok = $false; Text = Get-GitText $p }
}

# ── 앱 추가: winget 에서 찾아 catalog.txt 에 한 줄 넣고 GitHub 에 올린다 ──
# 목록을 고쳐 올릴 저장소 — 이 설치 프로그램이 저장소 안에서 돌면 그곳, 아니면 이 PC 의 C:\dev\dev-env
function Find-EditRepo { foreach ($d in @($Repo, (Join-Path $DevRoot 'dev-env'))) { if (Test-Path -LiteralPath (Join-Path $d '.git')) { return (Resolve-Path -LiteralPath $d).Path } }; $null }
# catalog.txt 에 한 줄 넣기 — 같은 묶음의 마지막 줄 뒤에(없으면 맨 끝). 파일의 BOM·줄바꿈 방식은 그대로
function Add-CatalogLine($file, $group, $line) {
  $bytes = [IO.File]::ReadAllBytes($file)
  $skip = if ($bytes.Length -ge 3 -and $bytes[0] -eq 0xEF -and $bytes[1] -eq 0xBB -and $bytes[2] -eq 0xBF) { 3 } else { 0 }
  $text = [Text.Encoding]::UTF8.GetString($bytes, $skip, $bytes.Length - $skip)
  $nl = if ($text -match "`r`n") { "`r`n" } else { "`n" }
  $lines = [Collections.Generic.List[string]]::new([string[]]($text -split "`r?`n"))
  while ($lines.Count -and $lines[$lines.Count - 1] -eq '') { $lines.RemoveAt($lines.Count - 1) }
  $at = -1; for ($i = 0; $i -lt $lines.Count; $i++) { if ($lines[$i] -notmatch '^\s*#' -and $lines[$i].Split('|')[0].Trim() -eq $group) { $at = $i } }
  if ($at -ge 0) { $lines.Insert($at + 1, $line) } else { $lines.Add($line) }
  [IO.File]::WriteAllText($file, (($lines -join $nl) + $nl), (New-Object Text.UTF8Encoding ($skip -eq 3)))
}
# 저장소의 catalog.txt 에 넣고 커밋·푸시. 결과: @{ Ok(목록에 들어갔나); Pushed; Text }
function Publish-CatalogLine($group, $line, $what) {
  $rp = Find-EditRepo
  if (-not $rp) { return @{ Ok = $false; Pushed = $false; NoRepo = $true; Text = "이 PC 에 dev-env 저장소($DevRoot\dev-env)가 없어 GitHub 목록에는 올리지 못했습니다 — 이번 설치 화면에만 넣었습니다.`r`n(⑥ 개발 소스의 dev-env 를 받은 뒤 다시 추가하면 모든 PC 에 나옵니다)" } }
  $r = Invoke-Git $rp @('diff', '--cached', '--quiet')
  if ($r.Code -ne 0) { return @{ Ok = $false; Text = "$rp 에 커밋을 기다리는 다른 변경이 있어 건드리지 않았습니다 — 그것부터 정리하세요." } }
  $r = Invoke-Git $rp @('diff', '--quiet', '--', 'catalog.txt')
  if ($r.Code -ne 0) { return @{ Ok = $false; Text = "$rp\catalog.txt 에 올리지 않은 수정이 있어 건드리지 않았습니다 — 그것부터 올리거나 되돌리세요." } }
  $r = Invoke-Git $rp @('pull', '--ff-only', '-q') 120
  if ($r.Code -ne 0) { return @{ Ok = $false; Text = "GitHub 최신 목록을 받지 못해 넣지 않았습니다:`r`n$(Get-GitText $r)" } }
  Add-CatalogLine (Join-Path $rp 'catalog.txt') $group $line
  # 버전 = 지금 시각 (저장소의 커밋 훅과 같은 형식 — 훅이 없는 PC 에서도 설치 화면 제목에 새 버전이 보이게)
  [IO.File]::WriteAllText((Join-Path $rp 'windows\version.txt'), (Get-Date -Format 'yyyy-MM-dd HH:mm') + "`n")
  [void](Invoke-Git $rp @('add', '--', 'catalog.txt', 'windows/version.txt'))
  $c = Invoke-GitCommit $rp "앱 추가 — $what"
  if ($c.Code -ne 0) { return @{ Ok = $true; Pushed = $false; Repo = $rp; Text = "목록에는 넣었지만 커밋하지 못했습니다:`r`n$(Get-GitText $c)" } }
  $p = Invoke-GitPush $rp $true
  if (-not $p.Ok) { return @{ Ok = $true; Pushed = $false; Repo = $rp; Text = "커밋은 했지만 GitHub 에 올리지 못했습니다 — 나중에 '소스 올리기' 로 dev-env 를 올리세요:`r`n$($p.Text)" } }
  return @{ Ok = $true; Pushed = $true; Repo = $rp; Text = 'GitHub 에 올렸습니다 — 다른 PC 의 설치 화면에도 이 앱이 나옵니다.' }
}
function Show-AddApp {
  if (-not (Test-Winget)) { [void](Show-Msg 'winget 이 없습니다 — ④ 도구·앱 의 winget 을 먼저 설치하세요.'); return }
  $wgExe = (Get-Command winget).Source
  $st = @{ Line = ''; Id = ''; How = '' }
  $d = New-Object System.Windows.Forms.Form
  $d.Text = '앱 추가 — winget 에서 찾아 목록(catalog.txt)에 넣기'; $d.ClientSize = Sz 720 560; $d.StartPosition = 'CenterParent'; $d.Font = $font
  $d.FormBorderStyle = 'FixedDialog'; $d.MaximizeBox = $false; $d.MinimizeBox = $false
  $q = New-Ctl TextBox 12 14 580 26
  $bFind = New-Ctl Button 600 12 108 30 '찾기'
  $lv = New-Ctl ListView 12 52 696 300
  $lv.View = 'Details'; $lv.FullRowSelect = $true; $lv.MultiSelect = $false; $lv.HideSelection = $false
  foreach ($c in @(@('이름', 210), @('아이디', 270), @('버전', 110), @('찾은 곳', 90))) { [void]$lv.Columns.Add($c[0], $c[1]) }
  $l1 = New-Ctl Label 12 366 130 24 '목록에 보일 이름'
  $nm = New-Ctl TextBox 146 362 562 26
  $l2 = New-Ctl Label 12 400 130 24 '묶음'
  $grp = New-Ctl ComboBox 146 396 200 26
  foreach ($g in @(@('기본 앱', '원격', '도구', 'Claude', '개발 환경') + @($Catalog | ForEach-Object Group) | Select-Object -Unique)) { [void]$grp.Items.Add($g) }
  $grp.Text = '도구'
  $on = New-Ctl CheckBox 360 398 220 24 '새 PC 에서 기본으로 체크'
  $pin = New-Ctl CheckBox 584 398 130 24 '이 버전으로 고정'
  $prev = New-Ctl Label 12 434 696 44
  $prev.ForeColor = [System.Drawing.Color]::DimGray
  $msg = New-Ctl Label 12 482 696 26 '이름(영어가 잘 찾아진다)을 넣고 찾기 — 예: notepad, honeyview, kakaotalk'
  $bOk = New-Ctl Button 412 516 190 34 '목록에 추가하고 올리기'
  $bOk.Enabled = $false
  $bX = New-Ctl Button 608 516 100 34 '닫기'
  $bX.DialogResult = 'Cancel'
  $d.Controls.AddRange(@($q, $bFind, $lv, $l1, $nm, $l2, $grp, $on, $pin, $prev, $msg, $bOk, $bX))
  $d.AcceptButton = $bFind; $d.CancelButton = $bX

  $bFind.Add_Click({
    $text = $q.Text.Trim(); if (-not $text) { return }
    $bFind.Enabled = $false; $bOk.Enabled = $false; $lv.Items.Clear(); $msg.Text = "winget 에서 '$text' 를 찾는 중..."
    $r = Invoke-Timed $wgExe @('search', (Quote-Arg $text), '--source', 'winget', '--accept-source-agreements', '--disable-interactivity') 60
    $rows = @(if ($r) { ConvertFrom-WingetTable $r.Out })
    foreach ($row in $rows) {
      if ($row.Count -lt 3 -or -not $row[1]) { continue }
      $li = New-Object System.Windows.Forms.ListViewItem($row[0])
      foreach ($k in 1..3) { [void]$li.SubItems.Add($(if ($row.Count -gt $k) { $row[$k] } else { '' })) }
      [void]$lv.Items.Add($li)
    }
    $msg.Text = if (-not $r) { 'winget 이 1분 안에 답하지 않았습니다 — 다시 찾기' } elseif ($lv.Items.Count) { "$($lv.Items.Count)개 — 하나를 고르세요" } else { '찾은 앱이 없습니다 — 다른 이름(영어)으로 찾아 보세요' }
    $bFind.Enabled = $true
  })
  $upd = {
    if ($lv.SelectedItems.Count -eq 0) { $bOk.Enabled = $false; $prev.Text = ''; return }
    $s = $lv.SelectedItems[0]
    $st.Id = $s.SubItems[1].Text; $ver = $s.SubItems[2].Text
    $st.How = "winget:$($st.Id)" + $(if ($pin.Checked -and $ver -and $ver -ne 'Unknown') { "@$ver" } else { '' })
    $name = $nm.Text.Trim() -replace '\|', '/'; $g = $grp.Text.Trim() -replace '[|/]', ' '
    $st.Line = "$g | $name | $($st.How) | $(if ($on.Checked) { 'on' } else { 'off' })"
    $prev.Text = "catalog.txt 에 넣을 줄:`r`n$($st.Line)"
    $bOk.Enabled = [bool]($name -and $g -and $st.Id -notmatch '…')
  }
  $lv.Add_SelectedIndexChanged({ if ($lv.SelectedItems.Count) { $nm.Text = $lv.SelectedItems[0].Text.TrimEnd('…') }; & $upd })
  $nm.Add_TextChanged($upd); $grp.Add_TextChanged($upd); $on.Add_CheckedChanged($upd); $pin.Add_CheckedChanged($upd)
  $bOk.Add_Click({
    & $upd
    $name = $nm.Text.Trim() -replace '\|', '/'; $g = $grp.Text.Trim() -replace '[|/]', ' '
    $dup = $Catalog | Where-Object { $_.How -match '^(winget|msstore):([^@]+)' -and $Matches[2] -eq $st.Id } | Select-Object -First 1
    if ($dup) { $msg.Text = "이미 목록에 있습니다: $($dup.Group) | $($dup.Name)"; return }
    if ($Items | Where-Object Id -eq "app:$name") { $msg.Text = "같은 이름의 항목이 이미 있습니다 — 이름을 바꾸세요"; return }
    $bOk.Enabled = $false; $bFind.Enabled = $false; $msg.Text = '목록에 넣고 GitHub 에 올리는 중...'
    [System.Windows.Forms.Application]::DoEvents()
    $res = Publish-CatalogLine $g $st.Line "$name ($($st.How))"
    if (-not $res.Ok -and -not $res.NoRepo) { $bFind.Enabled = $true; & $upd; [void](Show-Msg $res.Text 'OK' 'Warning'); return }
    # 이번 설치 화면과 일꾼이 읽는 목록(이 설치 프로그램 폴더의 catalog.txt)에도 넣는다
    $here = Join-Path $Repo 'catalog.txt'
    if (-not $res.Repo -or (Resolve-Path -LiteralPath $Repo).Path -ne $res.Repo) { Add-CatalogLine $here $g $st.Line }
    $e = [pscustomobject]@{ Group = $g; Name = $name; How = $st.How; On = $on.Checked; Hint = '' }
    $script:Catalog = @($Catalog) + $e
    Add-CatalogItem $e
    $it = $Items[$Items.Count - 1]; $it.Installed = Invoke-Check $it
    $node = Add-ItemNode $it
    $script:Busy = $true; $node.Checked = -not $it.Installed; Sync-Up $node.Parent; $script:Busy = $false
    $node.Parent.Expand(); $node.EnsureVisible(); $tv.SelectedNode = $node
    $log.AppendText("앱 추가: $($st.Line)`r`n")
    $tail = if ($it.Installed) { '이 PC 에는 이미 설치돼 있습니다.' } else { "설치 화면에 체크된 채로 넣었습니다 — '선택한 것 설치' 로 이 PC 에 시험 설치할 수 있습니다." }
    [void](Show-Msg "$($res.Text)`r`n`r`n$tail" 'OK' $(if ($res.Pushed) { 'Information' } else { 'Warning' }))
    $d.Close()
  })
  [void]$d.ShowDialog($form)
  $d.Dispose()
}

# ── 소스 올리기: C:\dev 의 내 프로젝트(git 저장소)를 비밀정보 검사 → 파일 목록 확인 → 커밋·푸시 ──
# 비밀정보 규칙 — Block: 올리지 못하게 막는다 · 아니면 '확인했다' 를 체크해야 올린다. 찾은 값은 앞 몇 글자만 보인다
$SecretRules = @(
  @{ Re = '-----BEGIN [A-Z ]*PRIVATE KEY-----'; What = '개인 키'; Block = $true }
  @{ Re = 'AKIA[0-9A-Z]{16}'; What = 'AWS 액세스 키'; Block = $true }
  @{ Re = 'gh[pousr]_[A-Za-z0-9]{36,}|github_pat_[A-Za-z0-9_]{40,}'; What = 'GitHub 토큰'; Block = $true }
  @{ Re = 'xox[abprs]-[A-Za-z0-9-]{10,}'; What = 'Slack 토큰'; Block = $true }
  @{ Re = 'sk-ant-[A-Za-z0-9_\-]{20,}'; What = 'Anthropic API 키'; Block = $true }
  @{ Re = 'sk-(proj-)?[A-Za-z0-9_\-]{32,}'; What = 'OpenAI API 키'; Block = $true }
  @{ Re = '[sr]k_live_[0-9A-Za-z]{20,}'; What = 'Stripe 키'; Block = $true }
  @{ Re = 'npm_[A-Za-z0-9]{36}'; What = 'npm 토큰'; Block = $true }
  @{ Re = '"type"\s*:\s*"service_account"'; What = '구글 서비스 계정 키 파일'; Block = $true }
  @{ Re = 'AIza[0-9A-Za-z_\-]{35}'; What = 'Google API 키 (Firebase 웹 설정이면 공개용이라 괜찮다)'; Block = $false }
  @{ Re = '://[^/\s:@''"]{1,64}:[^/\s:@''"]{3,}@'; What = '주소 안의 아이디:비밀번호'; Block = $false }
  @{ Re = '(?i)(password|passwd|pwd|secret|token|api[_-]?key|access[_-]?key|client[_-]?secret|비밀번호)["'']?\s*[:=]\s*["''][^"''\s]{6,}["'']'; What = '비밀번호·토큰처럼 보이는 값'; Block = $false }
)
# 이름만 봐도 비밀정보인 파일(.env·키 파일) — 막는다. 예시 파일(.env.example 등)은 괜찮다
function Test-SecretName($path) {
  $n = [IO.Path]::GetFileName($path).ToLower()
  ($n -match '^\.env(\..+)?$' -and $n -notmatch '\.(example|sample|template|dist)$') -or $n -match '\.(pem|key|pfx|p12|jks|keystore)$' -or $n -match '^id_(rsa|dsa|ecdsa|ed25519)$'
}
function Find-DevRepos {
  $out = @(); $queue = [Collections.Generic.Queue[object]]::new(); $queue.Enqueue(@($DevRoot, 0))
  while ($queue.Count) {
    $dir, $depth = $queue.Dequeue()
    if (Test-Path -LiteralPath (Join-Path $dir '.git')) { $out += $dir; continue }
    if ($depth -ge 3) { continue }
    foreach ($c in Get-ChildItem -LiteralPath $dir -Directory -EA 0) {
      if ($c.Name -notin 'node_modules', 'venv', '__pycache__', 'dist', 'build' -and -not $c.Name.StartsWith('.')) { $queue.Enqueue(@($c.FullName, ($depth + 1))) }
    }
  }
  $out
}
# git status -z: 'XY 경로' 마다 \0. 이름 바꾸기(R)·복사(C)는 예전 이름이 한 칸 더 온다
function Split-Porcelain([string]$z) {
  $parts = $z -split "`0"
  for ($i = 0; $i -lt $parts.Count; $i++) {
    $p = $parts[$i]; if ($p.Length -lt 4) { continue }
    if ($p[0] -eq 'R' -or $p[0] -eq 'C') { $i++ }
    [pscustomobject]@{ XY = $p.Substring(0, 2); Path = $p.Substring(3) }
  }
}
function Get-RepoState($dir) {
  $o = Invoke-Git $dir @('remote', 'get-url', 'origin')
  $s = Invoke-Git $dir @('status', '--porcelain=v1', '-z', '-uall')
  $u = Invoke-Git $dir @('rev-parse', '--abbrev-ref', '--symbolic-full-name', '@{u}')
  $h = Invoke-Git $dir @('rev-parse', 'HEAD')
  $up = if ($u.Code -eq 0) { $u.Out.Trim() } else { '' }
  $c = Invoke-Git $dir @('rev-list', '--count', $(if ($up) { '@{u}..HEAD' } else { 'HEAD' }))   # 한 번도 안 올린 브랜치면 커밋 전부
  $files = @(Split-Porcelain $s.Out)
  # 검사한 뒤 파일이 바뀌었는지 알아보는 지문 — 상태 + HEAD + 바뀐 파일의 크기·시각
  $fp = $s.Out + '|' + $h.Out + '|' + (($files | ForEach-Object { $fi = Get-Item -LiteralPath (Join-Path $dir $_.Path) -Force -EA 0; if ($fi) { "$($fi.Length):$($fi.LastWriteTimeUtc.Ticks)" } }) -join ',')
  [pscustomobject]@{
    Dir = $dir; Name = $dir.Substring($DevRoot.Length).TrimStart('\')
    Origin = $(if ($o.Code -eq 0) { $o.Out.Trim() } else { '' }); Upstream = $up
    Files = $files; Ahead = $(if ($c.Code -eq 0) { [int]$c.Out.Trim() } else { 0 }); HasHead = ($h.Code -eq 0); Finger = $fp
  }
}
# 글 안에서 규칙에 걸린 곳 — diff 글이면 더해지는 줄(+)만 본다
function Find-SecretsIn([string]$text, [string]$file, [switch]$Diff) {
  foreach ($rule in $SecretRules) {
    foreach ($m in [regex]::Matches($text, $rule.Re)) {
      $ls = if ($m.Index -gt 0) { $text.LastIndexOf("`n", $m.Index - 1) + 1 } else { 0 }
      $f = $file; $ln = 0
      if ($Diff) {
        if ($text[$ls] -ne '+' -or ($ls + 4 -le $text.Length -and $text.Substring($ls, 4) -eq '+++ ')) { continue }
        $fs = $text.LastIndexOf("`n+++ ", [Math]::Max(0, $ls - 1))
        if ($fs -ge 0) { $fe = $text.IndexOf("`n", $fs + 1); $f = $text.Substring($fs + 5, $fe - $fs - 5).TrimEnd("`r"); if ($f.StartsWith('b/')) { $f = $f.Substring(2) } }
        $hs = $text.LastIndexOf("`n@@ ", [Math]::Max(0, $ls - 1))
        if ($hs -ge 0 -and $text.Substring($hs + 1, [Math]::Min(80, $text.Length - $hs - 1)) -match '^@@ -\S+ \+(\d+)') {
          $he = $text.IndexOf("`n", $hs + 1) + 1
          $ln = [int]$Matches[1] + @(($text.Substring($he, [Math]::Max(0, $ls - $he)) -split "`n") | Where-Object { $_.StartsWith('+') }).Count
        }
      } else { $ln = ([regex]::Matches($text.Substring(0, $ls), "`n")).Count + 1 }
      $v = $m.Value
      [pscustomobject]@{ File = $f; Line = $ln; What = $rule.What; Block = $rule.Block; Peek = $v.Substring(0, [Math]::Min(6, $v.Length)) + '…' }
    }
  }
}
# 한 저장소 검사: 아직 안 올린 커밋 + 지금 바뀐 파일(+ 새 파일 전체)
function Test-RepoSecrets($r) {
  $hits = @()
  $diffs = @()
  if ($r.HasHead) {
    $diffs += (Invoke-Git $r.Dir @('log', '-p', '-U0', '--no-color', '--no-ext-diff', '--format=', $(if ($r.Upstream) { '@{u}..HEAD' } else { 'HEAD' })) 180).Out
    $diffs += (Invoke-Git $r.Dir @('diff', 'HEAD', '-U0', '--no-color', '--no-ext-diff') 120).Out
  }
  $names = @()
  foreach ($t in $diffs) { if ($t) { $hits += @(Find-SecretsIn $t '' -Diff); $names += @([regex]::Matches($t, '(?m)^\+\+\+ b/(.+?)\r?$') | ForEach-Object { $_.Groups[1].Value }) } }
  foreach ($f in $r.Files) {
    if ($f.XY -match 'D') { continue }
    $names += $f.Path
    if ($f.XY -ne '??' -and $r.HasHead) { continue }   # 추적 중인 파일은 위 diff 로 봤다
    $p = Join-Path $r.Dir $f.Path
    $fi = Get-Item -LiteralPath $p -Force -EA 0
    if (-not $fi -or $fi.PSIsContainer -or $fi.Length -gt 2MB) { continue }
    $b = [IO.File]::ReadAllBytes($p)
    if ([Array]::IndexOf($b, [byte]0, 0, [Math]::Min($b.Length, 8000)) -ge 0) { continue }   # 그림 같은 이진 파일
    $hits += @(Find-SecretsIn ([Text.Encoding]::UTF8.GetString($b)) $f.Path)
  }
  foreach ($n in ($names | Select-Object -Unique)) { if (Test-SecretName $n) { $hits += [pscustomobject]@{ File = $n; Line = 0; What = '비밀정보 파일 (.env·키 파일) — .gitignore 에 넣으세요'; Block = $true; Peek = '' } } }
  $hits
}
function Show-Upload {
  $repos = @(Find-DevRepos)
  if (-not $repos) { [void](Show-Msg "$DevRoot 에 git 프로젝트가 없습니다."); return }
  $st = @{ States = @(); Scan = $null }
  $d = New-Object System.Windows.Forms.Form
  $d.Text = "소스 올리기 — $DevRoot 의 프로젝트를 GitHub 에"; $d.ClientSize = Sz 820 660; $d.StartPosition = 'CenterParent'; $d.Font = $font
  $d.FormBorderStyle = 'FixedDialog'; $d.MaximizeBox = $false; $d.MinimizeBox = $false
  $lv = New-Ctl ListView 12 12 796 170
  $lv.View = 'Details'; $lv.CheckBoxes = $true; $lv.FullRowSelect = $true
  foreach ($c in @(@('프로젝트', 250), @('바뀐 파일', 80), @('안 올린 커밋', 95), @('GitHub', 345))) { [void]$lv.Columns.Add($c[0], $c[1]) }
  $bScan = New-Ctl Button 12 190 150 32 '검사하기'
  $hint = New-Ctl Label 170 196 638 24 '올릴 프로젝트를 체크하고 검사 — 올라갈 파일과 비밀정보 검사 결과가 아래에 나옵니다'
  $rep = New-Ctl TextBox 12 230 796 300
  $rep.Multiline = $true; $rep.ReadOnly = $true; $rep.ScrollBars = 'Both'; $rep.WordWrap = $false; $rep.BackColor = [System.Drawing.Color]::White
  $l1 = New-Ctl Label 12 542 90 24 '커밋 메시지'
  $cm = New-Ctl TextBox 104 538 704 26 "소스 올리기 — $env:COMPUTERNAME $(Get-Date -Format 'yyyy-MM-dd HH:mm')"
  $ack = New-Ctl CheckBox 12 572 796 24 '⚠ 의심 항목을 하나씩 봤고, 비밀정보가 아닙니다'
  $ack.Enabled = $false
  $bPush = New-Ctl Button 552 614 150 34 '올리기'
  $bPush.Enabled = $false
  $bX = New-Ctl Button 708 614 100 34 '닫기'
  $bX.DialogResult = 'Cancel'
  $d.Controls.AddRange(@($lv, $bScan, $hint, $rep, $l1, $cm, $ack, $bPush, $bX))
  $d.CancelButton = $bX

  $fill = {
    $st.States = @(foreach ($dir in $repos) { Get-RepoState $dir })
    $lv.Items.Clear()
    foreach ($r in $st.States) {
      $li = New-Object System.Windows.Forms.ListViewItem($r.Name)
      foreach ($v in @($r.Files.Count, $r.Ahead, $(if ($r.Origin) { $r.Origin } else { '(GitHub 주소 없음 — 올릴 수 없다)' }))) { [void]$li.SubItems.Add([string]$v) }
      $li.Tag = $r
      $li.Checked = [bool]$r.Origin -and ($r.Files.Count -or $r.Ahead)
      if (-not $r.Origin) { $li.ForeColor = [System.Drawing.Color]::Gray }
      [void]$lv.Items.Add($li)
    }
  }
  $reset = { $st.Scan = $null; $bPush.Enabled = $false; $ack.Checked = $false; $ack.Enabled = $false }
  $canPush = { $bPush.Enabled = [bool]$st.Scan -and -not $st.Scan.Block -and (-not $st.Scan.Warn -or $ack.Checked) }
  $scan = {
    $pick = @($lv.CheckedItems | ForEach-Object { $_.Tag } | Where-Object { $_.Origin -and ($_.Files.Count -or $_.Ahead) })
    if (-not $pick) { $rep.Text = '올릴 것이 있는 프로젝트(바뀐 파일·안 올린 커밋)를 체크하세요.'; return $null }
    $sb = New-Object Text.StringBuilder; $block = $false; $warn = $false
    foreach ($r in $pick) {
      $rep.Text = "검사 중: $($r.Name) ..."; [System.Windows.Forms.Application]::DoEvents()
      [void]$sb.AppendLine("■ $($r.Name)  →  $($r.Origin)")
      [void]$sb.AppendLine("   바뀐 파일 $($r.Files.Count)개 · 안 올린 커밋 $($r.Ahead)개")
      foreach ($f in $r.Files | Select-Object -First 300) { [void]$sb.AppendLine("     $($f.XY)  $($f.Path)") }
      if ($r.Files.Count -gt 300) { [void]$sb.AppendLine("     … 외 $($r.Files.Count - 300)개") }
      $hits = @(Test-RepoSecrets $r)
      if (-not $hits) { [void]$sb.AppendLine('   비밀정보 검사: 걸린 것 없음') }
      foreach ($h in $hits) {
        if ($h.Block) { $block = $true } else { $warn = $true }
        [void]$sb.AppendLine(('   {0} {1}{2} — {3}{4}' -f $(if ($h.Block) { '⛔ 막음' } else { '⚠ 의심' }), $h.File, $(if ($h.Line) { ":$($h.Line)" } else { '' }), $h.What, $(if ($h.Peek) { " ($($h.Peek))" } else { '' })))
      }
      [void]$sb.AppendLine('')
    }
    if ($block) { [void]$sb.AppendLine('⛔ 막힌 항목이 있어 올릴 수 없습니다 — 그 파일에서 비밀정보를 빼거나 .gitignore 에 넣은 뒤 다시 검사하세요. (이미 커밋했으면 그 커밋도 고쳐야 합니다)') }
    elseif ($warn) { [void]$sb.AppendLine("⚠ 의심 항목을 하나씩 확인하고, 비밀정보가 아니면 아래 '확인했다' 를 체크한 뒤 올리기.") }
    else { [void]$sb.AppendLine("위 파일들이 올라갑니다 — 맞으면 '올리기'.") }
    $rep.Text = $sb.ToString()
    @{ Repos = $pick; Finger = (($pick | ForEach-Object Finger) -join '#'); Block = $block; Warn = $warn }
  }
  $bScan.Add_Click({
    $bScan.Enabled = $false; & $reset
    try { $st.Scan = & $scan; $ack.Enabled = [bool]$st.Scan -and $st.Scan.Warn -and -not $st.Scan.Block; & $canPush } finally { $bScan.Enabled = $true }
  })
  $lv.Add_ItemChecked({ & $reset })
  $ack.Add_CheckedChanged({ & $canPush })
  $bPush.Add_Click({
    $bPush.Enabled = $false; $bScan.Enabled = $false
    try {
      # 검사한 뒤 파일이 바뀌었으면 다시 검사하게 한다 — 본 것과 올라가는 것이 같도록
      $now = @(foreach ($r in $st.Scan.Repos) { Get-RepoState $r.Dir })
      if ((($now | ForEach-Object Finger) -join '#') -ne $st.Scan.Finger) { & $reset; $rep.AppendText("`r`n검사한 뒤 파일이 바뀌었습니다 — '검사하기' 를 다시 누르세요.`r`n"); return }
      $msg = $cm.Text.Trim(); if (-not $msg) { $msg = "소스 올리기 — $env:COMPUTERNAME" }
      $out = New-Object Text.StringBuilder
      foreach ($r in $now) {
        $rep.Text = "올리는 중: $($r.Name) ..."; [System.Windows.Forms.Application]::DoEvents()
        $err = $null
        if ($r.Files.Count) {
          $a = Invoke-Git $r.Dir @('add', '-A') 120
          if ($a.Code -ne 0) { $err = "파일을 담지 못했습니다: $(Get-GitText $a)" }
          else { $c = Invoke-GitCommit $r.Dir $msg; if ($c.Code -ne 0) { $err = "커밋하지 못했습니다: $(Get-GitText $c)" } }
        }
        if (-not $err) { $p = Invoke-GitPush $r.Dir ([bool]$r.Upstream); if (-not $p.Ok) { $err = "GitHub 에 올리지 못했습니다: $($p.Text)" } }
        [void]$out.AppendLine($(if ($err) { "✗ $($r.Name) — $err" } else { "✓ $($r.Name) — 올렸습니다 $($p.Text)" }))
      }
      & $fill; & $reset
      $rep.Text = $out.ToString()
      $log.AppendText("소스 올리기:`r`n$($out.ToString())")
    } finally { $bScan.Enabled = $true }
  })
  & $fill
  [void]$d.ShowDialog($form)
  $d.Dispose()
}

$log.AppendText("필요한 것을 체크하고 '선택한 것 설치' 를 누르세요. 회색은 이미 된 것입니다. 묶음 이름을 체크하면 그 안이 다 체크됩니다.`r`n")
if (-not $script:Online) { $log.AppendText("인터넷이 없습니다 — ① 네트워크 드라이버부터 설치하세요.`r`n") }
$form.ActiveControl = $log   # 첫 묶음이 선택된 것처럼 파랗게 보이지 않게

if ($Snapshot) {
  $form.StartPosition = 'Manual'; $form.Location = Pt -4000 -4000; $form.Show(); [System.Windows.Forms.Application]::DoEvents()
  $bmp = New-Object System.Drawing.Bitmap($form.Width, $form.Height)
  $form.DrawToBitmap($bmp, (New-Object System.Drawing.Rectangle(0, 0, $form.Width, $form.Height)))
  $bmp.Save($Snapshot); $form.Close(); return
}
if ($script:Splash) { $script:Splash.Close(); $script:Splash.Dispose(); $script:Splash = $null }   # '준비 중' 창을 닫고 설치 화면으로
$form.Add_Shown({ $form.Activate() })
[void]$form.ShowDialog()
