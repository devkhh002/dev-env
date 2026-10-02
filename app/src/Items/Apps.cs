using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading;
using Microsoft.Win32;

namespace PcSetup
{
    // ④ 도구·앱 — winget 먼저, 그다음 catalog.txt 의 앱들, TrafficMonitor · 반디집 광고 · 한/영 · Tailscale · Sunshine
    public static partial class Defs
    {
        static partial void RegisterApps()
        {
            Add(G4, "winget", "winget — 아래 앱들을 늘 최신판으로 설치하는 도구", () => Winget.Alive(), () => Winget.InstallSelf());
            foreach (var e in Data.Catalog.Where(x => x.Group != "개발 환경")) AddCatalogItem(e);

            Add(G4 + "/도구", "trafficmonitor", "TrafficMonitor — 작업 표시줄에 속도 표시 · 로그인할 때 자동 실행 (공식 최신판)", AppsKit.TmCheck, AppsKit.TmInstall);
            Add(G4 + "/기본 앱", "bandiad", "반디집 광고 막기 (광고·분석·알림·업데이트 확인 주소를 hosts 로 — 방화벽이 꺼져 있어도 된다)", () => AppsKit.BandiMissing().Count == 0, AppsKit.BandiInstall);
            Add(G4 + "/원격", "hangul", "한/영 전환 (AutoHotkey 관리자 권한 + 10분마다 자동 확인·복구)", AppsKit.HangulCheck, AppsKit.HangulInstall);
            Add(G4 + "/원격", "tailscale", "Tailscale (원격 접속 VPN)", () => File.Exists(Path.Combine(Env.ProgramFiles, @"Tailscale\tailscale.exe")), () =>
            {
                Proc.RunWait(Net.Download("https://pkgs.tailscale.com/stable/tailscale-setup-latest.exe", "tailscale.exe"), "/quiet");
            });
            var sv = Conf.V["Sunshine"];
            Add(G4 + "/원격", "sunshine", "Sunshine " + sv + " (Moonlight 원격 호스트)", () => File.Exists(Path.Combine(Env.ProgramFiles, @"Sunshine\sunshine.exe")), () =>
            {
                var p = Net.Download("https://github.com/LizardByte/Sunshine/releases/download/" + sv + "/Sunshine-Windows-AMD64-installer.msi", "sunshine-" + sv + ".msi");
                Proc.RunWait(Path.Combine(Env.System32, "msiexec.exe"), "/i " + Proc.Quote(p) + " /qn /norestart");
            });
        }
    }

    // winget 자신 — LTSC 에는 없어서 GitHub 릴리스(앱 설치 관리자 + 의존 패키지)로 직접 깐다
    public static partial class Winget
    {
        static partial void InstallSelfImpl()
        {
            Log.Say("   winget 설치 1/3: GitHub 에서 설치 파일을 받습니다(합쳐서 약 300MB — 몇 분 걸릴 수 있다)");
            var rel = AppsKit.LatestRelease("microsoft/winget-cli");
            var bundle = AppsKit.FetchAsset(rel, "Microsoft.DesktopAppInstaller_8wekyb3d8bbwe.msixbundle");
            var depZip = AppsKit.FetchAsset(rel, "DesktopAppInstaller_Dependencies.zip");
            var lic = AppsKit.FetchAsset(rel, "*License1.xml");

            // 의존 패키지 묶음에서 x64 의 .appx·.msix 만 꺼낸다 (예전에 꺼낸 것은 지운다 — 낡은 판이 섞이지 않게)
            Log.Say("   winget 설치 2/3: 필요한 64비트 파일만 꺼냅니다");
            var outDir = Path.Combine(Net.Cache, "wgdeps");
            try { if (Directory.Exists(outDir)) Directory.Delete(outDir, true); } catch { }
            Directory.CreateDirectory(outDir);
            using (var zip = ZipFile.OpenRead(depZip))
                foreach (var en in zip.Entries)
                    if (Regex.IsMatch(en.FullName, "x64", RegexOptions.IgnoreCase) && Regex.IsMatch(en.Name, @"\.(appx|msix)$", RegexOptions.IgnoreCase))
                        en.ExtractToFile(Path.Combine(outDir, en.Name), true);
            var deps = Directory.GetFiles(outDir, "*", SearchOption.AllDirectories).Where(f => Regex.IsMatch(f, @"\.(appx|msix)$", RegexOptions.IgnoreCase)).ToList();

            Log.Say(string.Format("   winget 설치 3/3: 등록합니다(1~2분, 의존 패키지 {0}개)", deps.Count));
            // 이 사용자에게 (반쯤 깔린 것도 다시 등록) — 실패해도 모든 사용자용 등록은 해 본다
            Exception userErr = null;
            try { Appx.Add(bundle, deps); } catch (Exception ex) { userErr = ex; }
            // 모든 사용자(새 계정)에게 — 라이선스와 함께 Windows 내장 dism 으로. 안 되면 설치된 패키지로 등록
            var dism = Proc.Run(Path.Combine(Env.System32, "dism.exe"), "/Online /NoRestart /English /Add-ProvisionedAppxPackage /PackagePath:" + Proc.Quote(bundle) + string.Concat(deps.Select(d => " /DependencyPackagePath:" + Proc.Quote(d))) + " /LicensePath:" + Proc.Quote(lic), 900, enc: Proc.Oem);
            if (dism.Code != 0 && dism.Code != 3010)   // 3010 = 됨(재부팅하면 마무리)
            {
                var last = (dism.Text ?? "").Split('\n').Select(x => x.Trim()).LastOrDefault(x => x.Length > 0) ?? "";
                Log.Say("   (dism 으로 모든 사용자용 등록 실패 0x" + dism.Code.ToString("X8") + " " + last + ")", Tone.Dim);
                var fam = Appx.FamilyName("Microsoft.DesktopAppInstaller");
                if (fam != null) Appx.ProvisionForAll(fam);
            }
            Env.RefreshPath();
            if (userErr != null && !Alive(true)) throw new Exception("winget 등록 실패: " + userErr.Message);
        }
    }

    // ④ 의 개별 항목이 쓰는 것
    static class AppsKit
    {
        // GitHub 최신 릴리스 (한 번 받아 여러 파일에 쓴다)
        public static Dictionary<string, object> LatestRelease(string repo)
        {
            return (Dictionary<string, object>)Net.Json(Net.GetString("https://api.github.com/repos/" + repo + "/releases/latest", 30, new Dictionary<string, string> { { "Accept", "application/vnd.github+json" } }));
        }
        // 릴리스에서 이름(* 가능)이 맞는 첫 파일을 받아 경로를 돌려준다
        public static string FetchAsset(Dictionary<string, object> rel, string pattern, bool regex = false)
        {
            var rx = regex ? new Regex(pattern, RegexOptions.IgnoreCase) : new Regex("^" + Regex.Escape(pattern).Replace("\\*", ".*") + "$", RegexOptions.IgnoreCase);
            foreach (Dictionary<string, object> a in (object[])rel["assets"])
            {
                var name = (string)a["name"];
                if (rx.IsMatch(name)) return Net.Download((string)a["browser_download_url"], name, Convert.ToInt64(a["size"]));
            }
            throw new IOException("최신 릴리스에서 '" + pattern + "' 파일을 찾지 못했습니다");
        }

        // 예약 작업 XML 에서 한 값 (없으면 null)
        static string TaskValue(string xml, string tag)
        {
            var m = Regex.Match(xml ?? "", "<" + tag + @"(?:\s[^>]*)?>([^<]*)</" + tag + ">");
            return m.Success ? System.Net.WebUtility.HtmlDecode(m.Groups[1].Value).Trim() : null;
        }
        static bool TaskHighest(string xml) { return TaskValue(xml, "RunLevel") == "HighestAvailable"; }

        // 예약 작업 XML — 로그인할 때(사용자) [+ 10분마다] · 가장 높은 권한 · 시간 제한 없음 · 배터리여도 실행 · 이미 떠 있으면 안 띄움
        static string TaskXml(string exe, string args, string description, string logonDelay, bool every10Min, bool startWhenAvailable)
        {
            var user = Tasks.Escape(Tasks.UserId);
            var sb = new StringBuilder();
            sb.Append("<?xml version=\"1.0\" encoding=\"UTF-16\"?>\r\n");
            sb.Append("<Task version=\"1.2\" xmlns=\"http://schemas.microsoft.com/windows/2004/02/mit/task\">\r\n");
            sb.Append("  <RegistrationInfo>\r\n");
            if (!string.IsNullOrEmpty(description)) sb.Append("    <Description>" + Tasks.Escape(description) + "</Description>\r\n");
            sb.Append("  </RegistrationInfo>\r\n");
            sb.Append("  <Triggers>\r\n    <LogonTrigger>\r\n      <Enabled>true</Enabled>\r\n");
            if (!string.IsNullOrEmpty(logonDelay)) sb.Append("      <Delay>" + logonDelay + "</Delay>\r\n");
            sb.Append("      <UserId>" + user + "</UserId>\r\n    </LogonTrigger>\r\n");
            if (every10Min)
                sb.Append("    <TimeTrigger>\r\n      <Enabled>true</Enabled>\r\n      <StartBoundary>" + DateTime.Now.AddMinutes(1).ToString("yyyy-MM-dd'T'HH:mm:ss", System.Globalization.CultureInfo.InvariantCulture) + "</StartBoundary>\r\n" +
                          "      <Repetition>\r\n        <Interval>PT10M</Interval>\r\n        <Duration>P3650D</Duration>\r\n        <StopAtDurationEnd>false</StopAtDurationEnd>\r\n      </Repetition>\r\n    </TimeTrigger>\r\n");
            sb.Append("  </Triggers>\r\n");
            sb.Append("  <Principals>\r\n    <Principal id=\"Author\">\r\n      <UserId>" + user + "</UserId>\r\n      <LogonType>InteractiveToken</LogonType>\r\n      <RunLevel>HighestAvailable</RunLevel>\r\n    </Principal>\r\n  </Principals>\r\n");
            sb.Append("  <Settings>\r\n    <MultipleInstancesPolicy>IgnoreNew</MultipleInstancesPolicy>\r\n    <DisallowStartIfOnBatteries>false</DisallowStartIfOnBatteries>\r\n    <StopIfGoingOnBatteries>false</StopIfGoingOnBatteries>\r\n");
            sb.Append("    <AllowHardTerminate>true</AllowHardTerminate>\r\n    <StartWhenAvailable>" + (startWhenAvailable ? "true" : "false") + "</StartWhenAvailable>\r\n");
            sb.Append("    <AllowStartOnDemand>true</AllowStartOnDemand>\r\n    <Enabled>true</Enabled>\r\n    <Hidden>false</Hidden>\r\n    <ExecutionTimeLimit>PT0S</ExecutionTimeLimit>\r\n  </Settings>\r\n");
            sb.Append("  <Actions Context=\"Author\">\r\n    <Exec>\r\n      <Command>" + Tasks.Escape(exe) + "</Command>\r\n");
            if (!string.IsNullOrEmpty(args)) sb.Append("      <Arguments>" + Tasks.Escape(args) + "</Arguments>\r\n");
            sb.Append("    </Exec>\r\n  </Actions>\r\n</Task>\r\n");
            return sb.ToString();
        }

        // ── TrafficMonitor ─────────────────────────────────────────────
        // 공식 GitHub 최신판을 C:\Tools\TrafficMonitor 에 깔고, 지금 쓰는 설정(작업 표시줄에 속도 표시)을 넣고, 로그인할 때 자동 실행
        // (winget 판은 설정 없이 기본값이라 작업 표시줄에 안 나오고 자동 실행도 안 된다)
        // 실행 파일이 '관리자로만 실행' 이라 시작 프로그램 폴더·Run 으로는 로그인 때 막히거나 묻는다
        // → TrafficMonitor 가 스스로 쓰는 것과 같은 예약 작업(\TrafficMonitor\Autorun for 사용자, 가장 높은 권한)으로 띄운다
        const string TmDir = @"C:\Tools\TrafficMonitor";
        static string TmExe { get { return Path.Combine(TmDir, "TrafficMonitor.exe"); } }
        static string TmTask { get { return @"\TrafficMonitor\Autorun for " + Environment.UserName; } }

        static bool TmTaskOk()
        {
            var x = Tasks.Xml(TmTask);
            if (x == null) return false;
            return string.Equals((TaskValue(x, "Command") ?? "").Trim('"'), TmExe, StringComparison.OrdinalIgnoreCase) && TaskHighest(x);
        }
        public static bool TmCheck()
        {
            if (!File.Exists(TmExe) || !TmTaskOk()) return false;
            var ini = Path.Combine(TmDir, "config.ini");
            return File.Exists(ini) && File.ReadAllLines(ini).Any(l => Regex.IsMatch(l, @"^\s*show_task_bar_wnd\s*=\s*true", RegexOptions.IgnoreCase));
        }
        public static void TmInstall()
        {
            var zip = FetchAsset(LatestRelease("zhongyang219/TrafficMonitor"), @"_x64\.zip$", regex: true);   // Lite 가 아닌 전체판

            // 설정·사용 기록: 이미 깐 곳 → 예전에 쓰던 다운로드 폴더 → 저장소 기본 설정 순으로 가져온다
            var from = new[] { TmDir, Path.Combine(Env.Home, @"Downloads\TrafficMonitor") }.FirstOrDefault(d => File.Exists(Path.Combine(d, "config.ini")));
            var keep = Path.Combine(Net.Cache, "tm-keep");
            try { if (Directory.Exists(keep)) Directory.Delete(keep, true); } catch { }
            Directory.CreateDirectory(keep);
            if (from != null)
            {
                foreach (var f in new[] { "config.ini", "history_traffic.dat" }) try { File.Copy(Path.Combine(from, f), Path.Combine(keep, f), true); } catch { }
                Log.Say("   설정을 가져옵니다: " + from);
            }
            else
            {
                File.WriteAllBytes(Path.Combine(keep, "config.ini"), Data.ResourceBytes("trafficmonitor.config.ini"));
                Log.Say("   저장소의 기본 설정(작업 표시줄 표시)을 넣습니다");
            }
            foreach (var p in Process.GetProcessesByName("TrafficMonitor")) try { p.Kill(); p.WaitForExit(5000); } catch { }   // 파일을 바꾸려면 꺼야 한다
            Thread.Sleep(1000);

            // 예전 설치 목록이 winget 으로 깐 TrafficMonitor(설정 없는 판)는 지운다 — 두 벌이 되지 않게
            // (winget 은 '사용자 범위로 깐 것은 관리자 창에서 못 지운다' 며 거절한다 → winget 이 깔 때 만든 폴더·PATH·제어판 항목을 직접 지운다)
            using (var un = Registry.CurrentUser.OpenSubKey(@"Software\Microsoft\Windows\CurrentVersion\Uninstall", true))
                if (un != null)
                    foreach (var name in un.GetSubKeyNames().Where(n => n.StartsWith("zhongyang219.TrafficMonitor", StringComparison.OrdinalIgnoreCase)))
                    {
                        string loc; using (var k = un.OpenSubKey(name)) loc = k == null ? null : k.GetValue("InstallLocation") as string;
                        var pkgs = Path.Combine(Env.LocalAppData, @"Microsoft\WinGet\Packages") + "\\";
                        if (!string.IsNullOrEmpty(loc) && loc.StartsWith(pkgs, StringComparison.OrdinalIgnoreCase))
                        {
                            try { Directory.Delete(loc, true); } catch { }
                            var up = Environment.GetEnvironmentVariable("Path", EnvironmentVariableTarget.User);
                            if (!string.IsNullOrEmpty(up) && up.IndexOf(loc, StringComparison.OrdinalIgnoreCase) >= 0)
                                Environment.SetEnvironmentVariable("Path", string.Join(";", up.Split(';').Where(s => s.Length > 0 && !s.StartsWith(loc, StringComparison.OrdinalIgnoreCase))), EnvironmentVariableTarget.User);
                        }
                        un.DeleteSubKeyTree(name, false);
                        Log.Say("   예전에 winget 으로 깐 TrafficMonitor 를 지웠습니다");
                    }

            var x = Path.Combine(Net.Cache, "tm-zip");
            try { if (Directory.Exists(x)) Directory.Delete(x, true); } catch { }
            ZipFile.ExtractToDirectory(zip, x);
            var exe = Directory.GetFiles(x, "TrafficMonitor.exe", SearchOption.AllDirectories).FirstOrDefault();
            if (exe == null) throw new Exception("받은 파일 안에 TrafficMonitor.exe 가 없습니다: " + zip);
            CopyTree(Path.GetDirectoryName(exe), TmDir);
            foreach (var f in Directory.GetFiles(keep)) File.Copy(f, Path.Combine(TmDir, Path.GetFileName(f)), true);
            File.WriteAllText(Path.Combine(TmDir, "global_cfg.ini"), "[config]\r\nportable_mode = true\r\n", Encoding.ASCII);

            // 옛 자동 실행(시작 프로그램 바로가기·Run)은 지운다 — 예약 작업과 겹치면 두 번 떠서 '이미 실행 중' 창이 뜬다
            var st = Environment.GetFolderPath(Environment.SpecialFolder.Startup);
            if (Directory.Exists(st))
                foreach (var l in Directory.GetFiles(st, "*.lnk"))
                    try { var s = Shell.ReadShortcut(l); if (s != null && (s[0] ?? "").EndsWith(@"\TrafficMonitor.exe", StringComparison.OrdinalIgnoreCase)) File.Delete(l); } catch { }
            Reg.DeleteValue(@"HKEY_CURRENT_USER\Software\Microsoft\Windows\CurrentVersion\Run", "TrafficMonitor");

            // 로그인할 때 자동 실행 — TrafficMonitor 자기 설정과 같게(3초 뒤, 시간 제한 없음, 배터리여도 실행). 바로 띄운다 — 로그인 때와 똑같이 그 작업으로
            Tasks.Create(TmTask, TaskXml(TmExe, null, null, "PT3S", false, false));
            Tasks.Run(TmTask);
            Thread.Sleep(3000);
        }
        static void CopyTree(string src, string dst)
        {
            Directory.CreateDirectory(dst);
            foreach (var f in Directory.GetFiles(src)) File.Copy(f, Path.Combine(dst, Path.GetFileName(f)), true);
            foreach (var d in Directory.GetDirectories(src)) CopyTree(d, Path.Combine(dst, Path.GetFileName(d)));
        }

        // ── 반디집 광고 ────────────────────────────────────────────────
        // 무료판의 광고·분석·알림·업데이트 확인을 hosts 로 막는다 — 방화벽이 꺼져 있어도 동작한다
        // (주소는 반디집 파일 안에 적힌 것에서 뽑았다. 반디소프트 자기 주소만 — 구글 광고 주소는 브라우저까지 망가뜨려 두지 않는다)
        static string HostsFile { get { return Path.Combine(Env.System32, @"drivers\etc\hosts"); } }
        static readonly string[] BandiBlock = { "adv.bandi.so", "ana.bandi.so", "log.bandi.so", "ver.bandi.so", "notice.bandisoft.com", "go.bandisoft.com" };
        public static List<string> BandiMissing()
        {
            string[] h; try { h = File.ReadAllLines(HostsFile); } catch { h = new string[0]; }
            return BandiBlock.Where(d => !h.Any(l => Regex.IsMatch(l, @"^\s*0\.0\.0\.0\s+" + Regex.Escape(d) + @"\s*$", RegexOptions.IgnoreCase))).ToList();
        }
        public static void BandiInstall()
        {
            var add = BandiMissing();
            if (add.Count > 0) File.AppendAllText(HostsFile, "\r\n# dev-env: Bandizip ads/telemetry/notice/update-check block\r\n" + string.Join("\r\n", add.Select(d => "0.0.0.0 " + d)) + "\r\n", Encoding.ASCII);
            Proc.Run(Path.Combine(Env.System32, "ipconfig.exe"), "/flushdns", 30, enc: Proc.Oem);
        }

        // ── 한/영 전환 (AutoHotkey) ────────────────────────────────────
        const string HangulTask = "hangul-ahk";
        static string AhkExe { get { return Path.Combine(Env.ProgramFiles, @"AutoHotkey\v2\AutoHotkey64.exe"); } }
        static string AhkScript { get { return Path.Combine(Env.ProgramData, "hangul.ahk"); } }
        public static bool HangulCheck()
        {
            var x = Tasks.Xml(HangulTask);
            return x != null && TaskHighest(x) && File.Exists(AhkScript) && Process.GetProcessesByName("AutoHotkey64").Length > 0;
        }
        public static void HangulInstall()
        {
            var exe = AhkExe;
            if (!File.Exists(exe))
            {
                // (AutoHotkey 설치 파일은 서명이 없다 — 서명 검사를 하면 늘 막힌다)
                Proc.RunWait(Net.Download("https://www.autohotkey.com/download/ahk-v2.exe", "ahk-v2.exe"), "/silent");
                for (int i = 0; i < 60 && !File.Exists(exe); i++) Thread.Sleep(500);   // 설치가 다른 프로세스로 이어질 수 있어 잠깐 더 기다린다
                if (!File.Exists(exe)) throw new Exception("AutoHotkey 가 깔리지 않았습니다: " + exe);
            }
            var ahk = AhkScript;
            File.WriteAllBytes(ahk, Data.ResourceBytes("hangul.ahk"));
            // 옛 방식(시작 폴더) 정리 — 예약 작업이 대신한다
            var s = Environment.GetFolderPath(Environment.SpecialFolder.CommonStartup);
            foreach (var f in new[] { "hangul.ahk", "hangul.lnk" }) try { File.Delete(Path.Combine(s, f)); } catch { }
            // 떠 있는 것(예전 일반 권한 실행분 포함)은 끈다 — #SingleInstance Ignore 라 그대로 두면 새로 띄운 쪽이 빠진다
            if (Tasks.Exists(HangulTask)) Tasks.End(HangulTask);
            foreach (var p in Wmi.Query("SELECT ProcessId, CommandLine FROM Win32_Process WHERE Name LIKE 'AutoHotkey%'"))
            {
                var cl = p.ContainsKey("CommandLine") ? p["CommandLine"] as string : null;
                if (cl == null || cl.IndexOf("hangul.ahk", StringComparison.OrdinalIgnoreCase) < 0) continue;
                try { using (var pr = Process.GetProcessById(Convert.ToInt32(p["ProcessId"]))) { pr.Kill(); pr.WaitForExit(5000); } } catch { }
            }

            // 로그인할 때 + 10분마다: 스크립트가 죽어 있으면 다시 띄운다 (#SingleInstance Ignore 라 중복 안 뜬다)
            // 관리자 권한(Highest)으로 띄운다 — 일반 권한이면 관리자 창(관리자 PowerShell·VirtualBox 등)에 한/영 키를 못 보낸다(UIPI)
            if (Tasks.Exists(HangulTask)) Tasks.Delete(HangulTask);
            Tasks.Create(HangulTask, TaskXml(exe, "\"" + ahk + "\"", "원격 한/영 전환 스크립트 실행·감시", null, true, true));
            Tasks.Run(HangulTask);

            // 바탕화면: 이상할 때 두 번 누르면 즉시 복구 — 예약 작업을 껐다 켠다(직접 띄우면 일반 권한이 돼서 관리자 창에서 안 된다)
            Shell.Shortcut(Path.Combine(Env.Desktop, "한영 다시 시작.lnk"), Path.Combine(Env.System32, "cmd.exe"), "/c schtasks /end /tn hangul-ahk & schtasks /run /tn hangul-ahk", Env.ProgramData, exe + ",0", 7);
            // 작업이 스크립트를 띄울 때까지 잠깐 기다린다(바로 확인하면 아직 안 떠 있다)
            for (int i = 0; i < 20 && Process.GetProcessesByName("AutoHotkey64").Length == 0; i++) Thread.Sleep(250);
        }
    }
}
