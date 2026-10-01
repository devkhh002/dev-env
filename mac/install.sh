#!/bin/bash
# 한 줄 설치(Mac 터미널):
#   bash -c "$(curl -fsSL https://raw.githubusercontent.com/devkhh002/dev-env/main/mac/install.sh)"
# 설치에 필요한 파일만 받아(매뉴얼 캡처는 크다) 설치 목록(체크 창)을 연다.
set -e
RAW="https://raw.githubusercontent.com/devkhh002/dev-env/main"
T="$(mktemp -d)"
# setup.sh 가 읽는 파일 전부 — claude/ 에 파일을 더하면 여기도 더한다(windows/install.ps1 도)
for f in mac/setup.sh mac/inputsource.swift mac/karabiner-rule.json projects.txt \
         claude/CLAUDE.md claude/statusline.sh claude/agents/deep-reasoner.md claude/agents/runner.md \
         claude/commands/orchestra.md claude/fable/fable.md; do
  echo "download $f"
  mkdir -p "$T/$(dirname "$f")"
  curl -fsSL -o "$T/$f" "$RAW/$f"
done
exec bash "$T/mac/setup.sh" "$@"
