#Requires AutoHotkey v2.0
#SingleInstance Ignore   ; 이미 떠 있으면 아무것도 하지 않는다(감시 작업이 10분마다 실행)
; Mac(Karabiner)에서 Shift+Space -> Option+Space 로 넘어온 키를 Windows 한/영 키로 바꾼다
!Space::Send "{vk15}"
