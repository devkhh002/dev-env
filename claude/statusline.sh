#!/bin/bash
# Claude Code 상태 표시줄 — 오케스트라 모드 · 모델 · 폴더 (Mac bash / Windows Git Bash 공용)
in="$(cat)"
pick() { printf '%s' "$in" | sed -n "s/.*\"$1\"[[:space:]]*:[[:space:]]*\"\([^\"]*\)\".*/\1/p" | head -1; }
model="$(pick display_name)"
dir="$(pick current_dir)"
dir="${dir//\\\\//}"; dir="${dir//\\//}"; dir="${dir%/}"; dir="${dir##*/}"
if [ -f "$HOME/.claude/fable/active.md" ]; then mode="🎻 오케스트라 ON"; else mode="오케스트라 OFF"; fi
out="$mode"
[ -n "$model" ] && out="$out · $model"
[ -n "$dir" ] && out="$out · $dir"
printf '%s' "$out"
