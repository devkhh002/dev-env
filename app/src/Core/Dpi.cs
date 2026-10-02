using System;
using System.Drawing;

namespace PcSetup
{
    // 화면 배율(125%·150%) — 창·여백 크기를 이 값으로 늘린다. 글자(pt)는 Windows 가 알아서 키운다
    // (이 프로그램은 '시스템 배율' 방식: 자동 배율(AutoScaleMode)은 쓰지 않고 크기를 여기서 직접 맞춘다)
    public static class Dpi
    {
        public static readonly float Factor = Read();
        static float Read() { try { using (var g = Graphics.FromHwnd(IntPtr.Zero)) return Math.Max(1f, g.DpiX / 96f); } catch { return 1f; } }
        public static int S(int logical) { return (int)Math.Round(logical * Factor); }
        public static System.Windows.Forms.Padding P(int l, int t, int r, int b) { return new System.Windows.Forms.Padding(S(l), S(t), S(r), S(b)); }
    }
}
