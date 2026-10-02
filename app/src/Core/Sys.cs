using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Management;
using System.Runtime.InteropServices;
using System.Text;
using Microsoft.Win32;

namespace PcSetup
{
    // 환경 변수·PATH·실행 파일 찾기
    public static class Env
    {
        public static string Home { get { return Environment.GetFolderPath(Environment.SpecialFolder.UserProfile); } }
        public static string AppData { get { return Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData); } }
        public static string LocalAppData { get { return Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData); } }
        public static string ProgramData { get { return Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData); } }
        public static string ProgramFiles { get { return Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles); } }
        public static string Desktop { get { return Environment.GetFolderPath(Environment.SpecialFolder.DesktopDirectory); } }
        public static string StartMenuPrograms { get { return Environment.GetFolderPath(Environment.SpecialFolder.Programs); } }
        public static string Windows { get { return Environment.GetFolderPath(Environment.SpecialFolder.Windows); } }
        public static string System32 { get { return Environment.SystemDirectory; } }

        // 방금 깐 프로그램이 보이게 PATH 를 레지스트리에서 다시 읽는다 (+ npm 전역·Claude Code 위치)
        public static void RefreshPath()
        {
            var m = Environment.GetEnvironmentVariable("Path", EnvironmentVariableTarget.Machine) ?? "";
            var u = Environment.GetEnvironmentVariable("Path", EnvironmentVariableTarget.User) ?? "";
            Environment.SetEnvironmentVariable("Path", m + ";" + u + ";" + Path.Combine(AppData, "npm") + ";" + Path.Combine(Home, @".local\bin"));
        }

        // PATH 에서 실행 파일 찾기 (확장자 없이 주면 .exe·.cmd·.bat 순)
        public static string Which(string name)
        {
            var exts = Path.HasExtension(name) ? new[] { "" } : new[] { ".exe", ".cmd", ".bat", ".com" };
            foreach (var d in (Environment.GetEnvironmentVariable("Path") ?? "").Split(';'))
            {
                if (string.IsNullOrWhiteSpace(d)) continue;
                foreach (var e in exts)
                {
                    try { var p = Path.Combine(Environment.ExpandEnvironmentVariables(d.Trim().Trim('"')), name + e); if (File.Exists(p)) return p; } catch { }
                }
            }
            return null;
        }

        // 사용자 PATH 에 폴더 더하기(없을 때만)
        public static void AddUserPath(string dir)
        {
            var u = Environment.GetEnvironmentVariable("Path", EnvironmentVariableTarget.User) ?? "";
            if (u.Split(';').Any(x => x.Trim().TrimEnd('\\').Equals(dir.TrimEnd('\\'), StringComparison.OrdinalIgnoreCase))) return;
            Environment.SetEnvironmentVariable("Path", (u.TrimEnd(';') + ";" + dir).TrimStart(';'), EnvironmentVariableTarget.User);
        }
    }

    // 레지스트리 — 경로는 "HKEY_CURRENT_USER\..." / "HKEY_LOCAL_MACHINE\..." (64비트 보기)
    public static class Reg
    {
        static RegistryKey Root(string path, out string sub)
        {
            var i = path.IndexOf('\\'); var hive = path.Substring(0, i); sub = path.Substring(i + 1);
            switch (hive.ToUpperInvariant())
            {
                case "HKLM": case "HKEY_LOCAL_MACHINE": return RegistryKey.OpenBaseKey(RegistryHive.LocalMachine, RegistryView.Registry64);
                case "HKCU": case "HKEY_CURRENT_USER": return RegistryKey.OpenBaseKey(RegistryHive.CurrentUser, RegistryView.Registry64);
                case "HKCR": case "HKEY_CLASSES_ROOT": return RegistryKey.OpenBaseKey(RegistryHive.ClassesRoot, RegistryView.Registry64);
                default: throw new ArgumentException(path);
            }
        }
        public static object Get(string path, string name)
        {
            string sub; using (var root = Root(path, out sub)) using (var k = root.OpenSubKey(sub)) return k == null ? null : k.GetValue(name);
        }
        public static bool KeyExists(string path)
        {
            string sub; using (var root = Root(path, out sub)) using (var k = root.OpenSubKey(sub)) return k != null;
        }
        public static void Set(string path, string name, object value, RegistryValueKind kind = RegistryValueKind.Unknown)
        {
            string sub; using (var root = Root(path, out sub)) using (var k = root.CreateSubKey(sub))
            {
                if (kind == RegistryValueKind.Unknown) k.SetValue(name ?? "", value); else k.SetValue(name ?? "", value, kind);
            }
        }
        public static void CreateKey(string path) { string sub; using (var root = Root(path, out sub)) using (root.CreateSubKey(sub)) { } }
        public static void DeleteKey(string path) { string sub; using (var root = Root(path, out sub)) root.DeleteSubKeyTree(sub, false); }
        public static void DeleteValue(string path, string name) { string sub; using (var root = Root(path, out sub)) using (var k = root.OpenSubKey(sub, true)) if (k != null) k.DeleteValue(name, false); }
        public static int? Dword(string path, string name) { var v = Get(path, name); return v is int ? (int?)(int)v : null; }
    }

    // 제어판 '프로그램 추가/제거' 목록
    public static class Arp
    {
        public sealed class Entry { public string Key, Name, Version, InstallLocation, Hive; }
        public static List<Entry> All()
        {
            var list = new List<Entry>();
            foreach (var spec in new[] { new { H = RegistryHive.LocalMachine, V = RegistryView.Registry64 }, new { H = RegistryHive.LocalMachine, V = RegistryView.Registry32 }, new { H = RegistryHive.CurrentUser, V = RegistryView.Registry64 } })
            {
                try
                {
                    using (var root = RegistryKey.OpenBaseKey(spec.H, spec.V))
                    using (var k = root.OpenSubKey(@"Software\Microsoft\Windows\CurrentVersion\Uninstall"))
                    {
                        if (k == null) continue;
                        foreach (var n in k.GetSubKeyNames())
                            using (var s = k.OpenSubKey(n))
                            {
                                var dn = s == null ? null : s.GetValue("DisplayName") as string;
                                if (string.IsNullOrEmpty(dn)) continue;
                                list.Add(new Entry { Key = n, Name = dn, Version = s.GetValue("DisplayVersion") as string, InstallLocation = s.GetValue("InstallLocation") as string, Hive = spec.H == RegistryHive.CurrentUser ? "HKCU" : "HKLM" });
                            }
                    }
                }
                catch { }
            }
            return list;
        }
        public static bool Has(string part) { return All().Any(e => e.Name.IndexOf(part, StringComparison.OrdinalIgnoreCase) >= 0); }
        public static bool HasExact(string name) { return All().Any(e => e.Name.Equals(name, StringComparison.OrdinalIgnoreCase)); }
        public static string Version(string part) { var e = All().FirstOrDefault(x => x.Name.IndexOf(part, StringComparison.OrdinalIgnoreCase) >= 0); return e == null ? null : e.Version; }
    }

    // 바로가기 (.lnk)
    public static class Shell
    {
        public static void Shortcut(string lnkPath, string target, string args = "", string workDir = "", string icon = null, int windowStyle = 1, string description = null)
        {
            Directory.CreateDirectory(Path.GetDirectoryName(lnkPath));
            dynamic ws = Activator.CreateInstance(Type.GetTypeFromProgID("WScript.Shell"));
            try
            {
                dynamic s = ws.CreateShortcut(lnkPath);
                s.TargetPath = target; s.Arguments = args ?? ""; s.WorkingDirectory = workDir ?? ""; s.WindowStyle = windowStyle;
                if (icon != null) s.IconLocation = icon;
                if (description != null) s.Description = description;
                s.Save();
                Marshal.FinalReleaseComObject(s);
            }
            finally { Marshal.FinalReleaseComObject(ws); }
        }
        // 바로가기의 대상·인자 (없으면 null)
        public static string[] ReadShortcut(string lnkPath)
        {
            if (!File.Exists(lnkPath)) return null;
            dynamic ws = Activator.CreateInstance(Type.GetTypeFromProgID("WScript.Shell"));
            try { dynamic s = ws.CreateShortcut(lnkPath); var r = new[] { (string)s.TargetPath, (string)s.Arguments, (string)s.WorkingDirectory }; Marshal.FinalReleaseComObject(s); return r; }
            finally { Marshal.FinalReleaseComObject(ws); }
        }
    }

    // 실행 파일 서명 확인 (WinVerifyTrust) — 서명이 없거나 깨진 설치 파일은 실행하지 않는다
    public static class Trust
    {
        public enum State { Valid, NotSigned, HashMismatch, Other }
        public static State Check(string file)
        {
            var fi = new WINTRUST_FILE_INFO { cbStruct = (uint)Marshal.SizeOf(typeof(WINTRUST_FILE_INFO)), pcwszFilePath = file };
            var pFile = Marshal.AllocHGlobal(Marshal.SizeOf(fi));
            try
            {
                Marshal.StructureToPtr(fi, pFile, false);
                var wd = new WINTRUST_DATA { cbStruct = (uint)Marshal.SizeOf(typeof(WINTRUST_DATA)), dwUIChoice = 2 /*NONE*/, fdwRevocationChecks = 0, dwUnionChoice = 1 /*FILE*/, pFile = pFile, dwStateAction = 0, dwProvFlags = 0x00000080 /*SAFER*/ };
                var g = new Guid("00AAC56B-CD44-11d0-8CC2-00C04FC295EE"); // WINTRUST_ACTION_GENERIC_VERIFY_V2
                uint hr = WinVerifyTrust(IntPtr.Zero, ref g, ref wd);
                if (hr == 0) return State.Valid;
                if (hr == 0x800B0100) return State.NotSigned;       // TRUST_E_NOSIGNATURE
                if (hr == 0x80096010) return State.HashMismatch;    // TRUST_E_BAD_DIGEST
                return State.Other;
            }
            finally { Marshal.FreeHGlobal(pFile); }
        }
        [DllImport("wintrust.dll", CharSet = CharSet.Unicode)] static extern uint WinVerifyTrust(IntPtr hwnd, ref Guid action, ref WINTRUST_DATA data);
        [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)] struct WINTRUST_FILE_INFO { public uint cbStruct; public string pcwszFilePath; public IntPtr hFile; public IntPtr pgKnownSubject; }
        [StructLayout(LayoutKind.Sequential)] struct WINTRUST_DATA { public uint cbStruct; public IntPtr pPolicyCallbackData; public IntPtr pSIPClientData; public uint dwUIChoice; public uint fdwRevocationChecks; public uint dwUnionChoice; public IntPtr pFile; public uint dwStateAction; public IntPtr hWVTStateData; public IntPtr pwszURLReference; public uint dwProvFlags; public uint dwUIContext; public IntPtr pSignatureSettings; }
    }

    // 스토어 형식 앱(Appx·MSIX) — Windows 내장 PackageManager
    public static class Appx
    {
        public static bool Has(string name)
        {
            try { var pm = new Windows.Management.Deployment.PackageManager(); return pm.FindPackagesForUser("").Any(p => p.Id.Name.Equals(name, StringComparison.OrdinalIgnoreCase)); }
            catch { return false; }
        }
        // 비동기 작업이 끝날 때까지 기다린다 (SDK 없이 — 상태를 직접 본다)
        static T Wait<T, P>(Windows.Foundation.IAsyncOperationWithProgress<T, P> op, int timeoutSec)
        {
            var sw = System.Diagnostics.Stopwatch.StartNew();
            while (op.Status == Windows.Foundation.AsyncStatus.Started)
            {
                if (sw.Elapsed.TotalSeconds > timeoutSec) { try { op.Cancel(); } catch { } throw new TimeoutException("앱 설치가 " + timeoutSec + "초 안에 끝나지 않았습니다"); }
                System.Threading.Thread.Sleep(200);
            }
            if (op.Status == Windows.Foundation.AsyncStatus.Error) throw new Exception("0x" + op.ErrorCode.HResult.ToString("X8") + " " + op.ErrorCode.Message);
            if (op.Status == Windows.Foundation.AsyncStatus.Canceled) throw new Exception("취소됨");
            return op.GetResults();
        }
        // 이 사용자에게 설치(의존 패키지 포함). 실패하면 예외
        public static void Add(string package, IEnumerable<string> deps)
        {
            var pm = new Windows.Management.Deployment.PackageManager();
            var op = pm.AddPackageAsync(new Uri(package), deps == null ? null : deps.Select(d => new Uri(d)).ToList(), Windows.Management.Deployment.DeploymentOptions.ForceApplicationShutdown | Windows.Management.Deployment.DeploymentOptions.ForceUpdateFromAnyVersion);
            var r = Wait(op, 900);
            if (r.ExtendedErrorCode != null && r.ExtendedErrorCode.HResult != 0) throw new Exception(r.ErrorText + " (0x" + r.ExtendedErrorCode.HResult.ToString("X8") + ")");
        }
        // 모든 사용자에게(새 계정에도) — 실패해도 이 사용자 설치는 이미 됐다
        public static void ProvisionForAll(string familyName)
        {
            try
            {
                var pm = new Windows.Management.Deployment.PackageManager();
                Wait(pm.ProvisionPackageForAllUsersAsync(familyName), 300);
            }
            catch (Exception ex) { Log.Say("   (모든 사용자용 등록은 건너뜀: " + ex.GetBaseException().Message + ")", Tone.Dim); }
        }
        public static string FamilyName(string name)
        {
            try { var pm = new Windows.Management.Deployment.PackageManager(); var p = pm.FindPackagesForUser("").FirstOrDefault(x => x.Id.Name.Equals(name, StringComparison.OrdinalIgnoreCase)); return p == null ? null : p.Id.FamilyName; }
            catch { return null; }
        }
    }

    // 예약 작업 — schtasks /xml 로 만들고 읽는다
    public static class Tasks
    {
        static string Schtasks { get { return Path.Combine(Env.System32, "schtasks.exe"); } }
        public static bool Exists(string name) { return Proc.Run(Schtasks, "/query /tn " + Proc.Quote(name), 30, enc: Proc.Oem).Code == 0; }
        public static string Xml(string name) { var r = Proc.Run(Schtasks, "/query /tn " + Proc.Quote(name) + " /xml", 30, enc: Proc.Oem); return r.Code == 0 ? r.Out : null; }
        public static void Create(string name, string xml)
        {
            var f = Path.Combine(Net.Cache, "task-" + Guid.NewGuid().ToString("N") + ".xml");
            File.WriteAllText(f, xml, Encoding.Unicode);
            try
            {
                var r = Proc.Run(Schtasks, "/create /tn " + Proc.Quote(name) + " /xml " + Proc.Quote(f) + " /f", 60, enc: Proc.Oem);
                if (r.Code != 0) throw new Exception("예약 작업을 만들지 못했습니다(" + name + "): " + r.Text);
            }
            finally { try { File.Delete(f); } catch { } }
        }
        public static void Run(string name) { Proc.Run(Schtasks, "/run /tn " + Proc.Quote(name), 30, enc: Proc.Oem); }
        public static void End(string name) { Proc.Run(Schtasks, "/end /tn " + Proc.Quote(name), 30, enc: Proc.Oem); }
        public static void Delete(string name) { Proc.Run(Schtasks, "/delete /tn " + Proc.Quote(name) + " /f", 30, enc: Proc.Oem); }
        // 사용자 이름(도메인\이름)과 SID — 작업 XML 에 쓴다
        public static string UserId { get { return Environment.UserDomainName + "\\" + Environment.UserName; } }
        public static string Escape(string s) { return System.Security.SecurityElement.Escape(s); }
    }

    // 서비스
    public static class Svc
    {
        public static string StartType(string name)
        {
            try { using (var sc = new System.ServiceProcess.ServiceController(name)) return sc.StartType.ToString(); } catch { return null; }
        }
        public static void StopAndDisable(string name)
        {
            try { using (var sc = new System.ServiceProcess.ServiceController(name)) { if (sc.Status != System.ServiceProcess.ServiceControllerStatus.Stopped) { sc.Stop(); sc.WaitForStatus(System.ServiceProcess.ServiceControllerStatus.Stopped, TimeSpan.FromSeconds(30)); } } } catch { }
            Proc.Run(Path.Combine(Env.System32, "sc.exe"), "config " + name + " start= disabled", 30, enc: Proc.Oem);
        }
    }

    // WMI 조회
    public static class Wmi
    {
        public static List<Dictionary<string, object>> Query(string wql)
        {
            var list = new List<Dictionary<string, object>>();
            try
            {
                using (var s = new ManagementObjectSearcher(wql))
                    foreach (ManagementObject o in s.Get())
                    {
                        var d = new Dictionary<string, object>(StringComparer.OrdinalIgnoreCase);
                        foreach (var p in o.Properties) d[p.Name] = p.Value;
                        list.Add(d);
                    }
            }
            catch { }
            return list;
        }
    }

    // USB 설치 키트 — 드라이브 맨 위의 'PC설치' 폴더(네트워크 드라이버·도구). 이 프로그램이 USB 에서 실행됐으면 그 USB
    public static class Usb
    {
        public static string FindKit()
        {
            try
            {
                var self = Path.GetPathRoot(System.Windows.Forms.Application.ExecutablePath);
                var mine = Path.Combine(self, "PC설치");
                if (Directory.Exists(mine)) return mine;
            }
            catch { }
            foreach (var d in DriveInfo.GetDrives())
            {
                try
                {
                    if (d.DriveType != DriveType.Removable && d.DriveType != DriveType.Fixed) continue;   // 네트워크 드라이브는 끊겨 있으면 오래 멈춘다
                    if (!d.IsReady) continue;
                    var k = Path.Combine(d.RootDirectory.FullName, "PC설치");
                    if (Directory.Exists(k)) return k;
                }
                catch { }
            }
            return null;
        }
    }
}
