using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using System.Xml;

namespace PcSetup
{
    // ③ 드라이버 — 그래픽은 NVIDIA 공식 조회로 늘 최신판. 랜(네트워크) 드라이버는 지금 인터넷을 쓰는 어댑터라 건드리지 않는다(원격이 끊긴다) — ① 의 USB '네트워크 드라이버' 로만.
    public static partial class Defs
    {
        static partial void RegisterDrivers()
        {
            // NVIDIA GeForce 카드가 있을 때만 항목이 뜬다 — 드라이버가 아직 없어도 (없는 PC 에선 목록에 안 나온다)
            if (Nv.Gpu() == null) return;
            Add(G3, "gpudriver", "NVIDIA 그래픽 드라이버 — 공식 조회로 최신판 (설치 중 화면이 잠깐 깜빡인다 · 원격 중엔 기본 꺼짐)", () =>
            {
                var g = Nv.Gpu();
                if (g == null) return true;              // 드라이버가 깔려 이름이 'NVIDIA …'(GeForce 가 아닌 카드)로 바뀌었다 — 더 볼 것 없음
                if (!Nv.HasDriver(g)) return false;      // 기본 디스플레이 드라이버 = NVIDIA 드라이버가 아직 없다
                var inst = Nv.Ver(g.DriverVersion); var lat = Nv.Latest(g);
                return lat == null || inst == null || inst.Value >= lat.Version;   // 최신을 못 알아내면 들볶지 않는다
            }, () =>
            {
                var g = Nv.Gpu(); var lat = Nv.Latest(g, fresh: true);
                if (lat == null) { Log.Say("   NVIDIA 최신 드라이버를 못 찾았습니다 (인터넷·카드 이름 확인)", Tone.Warn); return; }
                Log.Say("   지금 " + (Nv.HasDriver(g) ? Nv.Fmt(Nv.Ver(g.DriverVersion)) : "드라이버 없음") + " → 최신 " + Nv.Fmt(lat.Version));
                var p = Net.Download(lat.Url, "nvidia-" + Nv.Fmt(lat.Version) + ".exe");
                if (Trust.Check(p) != Trust.State.Valid) { try { File.Delete(p); } catch { } throw new Exception("서명이 올바르지 않은 NVIDIA 설치 파일 — 실행하지 않음"); }
                Log.Say("   설치 중 — 화면이 잠깐 깜빡입니다 (원격이면 잠시 끊겨 보일 수 있음)");
                Proc.RunWait(p, "-s -noreboot", hidden: false);
            }, off: true);
        }
    }

    // NVIDIA 카드·드라이버 버전·공식 최신판 조회
    static class Nv
    {
        public sealed class Card { public string Name, PnpId, DriverVersion; }
        public sealed class Release { public double Version; public string Url; }

        static readonly string CacheDir = Path.Combine(Env.LocalAppData, "dev-env");
        const RegexOptions I = RegexOptions.IgnoreCase | RegexOptions.CultureInvariant;
        static readonly CultureInfo Inv = CultureInfo.InvariantCulture;

        // GeForce 카드 — 드라이버가 아직 없으면 이름이 'Microsoft 기본 디스플레이 어댑터' 로 보여도 장치 번호(VEN_10DE)로 찾는다
        // (드라이버가 깔린 Quadro 등 GeForce 가 아닌 NVIDIA 카드는 뺀다. 드라이버가 없는 동안은 그 카드도 잡혀 장치 번호로 그 카드의 공식 드라이버를 깐다)
        public static Card Gpu()
        {
            foreach (var r in Wmi.Query("SELECT Name, PNPDeviceID, DriverVersion FROM Win32_VideoController"))
            {
                var c = new Card { Name = Str(r, "Name"), PnpId = Str(r, "PNPDeviceID"), DriverVersion = Str(r, "DriverVersion") };
                if (c.PnpId.StartsWith(@"PCI\VEN_10DE", StringComparison.OrdinalIgnoreCase) && (Regex.IsMatch(c.Name, "GeForce", I) || !Regex.IsMatch(c.Name, "NVIDIA", I))) return c;
            }
            return null;
        }
        // 이름이 'NVIDIA …' = NVIDIA 드라이버가 깔려 있다
        public static bool HasDriver(Card g) { return g != null && Regex.IsMatch(g.Name, "NVIDIA", I); }

        // 윈도우 드라이버 버전(예: 27.21.14.5751) → NVIDIA 표기 버전(457.51): 끝 두 묶음을 붙여 뒤 5자리에 점 하나
        public static double? Ver(string v)
        {
            var p = (v ?? "").Split('.');
            var d = Regex.Replace(p.Length >= 2 ? p[p.Length - 2] + p[p.Length - 1] : p[p.Length - 1], "[^0-9]", "");
            if (d.Length < 5) return null;
            return double.Parse(d.Substring(d.Length - 5).Insert(3, "."), Inv);
        }
        public static string Fmt(double? v) { return v == null ? "" : v.Value.ToString(Inv); }

        // 최신 드라이버 — 1) PCI 장치 번호로(GeForce Experience 가 쓰는 공식 조회: 요청 한 번, 카드 이름·노트북 여부·드라이버 유무와 무관)
        // 2) 안 되면 카드 이름으로(nvidia.com 드라이버 찾기: 계열 psid·카드 pfid). 못 찾으면 null. 상태 확인이 느려지지 않게 12시간 캐시(설치할 때는 fresh 로 새로 조회).
        public static Release Latest(Card gpu, bool fresh = false)
        {
            if (gpu == null) return null;
            var cache = Path.Combine(CacheDir, "nv-" + Regex.Replace(gpu.PnpId, "[^A-Za-z0-9]", "_") + ".json");
            if (!fresh && File.Exists(cache) && File.GetLastWriteTime(cache) > DateTime.Now.AddHours(-12))
            {
                try
                {
                    var c = (Dictionary<string, object>)Net.Json(File.ReadAllText(cache));
                    return new Release { Version = Num(Get(c, "Version")), Url = Str(c, "Url") };
                }
                catch { }
            }
            Release r = null;
            try
            {
                var laptop = Laptop();   // 노트북·2-in-1 이면 노트북용(Notebooks) 드라이버
                var build = Environment.OSVersion.Version.Build;
                var dev = Regex.Match(gpu.PnpId, "DEV_([0-9A-F]{4})").Groups[1].Value;
                if (dev.Length > 0)
                {
                    var q = Net.ToJson(new Dictionary<string, object> {
                        { "dIDa", new[] { dev + "_10DE" } }, { "osC", "10.0" }, { "osB", build.ToString(Inv) }, { "is6", "1" }, { "lg", "1033" }, { "iLp", laptop ? "1" : "0" },
                        { "prvMd", "0" }, { "gcV", "3.27.0.112" }, { "gIsB", "1" }, { "dch", "1" }, { "upCRD", "0" }, { "isCRD", "0" } });
                    Dictionary<string, object> a = null;
                    try { a = Dict(Dict(Net.Json(Net.GetString("https://gfwsl.geforce.com/nvidia_web_services/controller.gfeclientcontent.NG.php/com.nvidia.services.GFEClientContent_NG.getDispDrvrByDevid/" + Uri.EscapeDataString(q), 20))), "DriverAttributes"); } catch { }
                    var url = Str(a, "DownloadURLAdmin");
                    if (url.Length > 0) r = new Release { Version = Num(Get(a, "Version")), Url = url };
                }
                if (r == null && Regex.IsMatch(gpu.Name, "GeForce", I))
                {
                    var name = Regex.Replace(gpu.Name, @"^NVIDIA\s+", "", I).Trim();
                    var series = Lookup("https://www.nvidia.com/Download/API/lookupValueSearch.aspx?TypeID=2")
                        .Where(s => Regex.IsMatch(s.Key, "Notebooks", I) == laptop)
                        .OrderByDescending(s => { int n; return int.TryParse(s.Value, NumberStyles.Integer, Inv, out n) ? n : 0; }).ToList();
                    string psid = null, pfid = null;
                    foreach (var s in series)
                    {
                        var hit = Lookup("https://www.nvidia.com/Download/API/lookupValueSearch.aspx?TypeID=3&ParentID=" + s.Value)
                            .FirstOrDefault(c => string.Equals(c.Key, name, StringComparison.OrdinalIgnoreCase));
                        if (hit.Key != null) { psid = s.Value; pfid = hit.Value; break; }
                    }
                    if (!string.IsNullOrEmpty(pfid))
                    {
                        var osid = build >= 22000 ? 135 : 57;   // Win11 / Win10
                        var u = "https://gfwsl.geforce.com/services_toolkit/services/com/nvidia/services/AjaxDriverService.php?func=DriverManualLookup&psid=" + psid + "&pfid=" + pfid + "&osID=" + osid + "&languageCode=1033&isWHQL=1&dch=1&sort1=0&numberOfResults=1";
                        var ids = Dict(Net.Json(Net.GetString(u, 20)));
                        var list = Get(ids, "IDS") as object[];
                        var d = list != null && list.Length > 0 ? Dict(Dict(list[0]), "downloadInfo") : null;
                        var dl = Str(d, "DownloadURL");
                        if (dl.Length > 0) r = new Release { Version = Num(Get(d, "Version")), Url = dl };
                    }
                }
            }
            catch { }
            if (r != null)
            {
                try { Directory.CreateDirectory(CacheDir); File.WriteAllText(cache, Net.ToJson(new Dictionary<string, object> { { "Version", r.Version }, { "Url", r.Url } })); } catch { }
            }
            return r;
        }

        // 노트북·2-in-1 몸체(ChassisTypes 8,9,10,14,30,31,32)
        static bool Laptop()
        {
            var kinds = new[] { 8, 9, 10, 14, 30, 31, 32 };
            foreach (var r in Wmi.Query("SELECT ChassisTypes FROM Win32_SystemEnclosure"))
            {
                object v; if (!r.TryGetValue("ChassisTypes", out v) || v == null) continue;
                var arr = v as Array;
                if (arr == null) { if (kinds.Contains(Convert.ToInt32(v, Inv))) return true; continue; }
                foreach (var x in arr) if (x != null && kinds.Contains(Convert.ToInt32(x, Inv))) return true;
            }
            return false;
        }

        // nvidia.com 드라이버 찾기 목록(XML) → (이름, 값)
        static List<KeyValuePair<string, string>> Lookup(string url)
        {
            var doc = new XmlDocument { XmlResolver = null };
            doc.LoadXml(Net.GetString(url, 20));
            var list = new List<KeyValuePair<string, string>>();
            foreach (XmlNode n in doc.SelectNodes("/LookupValueSearch/LookupValues/LookupValue"))
            {
                var nm = n.SelectSingleNode("Name"); var vl = n.SelectSingleNode("Value");
                list.Add(new KeyValuePair<string, string>(nm == null ? "" : nm.InnerText, vl == null ? "" : vl.InnerText));
            }
            return list;
        }

        static Dictionary<string, object> Dict(object o) { return o as Dictionary<string, object>; }
        // 키는 대소문자 무시(옛 스크립트의 $a.Version 처럼) — 응답 JSON 의 키 표기가 바뀌어도 잡는다
        static object Get(IDictionary<string, object> d, string key)
        {
            if (d == null) return null;
            object v; if (d.TryGetValue(key, out v)) return v;
            foreach (var kv in d) if (string.Equals(kv.Key, key, StringComparison.OrdinalIgnoreCase)) return kv.Value;
            return null;
        }
        static Dictionary<string, object> Dict(Dictionary<string, object> d, string key) { return Get(d, key) as Dictionary<string, object>; }
        static string Str(IDictionary<string, object> d, string key) { var v = Get(d, key); return v != null ? Convert.ToString(v, Inv) : ""; }
        static double Num(object o) { return o is string ? double.Parse((string)o, NumberStyles.Float, Inv) : Convert.ToDouble(o, Inv); }
    }
}
