using System;
using System.Diagnostics;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Text.RegularExpressions;

namespace PcSetup
{
    // catalog 의 url:·latest:·usb: 앱 설치와 '설치됐나' 확인
    public static class AppKit
    {
        // 링크에서 받아 바로 설치(늘 최신판) — 조용히 설치하는 옵션은 설치본 종류에 맞춘다: Inno Setup(HWiNFO 등) /VERYSILENT · 그 밖(NSIS: PotPlayer 등) /S
        // 서명이 아예 없거나 깨진 파일은 실행하지 않는다(받는 곳에서 바꿔치기됐을 때)
        public static void InstallUrlApp(string url, string name)
        {
            var leaf = url.Split(new[] { '/', '?', '#' }, StringSplitOptions.RemoveEmptyEntries).LastOrDefault() ?? "";
            if (!Regex.IsMatch(leaf, @"\.(exe|msi)$", RegexOptions.IgnoreCase)) leaf = SafeName(name) + ".exe";
            var p = Net.Download(url, leaf);
            var sig = Trust.Check(p);
            if (sig == Trust.State.NotSigned || sig == Trust.State.HashMismatch) { File.Delete(p); throw new Exception("서명이 없거나 깨진 설치 파일이라 실행하지 않았습니다(" + sig + "): " + url); }
            if (leaf.EndsWith(".msi", StringComparison.OrdinalIgnoreCase)) { Proc.RunWait(Path.Combine(Env.System32, "msiexec.exe"), "/i " + Proc.Quote(p) + " /qn /norestart"); return; }
            var inno = (FileVersionInfo.GetVersionInfo(p).Comments ?? "").IndexOf("Inno Setup", StringComparison.OrdinalIgnoreCase) >= 0;
            Proc.RunWait(p, inno ? "/VERYSILENT /SUPPRESSMSGBOXES /NORESTART /SP-" : "/S");
        }

        // 공식 페이지에서 가장 새 파일 — 파일 이름이 패턴(*)에 맞는 링크 중 번호가 가장 큰 것
        // (새 판이 나오면 예전 파일을 바로 지우는 곳은 winget 목록이 따라올 때까지 404 가 난다: HWiNFO)
        public static string FindLatestLink(string page, string pattern)
        {
            var html = Net.GetString(page, 30);
            var rx = new Regex("^" + Regex.Escape(pattern).Replace("\\*", ".*").Replace("\\?", ".") + "$", RegexOptions.IgnoreCase);
            var baseUri = new Uri(page);
            var links = Regex.Matches(html, "href\\s*=\\s*[\"']([^\"'>]+)", RegexOptions.IgnoreCase).Cast<Match>()
                .Select(m => { try { return new Uri(baseUri, System.Net.WebUtility.HtmlDecode(m.Groups[1].Value)); } catch { return null; } })
                .Where(u => u != null && rx.IsMatch(Path.GetFileName(u.AbsolutePath)))
                .Select(u => u.AbsoluteUri).ToList();
            return links.OrderBy(u => Regex.Replace(u, @"\d+", d => d.Value.PadLeft(12, '0')), StringComparer.Ordinal).LastOrDefault();
        }
        public static void InstallLatestApp(string spec, string name)
        {
            var parts = Regex.Split(spec.Trim(), @"\s+", RegexOptions.None);
            var page = parts[0]; var pattern = parts.Length > 1 ? parts[1] : "*";
            var url = FindLatestLink(page, pattern);
            if (url == null) throw new Exception("공식 페이지에서 '" + pattern + "' 파일을 찾지 못했습니다 — 페이지가 바뀌었으면 catalog.txt 를 고친다: " + page);
            Log.Say("   가장 새 파일: " + url);
            InstallUrlApp(url, name);
        }

        // winget 밖에서 깐 앱은 winget 목록에 안 잡힐 수 있다(예: Chrome) — 제어판 이름·스토어 앱 이름·설치 파일 경로로 한 번 더 본다
        public static bool TestHint(string hint, string name)
        {
            hint = hint ?? "";
            if (hint.StartsWith("appx:")) return Appx.Has(hint.Substring(5));
            if (hint.StartsWith("file:")) return File.Exists(hint.Substring(5));
            if (hint.StartsWith("arp=")) return Arp.HasExact(hint.Substring(4));     // 이름이 정확히 같을 때만(32/64 구분)
            if (hint.StartsWith("arp:")) return Arp.Has(hint.Substring(4));
            return Arp.Has(name);
        }

        // USB 도구(catalog 의 usb:) — .zip 은 C:\Tools\이름 에 풀고, .exe·.msi 는 실행(설치 창은 사람이 넘긴다)
        public static void InstallUsbTool(string name, string file)
        {
            if (State.Usb == null) { Log.Say("   USB(PC설치 폴더)를 찾지 못했습니다", Tone.Warn); return; }
            var p = Path.Combine(State.Usb, "도구", file);
            if (!File.Exists(p)) { Log.Say("   USB 에 파일이 없습니다: " + p, Tone.Warn); return; }
            switch (Path.GetExtension(p).ToLowerInvariant())
            {
                case ".zip":
                    var dest = Path.Combine(@"C:\Tools", name);
                    if (Directory.Exists(dest)) Directory.Delete(dest, true);
                    ZipFile.ExtractToDirectory(p, dest);
                    break;
                case ".msi": Proc.RunWait(Path.Combine(Env.System32, "msiexec.exe"), "/i " + Proc.Quote(p), hidden: false); break;
                default: Proc.RunWait(p, "", hidden: false); break;
            }
        }

        public static string SafeName(string s) { return Regex.Replace(s, @"[\\/:*?""<>|\s]+", "_"); }
    }
}
