using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Windows.Forms;

namespace PcSetup
{
    // ⑥ 개발 소스(projects.txt) · 관리(USB)
    public static partial class Defs
    {
        // ⑥ 개발 소스: projects.txt 한 줄 = 항목 하나 (GitHub 로그인이 필요해 기본 체크 해제)
        static partial void RegisterProjects()
        {
            foreach (var p in Data.Projects)
            {
                var folder = Path.Combine(Conf.DevRoot, p.Folder.Replace('/', '\\'));
                var url = p.Url ?? ""; var lnk = p.Link;
                var label = url.Length > 0 ? lnk + " — GitHub 에서 받기 + 바로가기" : lnk + " — 바로가기만 (폴더는 백업에서 직접 복원)";
                // GitHub 에서 받는 것은 폴더가 비어 있지 않아야 된 것 — 받다 실패해 빈 폴더만 남은 PC(예전 판)도 다시 받게
                Func<bool> check = () => (url.Length > 0 ? HasFiles(folder) : Directory.Exists(folder)) && File.Exists(Path.Combine(Env.Desktop, lnk + ".lnk"));
                Add(G6, "proj:" + p.Folder, label, check, () => InstallProject(folder, url, lnk), off: true);
            }
        }

        static bool HasFiles(string dir)
        {
            try { return Directory.Exists(dir) && Directory.EnumerateFileSystemEntries(dir).Any(); } catch { return false; }
        }

        static void InstallProject(string folder, string url, string lnk)
        {
            Directory.CreateDirectory(Path.GetDirectoryName(folder));
            if (url.Length > 0 && !HasFiles(folder))   // 없거나 빈 폴더면 받는다
            {
                var fail = "GitHub 에서 받지 못했습니다: " + url + " — GitHub 로그인·인터넷을 확인하고 이 항목만 다시 설치하세요";
                var git = Git.Exe;
                if (git == null) { Log.Say("   git 이 없습니다 — ⑤ 의 Git 을 먼저 설치하세요", Tone.Warn); throw new Exception(fail); }
                // 로그인이 필요하면 Git 자격 증명 관리자가 자기 창(브라우저 로그인)을 띄운다 — 콘솔에서 묻다 멈추지 않게 GIT_TERMINAL_PROMPT=0 만
                Log.Say("   GitHub 에서 받습니다 — 로그인 창이 뜨면 GitHub 계정으로 로그인하세요(브라우저가 열립니다)");
                var env = new Dictionary<string, string> { { "GIT_TERMINAL_PROMPT", "0" } };
                var r = Proc.Run(git, Proc.Args("clone", url, folder), 900, Log.Raw, Encoding.UTF8, Path.GetDirectoryName(folder), env);
                // 받지 못하면(로그인 취소·인터넷·받다 만 것) 실패로 끝낸다 — 받다 만 폴더는 지운다(없거나 빈 폴더일 때만 받으므로 사람의 파일은 없다).
                // 빈 폴더·바로가기가 남아 '설치됨' 으로 보이지 않게, 다음에 다시 받게
                if (r.Code != 0 || !HasFiles(folder))
                {
                    if (r.TimedOut || !r.Started) Log.Say("   " + r.Err, Tone.Warn);
                    DeleteTree(folder);
                    throw new Exception(fail);
                }
            }
            if (!Directory.Exists(folder)) { Log.Say("   폴더가 없습니다: " + folder + " — 백업을 풀어 넣은 뒤 이 항목만 다시 실행하세요"); Directory.CreateDirectory(folder); }
            var claude = Path.Combine(Env.Home, @".local\bin\claude.exe");
            var args = "--title \"" + lnk + "\" -d . \"C:\\Program Files\\PowerShell\\7\\pwsh.exe\" -NoLogo -NoExit -Command \"& '" + claude + "'\"";
            foreach (var dir in new[] { Env.Desktop, Env.StartMenuPrograms })
                Shell.Shortcut(Path.Combine(dir, lnk + ".lnk"), Path.Combine(Env.LocalAppData, @"Microsoft\WindowsApps\wt.exe"), args, folder, claude + ",0");
        }

        // 폴더 통째 지우기 — git 이 만든 읽기 전용 파일(.git\objects)도.
        // 시간 초과로 git 을 끝내면 그 자식(git-remote-https·index-pack)이 잠시 파일을 쥐고 있다 — 몇 번 더 해 본다
        // (못 지우면 반쪽 폴더가 '파일 있음' 으로 보여 다음에 받지 않고 설치됨이 된다)
        static void DeleteTree(string dir)
        {
            for (var i = 0; ; i++)
            {
                try
                {
                    if (!Directory.Exists(dir)) return;
                    foreach (var f in Directory.EnumerateFiles(dir, "*", SearchOption.AllDirectories))
                        try { File.SetAttributes(f, FileAttributes.Normal); } catch { }
                    Directory.Delete(dir, true);
                    return;
                }
                catch (Exception ex)
                {
                    if (i < 10) { System.Threading.Thread.Sleep(1000); continue; }
                    Log.Say("   받다 만 폴더를 지우지 못했습니다: " + dir + " (" + ex.Message + ") — 이 폴더를 직접 지운 뒤 이 항목만 다시 설치하세요", Tone.Warn);
                    return;
                }
            }
        }

        // 관리 — USB 맨 위에 이 프로그램(PC 설치.exe)을 넣는다. USB 로 다른 PC 를 설치할 때 이것을 실행한다
        const string UsbExeName = "PC 설치.exe";

        static partial void RegisterManage()
        {
            var v = BuildInfo.Version;
            var pretty = v.Length == 12 ? v.Substring(0, 4) + "-" + v.Substring(4, 2) + "-" + v.Substring(6, 2) + " " + v.Substring(8, 2) + ":" + v.Substring(10, 2) : v;
            Add(G9, "usbkit", "USB 에 이 프로그램(" + UsbExeName + ") 넣기 — 이 버전(" + pretty + ")으로", () =>
            {
                var root = UsbRoot();
                if (root == null) return false;
                var dst = Path.Combine(root, UsbExeName);
                return File.Exists(dst) && SameFile(Application.ExecutablePath, dst);
            }, () =>
            {
                var root = UsbRoot();
                if (root == null) throw new Exception("USB 를 찾지 못했습니다 — PC설치 폴더가 있는 USB 나 Ventoy USB 를 꽂고 이 항목만 다시 실행하세요");
                var kit = Path.Combine(root, "PC설치");
                Directory.CreateDirectory(Path.Combine(kit, "네트워크 드라이버"));
                Directory.CreateDirectory(Path.Combine(kit, "도구"));
                var self = Path.GetFullPath(Application.ExecutablePath);
                var dst = Path.Combine(root, UsbExeName);
                if (!string.Equals(self, Path.GetFullPath(dst), StringComparison.OrdinalIgnoreCase))
                {
                    // 받다 USB 를 뽑아도 반쪽 실행 파일이 남지 않게 .part 로 복사한 뒤 바꾼다
                    var part = dst + ".part";
                    try
                    {
                        File.Copy(self, part, true);
                        if (File.Exists(dst)) { File.SetAttributes(dst, FileAttributes.Normal); File.Delete(dst); }
                        File.Move(part, dst);
                    }
                    catch (Exception ex)
                    {
                        try { if (File.Exists(part)) File.Delete(part); } catch { }
                        throw new Exception("USB 에 넣지 못했습니다: " + dst + " (" + ex.Message + ") — USB 가 쓰기 잠김·꽉 참인지, 그 파일을 실행 중인 창이 없는지 확인하고 이 항목만 다시 실행하세요");
                    }
                }
                Log.Say("   " + dst + " 를 버전 " + pretty + " 으로 맞췄습니다 — 기종마다 랜 드라이버 폴더를 '" + Path.Combine(kit, "네트워크 드라이버") + "' 에 넣어 두세요");
            }, off: true);
        }

        // USB 맨 위 — PC설치 폴더가 있는 드라이브, 없으면 이름이 'Ventoy' 인 드라이브
        static string UsbRoot()
        {
            var k = Usb.FindKit() ?? State.Usb;
            if (k != null) return Path.GetPathRoot(k);
            foreach (var d in DriveInfo.GetDrives())
            {
                try
                {
                    if (d.DriveType != DriveType.Removable && d.DriveType != DriveType.Fixed) continue;
                    if (d.IsReady && string.Equals(d.VolumeLabel, "Ventoy", StringComparison.OrdinalIgnoreCase)) return d.RootDirectory.FullName;
                }
                catch { }
            }
            return null;
        }

        // 두 파일이 바이트까지 같은지 (크기 → SHA256)
        static bool SameFile(string a, string b)
        {
            try
            {
                if (new FileInfo(a).Length != new FileInfo(b).Length) return false;
                using (var sha = SHA256.Create())
                {
                    byte[] ha, hb;
                    using (var s = new FileStream(a, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete)) ha = sha.ComputeHash(s);
                    using (var s = new FileStream(b, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete)) hb = sha.ComputeHash(s);
                    return ha.SequenceEqual(hb);
                }
            }
            catch { return false; }
        }
    }
}
