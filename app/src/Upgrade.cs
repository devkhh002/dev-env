using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;

namespace PcSetup
{
    // ── 모두 최신으로 ─────────────────────────────────────────────
    // winget 으로 올릴 수 있는 앱을 하나씩 올린다. 버전 고정(catalog 의 @버전)과 Conf.UpgradeSkip(원격 호스트)은 건드리지 않는다.
    // url·latest 앱(winget 밖)은 공식 최신 설치본으로 덮어 깐다 — 이미 깐 것만.
    public static partial class Hooks
    {
        static partial void RunUpgradeImpl(ref int done, ref int fail) { Upgrader.Run(ref done, ref fail); }
    }

    // 실제 일 — Hooks 는 여러 파일이 나눠 쓰는 partial 이라 도우미 이름이 겹치지 않게 따로 둔다
    static class Upgrader
    {
        const int WgNoUpdate = -1978335189;   // 0x8A15002B 올릴 것 없음
        const int WgNoMatch = -1978335212;    // 0x8A150014 맞는 패키지 없음
        const RegexOptions Ci = RegexOptions.IgnoreCase;   // PowerShell -match 처럼 대소문자 무시

        public static void Run(ref int done, ref int fail)
        {
            Env.RefreshPath();
            if (!Winget.Alive(true)) Log.Say("   winget 이 없어 winget 앱은 건너뜁니다 — ④ 의 winget 항목으로 먼저 설치", Tone.Warn);
            else UpgradeWinget(ref done, ref fail);
            UpgradeOutside(ref done, ref fail);
        }

        sealed class UpTodo { public string Name, Id, Cur, New; }

        static void UpgradeWinget(ref int done, ref int fail)
        {
            var wg = Winget.Exe;
            // 버전 고정은 winget 핀으로도 묶는다(핀 없이 예전에 깐 PC 가 있다) — winget 이 스스로도 건너뛰게
            var pins = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (var e in Data.Catalog)
            {
                var m = Regex.Match(e.How ?? "", "^winget:([^@]+)@(.+)$", Ci);
                if (!m.Success) continue;
                pins.Add(m.Groups[1].Value);
                Proc.Run(wg, Proc.Args("pin", "add", "--id", m.Groups[1].Value, "--version", m.Groups[2].Value, "--force", "--accept-source-agreements", "--disable-interactivity"), 60);
            }
            // url·latest 로 까는 앱은 아래에서 따로 — winget 목록에 같은 앱이 보여도 넘긴다(HWiNFO: winget 주소가 404)
            var own = new List<string>();
            foreach (var e in Data.Catalog)
            {
                if (!Regex.IsMatch(e.How ?? "", "^(url|latest):", Ci)) continue;
                var m = Regex.Match(e.Hint ?? "", "^arp[:=](.+)$", Ci);
                if (m.Success) own.Add(m.Groups[1].Value);
            }

            Log.Say("   새 판이 있는 앱을 찾습니다 (1~2분)");
            var r = Proc.Run(wg, Proc.Args("upgrade", "--source", "winget", "--accept-source-agreements", "--disable-interactivity"), 180);
            bool ran = r.Started && !r.TimedOut;   // 확인을 실제로 했나 — 아니면 '모두 최신' 이라 하지 않는다
            if (!ran) { Log.Say("   winget 이 3분 안에 답하지 않아 winget 앱은 건너뜁니다", Tone.Warn); fail++; }
            var rows = ran ? Winget.ParseTable(r.Out, true) : new List<string[]>();

            Dictionary<string, string> map = null;
            var todo = new List<UpTodo>();
            foreach (var row in rows)
            {
                if (row.Length < 4 || string.IsNullOrEmpty(row[1])) continue;
                string name = row[0], id = row[1], cur = row[2], nw = row[3];
                // 칸이 좁아 아이디가 '…' 로 잘렸으면 설치 목록(winget export)에서 앞부분이 같은 것 하나를 찾는다
                if (id.EndsWith("…", StringComparison.Ordinal))
                {
                    if (map == null) map = Winget.Map(true);
                    var head = id.TrimEnd('…');
                    var hit = map.Keys.Where(k => k.StartsWith(head, StringComparison.Ordinal)).ToList();
                    if (hit.Count != 1) { Log.Say("   아이디가 잘려 건너뜀: " + name + " (" + id + ")", Tone.Dim); continue; }
                    id = hit[0];
                }
                if (Conf.UpgradeSkip.Contains(id, StringComparer.OrdinalIgnoreCase)) { Log.Say("   건너뜀(원격 접속이 끊긴다): " + name, Tone.Dim); continue; }
                if (pins.Contains(id) || Conf.UpgradeFixed.Any(p => Like(id, p))) { Log.Say("   건너뜀(버전 고정): " + name + " " + cur, Tone.Dim); continue; }
                if (Conf.UpgradeOwn.Contains(id, StringComparer.OrdinalIgnoreCase) || own.Any(o => Like(name, "*" + o + "*"))) continue;
                todo.Add(new UpTodo { Name = name, Id = id, Cur = cur, New = nw });
            }
            // 표가 없는데 winget 이 실패 코드로 끝났으면 '모두 최신' 이 아니라 확인을 못 한 것 (0 · 0x8A150014 · 0x8A15002B 는 '올릴 것 없음')
            if (ran && rows.Count == 0 && r.Code != 0 && r.Code != WgNoMatch && r.Code != WgNoUpdate)
            {
                Log.Say(string.Format("   winget 이 새 판 목록을 주지 못했습니다(0x{0:X8}) — winget 앱은 건너뜁니다", r.Code), Tone.Warn); fail++;
            }
            else if (ran && todo.Count == 0) Log.Say("   winget 앱은 모두 최신입니다", Tone.Ok);

            int n = 0;
            foreach (var t in todo)
            {
                n++; Log.Say("[" + n + "/" + todo.Count + "] " + t.Name + "  " + t.Cur + " → " + t.New, Tone.Info);
                var u = Proc.RunLogged(wg, Proc.Args("upgrade", "--id", t.Id, "--exact", "--silent", "--accept-package-agreements", "--accept-source-agreements", "--disable-interactivity"), 3600);
                if (!u.TimedOut && (u.Code == 0 || u.Code == WgNoUpdate)) { Log.Say("   완료", Tone.Ok); done++; }
                else
                {
                    if (!u.Started && u.Err.Length > 0) Log.Say("   " + u.Err.Trim(), Tone.Warn);
                    Log.Say("   실패 " + (u.TimedOut ? "시간 초과" : string.Format("0x{0:X8}", u.Code)) + " — 위의 winget 메시지 참고", Tone.Warn); fail++;
                }
            }
        }

        // winget 밖(url:·latest:) — 이 PC 에 깐 것만 공식 최신 설치본으로 다시 깐다
        static void UpgradeOutside(ref int done, ref int fail)
        {
            foreach (var e in Data.Catalog.Where(x => Regex.IsMatch(x.How ?? "", "^(url|latest):", Ci)).ToList())
            {
                bool has;
                try { has = AppKit.TestHint(e.Hint, e.Name); } catch { has = false; }
                if (!has) continue;   // 이 PC 에 깐 것만
                Log.Say("[winget 밖] " + e.Name, Tone.Info);
                try
                {
                    int c = e.How.IndexOf(':');
                    string kind = e.How.Substring(0, c), spec = e.How.Substring(c + 1);
                    if (kind.Equals("latest", StringComparison.OrdinalIgnoreCase))
                    {
                        var parts = Regex.Split(spec.Trim(), @"\s+");
                        string page = parts[0], pattern = parts.Length > 1 ? string.Join(" ", parts.Skip(1)) : "*";
                        var url = AppKit.FindLatestLink(page, pattern);
                        if (url == null) throw new Exception("공식 페이지에서 '" + pattern + "' 파일을 찾지 못했습니다: " + page);
                        // 파일 이름의 숫자에 지금 판 번호(제어판 버전에서 숫자만)가 들어 있으면 이미 최신 — 예: hwi_852x.exe ↔ 8.52
                        string dv = null;
                        var hm = Regex.Match(e.Hint ?? "", "^arp[:=](.+)$", Ci);
                        if (hm.Success) dv = Regex.Replace(Arp.Version(hm.Groups[1].Value) ?? "", @"\D", "");
                        var fileDigits = Regex.Replace(Path.GetFileName(new Uri(url).AbsolutePath), @"\D", "");
                        if (!string.IsNullOrEmpty(dv) && fileDigits.Contains(dv)) { Log.Say("   이미 최신", Tone.Dim); continue; }
                        Log.Say("   가장 새 파일: " + url);
                        AppKit.InstallUrlApp(url, e.Name);
                    }
                    else AppKit.InstallUrlApp(spec, e.Name);
                    Log.Say("   완료", Tone.Ok); done++;
                }
                catch (Exception ex) { Log.Say("   실패: " + ex.GetBaseException().Message, Tone.Error); fail++; }
            }
        }

        // PowerShell -like 와 같게 — * · ? 와일드카드, 대소문자 무시
        static bool Like(string s, string pattern)
        {
            var rx = "^" + Regex.Escape(pattern ?? "").Replace("\\*", ".*").Replace("\\?", ".") + "$";
            return Regex.IsMatch(s ?? "", rx, RegexOptions.IgnoreCase | RegexOptions.Singleline);
        }
    }
}
