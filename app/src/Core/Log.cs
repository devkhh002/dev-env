using System;
using System.IO;
using System.Text;

namespace PcSetup
{
    public enum Tone { Normal, Info, Ok, Warn, Error, Dim }

    // 기록 — 설치 화면 아래 칸과 %USERPROFILE%\dev-env-setup.log 에 같이 남긴다
    public static class Log
    {
        static readonly object Gate = new object();
        public static readonly string FilePath = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), "dev-env-setup.log");

        // 한 줄 (화면이 이것을 받아 보여 준다 — 어느 스레드에서든 온다)
        public static event Action<string, Tone> Line;
        // 잠깐 보이는 상태(받는 중 n% 등) — 기록에는 남기지 않는다
        public static event Action<string> Status;

        public static void Say(string msg, Tone tone = Tone.Normal)
        {
            lock (Gate)
            {
                try { File.AppendAllText(FilePath, "[" + DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss") + "] " + msg + "\r\n", new UTF8Encoding(false)); } catch { }
            }
            var h = Line; if (h != null) h(msg, tone);
        }

        // 바깥 프로그램(winget·git·설치 프로그램)이 낸 줄 — 흐리게. 진행 표시가 \r 로 덮어쓴 줄은 마지막 것만
        public static void Raw(string line)
        {
            if (line == null) return;
            var parts = line.Split('\r');
            var last = parts[parts.Length - 1].TrimEnd();
            if (last.Trim().Length == 0) return;
            if (IsSpinner(last)) return;
            Say("      " + last, Tone.Dim);
        }

        public static void SetStatus(string text) { var h = Status; if (h != null) h(text); }

        // winget 의 회전 표시(- \ | /)·막대만 있는 줄은 버린다
        static bool IsSpinner(string s)
        {
            var t = s.Trim();
            if (t.Length <= 2 && "-\\|/".IndexOf(t[0]) >= 0) return true;
            int bar = 0; foreach (var c in t) if (c == '█' || c == '▒' || c == '░') bar++;
            return bar > 5;
        }
    }
}
