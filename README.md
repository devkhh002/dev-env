# dev-env — 새 컴퓨터 개발 환경 한 번에 설치

새 윈도우·새 맥에서 **한 줄**을 실행하면 설치 목록이 뜬다. 체크한 것만 설치하고, 이미 깔린 것은 건너뛴다(몇 번을 다시 실행해도 안전).
한/영 전환(윈도우 AutoHotkey · 맥 구름 입력기·Karabiner)도 목록에 들어 있다.

## 1. 실행

**Windows** — PowerShell에 붙여넣기 (관리자 권한 창이 한 번 뜬다)

```powershell
irm https://raw.githubusercontent.com/devkhh002/dev-env/main/windows/install.ps1 | iex
```

**Mac** — 터미널에 붙여넣기

```bash
bash -c "$(curl -fsSL https://raw.githubusercontent.com/devkhh002/dev-env/main/mac/install.sh)"
```

이미 이 저장소를 받아 둔 컴퓨터에서는 `windows\setup.ps1` / `mac/setup.sh` 를 직접 실행해도 된다.
옵션: `-All`(전부) · `-Only git,node` · `-List`(상태만) — Mac은 `--all` · `--only brew,gureum` · `--list` · `--dry-run`.

## 2. 설치 목록

| Windows | Mac |
|---|---|
| Git(Git Bash) · Node.js 24 · Python 3.13 · GitHub CLI | Xcode 명령줄 도구 · Homebrew |
| PowerShell 7 · Windows Terminal(PowerShell을 탭으로) | git · GitHub CLI · Node 24 · Python 3.13 |
| clasp · firebase-tools | clasp · firebase-tools |
| Claude Code | Claude Code |
| git 기본 설정(줄바꿈 유지·한글 파일명) | git 기본 설정(한글 파일명) |
| **한/영 전환** — AutoHotkey + `hangul.ahk`(자동 시작) | **한/영 전환** — 구름 입력기 + Shift+Space · Karabiner 원격 규칙 |
| Tailscale · Sunshine(원격 호스트) | Tailscale · Moonlight · Chrome |
| 프로젝트(`projects.txt`) → `C:\dev` + 바로가기 | 프로젝트(`projects.txt`) → `~/dev` |

버전은 `windows/setup.ps1`·`mac/setup.sh` 맨 위 한 곳에 모여 있다. 올릴 때는 거기만 고친다.

## 3. 프로젝트 추가

`projects.txt`에 한 줄 추가 → 커밋 → 설치를 다시 실행해 그 프로젝트만 체크.

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

**기본 모델**도 같이 넣는다 — `claude-opus-5-5[1m]`(설치 스크립트 맨 위 `ClaudeModel`/`CLAUDE_MODEL`).
`/model` 목록에서 Opus 5.5가 비활성으로 보이던 때(2.1.273)의 우회책이다. 목록이 정상이 되면 `~/.claude/settings.json` 의 `"model"` 줄만 지우면 된다.

**켜고 끄기는 말로 하면 된다** — Claude에게 "오케스트라 모드 켜줘" / "꺼" / "켜져 있어?".
입력칸에 `/` 를 치면 나오는 목록의 `/orchestra`(on·off)도 같은 일을 하고, 화면 아래 상태 표시줄에 `🎻 오케스트라 ON` 이 보인다.

| 파일 | 역할 |
|---|---|
| `agents/deep-reasoner.md` | 어려운 추론·설계·원인 분석 담당 보조 — Opus 5, effort max |
| `agents/runner.md` | 명령 실행·검색·로그 확인 잡무 담당 보조 — Haiku 4.5, effort low |
| `fable/fable.md` | "직접 하지 말고 두 보조에게 나눠 맡겨라" 지시문 |
| `CLAUDE.md` | `@~/.claude/fable/active.md` — `active.md`가 있으면 그 내용을 읽는다 |

- **켜기**: `fable/fable.md` 를 같은 폴더에 `active.md` 로 복사
  - Windows: `Copy-Item ~\.claude\fable\fable.md ~\.claude\fable\active.md`
  - Mac: `cp ~/.claude/fable/fable.md ~/.claude/fable/active.md`
- **끄기**: `active.md` 삭제 (`Remove-Item ~\.claude\fable\active.md` / `rm ~/.claude/fable/active.md`)
- 바꾼 뒤 새로 연 Claude 세션부터 적용된다. 두 보조는 모드와 상관없이 필요하면 부를 수 있다.

## 6. 한/영 전환 매뉴얼

자동 설치가 무엇을 하는지, 막혔을 때 어디를 보는지는 [`manual/한영전환_원격_매뉴얼.md`](manual/한영전환_원격_매뉴얼.md) (캡처는 `manual/캡처`).
