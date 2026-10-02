using System;
using System.ComponentModel;
using System.Diagnostics;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Net.NetworkInformation;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Forms;

namespace PcSetup
{
    // 새 판 — 시작할 때 GitHub 의 dist/version.txt 가 이 판보다 새것이면 dist/PcSetup.exe 를 받아(SHA256 대조) 바꾸고 다시 연다.
    //   폴더에 쓸 수 있으면: 지금 파일 → '<이름>.old', 새 파일을 원래 이름으로, 그것을 같은 인자 + --no-update 로 연다
    //   쓸 수 없으면(읽기 전용 USB 등): 받은 파일을 %TEMP% 에서 그대로 연다
    //   무엇이든 안 되면 기록에 남기고 이 판으로 연다. 개발 PC 의 C:\dev\dev-env\dist 에서 연 것은 바꾸지 않는다.
    public static partial class Hooks
    {
        static partial void TryUpdateImpl(string[] args, ref bool restarting)
        {
            restarting = Updater.Run(Updater.SelfPath(), args, Net.Raw + "dist/");
        }
    }

    static class Updater
    {
        const string MutexName = "Global\\dev-env-PcSetup";   // Program.cs 와 같은 이름 — 지금 프로그램이 이것을 놓은 뒤에 새 판을 연다

        public static string SelfPath()
        {
            try { return Path.GetFullPath(Process.GetCurrentProcess().MainModule.FileName); } catch { return Path.GetFullPath(Application.ExecutablePath); }
        }

        // 시작할 때마다(새 판으로 다시 연 --no-update 포함 — Program 이 그때는 TryUpdate 를 부르지 않으므로 여기서) 지난번 '<이름>.old' 를 지운다
        [System.Runtime.CompilerServices.ModuleInitializer]
        internal static void OnLoad()
        {
            try { CleanOld(SelfPath()); } catch { }
        }

        // 새 판으로 다시 여는 중이면 true (지금 프로그램은 바로 끝내야 한다)
        public static bool Run(string exe, string[] args, string baseUrl)
        {
            var dir = Path.GetDirectoryName(exe);
            if (IsDevBuild(dir)) return false;
            long mine;
            if (!long.TryParse(BuildInfo.Version, out mine) || BuildInfo.Version.Length != 12) return false;

            // 인터넷이 없으면 5초 안에 포기한다 (HttpWebRequest 의 시간 제한은 이름 풀기를 기다리지 않으므로 따로 끊는다)
            var remote = FetchVersion(baseUrl + "version.txt", 5000);
            if (remote == null) { Log.Say("새 판 확인: GitHub 에 닿지 않아 이 판(" + BuildInfo.Version + ")으로 엽니다", Tone.Dim); return false; }
            long theirs;
            if (remote.Length != 12 || !long.TryParse(remote, out theirs)) { Log.Say("새 판 확인: GitHub 의 version.txt 를 읽지 못했습니다(" + remote + ") — 이 판으로 엽니다", Tone.Warn); return false; }
            if (theirs <= mine) { Log.Say("새 판 확인: 이 판이 최신입니다 (" + BuildInfo.Version + ")", Tone.Dim); return false; }

            Log.Say("새 판이 있습니다: " + BuildInfo.Version + " → " + remote + " — 받아서 바꿉니다", Tone.Info);
            var win = Waiting.Open("PC 설치 — 새 판을 받는 중…", "버전 " + remote);
            string fresh;
            try
            {
                fresh = Fetch(baseUrl, remote);
                if (fresh == null) return false;
                if (Sha256(fresh) == Sha256(exe)) { Log.Say("   받은 파일이 지금 판과 같습니다(GitHub 이 아직 새 파일을 내주지 않음) — 이 판으로 엽니다", Tone.Warn); return false; }
            }
            catch (Exception ex) { Log.Say("새 판을 받지 못했습니다(이 판으로 엽니다): " + ex.Message, Tone.Warn); return false; }
            finally { win.Close(); }

            var line = ArgLine(args);
            // 1) 같은 자리에서 바꾼다
            if (Writable(dir))
            {
                var old = exe + ".old";
                try
                {
                    try { if (File.Exists(old)) { File.SetAttributes(old, FileAttributes.Normal); File.Delete(old); } }
                    catch { if (File.Exists(old)) throw; }   // 뒤에서 지우던 것과 겹쳤으면 괜찮다
                    File.Move(exe, old);   // 실행 중인 파일도 이름은 바꿀 수 있다
                    try { File.Copy(fresh, exe, false); }
                    catch { try { if (File.Exists(exe)) File.Delete(exe); File.Move(old, exe); } catch { } throw; }
                    DeleteAtReboot(old);   // 새 판이 열리며 지우지만, 못 지우면 재부팅 때 지운다
                    try { File.Delete(fresh); } catch { }   // 임시 폴더에 받아 둔 것은 이제 필요 없다
                    Log.Say("   새 판으로 바꿨습니다 — 다시 엽니다: " + exe, Tone.Ok);
                    StartAfterExit(exe, line, old, null);
                    return true;
                }
                catch (Exception ex) { Log.Say("   이 폴더의 파일을 바꾸지 못했습니다(" + ex.Message + ") — 받은 판을 임시 폴더에서 엽니다", Tone.Warn); }
            }
            // 2) 쓸 수 없는 곳(읽기 전용 USB 등) — 받은 파일을 %TEMP% 에서 연다
            Log.Say("   받은 새 판을 임시 폴더에서 엽니다: " + fresh, Tone.Info);
            StartAfterExit(fresh, line, null, exe);
            return true;
        }

        // 개발 PC 에서 build.cmd 로 만든 것(C:\dev\dev-env\dist)은 바꾸지 않는다
        static bool IsDevBuild(string dir)
        {
            var dev = Path.Combine(Conf.DevRoot, "dev-env", "dist");
            return string.Equals(Path.GetFullPath(dir).TrimEnd('\\'), dev, StringComparison.OrdinalIgnoreCase);
        }

        // 지난번 바꾸며 남긴 '<이름>.old' — 그 프로그램이 아직 끝나는 중일 수 있어 잠깐 몇 번 더 해 본다(뒤에서)
        static void CleanOld(string exe)
        {
            var old = exe + ".old";
            if (!File.Exists(old)) return;
            var t = new Thread(() =>
            {
                for (int i = 0; i < 20; i++)
                {
                    try { File.SetAttributes(old, FileAttributes.Normal); File.Delete(old); return; } catch { }
                    Thread.Sleep(500);
                }
            }) { IsBackground = true, Name = "old-clean" };
            t.Start();
        }

        // version.txt 첫 줄 (12자리) — 시간 안에 못 받으면 null
        static string FetchVersion(string url, int ms)
        {
            try { if (!NetworkInterface.GetIsNetworkAvailable()) return null; } catch { }
            var task = Task.Run(() => { string s; return Net.TryGetString(url, out s, Math.Max(1, ms / 1000)) ? s : null; });
            try { if (!task.Wait(ms) || task.Result == null) return null; } catch { return null; }
            var first = task.Result.Split('\n').Select(x => x.Trim().TrimStart('\uFEFF')).FirstOrDefault(x => x.Length > 0) ?? "";
            return Regex.IsMatch(first, "^[0-9]{12}$") ? first : (first.Length > 40 ? first.Substring(0, 40) : first);
        }

        // PcSetup.exe·.sha256 을 받아 대조 → 받은 파일 경로. 맞지 않으면 한 번 더 받고, 그래도 다르면 null
        static string Fetch(string baseUrl, string ver)
        {
            string sumText;
            if (!Net.TryGetString(baseUrl + "PcSetup.exe.sha256", out sumText, 15)) { Log.Say("   PcSetup.exe.sha256 을 받지 못했습니다 — 이 판으로 엽니다", Tone.Warn); return null; }
            var m = Regex.Match(sumText ?? "", "\\b[0-9a-fA-F]{64}\\b");
            if (!m.Success) { Log.Say("   PcSetup.exe.sha256 에 SHA256 값이 없습니다 — 이 판으로 엽니다", Tone.Warn); return null; }
            var want = m.Value.ToLowerInvariant();
            var name = "PcSetup-" + ver + ".exe";
            for (int attempt = 1; attempt <= 2; attempt++)
            {
                var path = Net.Download(baseUrl + "PcSetup.exe", name);
                if (Sha256(path) == want) return path;
                try { File.Delete(path); } catch { }   // 받다 만 것·예전에 받아 둔 것 — 지우고 새로 받는다
                if (attempt == 2) Log.Say("   받은 PcSetup.exe 의 SHA256 이 맞지 않습니다 — 이 판으로 엽니다", Tone.Warn);
            }
            return null;
        }

        static string Sha256(string file)
        {
            using (var h = SHA256.Create())
            using (var f = new FileStream(file, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete))
                return BitConverter.ToString(h.ComputeHash(f)).Replace("-", "").ToLowerInvariant();
        }

        // 그 폴더에 파일을 만들 수 있는가
        static bool Writable(string dir)
        {
            try
            {
                var probe = Path.Combine(dir, ".pcsetup-" + Guid.NewGuid().ToString("N") + ".tmp");
                using (new FileStream(probe, FileMode.CreateNew, FileAccess.Write, FileShare.None, 1, FileOptions.DeleteOnClose)) { }
                return true;
            }
            catch { return false; }
        }

        static string ArgLine(string[] args)
        {
            var a = (args ?? new string[0]).Where(x => x != null).ToList();
            if (!a.Contains("--no-update")) a.Add("--no-update");
            return Proc.Args(a.ToArray());
        }

        // 새 판은 지금 프로그램이 '이미 열려 있음' 표시(뮤텍스)를 놓은 뒤에 연다 — 먼저 열면 새 판이 "이미 열려 있습니다" 로 끝난다.
        // 앞 스레드라 Main 이 끝나도 이것이 끝날 때까지 프로세스가 남는다(최대 10초 기다림).
        //   새 판이 열리지 않으면: 바꾼 경우는 예전 파일을 되돌려 열고, 임시 폴더 경우는 지금 판(fallbackExe)을 연다 — 아무것도 안 뜨는 일이 없게
        static void StartAfterExit(string path, string argLine, string oldToRestore, string fallbackExe)
        {
            var t = new Thread(() =>
            {
                try
                {
                    var until = Environment.TickCount + 10000;
                    while (Environment.TickCount - until < 0)
                    {
                        Mutex m;
                        try { if (!Mutex.TryOpenExisting(MutexName, out m)) break; } catch { break; }
                        m.Dispose();
                        Thread.Sleep(50);
                    }
                    if (Launch(path, argLine)) return;
                    if (fallbackExe != null)
                    {
                        Log.Say("   새 판을 열지 못해 이 판으로 엽니다", Tone.Warn);
                        Launch(fallbackExe, argLine);
                        return;
                    }
                    if (oldToRestore == null) return;
                    try
                    {
                        File.Delete(path); File.Move(oldToRestore, path);
                        Log.Say("   새 판을 열지 못해 예전 판으로 되돌렸습니다", Tone.Warn);
                        Launch(path, argLine);
                    }
                    catch (Exception ex)
                    {
                        Log.Say("   예전 판으로 되돌리지 못했습니다(" + ex.Message + ") — 남은 예전 파일을 그대로 엽니다", Tone.Error);
                        if (File.Exists(oldToRestore)) Launch(oldToRestore, argLine);
                    }
                }
                catch (Exception ex) { try { Log.Say("   다시 열기 중 오류: " + ex.Message, Tone.Error); } catch { } }
            }) { IsBackground = false, Name = "restart" };
            t.Start();
        }

        static bool Launch(string path, string argLine)
        {
            var psi = new ProcessStartInfo(path, argLine) { UseShellExecute = false, WorkingDirectory = Path.GetDirectoryName(path) };
            try { using (Process.Start(psi)) { } return true; }
            catch (Win32Exception ex) when (ex.NativeErrorCode == 740)   // 관리자 권한이 필요 — 이 프로그램이 관리자가 아니었을 때
            {
                try { psi.UseShellExecute = true; psi.Verb = "runas"; using (Process.Start(psi)) { } return true; }
                catch (Exception ex2) { Log.Say("   새 판을 열지 못했습니다: " + ex2.Message, Tone.Error); return false; }
            }
            catch (Exception ex) { Log.Say("   새 판을 열지 못했습니다: " + ex.Message, Tone.Error); return false; }
        }

        [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
        static extern bool MoveFileEx(string from, string to, int flags);
        static void DeleteAtReboot(string file) { try { MoveFileEx(file, null, 4 /* MOVEFILE_DELAY_UNTIL_REBOOT */); } catch { } }

        // 받는 동안 띄우는 작은 창 — 따로 스레드에서 돈다(누를 것 없음). 받는 중 n% 는 Log.Status 로 온다
        sealed class Waiting
        {
            Form form;
            readonly object gate = new object();
            bool done;
            Action<string> onStatus;

            public static Waiting Open(string title, string detail)
            {
                var w = new Waiting();
                try
                {
                    var t = new Thread(() => w.Loop(title, detail)) { IsBackground = true, Name = "update-window" };
                    t.SetApartmentState(ApartmentState.STA);
                    t.Start();
                }
                catch { }
                return w;
            }

            void Loop(string title, string detail)
            {
                try
                {
                    var f = new Form
                    {
                        Text = "PC 설치", FormBorderStyle = FormBorderStyle.FixedDialog, MaximizeBox = false, MinimizeBox = false, ControlBox = false,
                        StartPosition = FormStartPosition.CenterScreen, ShowInTaskbar = true, TopMost = true, Font = MainForm.UiFont,
                        AutoScaleMode = AutoScaleMode.Dpi, AutoScaleDimensions = new SizeF(96f, 96f), ClientSize = new Size(420, 120),
                    };
                    try { f.Icon = Icon.ExtractAssociatedIcon(Application.ExecutablePath); } catch { }
                    var head = new Label { Text = title, AutoSize = false, Location = new Point(16, 14), Size = new Size(388, 26), Font = new Font(MainForm.UiFont, FontStyle.Bold) };
                    var bar = new ProgressBar { Style = ProgressBarStyle.Marquee, MarqueeAnimationSpeed = 30, Location = new Point(16, 46), Size = new Size(388, 18) };
                    var st = new Label { Text = detail, AutoSize = false, AutoEllipsis = true, Location = new Point(16, 74), Size = new Size(388, 34), ForeColor = Color.DimGray };
                    f.Controls.AddRange(new Control[] { head, bar, st });
                    lock (gate)
                    {
                        if (done) { f.Dispose(); return; }
                        form = f;
                    }
                    onStatus = text =>
                    {
                        if (string.IsNullOrEmpty(text)) return;
                        try { if (f.IsHandleCreated) f.BeginInvoke(new Action(() => st.Text = text)); } catch { }
                    };
                    Log.Status += onStatus;
                    f.Shown += (s, e) => { lock (gate) if (done) f.Close(); };
                    Application.Run(f);
                }
                catch { }
                finally { if (onStatus != null) Log.Status -= onStatus; }
            }

            public void Close()
            {
                Form f;
                lock (gate) { done = true; f = form; }
                if (f == null) return;
                try { if (f.IsHandleCreated) f.BeginInvoke(new Action(f.Close)); } catch { }
            }
        }
    }
}

namespace System.Runtime.CompilerServices
{
    // .NET Framework 4.8 에는 없어 직접 둔다 — C# 컴파일러가 이 이름을 보고 모듈이 올라올 때(Main 전에) 부른다. 다른 파일에서 또 만들지 말 것
    [AttributeUsage(AttributeTargets.Method, Inherited = false)]
    sealed class ModuleInitializerAttribute : Attribute { }
}
