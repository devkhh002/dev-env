using System;
using System.Collections.Generic;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading;
using System.Windows.Forms;

namespace PcSetup
{
    public static partial class Hooks
    {
        static partial void ShowUploadImpl(MainForm f) { Upload.Show(f); }
    }

    // 비밀정보 검사에 걸린 것 하나 — Block: 올리지 못하게 막는다 · 아니면 '확인했다' 를 체크해야 올린다
    public sealed class SecretHit { public string File = ""; public int Line; public string What = ""; public bool Block; public string Peek = ""; }
    // git status 한 줄 (XY 경로)
    public sealed class RepoFile { public string XY, Path; }
    // 저장소 하나의 지금 상태
    public sealed class RepoState
    {
        public string Dir, Name, Origin = "", Upstream = "", Finger = "";
        public List<RepoFile> Files = new List<RepoFile>();
        public int Ahead;
        public bool HasHead;
    }

    // ── 소스 올리기: C:\dev 의 내 프로젝트(git 저장소)를 비밀정보 검사 → 파일 목록 확인 → 커밋·푸시 ──
    public static class Upload
    {
        sealed class Rule { public Regex Re; public string What; public bool Block; public Rule(string re, string what, bool block) { Re = new Regex(re, RegexOptions.CultureInvariant); What = what; Block = block; } }

        // 비밀정보 규칙 — 찾은 값은 앞 몇 글자만 보인다
        static readonly Rule[] SecretRules =
        {
            new Rule(@"-----BEGIN [A-Z ]*PRIVATE KEY-----", "개인 키", true),
            new Rule(@"AKIA[0-9A-Z]{16}", "AWS 액세스 키", true),
            new Rule(@"gh[pousr]_[A-Za-z0-9]{36,}|github_pat_[A-Za-z0-9_]{40,}", "GitHub 토큰", true),
            new Rule(@"xox[abprs]-[A-Za-z0-9-]{10,}", "Slack 토큰", true),
            new Rule(@"sk-ant-[A-Za-z0-9_\-]{20,}", "Anthropic API 키", true),
            new Rule(@"(?<![A-Za-z0-9_\-])sk-(?!ant-)(proj-)?[A-Za-z0-9_\-]{32,}", "OpenAI API 키", true),   // 낱말 중간(mask-…·task-…)은 아니다
            new Rule(@"[sr]k_live_[0-9A-Za-z]{20,}", "Stripe 키", true),
            new Rule(@"npm_[A-Za-z0-9]{36}", "npm 토큰", true),
            new Rule(@"""type""\s*:\s*""service_account""", "구글 서비스 계정 키 파일", true),
            new Rule(@"AIza[0-9A-Za-z_\-]{35}", "Google API 키 (Firebase 웹 설정이면 공개용이라 괜찮다)", false),
            new Rule(@"://[^/\s:@'""]{1,64}:[^/\s:@'""]{3,}@", "주소 안의 아이디:비밀번호", false),
            new Rule(@"(?i)(password|passwd|pwd|secret|token|api[_-]?key|access[_-]?key|client[_-]?secret|비밀번호)[""']?\s*[:=]\s*[""'][^""'\s]{6,}[""']", "비밀번호·토큰처럼 보이는 값", false),
        };
        const long TwoMB = 2L * 1024 * 1024;
        static readonly Regex BinExt = new Regex(@"\.(png|jpe?g|gif|bmp|ico|webp|pdf|zip|7z|rar|gz|exe|dll|msi|woff2?|ttf|otf|eot|mp[34]|wav|mov|avi|mkv|psd|db|sqlite)$", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);
        static readonly Regex NewName = new Regex(@"(?m)^\+\+\+ b/([^\t\r\n]+)");   // 빈칸이 든 이름은 git 이 끝에 탭을 붙인다
        // (--no-renames 라 a/ 와 b/ 경로가 같다 — 역참조로 맞춰야 ' and b/' 가 든 폴더 이름에서도 바르게 끊는다. 지운 파일 'a/… and /dev/null' 은 건너뜀)
        static readonly Regex BinLine = new Regex(@"(?m)^(?:commit ([0-9a-f]{40,64})|Binary files (?:a/(.+) and b/\2|/dev/null and b/(.+?)) differ)\r?$");
        static readonly Regex Hunk = new Regex(@"^@@ -\S+ \+(\d+)");

        // 이름만 봐도 비밀정보인 파일(.env·키 파일) — 막는다. 예시 파일(.env.example 등)은 괜찮다
        public static bool IsSecretName(string path)
        {
            var parts = (path ?? "").Trim().Split('\\', '/');   // Path.GetFileName 은 탭·따옴표가 든 이름에서 오류가 난다
            var n = parts[parts.Length - 1].ToLowerInvariant();
            return (Regex.IsMatch(n, @"^\.env(\..+)?$") && !Regex.IsMatch(n, @"\.(example|sample|template|dist)$"))
                || Regex.IsMatch(n, @"\.(pem|key|pfx|p12|jks|keystore)$") || Regex.IsMatch(n, @"^id_(rsa|dsa|ecdsa|ed25519)$");
        }

        // root 아래(3 단계까지)의 git 저장소 — 숨김·점 폴더·node_modules 등은 들어가지 않는다
        public static List<string> FindDevRepos(string root)
        {
            var skip = new[] { "node_modules", "venv", "__pycache__", "dist", "build" };
            var outList = new List<string>();
            var queue = new Queue<KeyValuePair<string, int>>();
            queue.Enqueue(new KeyValuePair<string, int>(root, 0));
            while (queue.Count > 0)
            {
                var q = queue.Dequeue(); var dir = q.Key;
                var g = Path.Combine(dir, ".git");
                if (Directory.Exists(g) || File.Exists(g)) { outList.Add(dir); continue; }
                if (q.Value >= 3) continue;
                DirectoryInfo[] subs;
                try { subs = new DirectoryInfo(dir).GetDirectories(); } catch { continue; }
                foreach (var c in subs.OrderBy(x => x.Name, StringComparer.OrdinalIgnoreCase))
                {
                    bool hidden; try { hidden = (c.Attributes & FileAttributes.Hidden) != 0; } catch { hidden = true; }
                    if (hidden || c.Name.StartsWith(".") || skip.Contains(c.Name, StringComparer.OrdinalIgnoreCase)) continue;
                    queue.Enqueue(new KeyValuePair<string, int>(c.FullName, q.Value + 1));
                }
            }
            return outList;
        }

        // git status -z: 'XY 경로' 마다 \0. 이름 바꾸기(R)·복사(C)는 예전 이름이 한 칸 더 온다
        public static List<RepoFile> SplitPorcelain(string z)
        {
            var list = new List<RepoFile>();
            var parts = (z ?? "").Split('\0');
            for (int i = 0; i < parts.Length; i++)
            {
                var p = parts[i]; if (p.Length < 4) continue;
                if (p[0] == 'R' || p[0] == 'C') i++;
                list.Add(new RepoFile { XY = p.Substring(0, 2), Path = p.Substring(3) });
            }
            return list;
        }

        // 읽기만 하는 git — 다른 프로그램(편집기의 git)과 index.lock 으로 부딪치지 않게
        static ProcResult G(string dir, int sec, params string[] a) { return Git.Run(dir, sec, new[] { "--no-optional-locks" }.Concat(a).ToArray()); }
        // 출력을 바이트로 받아 UTF-8 로 — 줄 끝(\r)·\0 을 그대로 둔다
        static ProcResult GB(string dir, int sec, params string[] a)
        {
            var r = Git.RunBytes(dir, sec, new[] { "--no-optional-locks" }.Concat(a).ToArray());
            if (r.TimedOut) r.Err = "git 이 " + sec + "초 안에 끝나지 않았습니다";
            r.Out = r.Bytes == null ? "" : Encoding.UTF8.GetString(r.Bytes);
            return r;
        }

        static FileSystemInfo Stat(string dir, string rel)
        {
            try
            {
                var full = Path.Combine(dir, rel.Replace('/', '\\'));
                var fi = new FileInfo(full); if (fi.Exists) return fi;
                var di = new DirectoryInfo(full.TrimEnd('\\')); if (di.Exists) return di;
            }
            catch { }
            return null;
        }

        public static RepoState GetRepoState(string dir)
        {
            var o = G(dir, 60, "remote", "get-url", "origin");
            var s = GB(dir, 60, "status", "--porcelain=v1", "-z", "-uall");
            var u = G(dir, 60, "rev-parse", "--abbrev-ref", "--symbolic-full-name", "@{u}");
            var h = G(dir, 60, "rev-parse", "HEAD");
            var up = u.Code == 0 ? u.Out.Trim() : "";
            var c = G(dir, 60, "rev-list", "--count", up.Length > 0 ? "@{u}..HEAD" : "HEAD");   // 한 번도 안 올린 브랜치면 커밋 전부
            var files = SplitPorcelain(s.Out);
            // 검사한 뒤 파일이 바뀌었는지 알아보는 지문 — 상태 + HEAD + 바뀐 파일의 크기·시각
            var marks = new List<string>();
            foreach (var f in files)
            {
                var fi = Stat(dir, f.Path);
                if (fi is FileInfo) marks.Add(((FileInfo)fi).Length + ":" + fi.LastWriteTimeUtc.Ticks);
                else if (fi != null) marks.Add(":" + fi.LastWriteTimeUtc.Ticks);
            }
            int ahead = 0; if (c.Code == 0) int.TryParse(c.Out.Trim(), out ahead);
            var root = Conf.DevRoot;
            return new RepoState
            {
                Dir = dir,
                Name = dir.StartsWith(root, StringComparison.OrdinalIgnoreCase) ? dir.Substring(root.Length).TrimStart('\\') : dir,
                Origin = o.Code == 0 ? o.Out.Trim() : "", Upstream = up,
                Files = files, Ahead = ahead, HasHead = h.Code == 0,
                Finger = s.Out + "|" + h.Out + "|" + string.Join(",", marks),
            };
        }

        // 글 안에서 규칙에 걸린 곳 — diff 글이면 더해지는 줄(+)만 본다
        public static List<SecretHit> FindSecretsIn(string text, string file, bool diff)
        {
            var hits = new List<SecretHit>();
            if (string.IsNullOrEmpty(text)) return hits;
            foreach (var rule in SecretRules)
            {
                foreach (Match m in rule.Re.Matches(text))
                {
                    int ls = m.Index > 0 ? text.LastIndexOf('\n', m.Index - 1) + 1 : 0;
                    var f = file; int ln = 0;
                    if (diff)
                    {
                        if (text[ls] != '+' || (ls + 4 <= text.Length && string.CompareOrdinal(text, ls, "+++ ", 0, 4) == 0)) continue;
                        int fs = text.LastIndexOf("\n+++ ", Math.Max(0, ls - 1), StringComparison.Ordinal);
                        if (fs >= 0)
                        {
                            int fe = text.IndexOf('\n', fs + 1); if (fe < 0) fe = text.Length;
                            f = text.Substring(fs + 5, fe - fs - 5).TrimEnd('\r', '\t');   // 빈칸이 든 이름은 git 이 끝에 탭을 붙인다
                            if (f.StartsWith("b/")) f = f.Substring(2);
                        }
                        int hs = text.LastIndexOf("\n@@ ", Math.Max(0, ls - 1), StringComparison.Ordinal);
                        if (hs >= 0)
                        {
                            var hm = Hunk.Match(text.Substring(hs + 1, Math.Min(80, text.Length - hs - 1)));
                            int start;
                            if (hm.Success && int.TryParse(hm.Groups[1].Value, out start))
                            {
                                int he = text.IndexOf('\n', hs + 1) + 1;
                                int plus = 0;
                                foreach (var l in text.Substring(he, Math.Max(0, ls - he)).Split('\n')) if (l.StartsWith("+")) plus++;
                                ln = start + plus;
                            }
                        }
                    }
                    else
                    {
                        ln = 1; for (int i = 0; i < ls; i++) if (text[i] == '\n') ln++;
                    }
                    var v = m.Value;
                    hits.Add(new SecretHit { File = f, Line = ln, What = rule.What, Block = rule.Block, Peek = v.Substring(0, Math.Min(6, v.Length)) + "…" });
                }
            }
            return hits;
        }

        // 파일 하나를 직접 읽어 검사 — UTF-16(.reg 등)은 풀어 읽고, 못 읽는 파일(2MB 넘는 것·이진)은 말없이 건너뛰지 않고 '의심' 으로 보인다(그림·압축 같은 이진 파일은 빼고)
        // rev 를 주면 작업 폴더가 아니라 그 커밋에 담긴 판을 읽는다 — 안 올린 커밋에 넣었다가 나중 커밋에서 지운 것도 올라가기 때문
        public static List<SecretHit> TestFileSecrets(string dir, string path, string rev)
        {
            var none = new List<SecretHit>();
            bool bin = BinExt.IsMatch(path);
            var where = !string.IsNullOrEmpty(rev) ? path + " (커밋 " + rev.Substring(0, Math.Min(7, rev.Length)) + ")" : path;
            byte[] b = null; long size = -1;
            if (!string.IsNullOrEmpty(rev))
            {
                var s = G(dir, 60, "cat-file", "-s", rev + ":" + path);
                long n; if (s.Code == 0 && long.TryParse(s.Out.Trim(), out n)) size = n;
                if (size >= 0 && size <= TwoMB) { var x = Git.RunBytes(dir, 60, "cat-file", "blob", rev + ":" + path); if (x.Code == 0) b = x.Bytes; }
            }
            else
            {
                var fi = Stat(dir, path);
                if (fi is DirectoryInfo) return none;
                if (fi != null)
                {
                    size = ((FileInfo)fi).Length;
                    if (size <= TwoMB) { try { b = File.ReadAllBytes(fi.FullName); } catch { } }
                }
            }
            if (size > TwoMB)
            {
                if (!bin) none.Add(new SecretHit { File = where, What = "2MB 넘는 파일 — 내용은 검사하지 못했습니다(직접 확인)" });
                return none;
            }
            string text = null;
            if (b != null)
            {
                if (b.Length >= 2 && b[0] == 0xFF && b[1] == 0xFE) text = Encoding.Unicode.GetString(b, 2, b.Length - 2);   // UTF-16 (.reg 등)
                else if (b.Length >= 2 && b[0] == 0xFE && b[1] == 0xFF) text = Encoding.BigEndianUnicode.GetString(b, 2, b.Length - 2);
                else if (Array.IndexOf(b, (byte)0, 0, Math.Min(b.Length, 8000)) < 0) text = Encoding.UTF8.GetString(b);
            }
            if (text == null)
            {
                if (!bin) none.Add(new SecretHit { File = where, What = "이진 파일 — 내용은 검사하지 못했습니다(직접 확인)" });
                return none;
            }
            return FindSecretsIn(text, where, false);
        }

        // 한 저장소 검사: 아직 안 올린 커밋 + 지금 바뀐 파일(+ 새 파일 전체)
        public static List<SecretHit> TestRepoSecrets(RepoState r)
        {
            var hits = new List<SecretHit>();
            var diffs = new List<string>();
            if (r.HasHead)
            {
                // git 이 실패하거나 시간 안에 못 끝내면 '걸린 것 없음' 이 아니라 '검사 못 함'(막음)
                // core.quotepath=false: 한글 파일 이름이 "\355…" 로 바뀌어 이름 검사에서 빠지지 않게 · --no-renames: 이름만 바꾼 파일(예: 설정 → .env)도 새 파일로 보이게
                var runs = new[]
                {
                    new { N = "log", S = 180, A = new[] { "-c", "core.quotepath=false", "log", "-p", "-U0", "--no-renames", "--no-color", "--no-ext-diff", "--format=commit %H", r.Upstream.Length > 0 ? "@{u}..HEAD" : "HEAD" } },
                    new { N = "diff", S = 120, A = new[] { "-c", "core.quotepath=false", "diff", "HEAD", "-U0", "--no-renames", "--no-color", "--no-ext-diff" } },
                };
                foreach (var g in runs)
                {
                    var x = GB(r.Dir, g.S, g.A);
                    if (x.Code == 0) diffs.Add(x.Out);
                    else hits.Add(new SecretHit { File = "(git " + g.N + ")", What = "검사하지 못했습니다 — " + x.Text, Block = true });
                }
            }
            var names = new List<string>();
            var binFiles = new List<KeyValuePair<string, string>>(); var binSeen = new HashSet<string>();   // (커밋, 경로)
            foreach (var t in diffs)
            {
                if (string.IsNullOrEmpty(t)) continue;
                hits.AddRange(FindSecretsIn(t, "", true));
                foreach (Match m in NewName.Matches(t)) names.Add(m.Groups[1].Value);
                // git 이 이진으로 본 파일(UTF-16 .reg 등)은 diff 에 '+' 줄이 없다 — 아래에서 직접 읽는다.
                // 안 올린 커밋(log)은 'commit <해시>' 줄로 어느 커밋의 판인지 안다 · 지금 바뀐 것(diff HEAD)은 작업 폴더를 읽는다
                var rev = "";
                foreach (Match m in BinLine.Matches(t))
                {
                    if (m.Groups[1].Success) { rev = m.Groups[1].Value; continue; }
                    var bp = m.Groups[2].Success ? m.Groups[2].Value : m.Groups[3].Value; names.Add(bp);
                    if (binSeen.Add(rev + "|" + bp)) binFiles.Add(new KeyValuePair<string, string>(rev, bp));
                }
            }
            var untracked = r.Files.Where(f => f.XY == "??").Select(f => f.Path).ToList();
            foreach (var f in r.Files)
            {
                if (f.XY.IndexOf('D') >= 0) continue;
                names.Add(f.Path);
                if (f.XY != "??" && r.HasHead) continue;   // 추적 중인 파일은 위 diff 로 봤다(이진이면 아래에서)
                hits.AddRange(TestFileSecrets(r.Dir, f.Path, null));
            }
            foreach (var bf in binFiles) hits.AddRange(TestFileSecrets(r.Dir, bf.Value, bf.Key));
            foreach (var n in names.Distinct(StringComparer.Ordinal))
            {
                if (!IsSecretName(n)) continue;
                // 이미 추적 중인 파일은 .gitignore 로 빠지지 않는다 — git rm --cached 를 알려 준다
                var what = untracked.Contains(n, StringComparer.OrdinalIgnoreCase)
                    ? "비밀정보 파일 (.env·키 파일) — .gitignore 에 넣으세요"
                    : "비밀정보 파일 (.env·키 파일) — 이미 git 이 추적하는 파일이라 .gitignore 만으로는 빠지지 않습니다: git rm --cached \"" + n + "\" 한 뒤 .gitignore 에 넣으세요";
                hits.Add(new SecretHit { File = n, What = what, Block = true });
            }
            return hits;
        }

        public sealed class ScanResult { public List<RepoState> Repos; public string Finger; public bool Block, Warn; public string Report; }

        // 고른 저장소들을 검사해 보고서를 만든다 (일하는 스레드에서). progress = '검사 중: …'
        public static ScanResult Scan(List<RepoState> pick, Action<string> progress)
        {
            var sb = new StringBuilder(); bool block = false, warn = false;
            foreach (var r in pick)
            {
                if (progress != null) progress("검사 중: " + r.Name + " ...");
                sb.AppendLine("■ " + r.Name + "  →  " + r.Origin + (r.Upstream.Length > 0 ? "  (" + r.Upstream + ")" : ""));   // 올라가는 곳 = 괄호 안 브랜치
                sb.AppendLine("   바뀐 파일 " + r.Files.Count + "개 · 안 올린 커밋 " + r.Ahead + "개");
                foreach (var f in r.Files.Take(300)) sb.AppendLine("     " + f.XY + "  " + f.Path);
                if (r.Files.Count > 300) sb.AppendLine("     … 외 " + (r.Files.Count - 300) + "개");
                var hits = TestRepoSecrets(r);
                if (hits.Count == 0) sb.AppendLine("   비밀정보 검사: 걸린 것 없음");
                foreach (var h in hits)
                {
                    if (h.Block) block = true; else warn = true;
                    sb.AppendLine(string.Format("   {0} {1}{2} — {3}{4}", h.Block ? "⛔ 막음" : "⚠ 의심", h.File, h.Line != 0 ? ":" + h.Line : "", h.What, h.Peek.Length > 0 ? " (" + h.Peek + ")" : ""));
                }
                sb.AppendLine("");
            }
            if (block) sb.AppendLine("⛔ 막힌 항목이 있어 올릴 수 없습니다 — 그 파일에서 비밀정보를 빼거나 .gitignore 에 넣은 뒤 다시 검사하세요. (이미 커밋했으면 그 커밋도 고쳐야 합니다)");
            else if (warn) sb.AppendLine("⚠ 의심 항목을 하나씩 확인하고, 비밀정보가 아니면 아래 '확인했다' 를 체크한 뒤 올리기.");
            else sb.AppendLine("위 파일들이 올라갑니다 — 맞으면 '올리기'.");
            return new ScanResult { Repos = pick, Finger = string.Join("#", pick.Select(x => x.Finger)), Block = block, Warn = warn, Report = sb.ToString() };
        }

        // 커밋·푸시 — 저장소마다 '✓ 이름 — 올렸습니다' / '✗ 이름 — 까닭' 한 줄
        public static List<string> Push(List<RepoState> now, string msg, Action<string> progress)
        {
            var lines = new List<string>();
            foreach (var r in now)
            {
                if (progress != null) progress("올리는 중: " + r.Name + " ...");
                string err = null; var p = new Git.PushResult();
                if (r.Files.Count > 0)
                {
                    var a = Git.Run(r.Dir, 120, "add", "-A");
                    if (a.Code != 0) err = "파일을 담지 못했습니다: " + a.Text;
                    else { var c = Git.Commit(r.Dir, msg); if (c.Code != 0) err = "커밋하지 못했습니다: " + c.Text; }
                }
                if (err == null) { p = Git.Push(r.Dir, r.Upstream.Length > 0); if (!p.Ok) err = "GitHub 에 올리지 못했습니다: " + p.Text; }
                lines.Add(err != null ? "✗ " + r.Name + " — " + err : ("✓ " + r.Name + " — 올렸습니다 " + p.Text).TrimEnd());
            }
            return lines;
        }

        public static void Show(MainForm owner)
        {
            Env.RefreshPath();
            if (Git.Exe == null) { MessageBox.Show(owner, "git 이 없습니다 — ⑤ 의 Git 을 먼저 설치하세요.", "PC 설치"); return; }
            var repos = FindDevRepos(Conf.DevRoot);
            if (repos.Count == 0) { MessageBox.Show(owner, Conf.DevRoot + " 에 git 프로젝트가 없습니다.", "PC 설치"); return; }
            using (var d = new UploadForm(repos)) d.ShowDialog(owner);
        }
    }

    // 소스 올리기 창 — 오래 걸리는 일(상태 읽기·검사·올리기)은 일하는 스레드에서, 그동안 단추는 잠근다
    public sealed class UploadForm : Form
    {
        readonly List<string> repos;
        readonly ListView lv = new ListView { Dock = DockStyle.Fill, View = View.Details, CheckBoxes = true, FullRowSelect = true, HideSelection = true };
        readonly Button bScan = new Button { Text = "검사하기", AutoSize = true, AutoSizeMode = AutoSizeMode.GrowAndShrink, Padding = new Padding(10, 3, 10, 3), UseVisualStyleBackColor = true };
        readonly Label hint = new Label { Text = "올릴 프로젝트를 체크하고 검사 — 올라갈 파일과 비밀정보 검사 결과가 아래에 나옵니다", Dock = DockStyle.Fill, AutoEllipsis = true, TextAlign = ContentAlignment.MiddleLeft };
        readonly TextBox rep = new TextBox { Dock = DockStyle.Fill, Multiline = true, ReadOnly = true, ScrollBars = ScrollBars.Both, WordWrap = false, BackColor = Color.White, MaxLength = int.MaxValue };
        readonly TextBox cm = new TextBox { Dock = DockStyle.Fill };
        readonly CheckBox ack = new CheckBox { Text = "⚠ 의심 항목을 하나씩 봤고, 비밀정보가 아닙니다", AutoSize = true, Enabled = false, Margin = new Padding(3, 4, 3, 2) };
        readonly Button bPush = new Button { Text = "올리기", AutoSize = true, AutoSizeMode = AutoSizeMode.GrowAndShrink, Padding = new Padding(18, 4, 18, 4), Enabled = false, UseVisualStyleBackColor = true };
        readonly Button bX = new Button { Text = "닫기", AutoSize = true, AutoSizeMode = AutoSizeMode.GrowAndShrink, Padding = new Padding(14, 4, 14, 4), DialogResult = DialogResult.Cancel, UseVisualStyleBackColor = true };
        Upload.ScanResult scan;
        bool busy;

        public UploadForm(List<string> repos)
        {
            this.repos = repos;
            Text = "소스 올리기 — " + Conf.DevRoot + " 의 프로젝트를 GitHub 에";
            Font = MainForm.UiFont; StartPosition = FormStartPosition.CenterParent;
            MinimizeBox = false; ShowInTaskbar = false;
            try { Icon = Icon.ExtractAssociatedIcon(Application.ExecutablePath); } catch { }
            cm.Text = "소스 올리기 — " + Environment.MachineName + " " + DateTime.Now.ToString("yyyy-MM-dd HH:mm", System.Globalization.CultureInfo.InvariantCulture);

            // 머리글이 잘리지 않게 — 글자 폭 + 여백보다 좁지 않게
            foreach (var c in new[] { new KeyValuePair<string, int>("프로젝트", 240), new KeyValuePair<string, int>("바뀐 파일", 90), new KeyValuePair<string, int>("안 올린 커밋", 110), new KeyValuePair<string, int>("GitHub", 120) })
                lv.Columns.Add(c.Key, Math.Max(Dpi.S(c.Value), TextRenderer.MeasureText(c.Key, Font).Width + Dpi.S(24)));

            var scanRow = Row(2); scanRow.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize)); scanRow.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
            scanRow.Controls.Add(bScan, 0, 0); scanRow.Controls.Add(hint, 1, 0);
            var msgRow = Row(2); msgRow.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize)); msgRow.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
            msgRow.Controls.Add(new Label { Text = "커밋 메시지", AutoSize = true, Anchor = AnchorStyles.Left, Margin = new Padding(0, 0, 6, 0) }, 0, 0); msgRow.Controls.Add(cm, 1, 0);
            var btnRow = new FlowLayoutPanel { Dock = DockStyle.Fill, AutoSize = true, FlowDirection = FlowDirection.RightToLeft, WrapContents = false, Margin = new Padding(0, 6, 0, 0) };
            btnRow.Controls.Add(bX); btnRow.Controls.Add(bPush);

            var root = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 1, RowCount = 6, Padding = new Padding(10, 10, 10, 8) };
            root.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
            root.RowStyles.Add(new RowStyle(SizeType.Absolute, Dpi.S(170)));
            root.RowStyles.Add(new RowStyle(SizeType.AutoSize));
            root.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
            root.RowStyles.Add(new RowStyle(SizeType.AutoSize));
            root.RowStyles.Add(new RowStyle(SizeType.AutoSize));
            root.RowStyles.Add(new RowStyle(SizeType.AutoSize));
            root.Controls.Add(lv, 0, 0); root.Controls.Add(scanRow, 0, 1); root.Controls.Add(rep, 0, 2);
            root.Controls.Add(msgRow, 0, 3); root.Controls.Add(ack, 0, 4); root.Controls.Add(btnRow, 0, 5);
            Controls.Add(root);
            CancelButton = bX;

            // 화면이 작으면 창을 줄인다 — 결과 칸이 줄고 아래 단추는 늘 보인다
            var wa = Screen.FromPoint(Cursor.Position).WorkingArea;
            Size = new Size(Math.Min(Dpi.S(840), wa.Width), Math.Min(Dpi.S(700), wa.Height));
            MinimumSize = new Size(Math.Min(Dpi.S(520), wa.Width), Math.Min(Dpi.S(420), wa.Height));

            // 목록 높이 = 창의 30% (80~170) — 창이 작아도 목록·결과 칸이 둘 다 보인다
            Action fitRows = () => { root.RowStyles[0].Height = Math.Min(Dpi.S(170), Math.Max(Dpi.S(80), ClientSize.Height * 30 / 100)); };
            fitRows(); Resize += (s, e) => fitRows();
            lv.Resize += (s, e) => FitColumns();
            lv.ItemChecked += (s, e) => { if (!busy) Reset(); };
            ack.CheckedChanged += (s, e) => CanPush();
            bScan.Click += (s, e) => StartScan();
            bPush.Click += (s, e) => StartPush();
            FormClosing += (s, e) => { if (busy) e.Cancel = true; };   // 검사·올리기 중에는 닫지 않는다(끝나면 닫힌다)
            Shown += (s, e) =>
            {
                rep.Text = "프로젝트 상태를 읽는 중…";
                Work(() => { var st = repos.Select(Upload.GetRepoState).ToList(); Ui(() => { Fill(st); rep.Text = ""; }); }, ex => rep.Text = Crlf("상태를 읽지 못했습니다: " + ex.Message));
            };
        }

        // 글 칸은 \r\n 이라야 줄이 바뀐다 — git 출력의 \n 만 있는 줄 끝을 맞춘다
        static string Crlf(string t) { return Regex.Replace(t ?? "", "\r?\n", "\r\n"); }

        static TableLayoutPanel Row(int cols) { return new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = cols, RowCount = 1, AutoSize = true, Margin = new Padding(0, 4, 0, 4) }; }

        void FitColumns()
        {
            if (lv.Columns.Count < 4) return;
            int rest = lv.ClientSize.Width - lv.Columns[0].Width - lv.Columns[1].Width - lv.Columns[2].Width;
            lv.Columns[3].Width = Math.Max(rest - 4, Dpi.S(120));   // 남는 폭에 맞춘다(좁으면 줄여 가로 막대가 안 생기게)
        }

        void Ui(Action a) { if (IsDisposed) return; if (InvokeRequired) { try { BeginInvoke(a); } catch { } } else a(); }

        // 일하는 스레드에서 — 그동안 단추·목록을 잠근다. 끝나면 풀고 done
        void Work(Action work, Action<Exception> fail)
        {
            busy = true; SetEnabled(false); UseWaitCursor = true;
            new Thread(() =>
            {
                Exception err = null;
                try { work(); } catch (Exception ex) { err = ex; }
                Ui(() => { if (err != null && fail != null) fail(err.GetBaseException()); busy = false; UseWaitCursor = false; SetEnabled(true); CanPush(); });
            }) { IsBackground = true }.Start();
        }
        void SetEnabled(bool on) { bScan.Enabled = on; lv.Enabled = on; cm.Enabled = on; if (!on) { bPush.Enabled = false; ack.Enabled = false; } else ack.Enabled = scan != null && scan.Warn && !scan.Block; }

        void Fill(List<RepoState> states)
        {
            lv.BeginUpdate();
            lv.Items.Clear();
            foreach (var r in states)
            {
                var li = new ListViewItem(r.Name);
                li.SubItems.Add(r.Files.Count.ToString()); li.SubItems.Add(r.Ahead.ToString());
                li.SubItems.Add(r.Origin.Length > 0 ? r.Origin : "(GitHub 주소 없음 — 올릴 수 없다)");
                li.Tag = r;
                li.Checked = r.Origin.Length > 0 && (r.Files.Count > 0 || r.Ahead > 0);
                if (r.Origin.Length == 0) li.ForeColor = Color.Gray;
                lv.Items.Add(li);
            }
            lv.EndUpdate();
            FitColumns(); BeginInvoke((Action)FitColumns);   // 세로 막대가 생긴 뒤 한 번 더
        }
        void Reset() { scan = null; bPush.Enabled = false; ack.Checked = false; ack.Enabled = false; }
        void CanPush() { bPush.Enabled = !busy && scan != null && !scan.Block && (!scan.Warn || ack.Checked); }

        void StartScan()
        {
            if (busy) return;
            Reset();
            var items = lv.CheckedItems.Cast<ListViewItem>().ToList();
            var dirs = items.Select(i => ((RepoState)i.Tag).Dir).ToList();
            Work(() =>
            {
                // 검사할 때마다 지금 상태를 다시 읽는다 — 창을 연 뒤 바뀐 파일·새 파일까지 보고, 올리기 직전 대조와도 맞게
                var states = dirs.Select(Upload.GetRepoState).ToList();
                Ui(() => { for (int i = 0; i < items.Count; i++) { items[i].Tag = states[i]; items[i].SubItems[1].Text = states[i].Files.Count.ToString(); items[i].SubItems[2].Text = states[i].Ahead.ToString(); } });
                var pick = states.Where(s => s.Origin.Length > 0 && (s.Files.Count > 0 || s.Ahead > 0)).ToList();
                if (pick.Count == 0) { Ui(() => rep.Text = "올릴 것이 있는 프로젝트(바뀐 파일·안 올린 커밋)를 체크하세요."); return; }
                var res = Upload.Scan(pick, t => Ui(() => rep.Text = t));
                Ui(() => { scan = res; rep.Text = Crlf(res.Report); });
            }, ex => { scan = null; rep.Text = Crlf("검사하지 못했습니다: " + ex.Message); });
        }

        void StartPush()
        {
            if (busy || scan == null) return;
            var sc = scan;
            var msg = cm.Text.Trim(); if (msg.Length == 0) msg = "소스 올리기 — " + Environment.MachineName;
            Work(() =>
            {
                // 검사한 뒤 파일이 바뀌었으면 다시 검사하게 한다 — 본 것과 올라가는 것이 같도록
                var now = sc.Repos.Select(r => Upload.GetRepoState(r.Dir)).ToList();
                if (string.Join("#", now.Select(x => x.Finger)) != sc.Finger)
                {
                    Ui(() => { Reset(); rep.AppendText("\r\n검사한 뒤 파일이 바뀌었습니다 — '검사하기' 를 다시 누르세요.\r\n"); });
                    return;
                }
                var lines = Upload.Push(now, msg, t => Ui(() => rep.Text = t));
                var all = repos.Select(Upload.GetRepoState).ToList();
                Ui(() =>
                {
                    Fill(all); Reset();
                    rep.Text = Crlf(string.Join("\r\n", lines) + "\r\n");
                });
                Log.Say("소스 올리기:", Tone.Info);
                foreach (var l in lines)
                {
                    var tone = !l.StartsWith("✓") ? Tone.Error : l.Contains("주의:") ? Tone.Warn : Tone.Ok;
                    foreach (var part in l.Split(new[] { "\r\n", "\n" }, StringSplitOptions.None)) if (part.Trim().Length > 0) Log.Say(part, tone);
                }
            }, ex => { Reset(); rep.AppendText(Crlf("\r\n오류로 멈췄습니다: " + ex.Message + "\r\n")); });
        }
    }
}
