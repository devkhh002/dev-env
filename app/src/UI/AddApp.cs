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
    // ── 앱 추가: winget 에서 찾아 catalog.txt 에 한 줄 넣고 GitHub 에 올린다 ──
    public static partial class Hooks
    {
        static partial void ShowAddAppImpl(MainForm f) { AddAppDialog.Show(f); }
    }

    public static class AddAppDialog
    {
        public sealed class PublishResult { public bool Ok, Pushed, NoRepo; public string Repo, Text = ""; }

        static readonly Regex HowId = new Regex(@"^(winget|msstore):([^@]+)", RegexOptions.IgnoreCase);

        // 목록을 고쳐 올릴 저장소 — 이 PC 의 C:\dev\dev-env (프로그램은 이제 저장소 밖에서 돈다)
        public static string FindEditRepo()
        {
            var d = Path.Combine(Conf.DevRoot, "dev-env");
            var g = Path.Combine(d, ".git");
            return Directory.Exists(g) || File.Exists(g) ? Path.GetFullPath(d) : null;
        }

        // catalog.txt 에 한 줄 넣기 — 같은 묶음의 마지막 줄 뒤에(없으면 맨 끝). 파일의 BOM·줄바꿈 방식은 그대로
        public static void AddCatalogLine(string file, string group, string line)
        {
            var bytes = File.ReadAllBytes(file);
            int skip = bytes.Length >= 3 && bytes[0] == 0xEF && bytes[1] == 0xBB && bytes[2] == 0xBF ? 3 : 0;
            var text = Encoding.UTF8.GetString(bytes, skip, bytes.Length - skip);
            var nl = text.Contains("\r\n") ? "\r\n" : "\n";
            var lines = Regex.Split(text, "\r?\n").ToList();
            while (lines.Count > 0 && lines[lines.Count - 1] == "") lines.RemoveAt(lines.Count - 1);
            int at = -1;
            for (int i = 0; i < lines.Count; i++)
                if (!Regex.IsMatch(lines[i], @"^\s*#") && string.Equals(lines[i].Split('|')[0].Trim(), group, StringComparison.OrdinalIgnoreCase)) at = i;
            if (at >= 0) lines.Insert(at + 1, line); else lines.Add(line);
            File.WriteAllText(file, string.Join(nl, lines) + nl, new UTF8Encoding(skip == 3));
        }

        // 저장소의 catalog.txt 에 넣고 커밋·푸시. 결과: Ok(목록에 들어갔나) · Pushed · Text
        public static PublishResult PublishCatalogLine(string group, string line, string what) { return PublishCatalogLine(FindEditRepo(), group, line, what); }
        public static PublishResult PublishCatalogLine(string rp, string group, string line, string what)
        {
            if (rp == null) return new PublishResult { NoRepo = true, Text = "이 PC 에 dev-env 저장소(" + Conf.DevRoot + "\\dev-env)가 없어 GitHub 목록에는 올리지 못했습니다 — 이번 설치 화면에만 넣었습니다.\r\n(⑥ 개발 소스의 dev-env 를 받은 뒤 다시 추가하면 모든 PC 에 나옵니다)" };
            // git diff --quiet: 0 = 바뀐 것 없음 · 1 = 있음 · 그 밖(git 없음·시간 초과 -1 등) = 확인 못 함
            var r = Git.Run(rp, "diff", "--cached", "--quiet");
            if (r.Code == 1) return Fail(rp + " 에 커밋을 기다리는 다른 변경이 있어 건드리지 않았습니다 — 그것부터 정리하세요.");
            if (r.Code != 0) return Fail("git 으로 " + rp + " 를 확인하지 못했습니다:\r\n" + r.Text);
            r = Git.Run(rp, "diff", "--quiet", "--", "catalog.txt");
            if (r.Code == 1) return Fail(rp + " 의 catalog.txt 에 올리지 않은 수정이 있어 건드리지 않았습니다 — 그것부터 올리거나 되돌리세요.");
            if (r.Code != 0) return Fail("git 으로 " + rp + " 를 확인하지 못했습니다:\r\n" + r.Text);
            // 모든 PC 가 받는 곳은 GitHub 의 main — 다른 브랜치에 있으면 올려도 아무 PC 에도 안 보인다
            var u = Git.Run(rp, "rev-parse", "--abbrev-ref", "--symbolic-full-name", "@{u}");
            if (u.Code != 0 || !string.Equals(u.Out.Trim(), "origin/main", StringComparison.OrdinalIgnoreCase)) return Fail(rp + " 가 main(origin/main) 이 아닌 곳에 있어 건드리지 않았습니다 — main 으로 돌아온 뒤 다시 추가하세요.");
            r = Git.Run(rp, 120, "pull", "--ff-only", "-q");
            if (r.Code != 0) return Fail("GitHub 최신 목록을 받지 못해 넣지 않았습니다:\r\n" + r.Text);
            var cat = Path.Combine(rp, "catalog.txt");
            // 이 화면을 연 뒤 다른 PC 가 같은 앱을 먼저 올렸을 수 있다 — 방금 받은 목록으로 한 번 더 본다
            var parts = line.Split('|');
            var m = parts.Length >= 3 ? HowId.Match(parts[2].Trim()) : Match.Empty;
            if (m.Success)
            {
                var newId = m.Groups[2].Value;
                var have = File.ReadAllLines(cat, Encoding.UTF8).FirstOrDefault(l =>
                {
                    if (Regex.IsMatch(l, @"^\s*#")) return false;
                    var c = l.Split('|'); if (c.Length < 3) return false;
                    var mm = HowId.Match(c[2].Trim());
                    return mm.Success && string.Equals(mm.Groups[2].Value, newId, StringComparison.OrdinalIgnoreCase);
                });
                if (have != null) return Fail("GitHub 최신 목록에 이미 있습니다(다른 PC 에서 먼저 추가): " + have + "\r\n설치 화면을 다시 열면 보입니다.");
            }
            ProcResult c2;
            try
            {
                AddCatalogLine(cat, group, line);
                Git.Run(rp, "add", "--", "catalog.txt");   // 설치 화면은 열 때마다 GitHub 의 catalog.txt 를 읽는다 — 다시 빌드할 필요 없다
                c2 = Git.Commit(rp, "앱 추가 — " + what);
            }
            catch (Exception ex) { c2 = new ProcResult { Code = -1, Err = ex.Message }; }
            if (c2.Code != 0)
            {
                // 커밋을 못 하면(이 PC 에 git 이름·메일이 없다 등) 넣은 줄을 되돌린다 — 담긴 채 남으면 다음 '앱 추가' 가 모두 막힌다
                Git.Run(rp, "checkout", "HEAD", "--", "catalog.txt");
                return Fail("커밋하지 못해 목록을 되돌렸습니다 — ⑤ 'git 기본 설정' 을 먼저 하세요:\r\n" + c2.Text);
            }
            var p = Git.Push(rp, true);
            if (!p.Ok) return new PublishResult { Ok = true, Repo = rp, Text = "커밋은 했지만 GitHub 에 올리지 못했습니다 — 나중에 '소스 올리기' 로 dev-env 를 올리세요:\r\n" + p.Text };
            return new PublishResult { Ok = true, Pushed = true, Repo = rp, Text = "GitHub 에 올렸습니다 — 다른 PC 의 설치 화면에도 이 앱이 나옵니다." + (p.Text.Length > 0 ? "\r\n" + p.Text : "") };
        }
        static PublishResult Fail(string text) { return new PublishResult { Text = text }; }

        // 알림 — git 이 사용법 전체 같은 긴 글을 내면 창이 화면 밖으로 넘쳐 단추가 안 보인다: 앞 20줄 … 끝 4줄 (끝의 안내 글은 남긴다)
        static DialogResult Msg(IWin32Window owner, string text, MessageBoxIcon icon = MessageBoxIcon.None)
        {
            var ls = (text ?? "").Split('\n');
            if (ls.Length > 25) text = string.Join("\n", ls.Take(20)).TrimEnd() + "\r\n…\r\n" + string.Join("\n", ls.Skip(ls.Length - 4));
            return MessageBox.Show(owner, text, "PC 설치", MessageBoxButtons.OK, icon);
        }

        // 다른 스레드에서 끝난 일을 창에 — 창이 이미 닫혔으면 버린다
        static void On(Form d, Action a) { if (d.IsDisposed || !d.IsHandleCreated) return; try { d.BeginInvoke(a); } catch { } }

        public static void Show(MainForm f)
        {
            if (!Winget.Alive()) { Msg(f, "winget 이 없습니다 — ④ 도구·앱 의 winget 을 먼저 설치하세요."); return; }
            var wgExe = Winget.Exe;
            string stLine = "", stId = "", stHow = "";
            bool busy = false;   // 목록에 넣고 올리는 중 — 닫기·입력을 막는다
            int seq = 0;         // 찾기 차례 — 늦게 온 예전 결과는 버린다

            using (var d = new Form())
            {
                d.Text = "앱 추가 — winget 에서 찾아 목록(catalog.txt)에 넣기";
                d.Font = MainForm.UiFont; d.AutoScaleMode = AutoScaleMode.None;   // 크기는 Dpi.S 로 직접 맞춘다
                d.StartPosition = FormStartPosition.CenterParent; d.MaximizeBox = false; d.MinimizeBox = false; d.ShowInTaskbar = false;
                d.FormBorderStyle = FormBorderStyle.Sizable; d.ShowIcon = false;

                var q = new TextBox { Dock = DockStyle.Fill, Margin = new Padding(0, 3, 6, 3) };
                var bFind = Btn("찾기");
                var lv = new ListView { Dock = DockStyle.Fill, View = View.Details, FullRowSelect = true, MultiSelect = false, HideSelection = false, Margin = new Padding(0, 6, 0, 6) };
                var widths = new[] { 210, 270, 110, 90 };
                var heads = new[] { "이름", "아이디", "버전", "찾은 곳" };
                for (int k = 0; k < heads.Length; k++) lv.Columns.Add(heads[k], widths[k]);
                var nm = new TextBox { Dock = DockStyle.Fill, Margin = new Padding(0, 3, 0, 3) };
                var grp = new ComboBox { Width = 200, Anchor = AnchorStyles.Left, Margin = new Padding(0, 3, 0, 3) };
                foreach (var g in new[] { "기본 앱", "원격", "도구", "Claude", "개발 환경" }.Concat(Data.Catalog.Select(e => e.Group)).Distinct(StringComparer.OrdinalIgnoreCase)) grp.Items.Add(g);
                grp.Text = "도구";
                var on = new CheckBox { Text = "새 PC 에서 기본으로 체크", AutoSize = true, Margin = new Padding(0, 3, 16, 3) };
                var pin = new CheckBox { Text = "이 버전으로 고정", AutoSize = true, Margin = new Padding(0, 3, 0, 3) };
                var prev = new Label { AutoSize = true, ForeColor = Color.DimGray, Margin = new Padding(0, 6, 0, 0), UseMnemonic = false };
                var msg = new Label { AutoSize = true, Margin = new Padding(0, 6, 0, 6), UseMnemonic = false, Text = "이름(영어가 잘 찾아진다)을 넣고 찾기 — 예: notepad, honeyview, kakaotalk" };
                var bOk = Btn("목록에 추가하고 올리기"); bOk.Enabled = false;
                var bX = Btn("닫기"); bX.DialogResult = DialogResult.Cancel;

                // 배치 — 표·도킹만(고정 좌표 없음): 창 크기를 바꾸면 결과 목록이 늘고 준다
                var top = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 2, RowCount = 1, AutoSize = true, Margin = new Padding(0) };
                top.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100)); top.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
                top.Controls.Add(q, 0, 0); top.Controls.Add(bFind, 1, 0);

                // 체크 둘은 표의 두 칸에 걸쳐 한 줄로, 폭이 모자라면 두 줄로 — 높이는 아래 wrap 이 맞춘다(자동 크기 FlowLayoutPanel 은 표 안에서 높이를 잘못 잰다)
                var opts = new FlowLayoutPanel { Anchor = AnchorStyles.Left | AnchorStyles.Right | AnchorStyles.Top, WrapContents = true, Margin = new Padding(0) };
                opts.Controls.AddRange(new Control[] { on, pin });
                var form = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 2, RowCount = 3, AutoSize = true, Margin = new Padding(0) };
                form.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize)); form.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
                form.Controls.Add(Lbl("목록에 보일 이름"), 0, 0); form.Controls.Add(nm, 1, 0);
                form.Controls.Add(Lbl("묶음"), 0, 1); form.Controls.Add(grp, 1, 1);
                form.Controls.Add(opts, 0, 2); form.SetColumnSpan(opts, 2);

                var btns = new FlowLayoutPanel { Dock = DockStyle.Fill, AutoSize = true, FlowDirection = FlowDirection.RightToLeft, WrapContents = false, Margin = new Padding(0) };
                btns.Controls.AddRange(new Control[] { bX, bOk });

                var root = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 1, RowCount = 6, Padding = new Padding(10, 8, 10, 8) };
                root.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
                root.RowStyles.Add(new RowStyle(SizeType.AutoSize));
                root.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
                for (int k = 0; k < 4; k++) root.RowStyles.Add(new RowStyle(SizeType.AutoSize));
                root.Controls.Add(top, 0, 0); root.Controls.Add(lv, 0, 1); root.Controls.Add(form, 0, 2);
                root.Controls.Add(prev, 0, 3); root.Controls.Add(msg, 0, 4); root.Controls.Add(btns, 0, 5);
                d.Controls.Add(root);
                d.AcceptButton = bFind; d.CancelButton = bX;

                // 크기 — 화면(작업 영역)보다 크면 줄인다. 긴 안내 글은 창 폭에서 줄을 바꾼다
                var wa = Screen.FromControl(f).WorkingArea;
                d.Size = new Size(Math.Min(Dpi.S(740), wa.Width), Math.Min(Dpi.S(600), wa.Height));
                Action wrap = () =>
                {
                    var w = Math.Max(100, root.ClientSize.Width - root.Padding.Horizontal - 4);
                    prev.MaximumSize = new Size(w, 0); msg.MaximumSize = new Size(w, 0);
                    if (form.ClientSize.Width > 0) { var h = opts.GetPreferredSize(new Size(form.ClientSize.Width, 0)).Height; if (opts.Height != h) opts.Height = h; }
                };
                d.Layout += (s, e) => wrap();
                // 가장 작게 = 결과 목록이 몇 줄 보이고, 안내 글이 줄을 바꿔도 단추가 잘리지 않는 높이
                d.Shown += (s, e) =>
                {
                    var need = d.Height - lv.Height + Dpi.S(110) + 3 * d.Font.Height;
                    d.MinimumSize = new Size(Math.Min(Dpi.S(480), wa.Width), Math.Min(need, wa.Height));
                };
                lv.Resize += (s, e) =>
                {
                    // 열 너비를 목록 폭에 맞춰 같은 비율로
                    int total = widths.Sum(), avail = lv.ClientSize.Width - 4;
                    if (avail < 200) return;
                    for (int k = 0; k < lv.Columns.Count; k++) lv.Columns[k].Width = avail * widths[k] / total;
                };

                Action upd = () =>
                {
                    if (lv.SelectedItems.Count == 0) { bOk.Enabled = false; prev.Text = ""; return; }
                    var sel = lv.SelectedItems[0];
                    stId = sel.SubItems[1].Text; var ver = sel.SubItems[2].Text;
                    stHow = "winget:" + stId + (pin.Checked && ver.Length > 0 && !string.Equals(ver, "Unknown", StringComparison.OrdinalIgnoreCase) ? "@" + ver : "");
                    var name = nm.Text.Trim().Replace("|", "/"); var g = Regex.Replace(grp.Text.Trim(), "[|/]", " ");
                    stLine = g + " | " + name + " | " + stHow + " | " + (on.Checked ? "on" : "off");
                    prev.Text = "catalog.txt 에 넣을 줄:\r\n" + stLine;
                    bOk.Enabled = !busy && name.Length > 0 && g.Length > 0 && !stId.Contains("…");
                };

                bFind.Click += (s, e) =>
                {
                    var text = q.Text.Trim(); if (text.Length == 0 || busy) return;
                    bFind.Enabled = false; bOk.Enabled = false; lv.Items.Clear(); msg.Text = "winget 에서 '" + text + "' 를 찾는 중...";
                    int my = ++seq;
                    // winget 은 다른 스레드에서 — 찾는 동안에도 창은 움직이고 닫을 수 있다
                    new Thread(() =>
                    {
                        ProcResult r = null; List<string[]> rows = new List<string[]>();
                        try
                        {
                            r = Proc.Run(wgExe, Proc.Args("search", text, "--source", "winget", "--accept-source-agreements", "--disable-interactivity"), 60);
                            if (!r.Started || r.TimedOut) r = null; else rows = Winget.ParseTable(r.Out);
                        }
                        catch { r = null; }
                        On(d, () =>
                        {
                            if (my != seq) return;
                            lv.BeginUpdate();
                            foreach (var row in rows)
                            {
                                if (row.Length < 3 || row[1].Length == 0) continue;
                                var li = new ListViewItem(row[0]);
                                for (int k = 1; k <= 3; k++) li.SubItems.Add(row.Length > k ? row[k] : "");
                                lv.Items.Add(li);
                            }
                            lv.EndUpdate();
                            msg.Text = r == null ? "winget 이 1분 안에 답하지 않았습니다 — 다시 찾기" : lv.Items.Count > 0 ? lv.Items.Count + "개 — 하나를 고르세요" : "찾은 앱이 없습니다 — 다른 이름(영어)으로 찾아 보세요";
                            bFind.Enabled = !busy;
                        });
                    }) { IsBackground = true }.Start();
                };
                lv.SelectedIndexChanged += (s, e) => { if (lv.SelectedItems.Count > 0) nm.Text = lv.SelectedItems[0].Text.TrimEnd('…'); upd(); };
                nm.TextChanged += (s, e) => upd(); grp.TextChanged += (s, e) => upd();
                on.CheckedChanged += (s, e) => upd(); pin.CheckedChanged += (s, e) => upd();

                bOk.Click += (s, e) =>
                {
                    upd();
                    if (!bOk.Enabled || busy) return;
                    var name = nm.Text.Trim().Replace("|", "/"); var g = Regex.Replace(grp.Text.Trim(), "[|/]", " ");
                    var id = stId; var how = stHow; var line = stLine; var isOn = on.Checked;
                    var dup = Data.Catalog.FirstOrDefault(x => { var mm = HowId.Match(x.How ?? ""); return mm.Success && string.Equals(mm.Groups[2].Value, id, StringComparison.OrdinalIgnoreCase); });
                    if (dup != null) { msg.Text = "이미 목록에 있습니다: " + dup.Group + " | " + dup.Name; return; }
                    if (Defs.All.Any(x => string.Equals(x.Id, "app:" + name, StringComparison.OrdinalIgnoreCase))) { msg.Text = "같은 이름의 항목이 이미 있습니다 — 이름을 바꾸세요"; return; }
                    busy = true; bOk.Enabled = false; bFind.Enabled = false; top.Enabled = false; lv.Enabled = false; form.Enabled = false;
                    msg.Text = "목록에 넣고 GitHub 에 올리는 중...";
                    // git 받기·올리기는 몇 분 걸릴 수 있다 — 다른 스레드에서
                    new Thread(() =>
                    {
                        PublishResult res;
                        try { res = PublishCatalogLine(g, line, name + " (" + how + ")"); }
                        catch (Exception ex) { res = Fail("목록에 넣지 못했습니다: " + ex.Message); }
                        // 이번 설치 화면에도 넣는다 (저장소가 없으면 여기에만 — 파일은 쓰지 않는다). 설치 확인(winget 목록)도 이 스레드에서
                        // (이 스레드의 예외는 프로그램을 끝내고 창을 '올리는 중' 에 묶어 둔다 — 모두 여기서 받는다)
                        Item it = null;
                        try
                        {
                            if (res.Ok || res.NoRepo)
                            {
                                var en = new CatalogEntry { Group = g, Name = name, How = how, On = isOn, Hint = "", Line = line };
                                Data.Catalog.Add(en);
                                it = Defs.AddCatalogItem(en);
                                if (it != null) it.Installed = Defs.Check(it);
                            }
                        }
                        catch (Exception ex) { Log.Say("앱 추가: 이번 설치 화면에 넣지 못했습니다 — " + ex.Message, Tone.Warn); }
                        On(d, () =>
                        {
                            busy = false; top.Enabled = true; lv.Enabled = true; form.Enabled = true;
                            if (!res.Ok && !res.NoRepo) { bFind.Enabled = true; upd(); msg.Text = ""; Msg(d, res.Text, MessageBoxIcon.Warning); return; }
                            string tail = "";
                            if (it != null)
                            {
                                try
                                {
                                    var node = f.AddItemNode(it, true);
                                    node.Checked = !it.Installed;   // 기본 끔이어도 지금은 체크 — 이 PC 에 시험 설치할 수 있게 (묶음 체크는 화면이 맞춘다)
                                    tail = it.Installed ? "이 PC 에는 이미 설치돼 있습니다." : "설치 화면에 체크된 채로 넣었습니다 — '선택한 것 설치' 로 이 PC 에 시험 설치할 수 있습니다.";
                                }
                                catch (Exception ex) { Log.Say("앱 추가: 설치 화면에 넣지 못했습니다 — " + ex.Message, Tone.Warn); }
                            }
                            Log.Say("앱 추가: " + line);
                            Msg(d, res.Text + "\r\n\r\n" + tail, res.Pushed && !res.Text.Contains("주의:") ? MessageBoxIcon.Information : MessageBoxIcon.Warning);
                            d.Close();
                        });
                    }) { IsBackground = true }.Start();
                };
                d.FormClosing += (s, e) => { if (busy) { e.Cancel = true; msg.Text = "목록에 넣고 GitHub 에 올리는 중입니다 — 끝나면 닫힙니다"; } };

                d.ShowDialog(f);
            }
        }

        static Button Btn(string text) { return new Button { Text = text, AutoSize = true, AutoSizeMode = AutoSizeMode.GrowAndShrink, Padding = new Padding(8, 3, 8, 3), Margin = new Padding(6, 0, 0, 0), UseVisualStyleBackColor = true }; }
        static Label Lbl(string text) { return new Label { Text = text, AutoSize = true, Anchor = AnchorStyles.Left, Margin = new Padding(0, 3, 10, 3) }; }
    }
}
