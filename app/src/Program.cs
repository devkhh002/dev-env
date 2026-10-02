using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading;
using System.Windows.Forms;

namespace PcSetup
{
    static class Program
    {
        // PC 설치.exe
        //   (없음)               설치 화면
        //   --list <파일>        상태만 확인해 파일에 적는다 (시험용)
        //   --snapshot <png>     설치 화면을 그림으로 저장 (시험용)
        //   --only id,id [--yes] 화면 없이 그 항목만 설치 (시험용 — --yes 가 있어야 실제로 설치)
        //   --no-update          새 판 확인을 건너뛴다
        [STAThread]
        static int Main(string[] args)
        {
            Net.Init();
            Application.EnableVisualStyles();
            Application.SetCompatibleTextRenderingDefault(false);
            var a = args.ToList();
            string Opt(string name) { var i = a.IndexOf(name); return i >= 0 && i + 1 < a.Count ? a[i + 1] : null; }

            if (a.Contains("--list")) return ListMode(Opt("--list"));
            if (a.Contains("--snapshot"))
            {
                var f = new MainForm { Snapshot = true };
                f.Prepare(sync: true);
                Application.DoEvents();
                f.SaveSnapshot(Opt("--snapshot"));
                return 0;
            }
            if (a.Contains("--only")) return OnlyMode(Opt("--only"), a.Contains("--yes"));

            // 같은 PC 에서 두 번 열지 않는다
            bool fresh;
            using (var mutex = new Mutex(true, "Global\\dev-env-PcSetup", out fresh))
            {
                if (!fresh) { MessageBox.Show("PC 설치가 이미 열려 있습니다.", "PC 설치"); return 0; }
                if (!a.Contains("--no-update"))
                {
                    bool restarting = false;
                    try { Hooks.TryUpdate(args, ref restarting); } catch (Exception ex) { Log.Say("새 판 확인 중 오류(무시하고 이 판으로 엽니다): " + ex.Message, Tone.Warn); }
                    if (restarting) return 0;
                }
                var form = new MainForm();
                form.Shown += (s, e) => form.Prepare();
                Application.Run(form);
            }
            return 0;
        }

        static void Prepare()
        {
            State.Online = Net.Online();
            State.Usb = Usb.FindKit();
            Data.Load();
            Defs.Build();
        }

        static int ListMode(string file)
        {
            Prepare();
            Winget.Map();
            var sb = new StringBuilder();
            sb.AppendLine("버전 " + BuildInfo.Version + " · 인터넷 " + (State.Online ? "연결됨" : "없음") + " · USB " + (State.Usb ?? "없음") + " · 앱 목록 " + State.CatalogFrom);
            foreach (var it in Defs.All)
            {
                it.Installed = Defs.Check(it);
                sb.AppendLine(string.Format("{0,-4} {1,-26} {2,-30} {3}", it.Installed ? "OK" : "--", it.Group.Replace("/", " > "), it.Id, it.Name));
            }
            File.WriteAllText(file, sb.ToString(), new UTF8Encoding(true));
            return 0;
        }

        static int OnlyMode(string ids, bool yes)
        {
            Prepare();
            var want = (ids ?? "").Split(',').Select(x => x.Trim()).Where(x => x.Length > 0).ToList();
            var pick = Defs.All.Where(i => want.Contains(i.Id)).ToList();
            if (!yes) { Log.Say("--only 는 --yes 가 있어야 실제로 설치합니다. 고른 항목: " + string.Join(", ", pick.Select(p => p.Id)), Tone.Warn); return 2; }
            var reboot = MainForm.Install(pick);
            if (reboot.Count > 0) Log.Say("재부팅해야 적용되는 것: " + string.Join(", ", reboot), Tone.Warn);
            return 0;
        }
    }
}
