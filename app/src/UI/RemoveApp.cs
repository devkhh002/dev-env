using System;
using System.Collections.Generic;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading;
using System.Windows.Forms;

namespace PcSetup
{
    // ── 앱 빼기: catalog.txt 에서 골라 지우고 GitHub 에 올린다 (이 PC 에 깔린 앱은 그대로) ──
    public static partial class Hooks
    {
        static partial void ShowRemoveAppImpl(MainForm f) { RemoveAppDialog.Show(f); }
    }

    public static class RemoveAppDialog
    {
        // Ok = 커밋했다 · Pushed = GitHub 에도 올렸다 · Removed = 지운 이름 · Gone = 받은 목록에 이미 없던 이름
        public sealed class RemoveResult
        {
            public bool Ok, Pushed, NoRepo; public string Repo, Text = "";
            public List<string> Removed = new List<string>(), Gone = new List<string>();
        }

        // 한 줄의 이름(두 번째 칸) — 주석·빈 줄·칸이 모자란 줄(설치 화면이 읽지 않는 줄)은 null
        static string NameOf(string line)
        {
            if (line.Trim().Length == 0 || line.TrimStart().StartsWith("#")) return null;
            var c = line.Split('|');
            return c.Length >= 3 ? c[1].Trim() : null;
        }

        // catalog.txt 에서 이름이 맞는 줄을 지운다 — 다른 줄(주석·빈 줄 포함)은 글자 하나 바꾸지 않는다. BOM·줄바꿈 방식도 그대로. 지운 이름을 돌려준다
        public static List<string> RemoveCatalogLines(string file, ICollection<string> names)
        {
            var bytes = File.ReadAllBytes(file);
            int skip = bytes.Length >= 3 && bytes[0] == 0xEF && bytes[1] == 0xBB && bytes[2] == 0xBF ? 3 : 0;
            var keep = new MemoryStream(bytes.Length); var removed = new List<string>();
            keep.Write(bytes, 0, skip);
            // 줄을 끝의 줄바꿈(\n)까지 바이트 그대로 — 남기는 줄은 원래 바이트를 옮겨 적고, 지우는 줄은 줄바꿈째 뺀다 (UTF-8 에서 \n 바이트는 글자 가운데에 나오지 않는다)
            for (int i = skip; i < bytes.Length; )
            {
                int j = Array.IndexOf(bytes, (byte)'\n', i), end = j < 0 ? bytes.Length : j + 1;
                var name = NameOf(Encoding.UTF8.GetString(bytes, i, end - i).TrimEnd('\n').TrimEnd('\r'));
                if (name != null && names.Contains(name)) { if (!removed.Contains(name)) removed.Add(name); }
                else keep.Write(bytes, i, end - i);
                i = end;
            }
            if (removed.Count > 0) File.WriteAllBytes(file, keep.ToArray());
            return removed;
        }

        // 저장소의 catalog.txt 에서 지우고 커밋·푸시
        public static RemoveResult PublishRemoval(IList<string> names) { return PublishRemoval(AddAppDialog.FindEditRepo(), names); }
        public static RemoveResult PublishRemoval(string rp, IList<string> names)
        {
            if (rp == null) return new RemoveResult { NoRepo = true, Text = NoRepoText };
            // git diff --quiet: 0 = 바뀐 것 없음 · 1 = 있음 · 그 밖(git 없음·시간 초과 -1 등) = 확인 못 함
            var r = Git.Run(rp, "diff", "--cached", "--quiet");
            if (r.Code == 1) return Fail(rp + " 에 커밋을 기다리는 다른 변경이 있어 건드리지 않았습니다 — 그것부터 정리하세요.");
            if (r.Code != 0) return Fail("git 으로 " + rp + " 를 확인하지 못했습니다:\r\n" + r.Text);
            r = Git.Run(rp, "diff", "--quiet", "--", "catalog.txt");
            if (r.Code == 1) return Fail(rp + " 의 catalog.txt 에 올리지 않은 수정이 있어 건드리지 않았습니다 — 그것부터 올리거나 되돌리세요.");
            if (r.Code != 0) return Fail("git 으로 " + rp + " 를 확인하지 못했습니다:\r\n" + r.Text);
            // 모든 PC 가 받는 곳은 GitHub 의 main — 다른 브랜치에서 지우면 아무 PC 에도 반영되지 않는다
            var u = Git.Run(rp, "rev-parse", "--abbrev-ref", "--symbolic-full-name", "@{u}");
            if (u.Code != 0 || !string.Equals(u.Out.Trim(), "origin/main", StringComparison.OrdinalIgnoreCase)) return Fail(rp + " 가 main(origin/main) 이 아닌 곳에 있어 건드리지 않았습니다 — main 으로 돌아온 뒤 다시 빼세요.");
            r = Git.Run(rp, 120, "pull", "--ff-only", "-q");
            if (r.Code != 0) return Fail("GitHub 최신 목록을 받지 못해 빼지 않았습니다:\r\n" + r.Text);
            var cat = Path.Combine(rp, "catalog.txt");
            // 이 화면을 연 뒤 다른 PC 가 먼저 뺐을 수 있다 — 방금 받은 목록으로 한 번 더 본다
            var want = new HashSet<string>(names.Select(n => (n ?? "").Trim()).Where(n => n.Length > 0), StringComparer.Ordinal);
            List<string> removed;
            ProcResult c2;
            try
            {
                removed = RemoveCatalogLines(cat, want);
                if (removed.Count == 0)
                    return new RemoveResult { Repo = rp, Gone = want.ToList(), Text = "GitHub 최신 목록에 이미 없습니다(다른 PC 에서 먼저 뺌): " + string.Join(", ", want) + "\r\n바꿀 것이 없어 올리지 않았습니다." };
                Git.Run(rp, "add", "--", "catalog.txt");   // 설치 화면은 열 때마다 GitHub 의 catalog.txt 를 읽는다 — 다시 빌드할 필요 없다
                c2 = Git.Commit(rp, "앱 빼기 — " + string.Join(", ", removed));
            }
            catch (Exception ex) { removed = new List<string>(); c2 = new ProcResult { Code = -1, Err = ex.Message }; }
            if (c2.Code != 0)
            {
                // 커밋을 못 하면(이 PC 에 git 이름·메일이 없다 등) 지운 줄을 되돌린다 — 담긴 채 남으면 다음 '앱 추가'·'앱 빼기' 가 모두 막힌다
                Git.Run(rp, "checkout", "HEAD", "--", "catalog.txt");
                return Fail("커밋하지 못해 목록을 되돌렸습니다 — ⑤ 'git 기본 설정' 을 먼저 하세요:\r\n" + c2.Text);
            }
            var gone = want.Where(n => !removed.Contains(n)).ToList();
            var note = gone.Count > 0 ? "\r\n이미 빠져 있음(다른 PC 에서 먼저 뺌): " + string.Join(", ", gone) : "";
            // 커밋은 됐다 — 여기서 예외가 나도 '커밋함' 으로 돌려준다(그래야 화면에서도 뺀다)
            Git.PushResult p;
            try { p = Git.Push(rp, true); } catch (Exception ex) { p = new Git.PushResult { Ok = false, Text = ex.Message }; }
            if (!p.Ok) return new RemoveResult { Ok = true, Repo = rp, Removed = removed, Gone = gone, Text = "커밋은 했지만 GitHub 에 올리지 못했습니다 — 나중에 '소스 올리기' 로 dev-env 를 올리세요:\r\n" + p.Text + note };
            return new RemoveResult { Ok = true, Pushed = true, Repo = rp, Removed = removed, Gone = gone, Text = "GitHub 에 올렸습니다 — 다른 PC 의 설치 화면에서도 빠집니다." + (p.Text.Length > 0 ? "\r\n" + p.Text : "") + note };
        }
        static RemoveResult Fail(string text) { return new RemoveResult { Text = text }; }
        static string NoRepoText { get { return "이 PC 에는 dev-env 저장소(" + Conf.DevRoot + "\\dev-env)가 없어 목록을 고칠 수 없습니다 — ⑥ 개발 소스의 dev-env 를 받은 PC 에서 하세요."; } }

        // 알림 — git 이 사용법 전체 같은 긴 글을 내면 창이 화면 밖으로 넘쳐 단추가 안 보인다: 앞 20줄 … 끝 4줄 (끝의 안내 글은 남긴다)
        static DialogResult Msg(IWin32Window owner, string text, MessageBoxIcon icon = MessageBoxIcon.None, MessageBoxButtons buttons = MessageBoxButtons.OK)
        {
            var ls = (text ?? "").Split('\n');
            if (ls.Length > 25) text = string.Join("\n", ls.Take(20)).TrimEnd() + "\r\n…\r\n" + string.Join("\n", ls.Skip(ls.Length - 4));
            return MessageBox.Show(owner, text, "PC 설치", buttons, icon);
        }

        // 다른 스레드에서 끝난 일을 창에 — 창이 이미 닫혔으면 버린다
        static void On(Form d, Action a) { if (d.IsDisposed || !d.IsHandleCreated) return; try { d.BeginInvoke(a); } catch { } }

        public static void Show(MainForm f)
        {
            bool busy = false;   // 지우고 올리는 중 — 닫기·체크를 막는다

            using (var d = new Form())
            {
                d.Text = "앱 빼기 — 목록(catalog.txt)에서 지우기";
                d.Font = MainForm.UiFont; d.AutoScaleMode = AutoScaleMode.None;   // 크기는 Dpi.S 로 직접 맞춘다
                d.StartPosition = FormStartPosition.CenterParent; d.MaximizeBox = false; d.MinimizeBox = false; d.ShowInTaskbar = false;
                d.FormBorderStyle = FormBorderStyle.Sizable; d.ShowIcon = false;

                var info = new Label { AutoSize = true, Margin = new Padding(0, 0, 0, 6), UseMnemonic = false, Text = "체크한 앱을 catalog.txt 에서 지우고 GitHub 에 올립니다 — 모든 PC 의 설치 화면에서 사라집니다. 이 PC 에 깔린 앱은 지우지 않습니다." };
                var lv = new ListView { Dock = DockStyle.Fill, View = View.Details, CheckBoxes = true, FullRowSelect = true, MultiSelect = false, HideSelection = false, Margin = new Padding(0, 0, 0, 6) };
                var widths = new[] { 105, 195, 270, 110 };
                var heads = new[] { "묶음", "이름", "설치 방법", "처음 체크" };
                for (int k = 0; k < heads.Length; k++) lv.Columns.Add(heads[k], widths[k]);
                lv.BeginUpdate();
                foreach (var en in Data.Catalog)   // catalog.txt 에 적힌 차례 그대로
                {
                    var li = new ListViewItem(en.Group) { Tag = en.Name };
                    li.SubItems.Add(en.Name); li.SubItems.Add(en.How); li.SubItems.Add(en.On ? "on" : "off");
                    lv.Items.Add(li);
                }
                lv.EndUpdate();
                var msg = new Label { AutoSize = true, Margin = new Padding(0, 0, 0, 6), UseMnemonic = false };
                var bOk = Btn("목록에서 빼고 올리기"); bOk.Enabled = false;
                var bX = Btn("닫기"); bX.DialogResult = DialogResult.Cancel;

                // 배치 — 표·도킹만(고정 좌표 없음): 창 크기를 바꾸면 목록이 늘고 준다
                var btns = new FlowLayoutPanel { Dock = DockStyle.Fill, AutoSize = true, FlowDirection = FlowDirection.RightToLeft, WrapContents = false, Margin = new Padding(0) };
                btns.Controls.AddRange(new Control[] { bX, bOk });
                var root = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 1, RowCount = 4, Padding = new Padding(10, 8, 10, 8) };
                root.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
                root.RowStyles.Add(new RowStyle(SizeType.AutoSize));
                root.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
                for (int k = 0; k < 2; k++) root.RowStyles.Add(new RowStyle(SizeType.AutoSize));
                root.Controls.Add(info, 0, 0); root.Controls.Add(lv, 0, 1); root.Controls.Add(msg, 0, 2); root.Controls.Add(btns, 0, 3);
                d.Controls.Add(root);
                d.CancelButton = bX;

                // 크기 — 화면(작업 영역)보다 크면 줄인다. 긴 안내 글은 창 폭에서 줄을 바꾼다
                var wa = Screen.FromControl(f).WorkingArea;
                d.Size = new Size(Math.Min(Dpi.S(780), wa.Width), Math.Min(Dpi.S(560), wa.Height));
                Action wrap = () =>
                {
                    var w = Math.Max(100, root.ClientSize.Width - root.Padding.Horizontal - 4);
                    info.MaximumSize = new Size(w, 0); msg.MaximumSize = new Size(w, 0);
                };
                d.Layout += (s, e) => wrap();
                // 가장 작게 = 목록이 몇 줄 보이고, 안내 글이 줄을 바꿔도 단추가 잘리지 않는 높이
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
                    // 줄일 때 Windows 가 예전 열 너비로 만든 가로 스크롤 막대가 남는다 — 크기 바꾸기가 끝난 뒤 한 번 더 맞춰 지운다
                    if (lv.IsHandleCreated) lv.BeginInvoke((Action)(() => { if (!lv.IsDisposed) { var c = lv.Columns[lv.Columns.Count - 1]; var w = c.Width; c.Width = w - 1; c.Width = w; } }));
                };

                Func<List<string>> chosen = () => lv.CheckedItems.Cast<ListViewItem>().Select(x => (string)x.Tag).Distinct().ToList();
                // 저장소가 없으면 그 안내를 계속 보인다(체크해도 덮지 않는다)
                bool noRepo = AddAppDialog.FindEditRepo() == null;
                Action upd = () =>
                {
                    var n = lv.CheckedItems.Count;
                    bOk.Enabled = !busy && n > 0;
                    if (!busy) msg.Text = noRepo ? NoRepoText : lv.Items.Count == 0 ? "목록(catalog.txt)이 비어 있습니다." : n > 0 ? n + "개 체크 — 목록에서 빼고 올리기 를 누르세요" : "목록에서 뺄 앱을 체크하세요";
                };
                upd();
                lv.ItemChecked += (s, e) => { if (busy) return; upd(); };
                // 올리는 동안 체크를 바꾸지 못하게 (목록은 스크롤할 수 있게 켜 둔다)
                lv.ItemCheck += (s, e) => { if (busy) e.NewValue = e.CurrentValue; };

                bOk.Click += (s, e) =>
                {
                    if (busy) return;
                    var names = chosen();
                    if (names.Count == 0) { upd(); return; }
                    if (AddAppDialog.FindEditRepo() == null) { Msg(d, NoRepoText, MessageBoxIcon.Information); return; }   // 묻기 전에 — 어차피 고칠 수 없다
                    var ask = "이 " + names.Count + "개를 목록(catalog.txt)에서 빼고 GitHub 에 올릴까요?\r\n\r\n" + string.Join("\r\n", names.Select(n => "  · " + n)) +
                              "\r\n\r\n모든 PC 의 설치 화면에서 사라집니다. 이 PC 에 깔린 앱은 지우지 않습니다.";
                    if (Msg(d, ask, MessageBoxIcon.Question, MessageBoxButtons.YesNo) != DialogResult.Yes) return;
                    busy = true; bOk.Enabled = false; bX.Enabled = false;
                    msg.Text = "목록에서 빼고 GitHub 에 올리는 중...";
                    // git 받기·올리기는 몇 분 걸릴 수 있다 — 다른 스레드에서
                    new Thread(() =>
                    {
                        RemoveResult res;
                        try { res = PublishRemoval(names); }
                        catch (Exception ex) { res = Fail("목록에서 빼지 못했습니다: " + ex.Message); }
                        On(d, () =>
                        {
                            busy = false; bX.Enabled = true;
                            // 이번 설치 화면에서도 뺀다 — 커밋한 것, 그리고 받은 목록에 이미 없던 것(다른 PC 가 먼저 뺌)
                            var drop = res.Removed.Concat(res.Gone).Distinct().ToList();
                            foreach (var n in drop)
                            {
                                try { Data.Catalog.RemoveAll(x => x.Name == n); f.RemoveItemNode("app:" + n); }
                                catch (Exception ex) { Log.Say("앱 빼기: 설치 화면에서 빼지 못했습니다(" + n + ") — " + ex.Message, Tone.Warn); }
                            }
                            foreach (var item in lv.Items.Cast<ListViewItem>().Where(x => drop.Contains((string)x.Tag)).ToList()) lv.Items.Remove(item);
                            if (!res.Ok)
                            {
                                upd(); msg.Text = "";
                                Msg(d, res.Text, res.NoRepo || res.Gone.Count > 0 ? MessageBoxIcon.Information : MessageBoxIcon.Warning);
                                upd();
                                return;
                            }
                            Log.Say("앱 빼기: " + string.Join(", ", res.Removed) + (res.Pushed ? "" : " (GitHub 에는 아직 못 올림)"));
                            Msg(d, res.Text, res.Pushed && !res.Text.Contains("주의:") ? MessageBoxIcon.Information : MessageBoxIcon.Warning);
                            d.Close();
                        });
                    }) { IsBackground = true }.Start();
                };
                d.FormClosing += (s, e) => { if (busy) { e.Cancel = true; msg.Text = "목록에서 빼고 GitHub 에 올리는 중입니다 — 끝나면 닫힙니다"; } };

                d.ShowDialog(f);
            }
        }

        static Button Btn(string text) { return new Button { Text = text, AutoSize = true, AutoSizeMode = AutoSizeMode.GrowAndShrink, Padding = new Padding(8, 3, 8, 3), Margin = new Padding(6, 0, 0, 0), UseVisualStyleBackColor = true }; }
    }
}
