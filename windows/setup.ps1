# 개발 환경 설치 — Windows
#   실행하면 체크박스 목록이 뜬다. 고른 것만 설치하고, 이미 깔린 것은 건너뛴다(여러 번 실행해도 안전).
#   powershell -ExecutionPolicy Bypass -File setup.ps1            (목록에서 고르기)
#   powershell -ExecutionPolicy Bypass -File setup.ps1 -All       (전부)
#   powershell -ExecutionPolicy Bypass -File setup.ps1 -Only git,node
#   powershell -ExecutionPolicy Bypass -File setup.ps1 -List      (설치 상태만 보기)
param([switch]$All, [string[]]$Only, [switch]$List)

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
# ────────────────────────────────────────────────────────────────────

$ErrorActionPreference = 'Continue'
$ProgressPreference = 'SilentlyContinue'
[Net.ServicePointManager]::SecurityProtocol = 'Tls12'
[Console]::OutputEncoding = [Text.Encoding]::UTF8
$Repo = Split-Path $PSScriptRoot -Parent

$isAdmin = ([Security.Principal.WindowsPrincipal][Security.Principal.WindowsIdentity]::GetCurrent()).IsInRole([Security.Principal.WindowsBuiltInRole]::Administrator)
if (-not $isAdmin -and -not $List) {
  $a = @('-NoProfile', '-ExecutionPolicy', 'Bypass', '-File', "`"$PSCommandPath`"")
  if ($All) { $a += '-All' }
  if ($Only) { $a += '-Only'; $a += ($Only -join ',') }
  Start-Process powershell -Verb RunAs -ArgumentList $a
  return
}

$Tmp = "$env:TEMP\dev-env-dl"; New-Item -ItemType Directory $Tmp -Force | Out-Null
function Refresh-Path { $env:Path = [Environment]::GetEnvironmentVariable('Path', 'Machine') + ';' + [Environment]::GetEnvironmentVariable('Path', 'User') + ";$env:APPDATA\npm;$env:USERPROFILE\.local\bin" }
function Fetch($url, $name) { $p = "$Tmp\$name"; if (-not (Test-Path $p)) { Write-Host "   받는 중: $name"; Invoke-WebRequest $url -OutFile $p -UseBasicParsing }; return $p }
function Msi($p) { Start-Process msiexec -Wait -ArgumentList "/i `"$p`" /qn /norestart" }
function Has($cmd) { Refresh-Path; return [bool](Get-Command $cmd -EA 0) }

# ── 설치 항목 ───────────────────────────────────────────────────────
$Items = [System.Collections.Generic.List[object]]::new()
function Add-Item($id, $name, $check, $install) { $Items.Add([pscustomobject]@{ Id = $id; Name = $name; Check = $check; Install = $install }) }

Add-Item git "Git $($V.Git) (Git Bash 포함)" { Test-Path 'C:\Program Files\Git\cmd\git.exe' } {
  $p = Fetch "https://github.com/git-for-windows/git/releases/download/v$($V.Git)/$($V.GitFile)" $V.GitFile
  Start-Process $p -Wait -ArgumentList '/VERYSILENT', '/NORESTART', '/NOCANCEL', '/SP-', '/o:PathOption=Cmd', '/o:CRLFOption=CRLFCommitAsIs'
}
Add-Item node "Node.js $($V.Node)" { Test-Path 'C:\Program Files\nodejs\node.exe' } {
  Msi (Fetch "https://nodejs.org/dist/v$($V.Node)/node-v$($V.Node)-x64.msi" "node-$($V.Node).msi")
}
Add-Item python "Python $($V.Python)" { [bool](Get-ChildItem 'C:\Program Files\Python3*\python.exe' -EA 0) } {
  $p = Fetch "https://www.python.org/ftp/python/$($V.Python)/python-$($V.Python)-amd64.exe" "python-$($V.Python).exe"
  Start-Process $p -Wait -ArgumentList '/quiet', 'InstallAllUsers=1', 'PrependPath=1', 'Include_test=0'
}
Add-Item gh "GitHub CLI $($V.Gh)" { Test-Path 'C:\Program Files\GitHub CLI\gh.exe' } {
  Msi (Fetch "https://github.com/cli/cli/releases/download/v$($V.Gh)/gh_$($V.Gh)_windows_amd64.msi" "gh-$($V.Gh).msi")
}
Add-Item pwsh "PowerShell $($V.Pwsh)" { Test-Path 'C:\Program Files\PowerShell\7\pwsh.exe' } {
  $p = Fetch "https://github.com/PowerShell/PowerShell/releases/download/v$($V.Pwsh)/PowerShell-$($V.Pwsh)-win-x64.msi" "pwsh-$($V.Pwsh).msi"
  Start-Process msiexec -Wait -ArgumentList "/i `"$p`" /qn /norestart ADD_PATH=1 ENABLE_PSREMOTING=0 REGISTER_MANIFEST=1"
}
Add-Item terminal "Windows Terminal $($V.Terminal) (PowerShell을 탭으로 열기)" { [bool](Get-AppxPackage Microsoft.WindowsTerminal) } {
  $base = "https://github.com/microsoft/terminal/releases/download/v$($V.Terminal)/Microsoft.WindowsTerminal_$($V.Terminal)_8wekyb3d8bbwe.msixbundle"
  $bundle = Fetch $base "wt-$($V.Terminal).msixbundle"
  $kit = Fetch "$($base)_Windows10_PreinstallKit.zip" "wt-$($V.Terminal)-kit.zip"
  Expand-Archive $kit "$Tmp\wtkit" -Force
  $deps = Get-ChildItem "$Tmp\wtkit" -Recurse -Filter *.appx | Where-Object { $_.Name -match 'x64' } | ForEach-Object FullName
  Add-AppxPackage -Path $bundle -DependencyPath $deps
  $k = 'HKCU:\Console\%%Startup'; New-Item $k -Force | Out-Null
  Set-ItemProperty $k DelegationConsole '{2EACA947-7F5F-4CFA-BA87-8F7FBEEFBE69}'
  Set-ItemProperty $k DelegationTerminal '{E12CFF52-A866-4C77-9A90-F570A7AA2C6B}'
  $ls = "$env:LOCALAPPDATA\Packages\Microsoft.WindowsTerminal_8wekyb3d8bbwe\LocalState"; New-Item -ItemType Directory $ls -Force | Out-Null
  if (-not (Test-Path "$ls\settings.json")) {
    [IO.File]::WriteAllText("$ls\settings.json", '{"defaultProfile":"{574e775e-4f2a-5b96-ac1e-a2962a402336}","profiles":{"defaults":{},"list":[]}}', (New-Object Text.UTF8Encoding $false))
  }
}
Add-Item npmtools "clasp $($V.Clasp) · firebase-tools $($V.Firebase) (Node 필요)" { (Has 'clasp.cmd') -and (Has 'firebase.cmd') } {
  Refresh-Path; & npm.cmd i -g "@google/clasp@$($V.Clasp)" "firebase-tools@$($V.Firebase)" 2>&1 | Select-Object -Last 2
}
Add-Item claude 'Claude Code + 전역 설정(CLAUDE.md·agents)' { Test-Path "$env:USERPROFILE\.local\bin\claude.exe" } {
  Invoke-RestMethod https://claude.ai/install.ps1 | Invoke-Expression
  $up = [Environment]::GetEnvironmentVariable('Path', 'User')
  foreach ($d in @("$env:USERPROFILE\.local\bin", "$env:APPDATA\npm")) { if ($up -notlike "*$d*") { $up = ($up.TrimEnd(';') + ';' + $d).TrimStart(';') } }
  [Environment]::SetEnvironmentVariable('Path', $up, 'User')
  $dst = "$env:USERPROFILE\.claude"; New-Item -ItemType Directory $dst -Force | Out-Null
  $(if (Test-Path "$Repo\claude") { Get-ChildItem "$Repo\claude" } else { @() }) | ForEach-Object {
    $t = Join-Path $dst $_.Name
    if ($_.Name -eq 'settings.json' -and (Test-Path $t)) { return }   # 이미 쓰던 설정은 덮지 않는다
    Copy-Item $_.FullName $t -Recurse -Force
  }
}
Add-Item gitconfig 'git 기본 설정(줄바꿈 유지·한글 파일명·이름)' { (& git config --global core.autocrlf 2>$null) -eq 'false' } {
  Refresh-Path
  git config --global core.autocrlf false
  git config --global core.quotepath false
  git config --global init.defaultBranch main
  git config --global user.name $GitName
  git config --global user.email $GitEmail
}
Add-Item hangul '한/영 전환 (AutoHotkey: 원격에서 넘어온 Alt+Space → 한/영)' { Test-Path "$([Environment]::GetFolderPath('CommonStartup'))\hangul.ahk" } {
  $exe = "$env:ProgramFiles\AutoHotkey\v2\AutoHotkey64.exe"
  if (-not (Test-Path $exe)) { Start-Process (Fetch 'https://www.autohotkey.com/download/ahk-v2.exe' 'ahk-v2.exe') -Wait -ArgumentList '/silent' }
  $f = "$([Environment]::GetFolderPath('CommonStartup'))\hangul.ahk"
  Copy-Item "$PSScriptRoot\hangul.ahk" $f -Force
  Start-Process explorer.exe -ArgumentList "`"$f`""
}
Add-Item tailscale 'Tailscale (원격 접속 VPN)' { Test-Path "$env:ProgramFiles\Tailscale\tailscale.exe" } {
  Start-Process (Fetch 'https://pkgs.tailscale.com/stable/tailscale-setup-latest.exe' 'tailscale.exe') -Wait -ArgumentList '/quiet'
}
Add-Item sunshine "Sunshine $($V.Sunshine) (Moonlight 원격 호스트)" { Test-Path "$env:ProgramFiles\Sunshine\sunshine.exe" } {
  Msi (Fetch "https://github.com/LizardByte/Sunshine/releases/download/$($V.Sunshine)/Sunshine-Windows-AMD64-installer.msi" "sunshine-$($V.Sunshine).msi")
}

# 프로젝트: projects.txt 한 줄 = 항목 하나
$projLines = Get-Content "$Repo\projects.txt" -Encoding UTF8 | Where-Object { $_ -match '\S' -and $_ -notmatch '^\s*#' }
foreach ($line in $projLines) {
  $c = $line.Split('|') | ForEach-Object { $_.Trim() }
  $folder = Join-Path $DevRoot ($c[0] -replace '/', '\'); $url = $c[1]; $lnk = $c[2]
  $id = 'proj:' + $c[0]
  $label = if ($url) { "프로젝트: $lnk (GitHub에서 받기 + 바로가기)" } else { "프로젝트: $lnk (바로가기만 — 폴더는 백업에서 직접 복원)" }
  $check = [scriptblock]::Create("(Test-Path -LiteralPath '$($folder -replace "'", "''")') -and (Test-Path -LiteralPath '$(([Environment]::GetFolderPath('Desktop')) -replace "'", "''")\$($lnk -replace "'", "''").lnk')")
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
  }.GetNewClosure()
  $Items.Add([pscustomobject]@{ Id = $id; Name = $label; Check = $check; Install = [scriptblock]::Create("& {$install} '$($folder -replace "'", "''")' '$url' '$($lnk -replace "'", "''")'") })
}

# ── 상태 ────────────────────────────────────────────────────────────
foreach ($it in $Items) { $it | Add-Member Installed ([bool](& $it.Check)) -Force }
if ($List) { $Items | ForEach-Object { '{0,-4} {1,-38} {2}' -f $(if ($_.Installed) { 'OK' } else { '--' }), $_.Id, $_.Name }; return }

# ── 고르기 ──────────────────────────────────────────────────────────
if ($All) { $Pick = $Items }
elseif ($Only) { $ids = $Only -join ',' -split ','; $Pick = $Items | Where-Object { $ids -contains $_.Id } }
else {
  Add-Type -AssemblyName System.Windows.Forms, System.Drawing
  [System.Windows.Forms.Application]::EnableVisualStyles()
  $f = New-Object System.Windows.Forms.Form
  $f.Text = '개발 환경 설치 — 설치할 것을 고르세요'; $f.Size = New-Object System.Drawing.Size(640, 560); $f.StartPosition = 'CenterScreen'
  $f.Font = New-Object System.Drawing.Font('Malgun Gothic', 10)
  $lb = New-Object System.Windows.Forms.CheckedListBox
  $lb.Location = New-Object System.Drawing.Point(12, 12); $lb.Size = New-Object System.Drawing.Size(600, 440); $lb.CheckOnClick = $true
  foreach ($it in $Items) { [void]$lb.Items.Add($(if ($it.Installed) { "[설치됨] $($it.Name)" } else { $it.Name }), -not $it.Installed) }
  $f.Controls.Add($lb)
  function Btn($text, $x, $onClick) { $b = New-Object System.Windows.Forms.Button; $b.Text = $text; $b.Location = New-Object System.Drawing.Point($x, 468); $b.Size = New-Object System.Drawing.Size(110, 34); $b.Add_Click($onClick); $f.Controls.Add($b); return $b }
  [void](Btn '전체 선택' 12 { for ($i = 0; $i -lt $lb.Items.Count; $i++) { $lb.SetItemChecked($i, $true) } })
  [void](Btn '전체 해제' 128 { for ($i = 0; $i -lt $lb.Items.Count; $i++) { $lb.SetItemChecked($i, $false) } })
  $ok = Btn '설치' 386 { $f.DialogResult = 'OK'; $f.Close() }
  [void](Btn '취소' 502 { $f.DialogResult = 'Cancel'; $f.Close() })
  $f.AcceptButton = $ok
  if ($f.ShowDialog() -ne 'OK') { Write-Host '취소했습니다.'; return }
  $Pick = @(); foreach ($i in $lb.CheckedIndices) { $Pick += $Items[$i] }
}

# ── 설치 ────────────────────────────────────────────────────────────
Start-Transcript "$env:USERPROFILE\dev-env-setup.log" -Append | Out-Null
$n = 0
foreach ($it in $Pick) {
  $n++
  if (& $it.Check) { Write-Host "[$n/$($Pick.Count)] $($it.Name) — 이미 설치됨, 건너뜀" -ForegroundColor DarkGray; continue }
  Write-Host "[$n/$($Pick.Count)] $($it.Name)" -ForegroundColor Cyan
  try { & $it.Install; Refresh-Path; Write-Host $(if (& $it.Check) { '   완료' } else { '   확인 필요 — 로그 참고' }) -ForegroundColor $(if (& $it.Check) { 'Green' } else { 'Yellow' }) }
  catch { Write-Host "   실패: $_" -ForegroundColor Red }
}
Stop-Transcript | Out-Null
Remove-Item $Tmp -Recurse -Force -EA 0

Write-Host ''
Write-Host '──────── 사람이 해야 하는 일 ────────' -ForegroundColor Green
Write-Host ' • 새 터미널을 열고 claude 실행 → 브라우저 로그인'
Write-Host ' • clasp login / firebase login (Apps Script·Firebase 쓰는 프로젝트만)'
Write-Host ' • Tailscale 트레이 아이콘 → 로그인 / Sunshine: https://localhost:47990 관리자 계정·PIN'
Write-Host ' • git이 없는 프로젝트·Claude 기억은 구글 드라이브 백업에서 복원 (README 참고)'
Write-Host ' 로그: %USERPROFILE%\dev-env-setup.log'
Read-Host 'Enter를 누르면 닫습니다'
