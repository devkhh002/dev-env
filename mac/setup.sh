#!/bin/bash
# 개발 환경 설치 — Mac
#   bash setup.sh              목록에서 고르기(체크 창)
#   bash setup.sh --all        전부
#   bash setup.sh --only brew,node
#   bash setup.sh --list       설치 상태만 보기
#   bash setup.sh --dry-run    실제로 바꾸지 않고 할 일만 출력
# 이미 깔린 것은 건너뛴다(여러 번 실행해도 안전).

# ── 버전·설정(올릴 때는 여기만) ─────────────────────────────────────
NODE_FORMULA="node@24"
PYTHON_FORMULA="python@3.13"
CLASP_VER="3.3.0"
FIREBASE_VER="15.24.0"
GIT_NAME="Hyunhyo Kim"
GIT_EMAIL="devkhh002@gmail.com"
DEV_ROOT="$HOME/dev"
CLAUDE_MODEL="claude-opus-5-5[1m]"   # Claude Code 기본 모델 (빼려면 "" 로)
# ────────────────────────────────────────────────────────────────────

HERE="$(cd "$(dirname "$0")" && pwd)"
REPO="$(dirname "$HERE")"
MODE="pick"; ONLY=""; DRY=0
for a in "$@"; do
  case "$a" in
    --all) MODE="all" ;; --list) MODE="list" ;; --dry-run) DRY=1 ;;
    --only) MODE="only" ;; *) [ "$MODE" = "only" ] && ONLY="$a" ;;
  esac
done

run() { if [ $DRY -eq 1 ]; then echo "   [dry-run] $*"; else eval "$@"; fi; }
brew_env() {
  for b in /opt/homebrew/bin/brew /usr/local/bin/brew; do [ -x "$b" ] && eval "$("$b" shellenv)" && return 0; done
  return 1
}
brew_env
export PATH="$HOME/.local/bin:$PATH"
has_app() { [ -d "/Applications/$1.app" ]; }
has_cmd() { command -v "$1" >/dev/null 2>&1; }

GUREUM_PREF="$HOME/Library/Containers/org.youknowone.inputmethod.Gureum/Data/Library/Preferences/org.youknowone.Gureum"

# ── 설치 항목: id|이름|확인|설치 (확인·설치는 함수 이름) ───────────────
chk_clt()      { xcode-select -p >/dev/null 2>&1; }
ins_clt()      { run "xcode-select --install"; echo "   ▶ 뜨는 창에서 '설치'를 누르고, 끝나면 이 설치를 다시 실행하세요."; }
chk_brew()     { has_cmd brew; }
ins_brew()     { run 'NONINTERACTIVE=1 /bin/bash -c "$(curl -fsSL https://raw.githubusercontent.com/Homebrew/install/HEAD/install.sh)"'; brew_env; }
chk_devtools() { has_cmd git && has_cmd gh && [ -x "$(brew --prefix 2>/dev/null)/opt/$NODE_FORMULA/bin/node" ] && brew list "$PYTHON_FORMULA" >/dev/null 2>&1; }
ins_devtools() { run "brew install git gh $NODE_FORMULA $PYTHON_FORMULA"; run "brew link --overwrite --force $NODE_FORMULA"; }
chk_npmtools() { has_cmd clasp && has_cmd firebase; }
ins_npmtools() { run "npm i -g @google/clasp@$CLASP_VER firebase-tools@$FIREBASE_VER"; }
chk_claude()   { has_cmd claude; }
ins_claude()   { run 'curl -fsSL https://claude.ai/install.sh | bash'; }
chk_claudeconfig() { [ -f "$HOME/.claude/commands/orchestra.md" ] && [ -f "$HOME/.claude/statusline.sh" ] && grep -q 'statusline.sh' "$HOME/.claude/settings.json" 2>/dev/null && grep -qF "$CLAUDE_MODEL" "$HOME/.claude/settings.json" 2>/dev/null; }
ins_claudeconfig() {
  run "mkdir -p \"\$HOME/.claude/agents\" \"\$HOME/.claude/fable\" \"\$HOME/.claude/commands\""
  run "cp -f \"$REPO/claude/agents/\"*.md \"\$HOME/.claude/agents/\""
  run "cp -f \"$REPO/claude/fable/fable.md\" \"\$HOME/.claude/fable/fable.md\""
  run "cp -f \"$REPO/claude/commands/\"*.md \"\$HOME/.claude/commands/\""
  run "cp -f \"$REPO/claude/statusline.sh\" \"$REPO/claude/CLAUDE.md\" \"\$HOME/.claude/\""
  # settings.json 은 덮지 않고 statusLine 만 넣는다
  if [ $DRY -eq 1 ]; then echo "   [dry-run] settings.json 에 statusLine·model 추가"; else
    CLAUDE_MODEL="$CLAUDE_MODEL" /usr/bin/python3 - <<'PY'
import json, os
p = os.path.expanduser('~/.claude/settings.json')
try: s = json.load(open(p))
except Exception: s = {}
s["statusLine"] = {"type": "command", "command": "bash ~/.claude/statusline.sh"}
m = os.environ.get("CLAUDE_MODEL", "")
if m: s["model"] = m
json.dump(s, open(p, "w"), indent=2, ensure_ascii=False)
PY
  fi
}
chk_gitconfig() { [ "$(git config --global core.autocrlf 2>/dev/null)" = "input" ] || [ "$(git config --global core.quotepath 2>/dev/null)" = "false" ]; }
ins_gitconfig() {
  run "git config --global core.quotepath false"
  run "git config --global core.precomposeunicode true"
  run "git config --global init.defaultBranch main"
  run "git config --global user.name \"$GIT_NAME\""
  run "git config --global user.email \"$GIT_EMAIL\""
}
chk_gureum() { [ -d "/Library/Input Methods/Gureum.app" ] && [ "$(defaults read "$GUREUM_PREF" InputModeExchangeKey 2>/dev/null | grep -c 131072)" -ge 1 ]; }
ins_gureum() {
  [ -d "/Library/Input Methods/Gureum.app" ] || run "brew install --cask gureumkim"
  # 구름: 한글/로마자 바꾸기 = Shift+Space
  run "mkdir -p \"$(dirname "$GUREUM_PREF")\""
  run "defaults write \"$GUREUM_PREF\" InputModeExchangeKey -dict keyCode -int 49 modifier -int 131072"
  # macOS 기본 입력 소스 단축키 2개 끄기(켜져 있으면 Shift+Space를 먼저 가져간다)
  run "defaults write com.apple.symbolichotkeys AppleSymbolicHotKeys -dict-add 60 '<dict><key>enabled</key><false/></dict>'"
  run "defaults write com.apple.symbolichotkeys AppleSymbolicHotKeys -dict-add 61 '<dict><key>enabled</key><false/></dict>'"
  # 문서마다 입력 소스 자동 전환 끄기
  run "defaults write com.apple.HIToolbox AppleGlobalTextInputProperties -dict TextInputGlobalPropertyPerContextInput -bool false"
  run "/System/Library/PrivateFrameworks/SystemAdministration.framework/Resources/activateSettings -u"
  # 입력 소스: 구름 두벌식·로마자 켜기, 애플 기본 2벌식 끄기
  local bin="${TMPDIR:-/tmp}/dev-env-inputsource"
  run "swiftc -O \"$HERE/inputsource.swift\" -o \"$bin\" 2>/dev/null"
  if [ $DRY -eq 1 ] || "$bin" enable org.youknowone.inputmethod.Gureum.han2 org.youknowone.inputmethod.Gureum.system disable com.apple.inputmethod.Korean.2SetKorean; then :; else
    echo "   ▶ 구름 입력기가 아직 목록에 없습니다. 로그아웃 → 다시 로그인 후 이 설치에서 '한/영 전환(구름)'만 다시 체크해 실행하세요."
  fi
}
chk_karabiner() { has_app "Karabiner-Elements" && grep -q "Moonlight + Chrome Remote" "$HOME/.config/karabiner/karabiner.json" 2>/dev/null; }
ins_karabiner() {
  has_app "Karabiner-Elements" || run "brew install --cask karabiner-elements"
  run "mkdir -p \"\$HOME/.config/karabiner\""
  if [ $DRY -eq 1 ]; then echo "   [dry-run] karabiner.json 에 규칙 병합"; else
    /usr/bin/python3 - "$HERE/karabiner-rule.json" <<'PY'
import json, os, sys
path = os.path.expanduser('~/.config/karabiner/karabiner.json')
rule = json.load(open(sys.argv[1]))
try: cfg = json.load(open(path))
except Exception: cfg = {"profiles": [{"name": "Default profile", "selected": True}]}
p = next((x for x in cfg["profiles"] if x.get("selected")), cfg["profiles"][0])
cm = p.setdefault("complex_modifications", {})
cm["rules"] = [r for r in cm.get("rules", []) if r.get("description") != rule["description"]] + [rule]
json.dump(cfg, open(path, "w"), indent=2, ensure_ascii=False)
print("   Karabiner 규칙 등록:", rule["description"])
PY
  fi
  run "open -a Karabiner-Elements"
  echo "   ▶ 뜨는 '허용' 요청(시스템 확장·입력 모니터링)은 전부 허용하세요."
}
chk_moonlight() { has_app "Moonlight"; }
ins_moonlight() { run "brew install --cask moonlight"; }
chk_tailscale() { has_app "Tailscale"; }
ins_tailscale() { run "brew install --cask tailscale-app"; }
chk_chrome()    { has_app "Google Chrome"; }
ins_chrome()    { run "brew install --cask google-chrome"; }

ITEMS=(
  "clt|Xcode 명령줄 도구 (git·컴파일러 — 맨 먼저)|chk_clt|ins_clt"
  "brew|Homebrew (맥 앱 설치 도구)|chk_brew|ins_brew"
  "devtools|git · GitHub CLI · Node 24 · Python 3.13|chk_devtools|ins_devtools"
  "npmtools|clasp $CLASP_VER · firebase-tools $FIREBASE_VER|chk_npmtools|ins_npmtools"
  "claude|Claude Code|chk_claude|ins_claude"
  "claudeconfig|Claude 설정 — 오케스트라 모드(말로 켜고 끄기·/orchestra)·상태 표시줄|chk_claudeconfig|ins_claudeconfig"
  "gitconfig|git 기본 설정(한글 파일명·이름)|chk_gitconfig|ins_gitconfig"
  "gureum|한/영 전환 — 구름 입력기 + Shift+Space|chk_gureum|ins_gureum"
  "karabiner|한/영 전환 — 원격 창에서 Shift+Space (Karabiner)|chk_karabiner|ins_karabiner"
  "moonlight|Moonlight (원격 화면)|chk_moonlight|ins_moonlight"
  "tailscale|Tailscale (원격 접속 VPN)|chk_tailscale|ins_tailscale"
  "chrome|Google Chrome (크롬 원격 데스크톱)|chk_chrome|ins_chrome"
)

# 프로젝트: projects.txt 한 줄 = 항목 하나 (Mac은 폴더만 — 바로가기 없음)
PROJ_LINES=()
while IFS= read -r line; do
  [[ "$line" =~ ^[[:space:]]*# || -z "${line// }" ]] && continue
  PROJ_LINES+=("$line")
done < "$REPO/projects.txt"
trim() { local s="$1"; s="${s#"${s%%[![:space:]]*}"}"; echo "${s%"${s##*[![:space:]]}"}"; }
for i in "${!PROJ_LINES[@]}"; do
  IFS='|' read -r pf pu pn <<< "${PROJ_LINES[$i]}"
  pf="$(trim "$pf")"; pu="$(trim "$pu")"; pn="$(trim "$pn")"
  eval "chk_proj$i() { [ -d \"$DEV_ROOT/$pf\" ]; }"
  eval "ins_proj$i() {
    run \"mkdir -p \\\"$(dirname "$DEV_ROOT/$pf")\\\"\"
    if [ -n \"$pu\" ]; then
      case \"$pu\" in *github.com*) gh auth status >/dev/null 2>&1 || run \"gh auth login -h github.com -p https -w && gh auth setup-git\" ;; esac
      run \"git clone \\\"$pu\\\" \\\"$DEV_ROOT/$pf\\\"\"
    else
      run \"mkdir -p \\\"$DEV_ROOT/$pf\\\"\"; echo \"   ▶ 이 프로젝트는 git이 없습니다 — 백업을 $DEV_ROOT/$pf 에 풀어 넣으세요.\"
    fi
  }"
  if [ -n "$pu" ]; then lab="프로젝트: $pn (GitHub에서 받기)"; else lab="프로젝트: $pn (폴더만 — 백업에서 복원)"; fi
  ITEMS+=("proj$i|$lab|chk_proj$i|ins_proj$i")
done

# ── 상태 ────────────────────────────────────────────────────────────
if [ "$MODE" = "list" ]; then
  for it in "${ITEMS[@]}"; do IFS='|' read -r id name chk ins <<< "$it"; if $chk; then s="OK"; else s="--"; fi; printf "%-3s %-10s %s\n" "$s" "$id" "$name"; done
  exit 0
fi

# ── 고르기 ──────────────────────────────────────────────────────────
PICK=()
if [ "$MODE" = "all" ]; then
  for it in "${ITEMS[@]}"; do PICK+=("$it"); done
elif [ "$MODE" = "only" ]; then
  for it in "${ITEMS[@]}"; do IFS='|' read -r id _ _ _ <<< "$it"; [[ ",$ONLY," == *",$id,"* ]] && PICK+=("$it"); done
else
  labels=(); defaults=()
  for it in "${ITEMS[@]}"; do
    IFS='|' read -r id name chk ins <<< "$it"
    if $chk; then labels+=("[설치됨] $name"); else labels+=("$name"); defaults+=("$name"); fi
  done
  as_list() { local out="" x; for x in "$@"; do x="${x//\\/\\\\}"; x="${x//\"/\\\"}"; out+="\"$x\","; done; echo "{${out%,}}"; }
  chosen="$(osascript -e "set r to choose from list $(as_list "${labels[@]}") with title \"개발 환경 설치\" with prompt \"설치할 것을 고르세요 (⌘ 누른 채 클릭하면 여러 개)\" default items $(as_list "${defaults[@]}") OK button name \"설치\" cancel button name \"취소\" with multiple selections allowed" -e 'if r is false then return "" ' -e 'set AppleScript'"'"'s text item delimiters to linefeed' -e 'return r as text')"
  [ -z "$chosen" ] && { echo "취소했습니다."; exit 0; }
  for it in "${ITEMS[@]}"; do
    IFS='|' read -r id name chk ins <<< "$it"
    while IFS= read -r c; do [ "$c" = "$name" ] || [ "$c" = "[설치됨] $name" ] && PICK+=("$it") && break; done <<< "$chosen"
  done
fi

# ── 설치 ────────────────────────────────────────────────────────────
n=0; total=${#PICK[@]}
for it in "${PICK[@]}"; do
  n=$((n+1)); IFS='|' read -r id name chk ins <<< "$it"
  if $chk; then echo "[$n/$total] $name — 이미 설치됨, 건너뜀"; continue; fi
  echo "[$n/$total] $name"
  $ins
  brew_env; hash -r
done

cat <<'EOF'

──────── 사람이 해야 하는 일 ────────
 • 새 터미널에서 claude 실행 → 브라우저 로그인
 • clasp login / firebase login (Apps Script·Firebase 쓰는 프로젝트만)
 • 구름 입력기를 처음 깔았다면 로그아웃 → 로그인 후, 이 설치에서 '한/영 전환(구름)'만 다시 실행
 • Karabiner·Tailscale 허용 요청 전부 허용, Tailscale 로그인, Moonlight에서 PC 추가 → PIN
 • git이 없는 프로젝트·Claude 기억은 구글 드라이브 백업에서 복원 (README 참고)
EOF
