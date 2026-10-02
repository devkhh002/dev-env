# dev-env — 새 PC 한 번에 설치 (Windows 설정·드라이버·앱·개발 환경)

새 윈도우·새 맥에서 하나만 실행하면 설치 화면이 뜬다. 체크한 것만 설치하고, 이미 된 것은 건너뛴다(몇 번을 다시 실행해도 안전).
Windows 는 단계별 화면(① 인터넷 → ② Windows 설정 → ③ 드라이버 → ④ 도구·앱 → ⑤ 개발 환경 → ⑥ 개발 소스)이고, 앱·도구는 winget 으로 늘 최신판이 깔린다.

## 1. 실행

**Windows**: `PC 설치.exe` 를 두 번 누른다 → 관리자 확인 한 번 → 설치 화면. (PowerShell 창은 뜨지 않는다)
- 새 PC: Windows 설치 → 랜 드라이버 → USB 맨 위 `PC 설치.exe`. 인터넷이 아직 안 되면 설치 화면의 ① 이 USB `PC설치\네트워크 드라이버` 의 드라이버를 깐다.
- 받는 곳: https://github.com/devkhh002/dev-env/raw/main/dist/PcSetup.exe (이름은 `PC 설치.exe` 로 바꿔 둔다)
- 새 판: 열 때 GitHub 의 `dist/version.txt` 가 더 새것이면 스스로 받아 바꾸고 다시 연다(USB 에 둔 것도 그 자리에서 바뀐다).
- 앱 목록(`catalog.txt`)·프로젝트(`projects.txt`)는 열 때마다 GitHub 최신을 읽는다(인터넷이 없으면 프로그램 안에 든 판).
- 버전: 설치 화면 제목의 빌드 시각(예: `2026-10-02 15:22`).
- 시험용: `PC 설치.exe --list <파일>`(상태만 파일로) · `--snapshot <png>`(화면을 그림으로).

**Mac** — 터미널에 붙여넣기

```bash
bash -c "$(curl -fsSL https://raw.githubusercontent.com/devkhh002/dev-env/main/mac/install.sh)"
```

Mac 옵션: `--all` · `--only brew,gureum` · `--list` · `--dry-run`.

**빌드(Windows 프로그램)**: `app\build.cmd` → `dist\PcSetup.exe` · `dist\version.txt` · `dist\PcSetup.exe.sha256`. C# 컴파일러와 .NET 4.8 참조 파일은 `app\.tools` 에 받아 쓴다(이 PC 에 설치하지 않는다). 커밋·푸시하면 모든 PC 가 다음에 열 때 새 판으로 바뀐다.
소스는 `app\src` — 항목은 `Items\*.cs`, 버튼은 `UI\*.cs`·`Upgrade.cs`, 버전 고정 값은 `Data.cs` 의 `Conf`.

## 2. 설치 목록

**Windows**

| 단계 | 항목 |
|---|---|
| ① 인터넷 | 네트워크 드라이버(USB `PC설치\네트워크 드라이버`) — 인터넷이 이미 되면 건드리지 않는다(원격 연결이 끊기지 않게) |
| ② Windows 설정 | 전원 고성능·절전 안 함 · 최대 절전 끄기 · 알림 끄기 · SSD 최적화(SysMain·검색 끄기) · 진단 데이터 최소화 · 키보드 유형 3(Shift+Space 한/영) · 옛 우클릭 메뉴(선택) |
| ③ 드라이버 | **그래픽(NVIDIA)** — 공식 조회로 최신판을 받아 설치(카드 자동 인식, 지금 버전보다 새것일 때만). 설치 중 화면이 깜빡여 원격 중엔 기본 꺼짐. NVIDIA 카드가 없으면 안 나온다. 랜(네트워크)은 지금 인터넷을 쓰는 어댑터라 건드리지 않는다(①의 USB 드라이버로만) |
| ④ 도구·앱 | winget · `catalog.txt` 의 앱(Chrome·웨일·반디집·팟플레이어 32·64비트·크롬 원격 호스트·유니콘 HTTPS·HWiNFO·CPU-Z·스티커 메모·Claude 앱 …) · **TrafficMonitor**(공식 최신판 + 저장소 설정 `windows/trafficmonitor/config.ini` → 작업 표시줄 표시, 로그인할 때 관리자 권한 예약 작업으로 자동 실행) · 반디집 광고 차단(hosts) · **한/영 전환**(AutoHotkey 관리자 권한) · Tailscale · Sunshine |
| ⑤ 개발 환경 | Git · Node.js 24 · Python 3.13 · GitHub CLI · PowerShell 7 · Windows Terminal(Ctrl+C/V) · clasp·firebase · Claude Code · Claude 설정 · git 설정 · VirtualBox 7.2.14 |
| ⑥ 개발 소스 | `projects.txt` 의 프로젝트 → `C:\dev` + 바로가기 (GitHub 로그인이라 기본 체크 해제) |
| 관리 | USB 맨 위에 이 프로그램(`PC 설치.exe`) 넣기 |

**아래 버튼**
- `앱 추가…` — winget 에서 찾아 고르면 `catalog.txt` 에 한 줄 넣고 커밋·푸시 → 모든 PC 의 설치 화면에 나온다. 넣은 앱은 체크된 채로 목록에 보여 바로 시험 설치할 수 있다.
- `모두 최신으로` — 설치된 앱을 최신판으로. 버전 고정(`@버전`·⑤ 개발 환경, `Conf.UpgradeFixed`)과 원격 호스트(Chrome 원격 데스크톱·Sunshine·Tailscale — `Conf.UpgradeSkip`, 올리는 동안 원격이 끊긴다)는 뺀다. 팟플레이어·HWiNFO 같은 `url:`·`latest:` 앱은 공식 최신 설치본으로.
- `소스 올리기…` — `C:\dev` 의 git 프로젝트를 고르면 올라갈 파일 목록과 비밀정보 검사 결과를 보여 주고, 막힌 것이 없을 때만 커밋·푸시. 개인 키·토큰·`.env`·키 파일은 막고, 비밀번호처럼 보이는 값·Google API 키는 '확인했다' 를 체크해야 올린다. 검사한 뒤 파일이 바뀌면 다시 검사하게 한다.

- 재부팅이 필요한 항목은 설치가 끝난 뒤 "지금 재부팅?"을 묻는다(원격 접속 중이면 끊긴다).
- 설치는 설치 화면 안에서 돈다. 아래 칸에 기록이 나오고, `자세히` 를 켜면 winget·설치 프로그램 메시지까지 보인다. 기록 파일: `%USERPROFILE%\dev-env-setup.log`.
- 개발 도구 버전은 `app\src\Data.cs` 의 `Conf.V` 한 곳. 앱·도구는 버전 없이 늘 최신(고정할 것만 `@버전`).

**Mac**: Xcode 명령줄 도구 · Homebrew · git · GitHub CLI · Node 24 · Python 3.13 · clasp·firebase · Claude Code · git 설정 · **한/영 전환**(구름 입력기 + Shift+Space · Karabiner 원격 규칙) · Tailscale · Moonlight · Chrome · 프로젝트(`~/dev`). 버전은 `mac/setup.sh` 맨 위.

## 3. 앱·도구·프로젝트 추가 (직접)

**앱·도구** — 설치 화면의 `앱 추가…` 가 가장 쉽다(이 PC 에 `C:\dev\dev-env` 저장소가 있으면 바로 커밋·푸시). 직접 하려면 `catalog.txt` 에 한 줄 추가 → 커밋·푸시 → 모든 PC 의 설치 화면에 나온다.

```
그룹 | 이름 | 설치 방법 | 기본 체크(on/off) | 확인 이름(없어도 됨)
도구 | Notepad++ | winget:Notepad++.Notepad++ | on
```

- 아이디 찾기: 설치 화면의 `앱 추가…`, 또는 명령 창에서 `winget search 이름` · 버전 고정: `winget:아이디@버전`
- winget 에 없는 것: 파일을 USB `PC설치\도구` 에 넣고 `usb:파일이름`
- winget 목록이 늦어 다운로드 주소가 없어지면(404) 설치 화면에 이유가 나온다 — 며칠 뒤 다시 하거나, 공식 주소로 바꾼다
  - `url:주소` — 늘 같은 주소가 최신판인 곳(팟플레이어)
  - `latest:페이지 파일이름` — 그 페이지에서 파일 이름(`*` 가능)이 맞는 링크 중 번호가 가장 큰 것(HWiNFO: `latest:https://www.sac.sk/files.php?d=13&l=H hwi_*x.exe`)
  - 조용히 설치하는 옵션(NSIS `/S` · Inno Setup `/VERYSILENT`)은 알아서 고르고, 서명이 없거나 깨진 설치 파일은 실행하지 않는다

**네트워크 드라이버** — 새 기종이면 랜(필요하면 Wi-Fi) 드라이버를 `.inf` 가 든 폴더째 USB `PC설치\네트워크 드라이버` 에 넣는다. 맞지 않는 PC 에서는 자동으로 건너뛴다.

**설치 프로그램에 넣는 파일**(한/영 스크립트·TrafficMonitor 설정·Claude 설정 파일)은 `app/build.rsp` 의 `/resource:` 줄로 프로그램 안에 들어간다 — 파일을 더하면 거기에도 한 줄 더하고 다시 빌드한다.

**프로젝트** — `projects.txt`에 한 줄 추가 → 커밋 → 설치 화면 ⑥ 에서 그 프로젝트만 체크.

```
폴더(C:\dev 또는 ~/dev 기준) | git 주소(비우면 백업에서 직접 복원) | 바로가기 이름
```

Windows 바로가기는 **시작 위치 칸이 폴더를 정한다**(대상은 `wt.exe … -d . pwsh … claude.exe`).
폴더를 옮기면 바로가기 속성의 **시작 위치**만 고치면 된다.

## 4. 자동으로 못 하는 일 (체크리스트)

**공통**
- [ ] 새 터미널에서 `claude` → 브라우저 로그인
- [ ] 비공개 저장소를 받을 때 GitHub 로그인(설치 중 브라우저가 열린다)
- [ ] `clasp login` · `firebase login` (Apps Script·Firebase 쓰는 프로젝트만)
- [ ] git이 없는 프로젝트 폴더는 구글 드라이브 백업을 `C:\dev`(맥은 `~/dev`)에 풀기
- [ ] Claude 기억 복원: 백업 zip의 `_claude_기억/<프로젝트>/memory` 를
      `~/.claude/projects/<폴더 주소의 영문·숫자 외 글자를 - 로 바꾼 이름>/memory` 로 복사
      (예: `C:\dev\SKPOS 지도_20260703` → `C--dev-SKPOS----20260703`). 해당 폴더에서 `claude`를 한 번 실행하면 그 이름의 폴더가 생긴다.

**Windows**
- [ ] Tailscale 트레이 아이콘 → 로그인
- [ ] Sunshine: `https://localhost:47990` → 관리자 계정 만들기 → 맥 Moonlight에서 PC 추가 후 **PIN 메뉴**에 숫자 입력

**Mac**
- [ ] Xcode 명령줄 도구 설치 창에서 '설치' → 끝나면 설치를 다시 실행
- [ ] 구름 입력기를 처음 깔았다면 **로그아웃 → 로그인** 후 설치에서 `한/영 전환 — 구름`만 다시 실행(입력 소스 켜기)
- [ ] Karabiner 허용 요청(시스템 확장·입력 모니터링) 전부 허용
- [ ] Tailscale 로그인 · Moonlight 화질 설정(1440p · 40Mbps · 코덱 자동 · YUV 4:4:4)

## 5. Claude 오케스트레이션 모드 (기본 꺼짐)

설치 목록의 **Claude 설정** 항목이 `claude/` 를 `~/.claude/` 로 복사한다(`settings.json`은 덮지 않고 상태 표시줄 설정만 넣는다).

**기본 모델**도 같이 넣는다 — `opus[1m]`(설치 스크립트 맨 위 `ClaudeModel`/`CLAUDE_MODEL`).
버전 번호 대신 별칭이라 새 Opus가 나오면 Claude Code 업데이트와 함께 자동으로 따라간다(2.1.286에서 `claude-opus-5-5[1m]`로 풀리는 것 확인).
두 보조도 별칭(`opus`·`haiku`)이라, 모델이 바뀌어도 이 저장소를 고칠 일은 없다.

**켜고 끄기는 말로 하면 된다** — Claude에게 "오케스트라 모드 켜줘" / "꺼" / "켜져 있어?".
입력칸에 `/` 를 치면 나오는 목록의 `/orchestra`(on·off)도 같은 일을 하고, 화면 아래 상태 표시줄에 `🎻 오케스트라 ON` 이 보인다.

| 파일 | 역할 |
|---|---|
| `agents/deep-reasoner.md` | 어려운 추론·설계·원인 분석 담당 보조 — 최신 Opus(`opus`), effort max |
| `agents/runner.md` | 명령 실행·검색·로그 확인 잡무 담당 보조 — 최신 Haiku(`haiku`), effort low |
| `fable/fable.md` | "직접 하지 말고 두 보조에게 나눠 맡겨라" 지시문 |
| `CLAUDE.md` | `@~/.claude/fable/active.md` — `active.md`가 있으면 그 내용을 읽는다 |

- **켜기**: `fable/fable.md` 를 같은 폴더에 `active.md` 로 복사
  - Windows: `Copy-Item ~\.claude\fable\fable.md ~\.claude\fable\active.md`
  - Mac: `cp ~/.claude/fable/fable.md ~/.claude/fable/active.md`
- **끄기**: `active.md` 삭제 (`Remove-Item ~\.claude\fable\active.md` / `rm ~/.claude/fable/active.md`)
- 바꾼 뒤 새로 연 Claude 세션부터 적용된다. 두 보조는 모드와 상관없이 필요하면 부를 수 있다.

## 6. 한/영 전환 매뉴얼

자동 설치가 무엇을 하는지, 막혔을 때 어디를 보는지는 [`manual/한영전환_원격_매뉴얼.md`](manual/한영전환_원격_매뉴얼.md) (캡처는 `manual/캡처`).
