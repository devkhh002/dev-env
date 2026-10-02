using System;
using System.Collections.Generic;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Threading;
using System.Windows.Forms;

namespace PcSetup
{
    // 체크 상자를 빠르게 두 번 누르면 화면과 실제 체크가 어긋나는 TreeView 버그를 막는다
    public sealed class Tree : TreeView
    {
        protected override void WndProc(ref Message m) { if (m.Msg == 0x0203) { m.Result = IntPtr.Zero; return; } base.WndProc(ref m); }
    }

    public sealed class MainForm : Form
    {
        public static readonly Font UiFont = new Font("Malgun Gothic", 10f);
        readonly Label head = new Label { Dock = DockStyle.Fill, AutoEllipsis = true, TextAlign = ContentAlignment.MiddleLeft };
        readonly CheckBox detail = new CheckBox { Text = "자세히", AutoSize = true, Anchor = AnchorStyles.Right, Margin = Dpi.P(8, 6, 4, 0) };
        public readonly Tree TreeView = new Tree { Dock = DockStyle.Fill, CheckBoxes = true, HideSelection = true };
        readonly RichTextBox log = new RichTextBox { Dock = DockStyle.Fill, ReadOnly = true, BackColor = Color.White, BorderStyle = BorderStyle.FixedSingle, DetectUrls = false, WordWrap = true };
        readonly Label status = new Label { Dock = DockStyle.Fill, AutoEllipsis = true, ForeColor = Color.DimGray, Height = Dpi.S(22) };
        readonly FlowLayoutPanel buttons = new FlowLayoutPanel { Dock = DockStyle.Fill, AutoSize = true, WrapContents = true, Padding = Dpi.P(0, 4, 0, 0) };
        Button bAll, bNone, bAdd, bUp, bSrc, bGo, bClose;
        readonly Dictionary<string, TreeNode> groupNodes = new Dictionary<string, TreeNode>();
        public readonly Dictionary<string, TreeNode> NodeById = new Dictionary<string, TreeNode>();
        readonly List<KeyValuePair<string, Tone>> lines = new List<KeyValuePair<string, Tone>>();
        bool busyCheck;          // 체크 연쇄(묶음 ↔ 항목) 중
        volatile bool working;   // 설치·최신으로 중
        public bool Snapshot;    // --snapshot (화면을 그림으로만)

        public MainForm()
        {
            Text = "PC 설치 — 버전 " + PrettyVersion(BuildInfo.Version);
            Font = UiFont; AutoScaleMode = AutoScaleMode.None;   // 크기는 Dpi.S 로 직접 맞춘다
            StartPosition = FormStartPosition.CenterScreen;
            try { Icon = Icon.ExtractAssociatedIcon(Application.ExecutablePath); } catch { }

            TreeView.Font = UiFont;
            var split = new SplitContainer { Dock = DockStyle.Fill, Orientation = Orientation.Horizontal, SplitterWidth = Dpi.S(6), FixedPanel = FixedPanel.Panel2 };
            split.Panel1.Controls.Add(TreeView);
            split.Panel2.Controls.Add(log);

            var top = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 2, RowCount = 1, AutoSize = true, Margin = new Padding(0) };
            top.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100)); top.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
            head.Height = Dpi.S(26); top.Controls.Add(head, 0, 0); top.Controls.Add(detail, 1, 0);

            bAll = Btn("전체 선택", (s, e) => SetAll(true));
            bNone = Btn("전체 해제", (s, e) => SetAll(false));
            bAdd = Btn("앱 추가…", (s, e) => { if (!working) Hooks.ShowAddApp(this); });
            bUp = Btn("모두 최신으로", (s, e) => StartUpgrade());
            bSrc = Btn("소스 올리기…", (s, e) => { if (!working) Hooks.ShowUpload(this); });
            bGo = Btn("선택한 것 설치", (s, e) => StartInstall()); bGo.Font = new Font(UiFont, FontStyle.Bold);
            bClose = Btn("닫기", (s, e) => Close());
            buttons.Controls.AddRange(new Control[] { bAll, bNone, bAdd, bUp, bSrc, bGo, bClose });

            var root = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 1, RowCount = 4, Padding = Dpi.P(10, 6, 10, 8) };
            root.RowStyles.Add(new RowStyle(SizeType.AutoSize));
            root.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
            root.RowStyles.Add(new RowStyle(SizeType.AutoSize));
            root.RowStyles.Add(new RowStyle(SizeType.AutoSize));
            root.Controls.Add(top, 0, 0); root.Controls.Add(split, 0, 1); root.Controls.Add(status, 0, 2); root.Controls.Add(buttons, 0, 3);
            Controls.Add(root);

            // 크기 — 화면(작업 영역)보다 크면 줄인다
            var wa = Screen.FromPoint(Cursor.Position).WorkingArea;
            Size = new Size(Math.Min(Dpi.S(920), wa.Width), Math.Min(Dpi.S(860), wa.Height));
            MinimumSize = new Size(Math.Min(Dpi.S(640), wa.Width), Math.Min(Dpi.S(460), wa.Height));
            Load += (s, e) => { try { split.SplitterDistance = Math.Max(120, split.Height - Dpi.S(190)); } catch { } };

            detail.CheckedChanged += (s, e) => Redraw();
            TreeView.AfterCheck += (s, e) =>
            {
                if (busyCheck) return; busyCheck = true;
                try { SetDown(e.Node, e.Node.Checked); SyncUp(e.Node.Parent); } finally { busyCheck = false; }
            };
            Log.Line += OnLine;
            Log.Status += t => Ui(() => status.Text = t);
            FormClosing += OnClosing;
            FormClosed += (s, e) => { Log.Line -= OnLine; };
            SetButtons(false);
            head.Text = "준비 중…";
        }

        Button Btn(string text, EventHandler click)
        {
            var b = new Button { Text = text, AutoSize = true, AutoSizeMode = AutoSizeMode.GrowAndShrink, Padding = Dpi.P(8, 3, 8, 3), Margin = Dpi.P(0, 0, 6, 0), UseVisualStyleBackColor = true };
            b.Click += click; return b;
        }
        static string PrettyVersion(string v) { return v.Length == 12 ? v.Substring(0, 4) + "-" + v.Substring(4, 2) + "-" + v.Substring(6, 2) + " " + v.Substring(8, 2) + ":" + v.Substring(10, 2) : v; }

        // ── 기록 칸 ──────────────────────────────────────────
        void OnLine(string text, Tone tone) { Ui(() => { lines.Add(new KeyValuePair<string, Tone>(text, tone)); if (tone != Tone.Dim || detail.Checked) Append(text, tone); }); }
        void Append(string text, Tone tone)
        {
            log.SelectionStart = log.TextLength; log.SelectionLength = 0;
            log.SelectionColor = tone == Tone.Ok ? Color.ForestGreen : tone == Tone.Warn ? Color.DarkOrange : tone == Tone.Error ? Color.Firebrick : tone == Tone.Info ? Color.Navy : tone == Tone.Dim ? Color.Gray : Color.Black;
            log.AppendText(text + "\n"); log.SelectionColor = log.ForeColor;
            log.ScrollToCaret();
        }
        void Redraw() { log.Clear(); foreach (var l in lines) if (l.Value != Tone.Dim || detail.Checked) Append(l.Key, l.Value); }
        public void Ui(Action a) { if (IsDisposed) return; if (InvokeRequired) { try { BeginInvoke(a); } catch { } } else a(); }

        // ── 처음: 목록을 받고 상태를 확인한다 ───────────────────
        public void Prepare(bool sync = false)
        {
            Action work = () =>
            {
                Log.SetStatus("인터넷·USB 를 확인합니다…");
                State.Online = Net.Online();
                State.Usb = Usb.FindKit();
                Data.Load();
                Defs.Build();
                Ui(BuildTree);
                Log.SetStatus("상태 확인: winget 이 살아 있는지 보고 설치된 앱 목록을 읽습니다 (처음엔 최대 2분)");
                Winget.Map();
                int n = 0;
                foreach (var it in Defs.All)
                {
                    n++; Log.SetStatus("상태 확인 " + n + "/" + Defs.All.Count + ": " + it.Name);
                    it.Installed = Defs.Check(it);
                    var item = it; Ui(() => { TreeNode node; if (NodeById.TryGetValue(item.Id, out node)) { Look(node); busyCheck = true; node.Checked = !item.Installed && item.Default; busyCheck = false; } });
                }
                Ui(() =>
                {
                    foreach (var node in NodeById.Values) SyncUp(node.Parent);
                    Log.SetStatus("");
                    head.Text = "버전 " + PrettyVersion(BuildInfo.Version) + " · " + Environment.MachineName + " · " + Board() + " · " + (State.Online ? "인터넷 연결됨" : "인터넷 없음 — 먼저 ① 네트워크 드라이버") + (State.Usb != null ? " · USB " + State.Usb : "") + (State.CatalogFrom == "프로그램 안" ? " · 앱 목록: 프로그램 안 판" : "");
                    Log.Say("필요한 것을 체크하고 '선택한 것 설치' 를 누르세요. 회색은 이미 된 것입니다. 묶음 이름을 체크하면 그 안이 다 체크됩니다.");
                    if (!State.Online) Log.Say("인터넷이 없습니다 — ① 네트워크 드라이버부터 설치하세요.", Tone.Warn);
                    SetButtons(true);
                });
            };
            if (sync) work(); else new Thread(() => { try { work(); } catch (Exception ex) { Log.Say("준비 중 오류: " + ex.Message, Tone.Error); Ui(() => SetButtons(true)); } }) { IsBackground = true }.Start();
        }

        static string Board() { var b = Wmi.Query("SELECT Product FROM Win32_BaseBoard").FirstOrDefault(); return b == null ? "" : Convert.ToString(b["Product"]); }

        void BuildTree()
        {
            TreeView.BeginUpdate();
            TreeView.Nodes.Clear(); groupNodes.Clear(); NodeById.Clear();
            foreach (var g in Defs.GroupOrder) GroupNode(g);
            foreach (var it in Defs.All) AddItemNode(it, false);
            // winget 은 ④ 의 맨 위에 — 아래 앱들을 까는 도구라서
            TreeNode w; if (NodeById.TryGetValue("winget", out w)) { var p = w.Parent; p.Nodes.Remove(w); p.Nodes.Insert(0, w); }
            foreach (var k in groupNodes.Keys.ToList()) if (groupNodes[k].Nodes.Count == 0) { groupNodes[k].Remove(); groupNodes.Remove(k); }
            TreeView.ExpandAll();
            if (TreeView.Nodes.Count > 0) TreeView.Nodes[0].EnsureVisible();
            TreeView.EndUpdate();
        }
        TreeNode GroupNode(string path)
        {
            TreeNode n; if (groupNodes.TryGetValue(path, out n)) return n;
            var parts = path.Split('/');
            n = new TreeNode(parts[parts.Length - 1]) { ForeColor = Color.Navy };
            if (parts.Length > 1) GroupNode(string.Join("/", parts.Take(parts.Length - 1))).Nodes.Add(n); else TreeView.Nodes.Add(n);
            groupNodes[path] = n; return n;
        }
        // 항목 하나를 화면에 (앱 추가가 새로 넣을 때도 쓴다)
        public TreeNode AddItemNode(Item it, bool reveal)
        {
            var node = new TreeNode(it.Name) { Tag = it };
            Look(node);
            busyCheck = true; node.Checked = !it.Installed && it.Default; busyCheck = false;
            GroupNode(it.Group).Nodes.Add(node);
            NodeById[it.Id] = node;
            if (reveal) { SyncUp(node.Parent); node.Parent.Expand(); node.EnsureVisible(); TreeView.SelectedNode = node; }
            return node;
        }
        void Look(TreeNode node)
        {
            var it = (Item)node.Tag;
            node.Text = it.Name + (it.Installed ? "   — " + it.OkText : it.Reboot ? "   (설치 후 재부팅해야 적용)" : "");
            node.ForeColor = it.Installed ? Color.Gray : Color.Black;
        }
        void SetDown(TreeNode n, bool v) { foreach (TreeNode c in n.Nodes) { c.Checked = v; SetDown(c, v); } }
        void SyncUp(TreeNode n) { while (n != null) { bool all = n.Nodes.Count > 0; foreach (TreeNode c in n.Nodes) if (!c.Checked) all = false; busyCheck = true; n.Checked = all; busyCheck = false; n = n.Parent; } }
        void SetAll(bool v) { busyCheck = true; foreach (TreeNode n in TreeView.Nodes) { n.Checked = v; SetDown(n, v); } busyCheck = false; }

        void SetButtons(bool on) { foreach (var b in new[] { bAll, bNone, bAdd, bUp, bSrc, bGo }) b.Enabled = on; }

        // ── 설치 ───────────────────────────────────────────────
        void StartInstall()
        {
            var sel = Defs.All.Where(it => NodeById.ContainsKey(it.Id) && NodeById[it.Id].Checked).ToList();
            if (sel.Count == 0) { MessageBox.Show(this, "설치할 것을 체크하세요.", "PC 설치"); return; }
            // winget 앱을 고르고 winget 이 없으면 winget 부터
            var wg = Defs.All.FirstOrDefault(i => i.Id == "winget");
            if (wg != null && !sel.Contains(wg) && sel.Any(i => i.Kind == "winget") && !Winget.Alive()) sel.Insert(0, wg);
            RunWork("설치를 시작합니다.", () => Install(sel));
        }
        void StartUpgrade()
        {
            var msg = "설치된 앱을 모두 최신판으로 올립니다.\r\n\r\n - winget 앱: 새 판이 있는 것만\r\n - 팟플레이어·HWiNFO 처럼 winget 밖의 앱: 공식 최신 설치본으로\r\n - 빼는 것: 버전 고정(VirtualBox·⑤ 개발 도구) · 원격 호스트(Chrome 원격 데스크톱·Sunshine·Tailscale — 올리는 동안 원격이 끊긴다)\r\n\r\n앱이 켜져 있으면 잠깐 꺼질 수 있습니다. 진행할까요?";
            if (MessageBox.Show(this, msg, "PC 설치", MessageBoxButtons.YesNo, MessageBoxIcon.Question) != DialogResult.Yes) return;
            RunWork("모두 최신으로 — 시작합니다.", () =>
            {
                int done = 0, fail = 0;
                Hooks.RunUpgrade(ref done, ref fail);
                Log.Say(string.Format("끝 — 올림 {0} · 실패 {1}", done, fail), fail > 0 ? Tone.Warn : Tone.Ok);
                return new List<string>();
            });
        }

        // 일하는 스레드에서 돌리고, 끝나면 상태를 다시 보고 재부팅을 묻는다
        void RunWork(string startMsg, Func<List<string>> work)
        {
            working = true; SetButtons(false);
            Log.Say(startMsg, Tone.Info);
            new Thread(() =>
            {
                List<string> reboot = new List<string>();
                try { reboot = work(); }
                catch (Exception ex) { Log.Say("오류로 멈췄습니다: " + ex.GetBaseException().Message, Tone.Error); }
                // 받아 둔 설치 파일 정리(예전 판도 끝나면 지웠다) · 상태 다시 보기
                try { System.IO.Directory.Delete(Net.Cache, true); } catch { }
                try { System.IO.Directory.CreateDirectory(Net.Cache); } catch { }
                Winget.Forget(); Env.RefreshPath();
                foreach (var it in Defs.All) { it.Installed = Defs.Check(it); var item = it; Ui(() => { TreeNode node; if (NodeById.TryGetValue(item.Id, out node)) { Look(node); if (item.Installed) { busyCheck = true; node.Checked = false; busyCheck = false; } } }); }
                Log.SetStatus("");
                Ui(() =>
                {
                    foreach (var node in NodeById.Values) SyncUp(node.Parent);
                    working = false; SetButtons(true);
                    Log.Say("끝났습니다. (기록: " + Log.FilePath + ")", Tone.Info);
                    if (reboot.Count > 0)
                    {
                        var m = "재부팅해야 적용되는 것이 있습니다:\r\n - " + string.Join("\r\n - ", reboot) + "\r\n\r\n원격으로 접속 중이면 재부팅하는 동안 연결이 끊깁니다.\r\n지금 재부팅할까요?";
                        if (MessageBox.Show(this, m, "PC 설치", MessageBoxButtons.YesNo, MessageBoxIcon.Question) == DialogResult.Yes)
                            Proc.Run(Path.Combine(Env.System32, "shutdown.exe"), "/r /t 5", 30);
                    }
                });
            }) { IsBackground = true }.Start();
        }

        public static List<string> Install(List<Item> pick)
        {
            Log.Say("설치 프로그램 버전 " + PrettyVersion(BuildInfo.Version), Tone.Info);
            var reboot = new List<string>(); int n = 0;
            foreach (var it in pick)
            {
                n++;
                if (Defs.Check(it)) { Log.Say("[" + n + "/" + pick.Count + "] " + it.Name + " — 이미 " + it.OkText + ", 건너뜀", Tone.Dim); continue; }
                Log.Say("[" + n + "/" + pick.Count + "] " + it.Name, Tone.Info);
                try
                {
                    it.Install(); Env.RefreshPath();
                    if (Defs.Check(it)) { Log.Say("   완료", Tone.Ok); if (it.Reboot) reboot.Add(it.Name); }
                    else Log.Say("   확인 필요 — 위 기록 참고", Tone.Warn);
                }
                catch (Exception ex) { Log.Say("   실패: " + ex.GetBaseException().Message, Tone.Error); }
            }
            Log.Say("──────── 사람이 해야 하는 일 ────────", Tone.Info);
            Log.Say(" • 새 터미널을 열고 claude 실행 → 브라우저 로그인");
            Log.Say(" • clasp login / firebase login (Apps Script·Firebase 쓰는 프로젝트만)");
            Log.Say(" • Tailscale 트레이 아이콘 → 로그인 / Sunshine: https://localhost:47990 관리자 계정·PIN");
            Log.Say(" • git 이 없는 프로젝트·Claude 기억은 구글 드라이브 백업에서 복원 (README 참고)");
            return reboot;
        }

        void OnClosing(object sender, FormClosingEventArgs e)
        {
            if (!working || Snapshot) return;
            if (MessageBox.Show(this, "아직 설치 중입니다. 지금 닫으면 진행 중인 설치가 중간에 멈출 수 있습니다.\r\n그래도 닫을까요?", "PC 설치", MessageBoxButtons.YesNo, MessageBoxIcon.Warning) != DialogResult.Yes) e.Cancel = true;
            else Environment.Exit(1);
        }

        // --snapshot: 화면을 그림 파일로
        public void SaveSnapshot(string png)
        {
            StartPosition = FormStartPosition.Manual; Location = new Point(-5000, -5000);
            Show(); Application.DoEvents();
            using (var bmp = new Bitmap(Width, Height)) { DrawToBitmap(bmp, new Rectangle(0, 0, Width, Height)); bmp.Save(png); }
            Close();
        }
    }
}
