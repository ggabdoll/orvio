using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;

namespace Orvio.Core
{
    // UI 스레드에서 Playnite Game 을 베껴 둔 값. 이 뒤로는 Playnite 없이 다룸
    public sealed class GameInfo
    {
        public Guid Id;
        public string Name;
        public string Source;
        public List<string> Genres = new List<string>();
        public List<string> Tags = new List<string>();
        public List<string> Features = new List<string>();
        public ulong Playtime;
        public DateTime? LastActivity;
        public DateTime? Added;
        public string CoverPath;
    }

    public struct CropRect
    {
        public int X, Y, Side, Out;
    }

    // 게임 DTO 규칙 (protocol 4절 '게임')
    public static class GameRules
    {
        static readonly char[] Colons = { ':', '：' };
        static readonly Regex Separators = new Regex(@"[\s·・.\/,&+\-]");

        // 태그 "키: 값" 에서 값. 키가 다르면 null
        public static string TagValue(string tag, string key)
        {
            var t = (tag ?? "").Trim();
            int i = t.IndexOfAny(Colons);
            if (i <= 0 || t.Substring(0, i).Trim() != key) return null;
            return t.Substring(i + 1).Trim();
        }

        public static string Input(IEnumerable<string> tags, IEnumerable<string> features)
        {
            var found = new HashSet<string>();
            foreach (var tag in tags ?? Enumerable.Empty<string>())
            {
                var v = TagValue(tag, "입력");
                if (v == null) continue;
                switch (Separators.Replace(v, "").ToLowerInvariant())
                {
                    case "컨트롤러": found.Add("pad"); break;
                    case "키보드마우스": found.Add("kbm"); break;
                    case "둘다": found.Add("both"); break;
                }
            }
            if (found.Count == 1) return found.First();
            if (found.Count > 1) return "both";

            // 태그가 없으면 Steam 기능 이름으로 추정
            foreach (var f in features ?? Enumerable.Empty<string>())
            {
                if (Has(f, "full controller")) return "pad";
                if (Has(f, "partial controller")) return "both";
            }
            return "both";
        }

        public static string Players(IEnumerable<string> tags, IEnumerable<string> features)
        {
            foreach (var tag in tags ?? Enumerable.Empty<string>())
            {
                var v = TagValue(tag, "인원");
                if (!string.IsNullOrEmpty(v)) return v.Length > 10 ? v.Substring(0, 10) : v;
            }
            bool together = false, online = false;
            foreach (var f in features ?? Enumerable.Empty<string>())
            {
                bool isOnline = Has(f, "online") || Has(f, "mmo") || Has(f, "massively");
                bool isMulti = Has(f, "multi") || Has(f, "co-op") || Has(f, "coop") || Has(f, "co-operative") || Has(f, "cooperative") || Has(f, "split") || Has(f, "shared");
                if (isOnline) online = true;
                else if (isMulti) together = true;
            }
            if (together) return "같이";
            if (online) return "온라인";
            return "1";
        }

        static bool Has(string s, string w)
        {
            return s != null && s.IndexOf(w, StringComparison.OrdinalIgnoreCase) >= 0;
        }

        public static uint Fnv1a(string s)
        {
            uint h = 2166136261;
            foreach (var b in Encoding.UTF8.GetBytes(s ?? ""))
            {
                h ^= b;
                h *= 16777619;
            }
            return h;
        }

        // 대체 표지 두 색: ID 해시로 색상만 고르고, 채도는 낮게·명도는 어둡게(시안 color 배열 꼴)
        public static string[] ColorPair(Guid id)
        {
            double hue = Fnv1a(id.ToString("D")) % 360;
            return new[] { Hsl(hue, 0.22, 0.17), Hsl(hue, 0.30, 0.44) };
        }

        static string Hsl(double h, double s, double l)
        {
            double c = (1 - Math.Abs(2 * l - 1)) * s;
            double x = c * (1 - Math.Abs(h / 60 % 2 - 1));
            double m = l - c / 2;
            double r = 0, g = 0, b = 0;
            if (h < 60) { r = c; g = x; }
            else if (h < 120) { r = x; g = c; }
            else if (h < 180) { g = c; b = x; }
            else if (h < 240) { g = x; b = c; }
            else if (h < 300) { r = x; b = c; }
            else { r = c; b = x; }
            return "#" + Hex(r + m) + Hex(g + m) + Hex(b + m);
        }

        static string Hex(double v)
        {
            return ((int)Math.Round(v * 255)).ToString("x2", CultureInfo.InvariantCulture);
        }

        // 가운데 정사각 자르기. Out 은 저장할 한 변(최대 max)
        public static CropRect SquareCrop(int w, int h, int max)
        {
            int side = Math.Max(0, Math.Min(w, h));
            return new CropRect { X = (w - side) / 2, Y = (h - side) / 2, Side = side, Out = Math.Min(side, max) };
        }

        public static string IsoLocal(DateTime? d)
        {
            if (d == null) return null;
            var v = d.Value.Kind == DateTimeKind.Utc ? d.Value.ToLocalTime() : d.Value;
            return v.ToString("yyyy-MM-dd'T'HH:mm:ss", CultureInfo.InvariantCulture);
        }

        public static Dictionary<string, object> ToDto(GameInfo g, string originalTitle, string coverUrl)
        {
            var d = new Dictionary<string, object>();
            d["id"] = g.Id.ToString("D");
            d["title"] = g.Name ?? "";
            if (!string.IsNullOrEmpty(originalTitle) && originalTitle != g.Name) d["originalTitle"] = originalTitle;
            d["source"] = string.IsNullOrWhiteSpace(g.Source) ? "PC" : g.Source;
            d["genres"] = (g.Genres ?? new List<string>()).Where(x => !string.IsNullOrWhiteSpace(x)).ToList();
            d["input"] = Input(g.Tags, g.Features);
            d["players"] = Players(g.Tags, g.Features);
            d["playtimeSec"] = g.Playtime;
            d["lastPlayed"] = IsoLocal(g.LastActivity);
            d["added"] = IsoLocal(g.Added);
            d["cover"] = coverUrl;
            d["color"] = ColorPair(g.Id);
            return d;
        }
    }

    // XInput → W3C 표준 게임패드 (protocol 5절)
    public static class PadRules
    {
        public const int LeftDeadzone = 7849;   // XINPUT_GAMEPAD_LEFT_THUMB_DEADZONE
        public const int RightDeadzone = 8689;  // XINPUT_GAMEPAD_RIGHT_THUMB_DEADZONE
        public const int TriggerThreshold = 30; // XINPUT_GAMEPAD_TRIGGER_THRESHOLD (이보다 커야 눌림)

        public const ushort A = 0x1000, B = 0x2000, X = 0x4000, Y = 0x8000;
        public const ushort LB = 0x0100, RB = 0x0200, Back = 0x0020, Start = 0x0010;
        public const ushort LThumb = 0x0040, RThumb = 0x0080;
        public const ushort Up = 0x0001, Down = 0x0002, Left = 0x0004, Right = 0x0008;

        // 0 A · 1 B · 2 X · 3 Y · 4 LB · 5 RB · 6 LT · 7 RT · 8 View · 9 Menu · 10 LS · 11 RS · 12~15 십자 · 16 가운데
        public static bool[] Buttons(ushort b, byte lt, byte rt)
        {
            return new[]
            {
                (b & A) != 0, (b & B) != 0, (b & X) != 0, (b & Y) != 0,
                (b & LB) != 0, (b & RB) != 0, lt > TriggerThreshold, rt > TriggerThreshold,
                (b & Back) != 0, (b & Start) != 0, (b & LThumb) != 0, (b & RThumb) != 0,
                (b & Up) != 0, (b & Down) != 0, (b & Left) != 0, (b & Right) != 0,
                false,
            };
        }

        public static int Bits(bool[] buttons)
        {
            int bits = 0;
            for (int i = 0; i < buttons.Length; i++) if (buttons[i]) bits |= 1 << i;
            return bits;
        }

        public static bool[] FromBits(int bits)
        {
            var r = new bool[17];
            for (int i = 0; i < 17; i++) r[i] = (bits & (1 << i)) != 0;
            return r;
        }

        // 원형 데드존 뒤 0~1 로 다시 늘림. 방향은 W3C(아래가 +). 소수 둘째 자리
        public static void Stick(short x, short y, int deadzone, out double ox, out double oy)
        {
            double fx = Math.Max(-1, x / 32767.0), fy = Math.Max(-1, y / 32767.0);
            double mag = Math.Sqrt(fx * fx + fy * fy);
            double dz = deadzone / 32767.0;
            if (mag <= dz) { ox = 0; oy = 0; return; }
            double scaled = Math.Min(1, (mag - dz) / (1 - dz));
            ox = Round2(fx / mag * scaled);
            oy = Round2(-fy / mag * scaled);
        }

        public static double[] Axes(short lx, short ly, short rx, short ry)
        {
            double a0, a1, a2, a3;
            Stick(lx, ly, LeftDeadzone, out a0, out a1);
            Stick(rx, ry, RightDeadzone, out a2, out a3);
            return new[] { a0, a1, a2, a3 };
        }

        // +0.0 은 -0 을 0 으로 (문자열 비교가 흔들리지 않게)
        static double Round2(double v) { return Math.Round(v, 2) + 0.0; }
    }

    // 상태 줄 (protocol 4절 '상태')
    public static class StatusRules
    {
        static readonly Regex SignalRe = new Regex(@"^\s*[^:\r\n]+:\s*(\d{1,3})\s*%\s*$", RegexOptions.Multiline);
        static readonly Regex VirtualNet = new Regex("virtual|hyper-v|vmware|virtualbox|vpn|tap-|tunnel|loopback|bluetooth|wan miniport|npcap|zerotier|tailscale|wireguard", RegexOptions.IgnoreCase);

        // netsh wlan show interfaces 의 '신호 : 87%' / 'Signal : 87%' 줄 (언어와 상관없이 % 가 붙은 첫 줄)
        public static int? WifiSignalPercent(string netshOutput)
        {
            var m = SignalRe.Match(netshOutput ?? "");
            if (!m.Success) return null;
            return Math.Min(100, int.Parse(m.Groups[1].Value, CultureInfo.InvariantCulture));
        }

        public static int WifiBars(int percent)
        {
            if (percent >= 80) return 4;
            if (percent >= 60) return 3;
            if (percent >= 40) return 2;
            if (percent >= 20) return 1;
            return 0;
        }

        public sealed class NetIf
        {
            public string Kind;        // wifi · wired · other
            public bool Up;
            public bool HasGateway;
            public string Description;
        }

        // 기본 게이트웨이가 있는 진짜 어댑터만. 둘 다면 유선
        public static string NetworkKind(IEnumerable<NetIf> list)
        {
            var real = (list ?? Enumerable.Empty<NetIf>())
                .Where(n => n != null && n.Up && n.HasGateway && !VirtualNet.IsMatch(n.Description ?? ""))
                .ToList();
            if (real.Any(n => n.Kind == "wired")) return "wired";
            if (real.Any(n => n.Kind == "wifi")) return "wifi";
            return "none";
        }

        // 원격 데스크톱·루트 열거 장치는 실제 키보드·마우스가 아님 (진단 로그로 더 다듬을 곳)
        public static bool IsVirtualInputDevice(string name)
        {
            var n = name ?? "";
            return n.IndexOf("RDP_KBD", StringComparison.OrdinalIgnoreCase) >= 0
                || n.IndexOf("RDP_MOU", StringComparison.OrdinalIgnoreCase) >= 0
                || n.IndexOf(@"\Root#", StringComparison.OrdinalIgnoreCase) >= 0;
        }

        // XINPUT_BATTERY_INFORMATION → battery 객체. 레벨은 0~3 (xinput.h BATTERY_LEVEL_*)
        public static Dictionary<string, object> Battery(byte type, byte level)
        {
            switch (type)
            {
                case 0x01: return new Dictionary<string, object> { { "kind", "wired" } };
                case 0x02:
                case 0x03: return new Dictionary<string, object> { { "kind", "level" }, { "value", (int)Math.Min(level, (byte)3) } };
                default: return new Dictionary<string, object> { { "kind", "unknown" } };
            }
        }
    }

    // 표지 원본 검사 (보안 리뷰 🟠4)
    public static class CoverRules
    {
        public const int MaxSide = 8192;
        public const long MaxPixels = 40000000;

        // 로컬 경로이고, 정규화한 뒤 Playnite DB 폴더 안일 때만 그 전체 경로. 아니면 null
        public static string SafeCoverPath(string raw, string dbRoot)
        {
            if (string.IsNullOrWhiteSpace(raw) || string.IsNullOrWhiteSpace(dbRoot)) return null;
            if (raw.StartsWith(@"\\", StringComparison.Ordinal) || raw.StartsWith("//", StringComparison.Ordinal)) return null;   // UNC·장치 경로
            if (raw.IndexOf("://", StringComparison.Ordinal) >= 0) return null;   // http(s)·file:// 등
            string full, root;
            try
            {
                full = System.IO.Path.GetFullPath(raw);
                root = System.IO.Path.GetFullPath(dbRoot);
            }
            catch { return null; }
            if (full.StartsWith(@"\\", StringComparison.Ordinal)) return null;
            root = root.TrimEnd('\\', '/') + System.IO.Path.DirectorySeparatorChar;
            return full.StartsWith(root, StringComparison.OrdinalIgnoreCase) ? full : null;
        }

        // 전부 풀기 전에 머리글의 크기만 보고 거름
        public static bool SizeOk(int width, int height)
        {
            return width > 0 && height > 0 && width <= MaxSide && height <= MaxSide && (long)width * height <= MaxPixels;
        }
    }

    // Edge·WebView2 주소 규칙
    public static class WebRules
    {
        const string AppPrefix = "https://orvio-app.example/";
        const string CoverPrefix = "https://orvio-covers.example/";

        public static bool IsAppUrl(string uri)
        {
            return uri != null && uri.StartsWith(AppPrefix, StringComparison.OrdinalIgnoreCase);
        }

        // 홈 WebView2 가 내보내도 되는 요청: 두 가상 호스트만
        public static bool IsAllowedRequest(string uri)
        {
            return IsAppUrl(uri) || (uri != null && uri.StartsWith(CoverPrefix, StringComparison.OrdinalIgnoreCase));
        }

        // 로그용: 스킴과 호스트만
        public static string SchemeHost(string uri)
        {
            Uri u;
            return uri != null && Uri.TryCreate(uri, UriKind.Absolute, out u) ? u.Scheme + "://" + u.Host : "(알 수 없음)";
        }

        // http(s) 만, 인코딩을 마친 주소. 아니면 null
        public static string FinalUrl(string url)
        {
            Uri u;
            if (url == null || !Uri.TryCreate(url, UriKind.Absolute, out u)) return null;
            if (u.Scheme != Uri.UriSchemeHttps && u.Scheme != Uri.UriSchemeHttp) return null;
            return u.AbsoluteUri;
        }

        // 명령줄 인자로 감싼 주소. " 나 \ 가 남아 있으면 인자를 깰 수 있으므로 null
        public static string QuoteArg(string finalUrl)
        {
            if (string.IsNullOrEmpty(finalUrl) || finalUrl.IndexOf('"') >= 0 || finalUrl.IndexOf('\\') >= 0) return null;
            return "\"" + finalUrl + "\"";
        }

        // 윈도우 경로를 어느 OS 에서나 같게 다룸(검사 프로젝트가 맥에서 돎)
        static string Norm(string path)
        {
            return path.Replace('/', '\\').TrimEnd('\\').ToLowerInvariant();
        }

        public static string FileName(string path)
        {
            if (string.IsNullOrEmpty(path)) return "";
            var n = Norm(path);
            return n.Substring(n.LastIndexOf('\\') + 1);
        }

        public static bool IsEdgeExeName(string path)
        {
            return FileName(path) == "msedge.exe";
        }

        // 드라이브 문자로 시작하는 절대 경로만(C:\…). 상대·UNC(\\서버)·드라이브 상대(C:x)는 아님.
        // Path.IsPathRooted 는 맥에서 윈도우 경로를 못 읽어 검사 프로젝트용으로 직접 봄
        public static bool IsDriveRooted(string path)
        {
            return path != null && path.Length >= 3 && char.IsLetter(path[0]) && path[0] < 128 && path[1] == ':' && (path[2] == '\\' || path[2] == '/');
        }

        // 앞 창 프로세스의 전체 경로가 찾아 둔 Edge 설치 폴더 안의 msedge.exe 인지 (이름만 같은 다른 exe 는 아님)
        public static bool IsEdgeProcess(string processPath, string edgeExePath)
        {
            if (string.IsNullOrEmpty(processPath) || string.IsNullOrEmpty(edgeExePath)) return false;
            if (!IsEdgeExeName(processPath) || !IsEdgeExeName(edgeExePath)) return false;
            var e = Norm(edgeExePath);
            var dir = e.Substring(0, e.LastIndexOf('\\') + 1);
            return dir.Length > 1 && Norm(processPath).StartsWith(dir, StringComparison.Ordinal);
        }
    }

    public static class SoundRules
    {
        // 소리 출력을 깨워 두는 PCM WAV. 들리지 않는 아주 작은 잡음(±2/32768): 완전한 무음은 신호 없음으로 치는 TV 가 있음
        public static byte[] KeepAwakeWav(int seconds)
        {
            const int rate = 8000;
            int bytes = rate * seconds * 2;
            using (var ms = new System.IO.MemoryStream(44 + bytes))
            using (var w = new System.IO.BinaryWriter(ms))
            {
                w.Write(Encoding.ASCII.GetBytes("RIFF")); w.Write(36 + bytes);
                w.Write(Encoding.ASCII.GetBytes("WAVEfmt ")); w.Write(16);
                w.Write((short)1); w.Write((short)1); w.Write(rate); w.Write(rate * 2); w.Write((short)2); w.Write((short)16);
                w.Write(Encoding.ASCII.GetBytes("data")); w.Write(bytes);
                var r = new Random(7);
                for (int i = 0; i < rate * seconds; i++) w.Write((short)r.Next(-2, 3));
                w.Flush();
                return ms.ToArray();
            }
        }

        public const long MaxBytes = 2 * 1024 * 1024;
        static readonly string[] Exts = { ".wav", ".mp3", ".flac", ".ogg" };

        // 화면으로 넘겨도 되는 소리 파일인가 (브라우저가 푸는 형식, 메시지가 너무 커지지 않는 크기)
        public static bool Usable(string fileName, long bytes)
        {
            return bytes > 0 && bytes <= MaxBytes && fileName != null && Exts.Any(x => fileName.EndsWith(x, StringComparison.OrdinalIgnoreCase));
        }
    }

    // 홈을 숨기고 Edge 로 나간 뒤 창이 모두 닫혔는지 판단. 1초마다 부름
    public sealed class WebCloseWatch
    {
        bool seen;
        int absent;

        // 창을 본 뒤에는 연속 2번 없어야 닫힌 것(잠깐 안 잡혔다고 홈이 튀어나오지 않게). 한 번도 못 봤으면 Edge 가 늦게 뜰 때를 기다려 15번
        public bool Closed(bool edgeWindow)
        {
            if (edgeWindow) { seen = true; absent = 0; return false; }
            return ++absent >= (seen ? 2 : 15);
        }
    }
}
