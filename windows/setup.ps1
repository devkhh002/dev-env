# 개발 PC 설치 — Windows
#   실행하면 단계별 설치 화면이 뜬다. 고른 것만 설치하고, 이미 된 것은 건너뛴다(여러 번 실행해도 안전).
#   powershell -ExecutionPolicy Bypass -File setup.ps1                (설치 화면)
#   powershell -ExecutionPolicy Bypass -File setup.ps1 -List          (상태만 보기)
#   powershell -ExecutionPolicy Bypass -File setup.ps1 -Only git,node (화면 없이 이것만)
#   powershell -ExecutionPolicy Bypass -File setup.ps1 -All           (화면 없이 전부)
# 앱·도구 목록은 저장소의 catalog.txt, 프로젝트는 projects.txt — 그 파일만 고치면 모든 PC 의 설치 화면에 반영된다.
# -Usb  : USB 의 PC설치 폴더(시작하기가 넘겨준다. 없으면 드라이브를 찾아본다)
# -Progress·-NoPause : 설치 화면이 설치 창(일꾼)에 넘기는 것.  -Snapshot : 화면을 그림으로 저장(시험용)
param([switch]$All, [string[]]$Only, [string]$OnlyFile, [switch]$List, [switch]$NoPause, [string]$Progress, [string]$Usb, [string]$Snapshot)

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
# ────────────────────────────────────────────────────────────────────

$ErrorActionPreference = 'Continue'
$ProgressPreference = 'SilentlyContinue'
[Net.ServicePointManager]::SecurityProtocol = 'Tls12'
[Console]::OutputEncoding = [Text.Encoding]::UTF8
$Repo = Split-Path $PSScriptRoot -Parent
# 버전 — windows/version.txt 한 줄(커밋할 때 자동으로 그 시각이 된다). 설치 화면 제목·USB 의 '버전 ….txt' 에 보인다
$Version = Get-Content "$PSScriptRoot\version.txt" -Encoding UTF8 -EA 0 | Select-Object -First 1
if (-not $Version) { $Version = '알 수 없음' }

$isAdmin = ([Security.Principal.WindowsPrincipal][Security.Principal.WindowsIdentity]::GetCurrent()).IsInRole([Security.Principal.WindowsBuiltInRole]::Administrator)
if (-not $isAdmin -and -not $List -and -not $Snapshot) {
  $a = @('-NoProfile', '-ExecutionPolicy', 'Bypass', '-File', "`"$PSCommandPath`"")
  if ($All) { $a += '-All' }
  if ($Only) { $a += '-Only'; $a += "`"$($Only -join ',')`"" }
  if ($NoPause) { $a += '-NoPause' }
  if ($Progress) { $a += '-Progress'; $a += "`"$Progress`"" }
  if ($Usb) { $a += '-Usb'; $a += "`"$Usb`"" }
  if ($OnlyFile) { $a += '-OnlyFile'; $a += "`"$OnlyFile`"" }
  Start-Process powershell -Verb RunAs -ArgumentList $a
  return
}
# 선택 항목은 명령줄 대신 파일로 받는다 — 한글·공백이 든 Id 가 프로세스 사이에서 깨지지 않게
if ($OnlyFile -and (Test-Path -LiteralPath $OnlyFile)) { $Only = @(Get-Content -LiteralPath $OnlyFile -Encoding UTF8 | Where-Object { $_ -match '\S' }) }

# 예전 판(dev-env-dl)이 멈추며 남긴 받다 만 파일을 보지 않도록 폴더 이름을 바꿨다
$Tmp = "$env:TEMP\dev-env-cache"; New-Item -ItemType Directory $Tmp -Force | Out-Null
function Refresh-Path { $env:Path = [Environment]::GetEnvironmentVariable('Path', 'Machine') + ';' + [Environment]::GetEnvironmentVariable('Path', 'User') + ";$env:APPDATA\npm;$env:USERPROFILE\.local\bin" }
# 큰 파일도 빨리 받게 Windows 내장 curl.exe 를 쓴다(PowerShell 5.1 의 Invoke-WebRequest 는 큰 파일에서 매우 느려 멈춘 것처럼 보인다).
# .part 로 받다가 다 받으면 이름을 바꾼다 — 중간에 끊긴 파일을 다 받은 것으로 착각하지 않게. 진행률은 검은 진행 창에 보인다
function Fetch($url, $name, [long]$size = 0) {
  $p = "$Tmp\$name"
  # 크기를 아는 파일(GitHub 릴리스)은 대조 — 받다 만 파일이면 지우고 다시 받는다
  if ((Test-Path $p) -and $size -gt 0 -and (Get-Item $p).Length -ne $size) { Say "   받다 만 파일이라 다시 받습니다: $name"; Remove-Item $p -Force }
  if (Test-Path $p) { return $p }
  $part = "$p.part"; Remove-Item $part -EA 0
  Say "   받는 중: $name  (진행률은 검은 진행 창에)"
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
function Invoke-Timed($exe, [string[]]$argList, [int]$sec) {
  $out = [IO.Path]::GetTempFileName()
  try {
    $p = Start-Process -FilePath $exe -ArgumentList $argList -NoNewWindow -PassThru -RedirectStandardOutput $out -EA Stop
    $null = $p.Handle   # PS 5.1 에서 ExitCode 를 받으려면 필요
    if (-not $p.WaitForExit($sec * 1000)) { try { $p.Kill() } catch {}; return $null }
    return @{ Code = $p.ExitCode; Out = (Get-Content $out -Raw -EA 0) }
  } catch { return $null } finally { Remove-Item $out -EA 0 }
}
function Test-Online { try { Invoke-WebRequest 'https://raw.githubusercontent.com/devkhh002/dev-env/main/README.md' -Method Head -UseBasicParsing -TimeoutSec 8 | Out-Null; $true } catch { $false } }
# 이동식(2)·로컬(3) 드라이브만 본다 — 연결이 끊긴 네트워크 드라이브(NAS 등)는 찾는 데 한참 멈춘다
function Find-UsbKit { foreach ($d in Get-CimInstance Win32_LogicalDisk -Filter 'DriveType=2 OR DriveType=3' -EA 0) { $k = Join-Path "$($d.DeviceID)\" 'PC설치'; if (Test-Path -LiteralPath (Join-Path $k 'start.ps1')) { return $k } }; return $null }
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
    default { '검은 진행 창의 winget 메시지를 본다' }
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
        if ($LASTEXITCODE -ne 0) { Write-Host '   GitHub 로그인이 필요합니다 (브라우저가 열립니다)'; & gh auth login -h github.com -p https -w; & gh auth setup-git }
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

# ── 상태 ────────────────────────────────────────────────────────────
function Invoke-Check($it) { try { [bool](& $it.Check) } catch { $false } }
if (-not $Progress) { Write-Host '상태 확인 1/2: winget 이 살아 있는지 보고 목록을 읽습니다 (처음엔 최대 2분)' }
if (Test-Winget) { $script:WG = Get-WingetMap }
else { $script:WG = @{}; if (-not $Progress) { Write-Host '   winget 이 없거나 응답하지 않습니다 — 설치 화면의 winget 항목으로 (다시) 깔 수 있습니다' -ForegroundColor Yellow } }
if (-not $Progress) { Write-Host '상태 확인 2/2: 나머지 항목' }
foreach ($it in $Items) { $it.Installed = Invoke-Check $it }
if ($List) { "버전 $Version"; foreach ($it in $Items) { '{0,-4} {1,-26} {2,-30} {3}' -f $(if ($it.Installed) { 'OK' } else { '--' }), ($it.Group -replace '/', ' > '), $it.Id, $it.Name }; return }

# ── 설치(일꾼) ──────────────────────────────────────────────────────
function Run-Install($Pick) {
  Start-Transcript "$env:USERPROFILE\dev-env-setup.log" -Append | Out-Null
  try { $Host.UI.RawUI.WindowTitle = "개발 PC 설치 버전 $Version — 진행 중 (이 창을 닫지 마세요)" } catch {}
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
Add-Type -AssemblyName System.Windows.Forms, System.Drawing
[System.Windows.Forms.Application]::EnableVisualStyles()
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
$font = New-Object System.Drawing.Font('Malgun Gothic', 10)

$form = New-Object System.Windows.Forms.Form
$form.Text = "개발 PC 설치 — 버전 $Version"; $form.ClientSize = Sz 864 800; $form.StartPosition = 'CenterScreen'; $form.Font = $font
$board = (Get-CimInstance Win32_BaseBoard -EA 0).Product
$head = New-Object System.Windows.Forms.Label
$head.Location = Pt 12 10; $head.Size = Sz 840 24
$head.Text = "버전 $Version · $env:COMPUTERNAME · $board · " + $(if ($script:Online) { '인터넷 연결됨' } else { '인터넷 없음 — 먼저 ① 네트워크 드라이버' }) + $(if ($Usb) { " · USB $Usb" } else { '' })
$tv.CheckBoxes = $true; $tv.Location = Pt 12 40; $tv.Size = Sz 840 560; $tv.Font = $font
$log = New-Object System.Windows.Forms.TextBox
$log.Multiline = $true; $log.ReadOnly = $true; $log.ScrollBars = 'Vertical'; $log.Location = Pt 12 610; $log.Size = Sz 840 130; $log.BackColor = [System.Drawing.Color]::White
$form.Controls.AddRange(@($head, $tv, $log))

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

foreach ($g in @($G1, $G2, $G4, "$G4/기본 앱", "$G4/원격", "$G4/도구", "$G4/Claude", $G5, $G6, $G9)) { [void](Get-GroupNode $g) }
$NodeById = @{}
foreach ($it in $Items) {
  $node = New-Object System.Windows.Forms.TreeNode($it.Name)
  $node.Tag = $it
  Set-NodeLook $node
  $node.Checked = (-not $it.Installed) -and $it.Default
  [void](Get-GroupNode $it.Group).Nodes.Add($node)
  $NodeById[$it.Id] = $node
}
foreach ($k in @($GroupNodes.Keys)) { if ($GroupNodes[$k].Nodes.Count -eq 0) { $GroupNodes[$k].Remove() } }   # 빈 묶음은 숨긴다
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

function New-Btn($text, $x, $w, $onClick) {
  $b = New-Object System.Windows.Forms.Button
  $b.Text = $text; $b.Location = Pt $x 754; $b.Size = Sz $w 34; $b.Add_Click($onClick); $form.Controls.Add($b); return $b
}
function Set-AllChecked($state) { $script:Busy = $true; foreach ($n in $tv.Nodes) { $n.Checked = $state; Set-Down $n $state }; $script:Busy = $false }
$bAll = New-Btn '전체 선택' 12 100 { Set-AllChecked $true }
$bNone = New-Btn '전체 해제' 118 100 { Set-AllChecked $false }
# Remiz WSH 도구(방화벽·디펜더·업데이트 등 윈도우 초기 설정)를 그대로 연다 — USB PC설치\도구 의 사용자 도구를 띄우기만 한다
$bWsh = New-Btn 'Remiz WSH 열기' 300 160 {
  $p = if ($Usb) { Join-Path (Join-Path $Usb '도구') 'WSH by Remiz.cmd' } else { $null }
  if ($p -and (Test-Path -LiteralPath $p)) { Start-Process -FilePath $p -Verb RunAs -WorkingDirectory (Split-Path $p) }   # 관리자 권한으로 띄운다
  else { [void][System.Windows.Forms.MessageBox]::Show("USB 의 PC설치\도구 폴더에 'WSH by Remiz.cmd' 가 없습니다.`r`nRemiz 도구를 그 폴더에 넣어 두세요.", 'Remiz WSH') }
}
$bGo = New-Btn '선택한 것 설치' 604 140 { Start-Worker }
$bClose = New-Btn '닫기' 752 100 { $form.Close() }

# 설치는 따로 뜨는 창(일꾼)에서 돌리고, 이 화면은 그 진행 기록만 읽는다 — 그래서 화면이 멈추지 않는다
$timer = New-Object System.Windows.Forms.Timer; $timer.Interval = 500
function Start-Worker {
  $sel = @($Items | Where-Object { $NodeById[$_.Id].Checked })
  if (-not $sel) { [void][System.Windows.Forms.MessageBox]::Show('설치할 것을 체크하세요.', '개발 PC 설치'); return }
  if (($sel | Where-Object Kind -eq 'winget') -and -not ($sel | Where-Object Id -eq 'winget') -and -not (Test-Winget)) { $sel = @($Items | Where-Object Id -eq 'winget') + $sel }
  $script:Prog = Join-Path $env:TEMP "dev-env-progress-$PID.log"
  [IO.File]::WriteAllText($script:Prog, '')
  $script:Pos = 0; $script:RebootList = @()
  $onlyFile = Join-Path $env:TEMP "dev-env-only-$PID.txt"
  [IO.File]::WriteAllLines($onlyFile, @($sel | ForEach-Object Id), (New-Object Text.UTF8Encoding $false))
  $a = @('-NoProfile', '-ExecutionPolicy', 'Bypass', '-File', "`"$PSCommandPath`"", '-OnlyFile', "`"$onlyFile`"", '-NoPause', '-Progress', "`"$script:Prog`"")
  if ($Usb) { $a += @('-Usb', "`"$Usb`"") }
  $log.AppendText("설치를 시작합니다 — 따로 뜨는 검은 진행 창은 닫지 마세요.`r`n")
  $script:Worker = Start-Process powershell -ArgumentList $a -PassThru
  foreach ($b in $bAll, $bNone, $bGo) { $b.Enabled = $false }
  $timer.Start()
}
function Show-Line($line) {
  if ($line -match "^@@STATUS (.+)`t([01])$") {
    $n = $NodeById[$Matches[1]]
    if ($n) {
      $n.Tag.Installed = ($Matches[2] -eq '1'); Set-NodeLook $n
      if ($n.Tag.Installed) { $script:Busy = $true; $n.Checked = $false; Sync-Up $n.Parent; $script:Busy = $false }
    }
  } elseif ($line -match '^@@REBOOT (.+)$') { $script:RebootList += $Matches[1] }
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
  foreach ($b in $bAll, $bNone, $bGo) { $b.Enabled = $true }
  $log.AppendText("끝났습니다. (기록: %USERPROFILE%\dev-env-setup.log)`r`n")
  if ($script:RebootList.Count) {
    $msg = "재부팅해야 적용되는 것이 있습니다:`r`n - " + ($script:RebootList -join "`r`n - ") + "`r`n`r`n원격으로 접속 중이면 재부팅하는 동안 연결이 끊깁니다.`r`n지금 재부팅할까요?"
    if ([System.Windows.Forms.MessageBox]::Show($msg, '개발 PC 설치', 'YesNo', 'Question') -eq 'Yes') { Restart-Computer -Force }
  }
}
$timer.Add_Tick({ Read-Progress; if ($script:Worker -and $script:Worker.HasExited) { $timer.Stop(); Read-Progress; Finish-Worker } })
$form.Add_FormClosing({
  param($s, $ev)
  if ($script:Worker -and -not $script:Worker.HasExited) {
    if ([System.Windows.Forms.MessageBox]::Show('설치가 아직 진행 중입니다. 이 화면을 닫아도 설치는 검은 진행 창에서 계속됩니다. 닫을까요?', '개발 PC 설치', 'YesNo') -ne 'Yes') { $ev.Cancel = $true }
  }
})
$log.AppendText("필요한 것을 체크하고 '선택한 것 설치' 를 누르세요. 회색은 이미 된 것입니다. 묶음 이름을 체크하면 그 안이 다 체크됩니다.`r`n")
if (-not $script:Online) { $log.AppendText("인터넷이 없습니다 — ① 네트워크 드라이버부터 설치하세요.`r`n") }
$form.ActiveControl = $log   # 첫 묶음이 선택된 것처럼 파랗게 보이지 않게

if ($Snapshot) {
  $form.StartPosition = 'Manual'; $form.Location = Pt -4000 -4000; $form.Show(); [System.Windows.Forms.Application]::DoEvents()
  $bmp = New-Object System.Drawing.Bitmap($form.Width, $form.Height)
  $form.DrawToBitmap($bmp, (New-Object System.Drawing.Rectangle(0, 0, $form.Width, $form.Height)))
  $bmp.Save($Snapshot); $form.Close(); return
}
[void]$form.ShowDialog()
