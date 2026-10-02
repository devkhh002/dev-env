using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;

namespace PcSetup
{
    // ⑤ 개발 환경 — 버전 고정(Conf.V)
    public static partial class Defs
    {
        static partial void RegisterDev()
        {
            var V = Conf.V;
            Add(G5, "git", "Git " + V["Git"] + " (Git Bash 포함)", () => File.Exists(@"C:\Program Files\Git\cmd\git.exe"), () =>
            {
                var p = Net.Download("https://github.com/git-for-windows/git/releases/download/v" + V["Git"] + "/" + V["GitFile"], V["GitFile"]);
                DevSetup.RunSetup(p, "/VERYSILENT /NORESTART /NOCANCEL /SP- /o:PathOption=Cmd /o:CRLFOption=CRLFCommitAsIs");
            });
            Add(G5, "node", "Node.js " + V["Node"], () => File.Exists(@"C:\Program Files\nodejs\node.exe"), () =>
                DevSetup.Msi(Net.Download("https://nodejs.org/dist/v" + V["Node"] + "/node-v" + V["Node"] + "-x64.msi", "node-" + V["Node"] + ".msi")));
            Add(G5, "python", "Python " + V["Python"], DevSetup.HasPython, () =>
            {
                var p = Net.Download("https://www.python.org/ftp/python/" + V["Python"] + "/python-" + V["Python"] + "-amd64.exe", "python-" + V["Python"] + ".exe");
                DevSetup.RunSetup(p, "/quiet InstallAllUsers=1 PrependPath=1 Include_test=0");
            });
            Add(G5, "gh", "GitHub CLI " + V["Gh"], () => File.Exists(@"C:\Program Files\GitHub CLI\gh.exe"), () =>
                DevSetup.Msi(Net.Download("https://github.com/cli/cli/releases/download/v" + V["Gh"] + "/gh_" + V["Gh"] + "_windows_amd64.msi", "gh-" + V["Gh"] + ".msi")));
            Add(G5, "pwsh", "PowerShell " + V["Pwsh"], () => File.Exists(@"C:\Program Files\PowerShell\7\pwsh.exe"), () =>
                DevSetup.Msi(Net.Download("https://github.com/PowerShell/PowerShell/releases/download/v" + V["Pwsh"] + "/PowerShell-" + V["Pwsh"] + "-win-x64.msi", "pwsh-" + V["Pwsh"] + ".msi"), "ADD_PATH=1 ENABLE_PSREMOTING=0 REGISTER_MANIFEST=1"));
            Add(G5, "terminal", "Windows Terminal " + V["Terminal"] + " (PowerShell을 탭으로 열기·Ctrl+C/V 복사·붙여넣기)",
                () => Appx.Has("Microsoft.WindowsTerminal") && DevSetup.TestWtKeys(), DevSetup.InstallTerminal);
            Add(G5, "npmtools", "clasp " + V["Clasp"] + " · firebase-tools " + V["Firebase"] + " (Node 필요)",
                () => { Env.RefreshPath(); return Env.Which("clasp.cmd") != null && Env.Which("firebase.cmd") != null; }, DevSetup.InstallNpmTools);
            Add(G5, "claude", "Claude Code", () => File.Exists(Path.Combine(Env.Home, @".local\bin\claude.exe")), DevSetup.InstallClaude);
            Add(G5, "claudeconfig", "Claude 설정 — 오케스트라 모드(/orchestra)·상태 표시줄·기본 모델 " + Conf.ClaudeModel + "(직접 고른 모델은 그대로)",
                DevSetup.TestClaudeConfig, DevSetup.InstallClaudeConfig);
            Add(G5, "gitconfig", "git 기본 설정(줄바꿈 유지·한글 파일명·이름)", DevSetup.TestGitConfig, DevSetup.InstallGitConfig);
            foreach (var e in Data.Catalog.Where(x => x.Group == "개발 환경")) AddCatalogItem(e);
        }
    }

    // ⑤ 의 설치·확인 본체
    static class DevSetup
    {
        static string Msiexec { get { return Path.Combine(Env.System32, "msiexec.exe"); } }

        // 설치 프로그램 실행 — 종료 코드가 0·3010(재부팅 필요)·1641 이 아니면 기록에 남긴다(된 것인지는 끝나고 다시 확인한다)
        public static void RunSetup(string exe, string args)
        {
            var code = Proc.RunWait(exe, args);
            if (code != 0 && code != 3010 && code != 1641 && code != -1) Log.Say("   " + Path.GetFileName(exe) + " 종료 코드 " + code, Tone.Warn);
        }
        public static void Msi(string p, string extra = null)
        {
            var code = Proc.RunWait(Msiexec, "/i " + Proc.Quote(p) + " /qn /norestart" + (string.IsNullOrEmpty(extra) ? "" : " " + extra));
            if (code != 0 && code != 3010 && code != 1641 && code != -1) Log.Say("   msiexec 종료 코드 " + code + " (" + Path.GetFileName(p) + ")", Tone.Warn);
        }

        public static bool HasPython()
        {
            var pf = @"C:\Program Files";
            return Directory.Exists(pf) && Directory.GetDirectories(pf, "Python3*").Any(d => File.Exists(Path.Combine(d, "python.exe")));
        }

        // ── Windows Terminal ─────────────────────────────────────────────
        // Windows Terminal 은 처음 뜰 때 만드는 설정에 Ctrl+C(선택한 글자 복사)·Ctrl+V(붙여넣기)를 넣는다.
        // 우리가 settings.json 을 먼저 만들면 그게 빠져 Ctrl+V 가 Claude Code 로 그냥 넘어간다(붙여넣기 안 됨) — 직접 넣는다
        static string WtSettings { get { return Path.Combine(Env.LocalAppData, @"Packages\Microsoft.WindowsTerminal_8wekyb3d8bbwe\LocalState\settings.json"); } }
        const string CopyId = "User.copy.644BA8F2", PasteId = "User.paste";

        public static bool TestWtKeys()
        {
            var f = WtSettings;
            return File.Exists(f) && Regex.IsMatch(File.ReadAllText(f, Encoding.UTF8), "\"keys\"\\s*:\\s*\"ctrl\\+v\"", RegexOptions.IgnoreCase);
        }

        public static void InstallTerminal()
        {
            var ver = Conf.V["Terminal"];
            if (!Appx.Has("Microsoft.WindowsTerminal"))
            {
                var bas = "https://github.com/microsoft/terminal/releases/download/v" + ver + "/Microsoft.WindowsTerminal_" + ver + "_8wekyb3d8bbwe.msixbundle";
                var bundle = Net.Download(bas, "wt-" + ver + ".msixbundle");
                var kit = Net.Download(bas + "_Windows10_PreinstallKit.zip", "wt-" + ver + "-kit.zip");
                var dir = Path.Combine(Net.Cache, "wtkit");
                if (Directory.Exists(dir)) Directory.Delete(dir, true);
                ZipFile.ExtractToDirectory(kit, dir);
                var deps = Directory.GetFiles(dir, "*.appx", SearchOption.AllDirectories).Where(x => Path.GetFileName(x).IndexOf("x64", StringComparison.OrdinalIgnoreCase) >= 0).ToList();
                Appx.Add(bundle, deps);
            }
            var k = @"HKEY_CURRENT_USER\Console\%%Startup";
            Reg.Set(k, "DelegationConsole", "{2EACA947-7F5F-4CFA-BA87-8F7FBEEFBE69}");
            Reg.Set(k, "DelegationTerminal", "{E12CFF52-A866-4C77-9A90-F570A7AA2C6B}");
            SetWtKeys(WtSettings);
        }

        // 읽지 못하는 파일(주석이 든 것 = WT 가 직접 만든 것이라 이미 들어 있다)은 그대로 둔다
        public static void SetWtKeys(string file)
        {
            Directory.CreateDirectory(Path.GetDirectoryName(file));
            JsonText.Obj s; string nl = "\r\n";
            var text = File.Exists(file) ? File.ReadAllText(file, Encoding.UTF8) : null;
            if (text != null && text.Trim().Length > 0)
            {
                try { s = JsonText.Parse(text) as JsonText.Obj; } catch { return; }
                if (s == null) return;
                if (text.IndexOf("\r\n", StringComparison.Ordinal) < 0 && text.IndexOf('\n') >= 0) nl = "\n";
            }
            else
            {
                s = new JsonText.Obj();
                s.Set("defaultProfile", "{574e775e-4f2a-5b96-ac1e-a2962a402336}");
                var prof = new JsonText.Obj(); prof.Set("defaults", new JsonText.Obj()); prof.Set("list", new List<object>());
                s.Set("profiles", prof);
            }
            var acts = JsonText.Items(s.Get("actions")).Where(a => Truthy(a) && !(a is JsonText.Obj && IsAny(((JsonText.Obj)a).Get("id"), CopyId, PasteId))).ToList();
            var copyCmd = new JsonText.Obj(); copyCmd.Set("action", "copy"); copyCmd.Set("singleLine", false);
            var a1 = new JsonText.Obj(); a1.Set("command", copyCmd); a1.Set("id", CopyId);
            var a2 = new JsonText.Obj(); a2.Set("command", "paste"); a2.Set("id", PasteId);
            acts.Add(a1); acts.Add(a2);
            var keys = JsonText.Items(s.Get("keybindings")).Where(b => Truthy(b) && !(b is JsonText.Obj && IsAny(((JsonText.Obj)b).Get("keys"), "ctrl+c", "ctrl+v"))).ToList();
            var k1 = new JsonText.Obj(); k1.Set("id", CopyId); k1.Set("keys", "ctrl+c");
            var k2 = new JsonText.Obj(); k2.Set("id", PasteId); k2.Set("keys", "ctrl+v");
            keys.Add(k1); keys.Add(k2);
            s.Set("actions", acts);
            s.Set("keybindings", keys);
            File.WriteAllText(file, JsonText.Write(s, nl), new UTF8Encoding(false));
        }
        static bool IsAny(object v, params string[] set) { var t = v as string; return t != null && set.Any(x => x.Equals(t, StringComparison.OrdinalIgnoreCase)); }
        // PowerShell 의 '$_ -and' 처럼 — null·false·빈 글자·0 은 버린다
        static bool Truthy(object v)
        {
            if (v == null) return false;
            if (v is bool) return (bool)v;
            if (v is string) return ((string)v).Length > 0;
            if (v is JsonText.Num) { double d; return !double.TryParse(((JsonText.Num)v).Raw, NumberStyles.Float, CultureInfo.InvariantCulture, out d) || d != 0; }
            return true;
        }

        // ── clasp · firebase-tools ───────────────────────────────────────
        public static void InstallNpmTools()
        {
            Env.RefreshPath();
            var npm = Env.Which("npm.cmd");
            if (npm == null) throw new Exception("npm 이 없습니다 — ⑤ 의 Node.js 를 먼저 설치하세요");
            var r = Proc.Run(npm, Proc.Args("i", "-g", "@google/clasp@" + Conf.V["Clasp"], "firebase-tools@" + Conf.V["Firebase"]), 1800);
            if (!r.Started) throw new Exception(r.Err);
            var lines = r.Text.Split(new[] { '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries).Where(x => x.Trim().Length > 0).ToList();
            foreach (var l in lines.Skip(Math.Max(0, lines.Count - (r.Code == 0 ? 2 : 10)))) Log.Raw(l);
            if (r.TimedOut) throw new Exception("npm 이 30분 안에 끝나지 않았습니다");
            if (r.Code != 0) Log.Say("   npm 종료 코드 " + r.Code, Tone.Warn);
        }

        // ── Claude Code — 공식 CMD 설치 스크립트(install.cmd) ─────────────
        public static void InstallClaude()
        {
            var f = Path.Combine(Net.Cache, "claude-install.cmd");
            try { File.Delete(f); } catch { }   // 늘 새로 받는다(설치 스크립트가 바뀌어도 따라가게)
            f = Net.Download("https://claude.ai/install.cmd", "claude-install.cmd");
            // 경로는 늘 따옴표로 — 빈칸이 없어도 & ( ) 같은 글자가 든 사용자 폴더에서 cmd 가 끊어 읽지 않게 (/s 는 바깥 따옴표만 벗긴다)
            var r = Proc.RunLogged(Path.Combine(Env.System32, "cmd.exe"), "/d /s /c \"chcp 65001 >nul & \"" + f + "\"\"", 1800, Encoding.UTF8);
            try { File.Delete(f); } catch { }
            if (!r.Started) throw new Exception(r.Err);
            if (r.TimedOut) throw new Exception("Claude Code 설치가 30분 안에 끝나지 않았습니다");
            if (r.Code != 0) throw new Exception("Claude Code 설치 스크립트가 실패했습니다(종료 코드 " + r.Code + ") — 위 기록 참고");
            Env.AddUserPath(Path.Combine(Env.Home, @".local\bin"));
            Env.AddUserPath(Path.Combine(Env.AppData, "npm"));
        }

        // ── Claude 설정 ─────────────────────────────────────────────────
        static string ClaudeDir { get { return Path.Combine(Env.Home, ".claude"); } }

        public static bool TestClaudeConfig()
        {
            var c = ClaudeDir;
            JsonText.Obj m = null;
            try { m = JsonText.Parse(File.ReadAllText(Path.Combine(c, "settings.json"), Encoding.UTF8)) as JsonText.Obj; } catch { }
            if (!File.Exists(Path.Combine(c, @"commands\orchestra.md")) || !File.Exists(Path.Combine(c, "statusline.sh")) || m == null) return false;
            var sl = m.Get("statusLine") as JsonText.Obj;
            var cmd = sl == null ? null : sl.Get("command") as string;
            if (cmd == null || !Regex.IsMatch(cmd, @"statusline\.sh", RegexOptions.IgnoreCase)) return false;
            return string.IsNullOrEmpty(Conf.ClaudeModel) || (Truthy(m.Get("model")) && !IsAny(m.Get("model"), Conf.OldClaudeModels));
        }

        public static void InstallClaudeConfig()
        {
            var c = ClaudeDir;
            foreach (var d in new[] { "agents", "fable", "commands" }) Directory.CreateDirectory(Path.Combine(c, d));
            // 프로그램에 든 claude/… 를 같은 하위 폴더로 (CLAUDE.md·statusline.sh·agents·commands·fable)
            foreach (var n in Data.ResourceNames("claude/"))
            {
                var dest = Path.Combine(c, n.Substring("claude/".Length).Replace('/', '\\'));
                Directory.CreateDirectory(Path.GetDirectoryName(dest));
                File.WriteAllBytes(dest, Data.ResourceBytes(n));
            }
            MergeClaudeSettings(Path.Combine(c, "settings.json"));
        }

        // settings.json 은 덮지 않고 statusLine 을 넣는다. 기본 모델은 비어 있거나 예전 기본값일 때만 넣는다(직접 고른 모델은 그대로).
        // UTF-8 로 읽고 쓴다 — 한글 경로가 든 권한 규칙이 깨지지 않게. 읽지 못하는 파일은 건드리지 않고 실패로 알린다
        public static void MergeClaudeSettings(string sf)
        {
            JsonText.Obj s; string nl = "\r\n";
            var text = File.Exists(sf) ? File.ReadAllText(sf, Encoding.UTF8) : null;
            if (text != null && text.Trim().Length > 0)
            {
                try { s = JsonText.Parse(text) as JsonText.Obj; } catch (Exception ex) { throw new Exception("settings.json 을 읽지 못해 그대로 두었습니다(" + ex.Message + ") — 직접 고치세요: " + sf); }
                if (s == null) throw new Exception("settings.json 이 { } 형식이 아니라 그대로 두었습니다: " + sf);
                if (text.IndexOf("\r\n", StringComparison.Ordinal) < 0 && text.IndexOf('\n') >= 0) nl = "\n";
            }
            else s = new JsonText.Obj();
            var sl = new JsonText.Obj(); sl.Set("type", "command"); sl.Set("command", "bash ~/.claude/statusline.sh");
            s.Set("statusLine", sl);
            if (!string.IsNullOrEmpty(Conf.ClaudeModel) && (!Truthy(s.Get("model")) || IsAny(s.Get("model"), Conf.OldClaudeModels))) s.Set("model", Conf.ClaudeModel);
            File.WriteAllText(sf, JsonText.Write(s, nl), new UTF8Encoding(false));
        }

        // ── git 기본 설정 ───────────────────────────────────────────────
        public static bool TestGitConfig()
        {
            var git = Git.Exe;
            if (git == null) return false;
            var r = Proc.Run(git, "config --global core.autocrlf", 30);
            return r.Code == 0 && r.Out.Trim().Equals("false", StringComparison.OrdinalIgnoreCase);
        }
        public static void InstallGitConfig()
        {
            var git = Git.Exe;
            if (git == null) throw new Exception("git 이 없습니다 — ⑤ 의 Git 을 먼저 설치하세요");
            var sets = new[] { new[] { "core.autocrlf", "false" }, new[] { "core.quotepath", "false" }, new[] { "init.defaultBranch", "main" }, new[] { "user.name", Conf.GitName }, new[] { "user.email", Conf.GitEmail } };
            foreach (var kv in sets)
            {
                var r = Proc.Run(git, Proc.Args("config", "--global", kv[0], kv[1]), 30);
                if (r.Code != 0) throw new Exception("git config " + kv[0] + " 실패: " + r.Text);
            }
        }
    }

    // 작은 JSON — 키 순서·숫자 모양을 그대로 두고, 한글을 \u 로 바꾸지 않고 2칸 들여쓰기로 쓴다.
    // 엄격하게 읽는다(주석·끝 쉼표가 있으면 예외) — 읽지 못한 파일은 덮어쓰지 않기 위해
    static class JsonText
    {
        public sealed class Num { public string Raw; public override string ToString() { return Raw; } }
        public sealed class Obj : List<KeyValuePair<string, object>>
        {
            public object Get(string key) { foreach (var kv in this) if (kv.Key == key) return kv.Value; return null; }
            public void Set(string key, object value)
            {
                int i = FindIndex(kv => kv.Key == key);   // 있던 자리에서 바꾼다(키 순서 유지)
                if (i < 0) { Add(new KeyValuePair<string, object>(key, value)); return; }
                this[i] = new KeyValuePair<string, object>(key, value);
                for (int j = Count - 1; j > i; j--) if (this[j].Key == key) RemoveAt(j);   // 같은 키가 또 있으면 지운다
            }
        }
        // 배열이면 그 항목들, 하나면 그것 하나, 없으면 빈 목록 (PowerShell 의 @(…) 처럼)
        public static List<object> Items(object v)
        {
            if (v == null) return new List<object>();
            var l = v as List<object>;
            return l != null ? new List<object>(l) : new List<object> { v };
        }

        public static object Parse(string text)
        {
            int i = 0;
            var v = Value(text, ref i);
            Ws(text, ref i);
            if (i < text.Length) throw Bad(text, i);
            return v;
        }
        static Exception Bad(string t, int i) { return new FormatException("JSON 형식 오류 (" + (i < t.Length ? "'" + t[i] + "' " : "끝 ") + "위치 " + i + ")"); }
        static void Ws(string t, ref int i) { while (i < t.Length && (t[i] == ' ' || t[i] == '\t' || t[i] == '\n' || t[i] == '\r' || t[i] == '\uFEFF')) i++; }
        static object Value(string t, ref int i)
        {
            Ws(t, ref i);
            if (i >= t.Length) throw Bad(t, i);
            char c = t[i];
            if (c == '{')
            {
                var o = new Obj(); i++; Ws(t, ref i);
                if (i < t.Length && t[i] == '}') { i++; return o; }
                while (true)
                {
                    Ws(t, ref i);
                    if (i >= t.Length || t[i] != '"') throw Bad(t, i);
                    var k = Str(t, ref i); Ws(t, ref i);
                    if (i >= t.Length || t[i] != ':') throw Bad(t, i);
                    i++;
                    o.Add(new KeyValuePair<string, object>(k, Value(t, ref i)));
                    Ws(t, ref i);
                    if (i < t.Length && t[i] == ',') { i++; continue; }
                    if (i < t.Length && t[i] == '}') { i++; return o; }
                    throw Bad(t, i);
                }
            }
            if (c == '[')
            {
                var a = new List<object>(); i++; Ws(t, ref i);
                if (i < t.Length && t[i] == ']') { i++; return a; }
                while (true)
                {
                    a.Add(Value(t, ref i)); Ws(t, ref i);
                    if (i < t.Length && t[i] == ',') { i++; continue; }
                    if (i < t.Length && t[i] == ']') { i++; return a; }
                    throw Bad(t, i);
                }
            }
            if (c == '"') return Str(t, ref i);
            if (Word(t, ref i, "true")) return true;
            if (Word(t, ref i, "false")) return false;
            if (Word(t, ref i, "null")) return null;
            var m = Regex.Match(t.Substring(i, Math.Min(64, t.Length - i)), @"^-?(0|[1-9][0-9]*)(\.[0-9]+)?([eE][+-]?[0-9]+)?");
            if (m.Success && m.Length > 0 && (i + m.Length >= t.Length || "0123456789.eE+-".IndexOf(t[i + m.Length]) < 0)) { i += m.Length; return new Num { Raw = m.Value }; }
            throw Bad(t, i);
        }
        static bool Word(string t, ref int i, string w) { if (string.CompareOrdinal(t, i, w, 0, w.Length) != 0) return false; i += w.Length; return true; }
        static string Str(string t, ref int i)
        {
            var sb = new StringBuilder(); i++;   // 여는 "
            while (true)
            {
                if (i >= t.Length) throw Bad(t, i);
                char c = t[i++];
                if (c == '"') return sb.ToString();
                if (c < 0x20) throw Bad(t, i - 1);
                if (c != '\\') { sb.Append(c); continue; }
                if (i >= t.Length) throw Bad(t, i);
                char e = t[i++];
                switch (e)
                {
                    case '"': sb.Append('"'); break;
                    case '\\': sb.Append('\\'); break;
                    case '/': sb.Append('/'); break;
                    case 'b': sb.Append('\b'); break;
                    case 'f': sb.Append('\f'); break;
                    case 'n': sb.Append('\n'); break;
                    case 'r': sb.Append('\r'); break;
                    case 't': sb.Append('\t'); break;
                    case 'u':
                        int code;
                        if (i + 4 > t.Length || !int.TryParse(t.Substring(i, 4), NumberStyles.AllowHexSpecifier, CultureInfo.InvariantCulture, out code)) throw Bad(t, i);
                        sb.Append((char)code); i += 4; break;
                    default: throw Bad(t, i - 1);
                }
            }
        }

        public static string Write(object v, string nl = "\n")
        {
            var sb = new StringBuilder();
            Emit(sb, v, 0, nl);
            return sb.ToString();
        }
        static void Emit(StringBuilder sb, object v, int depth, string nl)
        {
            var pad = new string(' ', (depth + 1) * 2); var end = new string(' ', depth * 2);
            if (v == null) { sb.Append("null"); return; }
            if (v is bool) { sb.Append((bool)v ? "true" : "false"); return; }
            if (v is string) { Quote(sb, (string)v); return; }
            if (v is Num) { sb.Append(((Num)v).Raw); return; }
            if (v is int || v is long || v is double || v is decimal) { sb.Append(Convert.ToString(v, CultureInfo.InvariantCulture)); return; }
            var o = v as Obj;
            if (o != null)
            {
                if (o.Count == 0) { sb.Append("{}"); return; }
                sb.Append('{');
                for (int k = 0; k < o.Count; k++)
                {
                    sb.Append(k == 0 ? "" : ",").Append(nl).Append(pad);
                    Quote(sb, o[k].Key); sb.Append(": ");
                    Emit(sb, o[k].Value, depth + 1, nl);
                }
                sb.Append(nl).Append(end).Append('}');
                return;
            }
            var a = v as System.Collections.IEnumerable;
            if (a != null)
            {
                var list = a.Cast<object>().ToList();
                if (list.Count == 0) { sb.Append("[]"); return; }
                sb.Append('[');
                for (int k = 0; k < list.Count; k++)
                {
                    sb.Append(k == 0 ? "" : ",").Append(nl).Append(pad);
                    Emit(sb, list[k], depth + 1, nl);
                }
                sb.Append(nl).Append(end).Append(']');
                return;
            }
            Quote(sb, v.ToString());
        }
        static void Quote(StringBuilder sb, string s)
        {
            sb.Append('"');
            for (int k = 0; k < s.Length; k++)
            {
                char c = s[k];
                switch (c)
                {
                    case '"': sb.Append("\\\""); break;
                    case '\\': sb.Append("\\\\"); break;
                    case '\b': sb.Append("\\b"); break;
                    case '\f': sb.Append("\\f"); break;
                    case '\n': sb.Append("\\n"); break;
                    case '\r': sb.Append("\\r"); break;
                    case '\t': sb.Append("\\t"); break;
                    default:
                        bool lone = (char.IsHighSurrogate(c) && !(k + 1 < s.Length && char.IsLowSurrogate(s[k + 1]))) || (char.IsLowSurrogate(c) && !(k > 0 && char.IsHighSurrogate(s[k - 1])));
                        if (c < 0x20 || lone) sb.Append("\\u").Append(((int)c).ToString("x4")); else sb.Append(c);
                        break;
                }
            }
            sb.Append('"');
        }
    }
}
