using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;

namespace PcSetup
{
    // winget — 앱을 늘 최신판으로 까는 도구
    public static partial class Winget
    {
        public static string Exe
        {
            get
            {
                var w = Env.Which("winget.exe");
                if (w != null) return w;
                var alias = Path.Combine(Env.LocalAppData, @"Microsoft\WindowsApps\winget.exe");
                return File.Exists(alias) ? alias : null;
            }
        }

        // 살아 있는가 — 20초 안에 버전을 말하고 1.6 이상(핀·--disable-interactivity). 낡거나 반쯤 깔린 winget 은 '없음' 으로 보고 다시 깔게 한다
        static bool? _alive;
        public static bool Alive(bool refresh = false)
        {
            if (_alive.HasValue && !refresh) return _alive.Value;
            var exe = Exe;
            if (exe == null) { _alive = false; return false; }
            var r = Proc.Run(exe, "--version", 20);
            var m = Regex.Match(r.Out ?? "", @"v?(\d+)\.(\d+)");
            _alive = r.Code == 0 && m.Success && new Version(int.Parse(m.Groups[1].Value), int.Parse(m.Groups[2].Value)) >= new Version(1, 6);
            return _alive.Value;
        }
        public static void Forget() { _alive = null; _map = null; }

        // 설치된 winget 앱 목록(아이디 → 버전) — winget export 한 번으로 (앱마다 물으면 느리다). 처음엔 목록을 받느라 2분까지
        static Dictionary<string, string> _map;
        public static Dictionary<string, string> Map(bool refresh = false)
        {
            if (_map != null && !refresh) return _map;
            var map = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            if (!Alive()) return _map = map;
            var f = Path.Combine(Net.Cache, "winget-export.json");
            try { File.Delete(f); } catch { }
            var r = Proc.Run(Exe, Proc.Args("export", "-o", f, "--source", "winget", "--include-versions", "--accept-source-agreements", "--disable-interactivity"), 120);
            if (r.TimedOut) Log.Say("   winget 목록을 2분 안에 못 읽어 건너뜁니다 — 앱 상태는 제어판 기준으로 봅니다", Tone.Warn);
            try
            {
                var j = Net.Json(File.ReadAllText(f, Encoding.UTF8)) as Dictionary<string, object>;
                foreach (Dictionary<string, object> src in (object[])j["Sources"])
                    foreach (Dictionary<string, object> p in (object[])src["Packages"])
                        map[(string)p["PackageIdentifier"]] = p.ContainsKey("Version") ? Convert.ToString(p["Version"]) : "";
            }
            catch { }
            return _map = map;
        }
        public static bool Has(string id, string ver = null)
        {
            string v; if (!Map().TryGetValue(id, out v)) return false;
            return string.IsNullOrEmpty(ver) || v == ver;
        }
        public static void Remember(string id, string ver) { Map()[id] = string.IsNullOrEmpty(ver) ? "installed" : ver; }

        // 앱 설치 — 성공 · 올릴 것 없음 · 이미 설치됨 이면 true. 버전 고정(@버전)은 핀으로 묶는다(모두 최신으로 에서도 안 올라가게)
        public static bool Install(string id, string ver, string source)
        {
            if (!Alive()) InstallSelf();
            if (!Alive(true)) { Log.Say("   winget 이 없어 설치하지 못했습니다", Tone.Warn); return false; }
            // 고정 버전을 바꾸면 예전 버전의 핀이 새 버전 설치를 막는다 — 먼저 풀고, 깐 뒤 새 버전으로 다시 묶는다
            if (!string.IsNullOrEmpty(ver)) Proc.Run(Exe, Proc.Args("pin", "remove", "--id", id, "--exact", "--accept-source-agreements", "--disable-interactivity"), 60);
            var a = new List<string> { "install", "--id", id, "--exact", "--silent", "--accept-package-agreements", "--accept-source-agreements", "--disable-interactivity" };
            if (!string.IsNullOrEmpty(ver)) { a.Add("--version"); a.Add(ver); }
            if (!string.IsNullOrEmpty(source)) { a.Add("--source"); a.Add(source); }
            var r = Proc.RunLogged(Exe, Proc.Args(a.ToArray()), 3600);
            if (r.Code == 0 || r.Code == -1978335189 || r.Code == -1978335135)   // 성공 · 올릴 것 없음 · 이미 설치됨
            {
                if (!string.IsNullOrEmpty(ver)) Proc.Run(Exe, Proc.Args("pin", "add", "--id", id, "--version", ver, "--force", "--accept-source-agreements", "--disable-interactivity"), 60);
                Remember(id, ver);
                return true;
            }
            var hex = r.TimedOut ? "시간 초과" : "0x" + r.Code.ToString("X8");
            string why;
            switch (hex)
            {
                case "0x80190194": why = "다운로드 주소가 없어졌다(404) — winget 목록이 아직 안 고쳐졌다. 며칠 뒤 다시 하거나 catalog.txt 에서 공식 주소(url:·latest:)로 바꾼다"; break;
                case "0x8A150014": why = "winget 에서 그 아이디를 찾지 못했다 — catalog.txt 의 아이디 확인 (winget search 이름)"; break;
                default: why = "위의 winget 메시지 참고"; break;
            }
            Log.Say("   winget 실패 " + hex + " — " + why, Tone.Warn);
            return false;
        }

        // winget 자신이 없을 때(LTSC 등) 설치 — Items 쪽에서 구현 (GitHub 릴리스의 앱 설치 관리자 + 의존 패키지)
        static partial void InstallSelfImpl();
        public static void InstallSelf() { InstallSelfImpl(); Forget(); }

        // ── winget 표(검색 결과·올릴 것 목록)를 줄마다 칸 배열로 ─────────────────────────
        // 한글은 화면에서 두 칸이라 글자 수가 아닌 화면 칸으로 열을 나눈다.
        // 열 시작 = 머리줄 낱말이 시작하는 칸 중 모든 줄에서 바로 앞 칸이 비어 있는 곳 (한글 머리줄 '장치 ID' 처럼 띄어 쓴 열 이름도 된다)
        public static List<string> Cells(string s)
        {
            var l = new List<string>(s.Length + 8);
            foreach (var ch in s)
            {
                int n = ch; l.Add(ch.ToString());
                if ((n >= 0x1100 && n <= 0x115F) || (n >= 0x2E80 && n <= 0xA4CF) || (n >= 0xAC00 && n <= 0xD7A3) || (n >= 0xF900 && n <= 0xFAFF) || (n >= 0xFF00 && n <= 0xFF60) || (n >= 0xFFE0 && n <= 0xFFE6)) l.Add("");
            }
            return l;
        }
        static bool Blank(string c) { return c == " "; }
        static bool Ink(string c) { return c.Length > 0 && !char.IsWhiteSpace(c[0]); }

        // mainOnly: 첫 표가 '명시적 대상 지정이 필요한' 패키지 표면(위에 그 안내 문장) 올릴 것이 아니다 — 본 표가 비어 있을 때 그 표만 나온다
        public static List<string[]> ParseTable(string text, bool mainOnly = false)
        {
            var rowsOut = new List<string[]>();
            var lines = (text ?? "").Split('\n').Select(x => { var p = x.TrimEnd('\r').Split('\r'); return p[p.Length - 1].TrimEnd(); }).ToList();   // 진행 표시가 \r 로 덮어쓴 줄은 마지막 것만
            int dash = -1;
            for (int i = 1; i < lines.Count; i++) if (Regex.IsMatch(lines[i], "^-{10,}$")) { dash = i; break; }
            if (dash < 1) return rowsOut;
            if (mainOnly) for (int k = dash - 2; k >= Math.Max(0, dash - 4); k--) if (Regex.IsMatch(lines[k], "explicit|명시적")) return rowsOut;
            var head = Cells(lines[dash - 1]);
            if (dash + 1 >= lines.Count || lines[dash + 1].Length == 0) return rowsOut;
            var first = Cells(lines[dash + 1]);
            var tok = new List<int>();
            for (int p = 0; p < head.Count; p++)
                if (Ink(head[p]) && (p == 0 || (Blank(head[p - 1]) && (p - 1 >= first.Count || Blank(first[p - 1]))))) tok.Add(p);
            if (tok.Count < 3) return rowsOut;
            var rows = new List<List<string>>();
            for (int i = dash + 1; i < lines.Count && lines[i].Length > 0; i++)
            {
                if (Regex.IsMatch(lines[i], @"^\d+ (upgrades? available|업그레이드를 사용할 수 있습니다)")) break;   // 표 밑 개수 줄 — 한글 줄은 칸이 우연히 맞으면 표 줄처럼 보인다
                var c = Cells(lines[i]);
                bool ok = true;
                foreach (var p in new[] { tok[1], tok[2] }) if (c.Count <= p || !Blank(c[p - 1]) || !Ink(c[p])) ok = false;
                if (!ok) break;   // 표 밑의 '8 업그레이드를…'·'1 패키지에 … 핀이 있습니다' 같은 줄에서 표가 끝난다
                rows.Add(c);
            }
            var starts = new List<int> { 0 };
            for (int p = 1; p < head.Count; p++)
            {
                if (!Ink(head[p]) || !Blank(head[p - 1])) continue;
                bool ok = true; foreach (var r in rows) if (p - 1 < r.Count && !Blank(r[p - 1])) { ok = false; break; }
                if (ok) starts.Add(p);
            }
            foreach (var r in rows)
            {
                var cols = new string[starts.Count];
                for (int k = 0; k < starts.Count; k++)
                {
                    int a = starts[k], b = k + 1 < starts.Count ? Math.Min(starts[k + 1], r.Count) : r.Count;
                    cols[k] = a >= b ? "" : string.Concat(r.Skip(a).Take(b - a)).Trim();
                }
                rowsOut.Add(cols);
            }
            return rowsOut;
        }
    }
}
