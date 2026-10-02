using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;

namespace PcSetup
{
    // git — 앱 추가·소스 올리기·프로젝트 받기
    public static class Git
    {
        public static string Exe { get { Env.RefreshPath(); return Env.Which("git.exe"); } }

        // 이 git 에만: 콘솔에서 비밀번호를 묻다 멈추지 않게(GitHub 로그인은 Git 자격 증명 창이 띄운다), 편집기를 띄우지 않게(커밋 메시지는 -F)
        static Dictionary<string, string> GitEnv() { return new Dictionary<string, string> { { "GIT_TERMINAL_PROMPT", "0" }, { "GIT_EDITOR", "true" } }; }

        public static ProcResult Run(string dir, int timeoutSec, params string[] args)
        {
            var exe = Exe;
            if (exe == null) return new ProcResult { Code = -1, Err = "git 이 없습니다 — ⑤ 의 Git 을 먼저 설치하세요" };
            var r = Proc.Run(exe, Proc.Args(new[] { "-C", dir }.Concat(args).ToArray()), timeoutSec, null, Encoding.UTF8, dir, GitEnv());
            if (r.TimedOut) r.Err = "git 이 " + timeoutSec + "초 안에 끝나지 않았습니다";
            return r;
        }
        public static ProcResult Run(string dir, params string[] args) { return Run(dir, 60, args); }
        public static ProcResult RunBytes(string dir, int timeoutSec, params string[] args)
        {
            var exe = Exe;
            if (exe == null) return new ProcResult { Code = -1, Err = "git 이 없습니다" };
            return Proc.RunBytes(exe, Proc.Args(new[] { "-C", dir }.Concat(args).ToArray()), timeoutSec, dir, GitEnv());
        }

        // 커밋 — 메시지는 파일로 넘긴다(한글이 명령줄에서 깨지지 않게)
        public static ProcResult Commit(string dir, string message)
        {
            var mf = Path.Combine(Path.GetTempPath(), "dev-env-commit-" + Guid.NewGuid().ToString("N") + ".txt");
            File.WriteAllText(mf, message + "\n", new UTF8Encoding(false));
            try { return Run(dir, 120, "commit", "-q", "-F", mf); } finally { try { File.Delete(mf); } catch { } }
        }

        public sealed class PushResult { public bool Ok; public string Text = ""; }

        // 올리기 — 다른 PC 에서 먼저 올린 것이 있어 거절되면 받아서(rebase) 한 번 더. 받다가 충돌하면 되돌리고 알린다
        public static PushResult Push(string dir, bool hasUpstream)
        {
            var push = hasUpstream ? new[] { "push", "-q" } : new[] { "push", "-q", "-u", "origin", "HEAD" };
            var p = Run(dir, 180, push);
            if (p.Code == 0) return new PushResult { Ok = true };
            if (hasUpstream && System.Text.RegularExpressions.Regex.IsMatch(p.Text, "rejected|fetch first|non-fast-forward"))
            {
                var pl = Run(dir, 180, "pull", "--rebase", "--autostash", "-q");   // 커밋하지 않은 다른 수정이 있어도 받는다(잠시 치웠다 되돌린다)
                if (pl.Code != 0) { Run(dir, "rebase", "--abort"); return new PushResult { Ok = false, Text = "GitHub 에 먼저 올라간 것과 같은 곳을 고쳐 합치지 못했습니다 — 직접 정리가 필요합니다:\r\n" + pl.Text }; }
                // 받기는 됐지만 치워 둔 수정을 되돌리다 겹쳤으면(autostash 충돌) — 그 파일에 <<<<<<< 표시가 남고 원래 수정은 stash 에 있다. 알리고 '합치지 못함' 표시는 풀어 둔다
                var note = "";
                var cf = Run(dir, "-c", "core.quotepath=false", "diff", "--name-only", "--diff-filter=U");
                var cfl = cf.Out.Split(new[] { '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries).Select(x => x.Trim()).Where(x => x.Length > 0).ToList();
                if (cf.Code == 0 && cfl.Count > 0)
                {
                    Run(dir, new[] { "reset", "-q", "--" }.Concat(cfl).ToArray());
                    note = "\r\n주의: 커밋하지 않은 수정(" + string.Join(", ", cfl) + ")이 받은 것과 겹쳐 그 파일에 충돌 표시(<<<<<<< … >>>>>>>)가 남았습니다 — 직접 고치세요. 원래 수정은 'git stash list' 의 autostash 에 있습니다.";
                }
                p = Run(dir, 180, push);
                if (p.Code == 0) return new PushResult { Ok = true, Text = "(GitHub 에 먼저 올라간 것을 받아 합친 뒤 올림)" + note };
                return new PushResult { Ok = false, Text = p.Text + note };
            }
            return new PushResult { Ok = false, Text = p.Text };
        }
    }
}
