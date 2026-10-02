using System;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using System.Threading;
using Microsoft.Win32;

namespace PcSetup
{
    // ① 인터넷 · ② Windows 설정
    public static partial class Defs
    {
        const string ToastKey = @"HKEY_CURRENT_USER\Software\Microsoft\Windows\CurrentVersion\PushNotifications";
        const string DcKey = @"HKEY_LOCAL_MACHINE\SOFTWARE\Policies\Microsoft\Windows\DataCollection";
        const string KbKey = @"HKEY_LOCAL_MACHINE\SYSTEM\CurrentControlSet\Services\i8042prt\Parameters";
        const string CtxKey = @"HKEY_CURRENT_USER\Software\Classes\CLSID\{86ca1aa0-34aa-4e8b-a509-50c905bae2a2}\InprocServer32";
        const string HighPerf = "8c5e7fda-e8bf-4a96-9a85-a6e23a8c635c";

        static string Powercfg { get { return Path.Combine(Env.System32, "powercfg.exe"); } }

        // ① 인터넷 — 연결돼 있으면 건드리지 않는다(지금 원격으로 쓰는 연결이 끊기지 않게)
        static partial void RegisterNet()
        {
            Add(G1, "netdriver", @"네트워크 드라이버 — USB 의 PC설치\네트워크 드라이버 (이 PC 에 맞는 것만 들어간다)", () => State.Online, () =>
            {
                if (State.Online) { Log.Say("   인터넷이 이미 연결돼 있어 건너뜁니다(지금 쓰는 연결을 끊지 않게)"); return; }
                if (State.Usb == null) { Log.Say("   USB(PC설치 폴더)를 찾지 못했습니다", Tone.Warn); return; }
                var dir = Path.Combine(State.Usb, "네트워크 드라이버");
                if (!Directory.Exists(dir)) { Log.Say("   USB 에 폴더가 없습니다: " + dir, Tone.Warn); return; }
                NwRun(Path.Combine(Env.System32, "pnputil.exe"), "/add-driver " + Proc.Quote(Path.Combine(dir, "*.inf")) + " /subdirs /install", 900);
                // 드라이버가 들어가고 연결이 잡힐 때까지 최대 1분
                Log.Say("   인터넷 연결을 기다립니다(최대 1분)", Tone.Dim);
                for (int i = 0; i < 12 && !Net.Online(); i++) Thread.Sleep(5000);
                State.Online = Net.Online();
            }, okText: "연결됨");
        }

        // ② Windows 설정
        static partial void RegisterWindows()
        {
            Add(G2, "power", "전원: 고성능 · 절전 안 함 (원격 PC 가 잠들지 않게)", () => AcIndex("SUB_SLEEP", "STANDBYIDLE") == 0, () =>
            {
                Proc.Run(Powercfg, "/setactive " + HighPerf, 60, enc: Proc.Oem);   // 고성능 계획이 없는 PC 도 있다 — 오류는 버린다
                NwRun(Powercfg, "/change standby-timeout-ac 0", 60);
                NwRun(Powercfg, "/change disk-timeout-ac 0", 60);
            });
            Add(G2, "hibernate", "최대 절전 끄기 (C 드라이브 용량 확보)", () => !File.Exists(@"C:\hiberfil.sys"), () => NwRun(Powercfg, "/hibernate off", 60));
            Add(G2, "toast", "알림 팝업 끄기", () => IsZero(Reg.Get(ToastKey, "ToastEnabled")), () => Reg.Set(ToastKey, "ToastEnabled", 0, RegistryValueKind.DWord));
            Add(G2, "ssd", "SSD 최적화: SysMain·Windows 검색 서비스 끄기 (시작 메뉴 파일 검색이 느려진다)",
                () => new[] { "SysMain", "WSearch" }.All(n => { var t = Svc.StartType(n); return t == null || t == "Disabled"; }),   // 없는 서비스는 세지 않는다
                () => { foreach (var n in new[] { "SysMain", "WSearch" }) Svc.StopAndDisable(n); });
            Add(G2, "telemetry", "진단 데이터 보내기 최소화", () => IsZero(Reg.Get(DcKey, "AllowTelemetry")), () => Reg.Set(DcKey, "AllowTelemetry", 0, RegistryValueKind.DWord));
            Add(G2, "kbtype3", "키보드 종류 유형 3 (Shift+Space 로 한/영)",
                () => string.Equals(Convert.ToString(Reg.Get(KbKey, "LayerDriver KOR")), "kbd101c.dll", StringComparison.OrdinalIgnoreCase)
                      && Convert.ToString(Reg.Get(KbKey, "OverrideKeyboardSubtype")) == "5",
                () =>
                {
                    Reg.Set(KbKey, "LayerDriver KOR", "kbd101c.dll", RegistryValueKind.String);
                    Reg.Set(KbKey, "OverrideKeyboardIdentifier", "PCAT_101CKEY", RegistryValueKind.String);
                    Reg.Set(KbKey, "OverrideKeyboardType", 8, RegistryValueKind.DWord);
                    Reg.Set(KbKey, "OverrideKeyboardSubtype", 5, RegistryValueKind.DWord);
                }, reboot: true);
            Add(G2, "classicmenu", "윈11 우클릭 메뉴를 예전 방식으로", () => Reg.KeyExists(CtxKey), () => Reg.Set(CtxKey, "", "", RegistryValueKind.String), off: true, reboot: true);
        }

        // 기록에 흘리며 실행 — 실행을 못 했거나 시간이 넘으면 실패(예외). 끝 코드는 다시 확인이 가린다
        static void NwRun(string exe, string args, int sec)
        {
            var r = Proc.RunLogged(exe, args, sec, Proc.Oem);
            if (!r.Started || r.TimedOut) throw new Exception(r.Err);
        }

        // 레지스트리 값이 0 인가 — PowerShell '-eq 0' 처럼 DWORD·QWORD 0 과 문자열 "0" 모두 (없으면 아님)
        static bool IsZero(object v) { return v != null && Convert.ToString(v) == "0"; }

        // 전원 설정의 현재 AC 값(초) — powercfg 출력의 마지막 두 16진수가 AC·DC
        static int AcIndex(string sub, string setting)
        {
            var r = Proc.Run(Powercfg, "/query SCHEME_CURRENT " + sub + " " + setting, 30, enc: Proc.Oem);
            var h = Regex.Matches(r.Out ?? "", "0x[0-9a-fA-F]{8}");
            return h.Count >= 2 ? Convert.ToInt32(h[h.Count - 2].Value, 16) : -1;
        }
    }
}
