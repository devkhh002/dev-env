using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading;

namespace PcSetup
{
    public sealed class ProcResult
    {
        public int Code = -1;
        public string Out = "";
        public string Err = "";
        public byte[] Bytes;          // RunBytes 일 때 stdout 그대로
        public bool TimedOut;
        public bool Started;
        public string Text { get { return ((Err ?? "") + "\n" + (Out ?? "")).Trim(); } }
    }

    // 바깥 프로그램 실행 — 창 없이, 시간 제한, 출력은 줄마다 기록으로
    public static class Proc
    {
        // stdout·stderr 를 받아 온다. onLine 이 있으면 줄마다 넘긴다. 시간이 지나면 끝내고 TimedOut.
        // (끝난 뒤 그 프로그램이 띄운 다른 프로그램이 출력 통로를 쥐고 있어도 3초 넘게 기다리지 않는다 — 설치 프로그램이 앱을 띄우면 멈추던 것)
        public static ProcResult Run(string exe, string args, int timeoutSec = 600, Action<string> onLine = null, Encoding enc = null, string dir = null, IDictionary<string, string> env = null)
        {
            var r = new ProcResult();
            var psi = Psi(exe, args, dir, env, true);
            psi.StandardOutputEncoding = enc ?? Encoding.UTF8;
            psi.StandardErrorEncoding = enc ?? Encoding.UTF8;
            var sbOut = new StringBuilder(); var sbErr = new StringBuilder();
            var eofOut = new ManualResetEventSlim(); var eofErr = new ManualResetEventSlim();
            using (var p = new Process { StartInfo = psi })
            {
                p.OutputDataReceived += (s, e) => { if (e.Data == null) { eofOut.Set(); return; } lock (sbOut) sbOut.AppendLine(e.Data); if (onLine != null) onLine(e.Data); };
                p.ErrorDataReceived += (s, e) => { if (e.Data == null) { eofErr.Set(); return; } lock (sbErr) sbErr.AppendLine(e.Data); if (onLine != null) onLine(e.Data); };
                try { p.Start(); r.Started = true; }
                catch (Exception ex) { r.Err = exe + " 을(를) 실행하지 못했습니다: " + ex.Message; return r; }
                p.BeginOutputReadLine(); p.BeginErrorReadLine();
                if (!p.WaitForExit(Ms(timeoutSec)))
                {
                    try { p.Kill(); } catch { }
                    r.TimedOut = true; r.Err = exe + " 이(가) " + timeoutSec + "초 안에 끝나지 않았습니다";
                    lock (sbOut) r.Out = sbOut.ToString();
                    return r;
                }
                eofOut.Wait(3000); eofErr.Wait(3000);
                r.Code = p.ExitCode;
                lock (sbOut) r.Out = sbOut.ToString();
                lock (sbErr) r.Err = sbErr.ToString();
                return r;
            }
        }

        // stdout 을 바이트 그대로 (git cat-file 로 UTF-16·이진 파일 내용 읽기)
        public static ProcResult RunBytes(string exe, string args, int timeoutSec = 60, string dir = null, IDictionary<string, string> env = null)
        {
            var r = new ProcResult();
            var psi = Psi(exe, args, dir, env, true);
            psi.StandardErrorEncoding = Encoding.UTF8;
            using (var p = new Process { StartInfo = psi })
            {
                var sbErr = new StringBuilder(); var eofErr = new ManualResetEventSlim();
                p.ErrorDataReceived += (s, e) => { if (e.Data == null) { eofErr.Set(); return; } lock (sbErr) sbErr.AppendLine(e.Data); };
                try { p.Start(); r.Started = true; }
                catch (Exception ex) { r.Err = ex.Message; return r; }
                p.BeginErrorReadLine();
                var ms = new MemoryStream();
                var copy = p.StandardOutput.BaseStream.CopyToAsync(ms);
                if (!p.WaitForExit(Ms(timeoutSec))) { try { p.Kill(); } catch { } r.TimedOut = true; r.Err = "시간 초과"; return r; }
                try { copy.Wait(5000); } catch { }
                eofErr.Wait(3000);
                r.Code = p.ExitCode; r.Bytes = ms.ToArray();
                lock (sbErr) r.Err = sbErr.ToString();
                return r;
            }
        }

        // 설치 프로그램 실행 — 출력은 받지 않고 '그 프로그램만' 끝나기를 기다린다(설치가 끝나며 띄운 앱은 기다리지 않는다)
        public static int RunWait(string exe, string args, int timeoutSec = 7200, bool hidden = true, string dir = null)
        {
            var psi = Psi(exe, args, dir, null, false);
            psi.CreateNoWindow = hidden;
            try
            {
                using (var p = Process.Start(psi))
                {
                    if (!p.WaitForExit(Ms(timeoutSec))) { Log.Say("   " + Path.GetFileName(exe) + " 이(가) " + timeoutSec / 60 + "분 넘게 끝나지 않아 기다리지 않고 넘어갑니다", Tone.Warn); return -1; }
                    return p.ExitCode;
                }
            }
            catch (Exception ex) { Log.Say("   " + exe + " 을(를) 실행하지 못했습니다: " + ex.Message, Tone.Error); return -1; }
        }

        // 기록에 줄마다 흘리며 실행 (winget·msiexec 등)
        public static ProcResult RunLogged(string exe, string args, int timeoutSec = 3600, Encoding enc = null, string dir = null, IDictionary<string, string> env = null)
        {
            return Run(exe, args, timeoutSec, Log.Raw, enc, dir, env);
        }

        // 명령줄 인자 하나를 따옴표로 감싼다(빈칸·한글 경로) — Windows 규칙(역슬래시+따옴표)
        public static string Quote(string s)
        {
            if (s == null) s = "";
            if (s.Length > 0 && !Regex.IsMatch(s, "[\\s\"]")) return s;
            var t = Regex.Replace(s, "(\\\\*)\"", "$1$1\\\"");
            t = Regex.Replace(t, "(\\\\+)$", "$1$1");
            return "\"" + t + "\"";
        }
        public static string Args(params string[] a) { return string.Join(" ", a.Where(x => x != null).Select(Quote)); }

        // 콘솔 프로그램(cmd·pnputil·sc 등)의 한국어 출력 = OEM 코드 페이지(949)
        public static Encoding Oem { get { try { return Encoding.GetEncoding(System.Globalization.CultureInfo.CurrentCulture.TextInfo.OEMCodePage); } catch { return Encoding.Default; } } }

        static ProcessStartInfo Psi(string exe, string args, string dir, IDictionary<string, string> env, bool redirect)
        {
            var psi = new ProcessStartInfo(exe, args ?? "")
            {
                UseShellExecute = false,
                CreateNoWindow = true,
                RedirectStandardOutput = redirect,
                RedirectStandardError = redirect,
                WorkingDirectory = dir ?? Environment.GetFolderPath(Environment.SpecialFolder.UserProfile),
            };
            if (env != null) foreach (var kv in env) psi.EnvironmentVariables[kv.Key] = kv.Value;
            return psi;
        }
        static int Ms(int sec) { return sec <= 0 || sec > int.MaxValue / 1000 ? int.MaxValue : sec * 1000; }
    }
}
