// 입력 소스 켜기/끄기 — macOS 설정 앱을 손으로 여는 대신
//   inputsource enable <ID>... disable <ID>...
// 찾지 못한 ID가 있으면 종료 코드 1 (구름 입력기는 설치 후 로그아웃해야 목록에 뜬다)
import Carbon

func find(_ id: String) -> TISInputSource? {
    let filter = [kTISPropertyInputSourceID as String: id] as CFDictionary
    guard let list = TISCreateInputSourceList(filter, true)?.takeRetainedValue() as? [TISInputSource] else { return nil }
    return list.first
}

var mode = "enable"
var missing = 0
for arg in CommandLine.arguments.dropFirst() {
    if arg == "enable" || arg == "disable" { mode = arg; continue }
    guard let src = find(arg) else {
        if mode == "enable" { print("   없음: \(arg)"); missing += 1 }
        continue
    }
    let r = mode == "enable" ? TISEnableInputSource(src) : TISDisableInputSource(src)
    print("   \(mode) \(arg): \(r == noErr ? "OK" : "오류 \(r)")")
}
exit(missing > 0 ? 1 : 0)
