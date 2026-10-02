using System;
using System.Collections.Generic;
using System.IO;
using System.Net;
using System.Text;
using System.Web.Script.Serialization;

namespace PcSetup
{
    // 인터넷 — 글 받기·파일 받기(진행률)·JSON
    public static class Net
    {
        public const string Raw = "https://raw.githubusercontent.com/devkhh002/dev-env/main/";
        public static readonly string Cache = Path.Combine(Path.GetTempPath(), "dev-env-cache");

        public static void Init()
        {
            ServicePointManager.SecurityProtocol |= SecurityProtocolType.Tls12;
            ServicePointManager.DefaultConnectionLimit = 8;
            Directory.CreateDirectory(Cache);
        }

        static HttpWebRequest Req(string url, int timeoutSec, string method = "GET")
        {
            var q = (HttpWebRequest)WebRequest.Create(url);
            q.Method = method;
            q.Timeout = timeoutSec * 1000;
            q.ReadWriteTimeout = Math.Max(timeoutSec, 60) * 1000;
            q.AllowAutoRedirect = true;
            q.UserAgent = "Mozilla/5.0 (Windows NT 10.0; Win64; x64) dev-env";
            q.AutomaticDecompression = DecompressionMethods.GZip | DecompressionMethods.Deflate;
            q.Headers["Cache-Control"] = "no-cache";
            return q;
        }

        // 글 받기 — 실패하면 예외
        public static string GetString(string url, int timeoutSec = 30, IDictionary<string, string> headers = null)
        {
            var q = Req(url, timeoutSec);
            if (headers != null) foreach (var kv in headers) { if (kv.Key.Equals("Accept", StringComparison.OrdinalIgnoreCase)) q.Accept = kv.Value; else q.Headers[kv.Key] = kv.Value; }
            using (var r = (HttpWebResponse)q.GetResponse())
            using (var s = r.GetResponseStream())
            using (var rd = new StreamReader(s, Encoding.UTF8, true)) return rd.ReadToEnd();
        }
        public static bool TryGetString(string url, out string text, int timeoutSec = 15)
        {
            try { text = GetString(url, timeoutSec); return true; } catch { text = null; return false; }
        }

        // 인터넷이 되는가 — GitHub 에 8초 안에 닿으면
        public static bool Online()
        {
            try { var q = Req(Raw + "README.md", 8, "HEAD"); using (q.GetResponse()) { } return true; } catch { return false; }
        }

        // 파일 받기 → 받은 경로. 받는 동안 .part 로 두었다가 다 받으면 이름을 바꾼다(끊긴 파일을 다 받은 것으로 착각하지 않게).
        // 크기를 알면(GitHub 릴리스) 이미 받아 둔 파일과 대조한다. 실패하면 3번까지 다시, 그래도 안 되면 예외.
        public static string Download(string url, string fileName, long expectedSize = 0)
        {
            Directory.CreateDirectory(Cache);
            var path = Path.Combine(Cache, fileName);
            if (File.Exists(path) && (expectedSize <= 0 || new FileInfo(path).Length == expectedSize)) return path;
            if (File.Exists(path)) File.Delete(path);
            Exception last = null;
            for (int attempt = 1; attempt <= 3; attempt++)
            {
                var part = path + ".part";
                try
                {
                    Log.Say("   받는 중: " + fileName);
                    var q = Req(url, 60);
                    using (var r = (HttpWebResponse)q.GetResponse())
                    using (var s = r.GetResponseStream())
                    using (var f = File.Create(part))
                    {
                        long total = r.ContentLength, got = 0; var buf = new byte[1 << 16]; int n; var tick = Environment.TickCount;
                        while ((n = s.Read(buf, 0, buf.Length)) > 0)
                        {
                            f.Write(buf, 0, n); got += n;
                            if (Environment.TickCount - tick > 500) { tick = Environment.TickCount; Log.SetStatus(total > 0 ? string.Format("받는 중 {0} — {1:N0} / {2:N0} MB ({3}%)", fileName, got >> 20, total >> 20, got * 100 / total) : string.Format("받는 중 {0} — {1:N0} MB", fileName, got >> 20)); }
                        }
                    }
                    if (expectedSize > 0 && new FileInfo(part).Length != expectedSize) throw new IOException("크기가 맞지 않습니다");
                    File.Move(part, path);
                    Log.SetStatus("");
                    Log.Say(string.Format("   받음: {0} ({1:N0}MB)", fileName, new FileInfo(path).Length / 1048576.0));
                    return path;
                }
                catch (Exception ex) { last = ex; try { File.Delete(part); } catch { } System.Threading.Thread.Sleep(2000 * attempt); }
            }
            Log.SetStatus("");
            throw new IOException("다운로드 실패: " + url + " — " + (last == null ? "" : last.Message));
        }

        // JSON → Dictionary<string,object> / object[] / 값
        public static object Json(string text)
        {
            var js = new JavaScriptSerializer { MaxJsonLength = int.MaxValue, RecursionLimit = 256 };
            return js.DeserializeObject(text);
        }
        public static string ToJson(object o)
        {
            var js = new JavaScriptSerializer { MaxJsonLength = int.MaxValue, RecursionLimit = 256 };
            return js.Serialize(o);
        }

        // GitHub 최신 릴리스의 파일 하나 (이름 패턴 *) → (주소, 크기)
        public static KeyValuePair<string, long> GitHubAsset(string repo, string pattern)
        {
            var rel = Json(GetString("https://api.github.com/repos/" + repo + "/releases/latest", 30, new Dictionary<string, string> { { "Accept", "application/vnd.github+json" } })) as Dictionary<string, object>;
            var rx = new System.Text.RegularExpressions.Regex("^" + System.Text.RegularExpressions.Regex.Escape(pattern).Replace("\\*", ".*") + "$", System.Text.RegularExpressions.RegexOptions.IgnoreCase);
            foreach (var a in (object[])rel["assets"])
            {
                var d = (Dictionary<string, object>)a;
                if (rx.IsMatch((string)d["name"])) return new KeyValuePair<string, long>((string)d["browser_download_url"], Convert.ToInt64(d["size"]));
            }
            throw new IOException(repo + " 최신 릴리스에서 '" + pattern + "' 파일을 찾지 못했습니다");
        }
    }
}
