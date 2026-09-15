---
description: 오케스트라(페이블) 모드 켜기/끄기/확인 — /orchestra on · /orchestra off · /orchestra
argument-hint: on | off | (비우면 상태 확인)
---

오케스트라(페이블) 모드 요청이다. 인자: "$ARGUMENTS"

- `on` · `켜기` · `켜` → `~/.claude/fable/fable.md` 를 `~/.claude/fable/active.md` 로 복사하고, fable.md 를 읽어 **지금부터** 그 방식(서브에이전트 위임)으로 일한다.
- `off` · `끄기` · `꺼` → `~/.claude/fable/active.md` 를 지우고 **지금부터** 직접 작업한다(위임 지시 무시).
- 비어 있거나 그 밖 → `~/.claude/fable/active.md` 가 있는지만 보고 켜짐/꺼짐을 알린다.

결과는 한 줄로만 보고한다. 예: "오케스트라 모드 켰습니다."
