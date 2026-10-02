using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

namespace PcSetup
{
    // 설치 항목 하나 = 화면의 체크박스 하나
    public sealed class Item
    {
        public string Group, Id, Name, OkText = "설치됨", Kind = "";
        public Func<bool> Check;     // 이미 됐나
        public Action Install;       // 설치 — 실패는 예외로
        public bool Default = true;  // 처음에 체크
        public bool Reboot;          // 재부팅해야 적용
        public bool Installed;       // 마지막 확인 결과
    }

    // 항목 목록 — 묶음 순서대로. 묶음별 등록은 Items\*.cs 가 채운다
    public static partial class Defs
    {
        public const string G1 = "① 인터넷", G2 = "② Windows 설정", G3 = "③ 드라이버", G4 = "④ 도구·앱", G5 = "⑤ 개발 환경", G6 = "⑥ 개발 소스 (GitHub 로그인)", G9 = "관리";
        // 화면에 미리 만들어 두는 묶음(이 순서로 보인다). 항목이 없는 묶음은 숨긴다
        public static readonly string[] GroupOrder = { G1, G2, G3, G4, G4 + "/기본 앱", G4 + "/원격", G4 + "/도구", G4 + "/Claude", G5, G6, G9 };

        public static readonly List<Item> All = new List<Item>();

        public static Item Add(string group, string id, string name, Func<bool> check, Action install, bool off = false, bool reboot = false, string okText = "설치됨", string kind = "")
        {
            var it = new Item { Group = group, Id = id, Name = name, Check = check, Install = install, Default = !off, Reboot = reboot, OkText = okText, Kind = kind };
            All.Add(it);
            return it;
        }

        // 확인 — 오류는 '안 됨'
        public static bool Check(Item it) { try { return it.Check != null && it.Check(); } catch { return false; } }

        public static void Build()
        {
            All.Clear();
            RegisterNet();       // ① 네트워크 드라이버
            RegisterWindows();   // ② Windows 설정
            RegisterDrivers();   // ③ NVIDIA 그래픽 드라이버
            RegisterApps();      // ④ winget · catalog 앱 · TrafficMonitor · 반디집 광고 · 한/영 · Tailscale · Sunshine
            RegisterDev();       // ⑤ Git · Node · Python · gh · PowerShell 7 · Terminal · clasp/firebase · Claude Code · Claude 설정 · git 설정 · catalog '개발 환경'
            RegisterProjects();  // ⑥ projects.txt
            RegisterManage();    // 관리 — USB
        }
        static partial void RegisterNet();
        static partial void RegisterWindows();
        static partial void RegisterDrivers();
        static partial void RegisterApps();
        static partial void RegisterDev();
        static partial void RegisterProjects();
        static partial void RegisterManage();

        // catalog.txt 한 줄 → 항목 (④ 의 묶음, '개발 환경' 은 ⑤)
        public static Item AddCatalogItem(CatalogEntry e)
        {
            var grp = e.Group == "개발 환경" ? G5 : G4 + "/" + e.Group;
            var kv = e.How.Split(new[] { ':' }, 2);
            var kind = kv[0]; var spec = kv.Length > 1 ? kv[1].Trim() : "";
            Func<bool> chk; Action ins; string k = "";
            switch (kind)
            {
                case "winget":
                    {
                        var p = spec.Split(new[] { '@' }, 2); var id = p[0]; var ver = p.Length > 1 ? p[1] : null;
                        chk = () => Winget.Has(id, ver) || (ver == null && AppKit.TestHint(e.Hint, e.Name));   // 버전 고정은 버전까지 맞아야 한다
                        ins = () => Winget.Install(id, ver, null); k = "winget"; break;
                    }
                case "msstore": chk = () => Winget.Has(spec) || AppKit.TestHint(e.Hint, e.Name); ins = () => Winget.Install(spec, null, "msstore"); k = "winget"; break;
                case "usb":
                    chk = spec.EndsWith(".zip", StringComparison.OrdinalIgnoreCase) ? (Func<bool>)(() => Directory.Exists(Path.Combine(@"C:\Tools", e.Name))) : () => Arp.Has(e.Name);
                    ins = () => AppKit.InstallUsbTool(e.Name, spec); k = "usb"; break;
                case "url": chk = () => AppKit.TestHint(e.Hint, e.Name); ins = () => AppKit.InstallUrlApp(spec, e.Name); break;
                case "latest": chk = () => AppKit.TestHint(e.Hint, e.Name); ins = () => AppKit.InstallLatestApp(spec, e.Name); break;
                default: return null;
            }
            return Add(grp, "app:" + e.Name, e.Name, chk, ins, off: !e.On, kind: k);
        }
    }
}
