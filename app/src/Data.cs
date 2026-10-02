using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;

namespace PcSetup
{
    public sealed class CatalogEntry
    {
        public string Group, Name, How, Hint = "";
        public bool On = true;
        public string Line;   // 원래 줄 (앱 추가가 쓴다)
    }
    public sealed class ProjectEntry { public string Folder, Url, Link; }

    // 고정 값 — 버전을 올릴 때는 여기만 (예전 setup.ps1 맨 위와 같다)
    public static class Conf
    {
        public static readonly Dictionary<string, string> V = new Dictionary<string, string>
        {
            { "Git", "2.55.0.windows.5" }, { "GitFile", "Git-2.55.0.5-64-bit.exe" },
            { "Node", "24.21.0" }, { "Python", "3.13.15" }, { "Gh", "2.101.0" }, { "Pwsh", "7.6.6" },
            { "Terminal", "1.24.11911.0" }, { "Sunshine", "v2026.914.233613" }, { "Clasp", "3.3.0" }, { "Firebase", "15.24.0" },
        };
        public const string GitName = "Hyunhyo Kim";
        public const string GitEmail = "devkhh002@gmail.com";
        public const string DevRoot = @"C:\dev";
        public const string ClaudeModel = "opus[1m]";                       // Claude Code 기본 모델 — 별칭이라 새 Opus 가 나오면 따라간다 (빼려면 "")
        public static readonly string[] OldClaudeModels = { "claude-opus-5-5[1m]" };   // 예전에 이 설치가 넣던 값 — 이것만 새 기본값으로 바꾼다
        public static readonly string[] UpgradeSkip = { "Google.ChromeRemoteDesktopHost", "LizardByte.Sunshine", "Tailscale.Tailscale" };   // 모두 최신으로 에서 빼는 원격 호스트(올리는 동안 원격이 끊긴다)
        public static readonly string[] UpgradeFixed = { "Git.Git", "OpenJS.NodeJS*", "Python.Python.3.13", "Python.Launcher", "GitHub.cli", "Microsoft.PowerShell", "Microsoft.WindowsTerminal" };   // ⑤ 개발 환경 — V 로 고정
        public static readonly string[] UpgradeOwn = { "Daum.PotPlayer" };   // catalog 에서 url: 로 까는 앱의 winget 아이디 — winget 밖 단계에서 올린다
    }

    // 지금 상태
    public static class State
    {
        public static bool Online;
        public static string Usb;            // USB 의 PC설치 폴더 (없으면 null)
        public static string CatalogFrom = "";   // "GitHub" / "프로그램 안"
    }

    // 앱 목록(catalog.txt)·프로젝트 목록(projects.txt) — GitHub 최신을 받고, 못 받으면 프로그램에 든 판
    public static class Data
    {
        public static List<CatalogEntry> Catalog = new List<CatalogEntry>();
        public static List<ProjectEntry> Projects = new List<ProjectEntry>();

        public static void Load()
        {
            string cat = null, proj = null;
            if (State.Online) { Net.TryGetString(Net.Raw + "catalog.txt", out cat, 10); Net.TryGetString(Net.Raw + "projects.txt", out proj, 10); }
            State.CatalogFrom = cat != null ? "GitHub" : "프로그램 안";
            Catalog = ParseCatalog(cat ?? Resource("catalog.txt"));
            Projects = ParseProjects(proj ?? Resource("projects.txt"));
        }

        public static List<CatalogEntry> ParseCatalog(string text)
        {
            var list = new List<CatalogEntry>();
            foreach (var raw in (text ?? "").Split('\n'))
            {
                var line = raw.TrimEnd('\r');
                if (line.Trim().Length == 0 || line.TrimStart().StartsWith("#")) continue;
                var c = line.Split('|').Select(x => x.Trim()).ToArray();
                if (c.Length < 3) continue;
                list.Add(new CatalogEntry { Group = c[0], Name = c[1], How = c[2], On = c.Length < 4 || c[3] != "off", Hint = c.Length >= 5 ? c[4] : "", Line = line });
            }
            return list;
        }
        public static List<ProjectEntry> ParseProjects(string text)
        {
            var list = new List<ProjectEntry>();
            foreach (var raw in (text ?? "").Split('\n'))
            {
                var line = raw.TrimEnd('\r');
                if (line.Trim().Length == 0 || line.TrimStart().StartsWith("#")) continue;
                var c = line.Split('|').Select(x => x.Trim()).ToArray();
                if (c.Length < 3) continue;
                list.Add(new ProjectEntry { Folder = c[0], Url = c[1], Link = c[2] });
            }
            return list;
        }

        // 프로그램에 든 파일 (catalog.txt·projects.txt·hangul.ahk·trafficmonitor.config.ini·claude/…)
        public static byte[] ResourceBytes(string name)
        {
            using (var s = typeof(Data).Assembly.GetManifestResourceStream(name))
            {
                if (s == null) throw new FileNotFoundException("프로그램 안에 없는 파일: " + name);
                using (var ms = new MemoryStream()) { s.CopyTo(ms); return ms.ToArray(); }
            }
        }
        public static string Resource(string name)
        {
            var b = ResourceBytes(name);
            int skip = b.Length >= 3 && b[0] == 0xEF && b[1] == 0xBB && b[2] == 0xBF ? 3 : 0;
            return Encoding.UTF8.GetString(b, skip, b.Length - skip);
        }
        public static IEnumerable<string> ResourceNames(string prefix) { return typeof(Data).Assembly.GetManifestResourceNames().Where(n => n.StartsWith(prefix, StringComparison.Ordinal)); }
    }
}
