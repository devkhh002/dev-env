#!/bin/bash
# 한 줄 설치(Mac 터미널):
#   bash -c "$(curl -fsSL https://raw.githubusercontent.com/devkhh002/dev-env/main/mac/install.sh)"
# 이 저장소를 받아 설치 목록(체크 창)을 연다.
set -e
T="$(mktemp -d)"
curl -fsSL -o "$T/main.zip" https://github.com/devkhh002/dev-env/archive/refs/heads/main.zip
ditto -x -k "$T/main.zip" "$T"
exec bash "$T/dev-env-main/mac/setup.sh" "$@"
