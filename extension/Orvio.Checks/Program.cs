using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Runtime.InteropServices;
using System.Threading;
using Orvio;
using Orvio.Core;

// Settings.cs 가 쓰는 Playnite 특성의 대역 (맥에는 Playnite.SDK 를 올리지 않음)
namespace Playnite.SDK.Data
{
    [AttributeUsage(AttributeTargets.Property | AttributeTargets.Field)]
    public sealed class SerializationPropertyNameAttribute : Attribute
    {
        public SerializationPropertyNameAttribute(string propertyName) { PropertyName = propertyName; }
        public string PropertyName { get; set; }
    }
}

namespace Orvio.Checks
{
    static class Program
    {
        static int passed, failed;

        // 소스에 \u 이스케이프를 쓰지 않으려고 글자는 코드로 만듦
        static readonly string Bs = "\\";
        static readonly string Ctrl1 = ((char)1).ToString();
        static readonly string LineSep = ((char)0x2028).ToString();

        static void Check(bool ok, string what)
        {
            if (ok) passed++;
            else { failed++; Console.WriteLine("실패: " + what); }
        }

        static void Eq<T>(T actual, T expected, string what)
        {
            Check(Equals(actual, expected), what + " → 기대 [" + expected + "] 실제 [" + actual + "]");
        }

        static int Main()
        {
            JsonChecks();
            MessageChecks();
            SettingsChecks();
            GameChecks();
            PadChecks();
            StatusChecks();
            NativeChecks();
            GateChecks();
            CoverChecks();
            WebRuleChecks();
            LogChecks();
            Console.WriteLine("통과 " + passed + " · 실패 " + failed);
            return failed == 0 ? 0 : 1;
        }

        static bool Throws(string json)
        {
            try { Json.Parse(json); return false; }
            catch (FormatException) { return true; }
        }

        static void JsonChecks()
        {
            var o = (Dictionary<string, object>)Json.Parse("{\"a\":1,\"b\":[true,null,\"x\"],\"c\":{\"d\":-1.5e2},\"e\":\"" + Bs + "uac00" + Bs + "n\"}");
            Eq(o["a"], (object)1.0, "json 수는 double");
            Eq(((List<object>)o["b"]).Count, 3, "json 배열");
            Eq(((Dictionary<string, object>)o["c"])["d"], (object)(-150.0), "json 지수");
            Eq(o["e"], (object)"가\n", "json 유니코드 이스케이프");
            Check(Throws("{\"a\":1,}"), "json 뒤 쉼표 거절");
            Check(Throws("{\"a\":1,\"a\":2}"), "json 키 중복 거절");
            Check(Throws("{\"a\":\"x"), "json 끝나지 않은 문자열 거절");
            Check(Throws("{\"a\":\"" + Ctrl1 + "\"}"), "json 제어 문자 거절");
            Check(Throws("{} x"), "json 뒤 쓰레기 거절");
            Check(Throws("01"), "json 앞자리 0 거절");
            Check(Throws("[[[[[[[[[[1]]]]]]]]]]"), "json 깊이 제한");
            Check(Throws("NaN"), "json NaN 거절");
            Check(Throws("1e999") && Throws("-1e999"), "json 범위 넘는 수 거절(net462 는 OverflowException)");

            var prev = Thread.CurrentThread.CurrentCulture;
            Thread.CurrentThread.CurrentCulture = new CultureInfo("de-DE");
            Eq(Json.Write(new Dictionary<string, object> { { "x", 0.5 }, { "n", null }, { "s", "a\"b\n" }, { "u", (ulong)763200 }, { "l", new[] { true, false } } }),
               "{\"x\":0.5,\"n\":null,\"s\":\"a" + Bs + "\"b" + Bs + "n\",\"u\":763200,\"l\":[true,false]}", "json 쓰기(문화권 무관)");
            Thread.CurrentThread.CurrentCulture = prev;
            Eq(Json.Write(LineSep), "\"" + Bs + "u2028\"", "json U+2028 이스케이프");
            Eq(Json.Write(double.NaN), "null", "json NaN 은 null");
            const string canon = "{\"type\":\"pad\",\"pads\":[{\"index\":0,\"axes\":[0,-1,0.25,1]}]}";
            Eq(Json.Write(Json.Parse(canon)), canon, "json 왕복");
        }

        static WebCommand Msg(string json)
        {
            string err;
            return Messages.Parse(json, out err);
        }

        static void MessageChecks()
        {
            var id = "64f78cd1-0b00-4271-a9e1-89abc55f66cc";
            var c = Msg("{\"type\":\"launch\",\"id\":\"" + id + "\"}");
            Check(c != null && c.GameId == Guid.Parse(id), "launch 통과");
            Check(Msg("{\"type\":\"launch\",\"id\":\"elden\"}") == null, "launch GUID 아님 거절");
            Check(Msg("{\"type\":\"launch\"}") == null, "launch id 없음 거절");
            Check(Msg("{\"type\":\"launch\",\"id\":\"" + id + "\",\"extra\":1}") == null, "모르는 필드 거절");
            Check(Msg("{\"type\":\"eval\",\"code\":\"x\"}") == null, "모르는 type 거절");
            Check(Msg("{\"id\":\"x\"}") == null, "type 없음 거절");
            Check(Msg("[1,2]") == null, "객체 아님 거절");
            Check(Msg("{\"type\":\"log\",\"level\":\"info\",\"msg\":\"" + new string('x', 9000) + "\"}") == null, "8KB 넘는 메시지 거절");

            Check(Msg("{\"type\":\"volume\",\"value\":150}") == null, "volume 100 넘으면 거절");
            Check(Msg("{\"type\":\"volume\",\"value\":-5}") == null, "volume 0 아래면 거절");
            Check(Msg("{\"type\":\"volume\",\"value\":40.4}") == null, "volume 정수 아니면 거절");
            Check(Msg("{\"type\":\"volume\",\"value\":1e999}") == null, "volume 범위 넘는 수 거절");
            Eq(Msg("{\"type\":\"volume\",\"value\":40}").Volume, 40, "volume 40");
            Eq(Msg("{\"type\":\"volume\",\"value\":0}").Volume, 0, "volume 0");
            Eq(Msg("{\"type\":\"volume\",\"value\":100}").Volume, 100, "volume 100");
            Check(Msg("{\"type\":\"volume\",\"value\":\"40\"}") == null, "volume 문자열 거절");

            var log = Msg("{\"type\":\"log\",\"level\":\"warn\",\"msg\":\"" + new string('가', 600) + "\"}");
            Check(log != null && log.Msg.Length == 500 && log.Level == "warn", "log 500자로 자름");
            Check(Msg("{\"type\":\"log\",\"level\":\"debug\",\"msg\":\"x\"}") == null, "log level 거절");
            Eq(Msg("{\"type\":\"sfx\",\"name\":\"navigation\"}").Sound, "navigation", "sfx 이름");
            Check(Msg("{\"type\":\"sfx\",\"name\":\"x\"}") == null && Msg("{\"type\":\"sfx\"}") == null, "sfx 모르는 이름·이름 없음 거절");

            Eq(Msg("{\"type\":\"openWeb\",\"id\":\"youtube\"}").WebId, "youtube", "openWeb id");
            Check(Msg("{\"type\":\"openWeb\",\"id\":\"YouTube\"}") == null, "openWeb id 형식 거절");
            Check(Msg("{\"type\":\"openWeb\",\"id\":\"youtube\",\"query\":\"a\"}") == null, "openWeb 둘 다 거절");
            Check(Msg("{\"type\":\"openWeb\",\"query\":\"   \"}") == null, "openWeb 빈 검색어 거절");
            Eq(Msg("{\"type\":\"openWeb\",\"query\":\"" + new string('a', 300) + "\"}").Query.Length, 200, "openWeb 검색어 200자");

            Eq(Msg("{\"type\":\"power\",\"action\":\"shutdown\"}").Action, "shutdown", "power 통과");
            Check(Msg("{\"type\":\"power\",\"action\":\"format\"}") == null, "power 모르는 동작 거절");

            Eq(Msg("{\"type\":\"setSetting\",\"key\":\"theme\",\"value\":\"dark\"}").Theme, "dark", "setSetting theme");
            Check(Msg("{\"type\":\"setSetting\",\"key\":\"theme\",\"value\":\"blue\"}") == null, "setSetting theme 값 거절");
            Check(Msg("{\"type\":\"setSetting\",\"key\":\"idle\",\"value\":true}").Idle, "setSetting idle");
            Check(Msg("{\"type\":\"setSetting\",\"key\":\"idle\",\"value\":\"true\"}") == null, "setSetting idle 문자열 거절");
            Check(Msg("{\"type\":\"setSetting\",\"key\":\"edgePath\",\"value\":\"x.exe\"}") == null, "setSetting 다른 key 거절");

            Eq(Msg("{\"type\":\"ready\",\"version\":1}").Version, "1", "ready 판(수)");
            Eq(Msg("{\"type\":\"ready\",\"version\":\"1\"}").Version, "1", "ready 판(글)");
            Check(Msg("{\"type\":\"ready\"}") != null, "ready 판 없음 허용");
        }

        static void SettingsChecks()
        {
            var s = OrvioSettings.CreateDefault();
            Eq(s.Theme, "auto", "기본 theme");
            Eq(s.Idle, false, "기본 idle");
            Eq(s.LaunchTimeoutSec, 90, "기본 launchTimeoutSec");
            Eq(s.EarlyStopSec, 30, "기본 earlyStopSec");
            Eq(s.MenuHoldMs, 1000, "기본 menuHoldMs");
            Eq(s.InputMode, "xinput", "기본 inputMode");
            Eq(s.EdgePath, "", "기본 edgePath");
            Eq(s.EdgeArgs, "--start-fullscreen", "기본 edgeArgs");
            Eq(s.DevMode, false, "기본 devMode");
            Eq(s.Diagnostics, true, "기본 diagnostics");
            Eq(string.Join(",", s.Web.Select(w => w.Id)), "youtube,netflix,chzzk,disney,tving,naver,search", "기본 웹 바로가기 순서");
            Eq(string.Join(",", s.Web.Select(w => w.Color)), "#ff3d3d,#e50914,#00ffa3,#1f6fff,#ff153c,#03c75a,#8a5cf5", "웹 색(시안 DATA.web)");
            Eq(s.UrlFor("chzzk"), "https://chzzk.naver.com", "치지직 주소");
            Eq(s.UrlFor("search"), "https://www.google.com/search?q=", "검색 칸만 고르면 빈 검색");
            Eq(s.UrlFor("nope"), null, "없는 바로가기");
            Eq(s.SearchUrl("엘든 링"), "https://www.google.com/search?q=%EC%97%98%EB%93%A0%20%EB%A7%81", "검색어 인코딩");

            var bad = new OrvioSettings
            {
                Theme = "neon", InputMode = "joystick", LaunchTimeoutSec = 1, EarlyStopSec = -3, MenuHoldMs = 99999, EdgeArgs = null,
                Web = new List<WebShortcut>
                {
                    new WebShortcut("ok", "  ", "https://example.com", "red"),
                    new WebShortcut("ok", "중복", "https://example.org", "#000000"),
                    new WebShortcut("Bad Id", "x", "https://x.com", "#000000"),
                    new WebShortcut("js", "x", "javascript:alert(1)", "#000000"),
                    null,
                },
            };
            bad.Normalize();
            Eq(bad.Theme, "auto", "잘못된 theme → auto");
            Eq(bad.InputMode, "xinput", "잘못된 inputMode → xinput");
            Eq(bad.LaunchTimeoutSec, 10, "launchTimeoutSec 아래 자름");
            Eq(bad.EarlyStopSec, 0, "earlyStopSec 아래 자름");
            Eq(bad.MenuHoldMs, 5000, "menuHoldMs 위 자름");
            Eq(bad.EdgeArgs, "--start-fullscreen", "edgeArgs null → 기본");
            Eq(string.Join(",", bad.Web.Select(w => w.Id)), "ok,search", "잘못된 바로가기 버리고 검색 칸 채움");
            Eq(bad.Web[0].Title, "ok", "빈 제목 → id");
            Eq(bad.Web[0].Color, "#8a5cf5", "잘못된 색 → 보라");

            var empty = new OrvioSettings { Web = new List<WebShortcut>() };
            empty.Normalize();
            Eq(empty.Web.Count, 7, "빈 목록 → 기본 7개");
            var noQ = new OrvioSettings { Web = new List<WebShortcut> { new WebShortcut("search", "검색", "https://www.bing.com/search", "#000000") } };
            noQ.Normalize();
            Eq(noQ.Web.Single(w => w.Id == "search").Url, "https://www.google.com/search?q={q}", "{q} 없는 검색 주소 → 기본");

            var init = Json.Write(OrvioSettings.CreateDefault().ToInitPayload());
            Check(init.StartsWith("{\"theme\":\"auto\",\"idle\":false,\"web\":[{\"id\":\"youtube\",\"title\":\"유튜브\",\"url\":\"https://www.youtube.com\",\"color\":\"#ff3d3d\"}"), "init.settings 꼴: " + init.Substring(0, Math.Min(120, init.Length)));
            Check(init.EndsWith(",\"launchTimeoutSec\":90}"), "init.settings launchTimeoutSec");
        }

        static void GameChecks()
        {
            Func<string[], string[], string> input = (t, f) => GameRules.Input(t, f);
            Eq(input(new[] { "입력: 컨트롤러" }, null), "pad", "태그 컨트롤러");
            Eq(input(new[] { "입력:키보드·마우스" }, null), "kbm", "태그 키보드·마우스(붙여 씀)");
            Eq(input(new[] { "입력: 키보드 / 마우스" }, null), "kbm", "태그 키보드 / 마우스");
            Eq(input(new[] { "입력: 둘 다" }, null), "both", "태그 둘 다");
            Eq(input(new[] { "입력: 컨트롤러", "입력: 키보드·마우스" }, null), "both", "태그 둘이면 both");
            Eq(input(new[] { "입력: 컨트롤러" }, new[] { "Partial Controller Support" }), "pad", "태그가 기능보다 먼저");
            Eq(input(null, new[] { "Single-player", "Full Controller Support" }), "pad", "기능 Full");
            Eq(input(null, new[] { "partial controller support" }), "both", "기능 Partial(대소문자 무관)");
            Eq(input(new[] { "RPG" }, new[] { "Steam Achievements" }), "both", "모르면 both");
            Eq(input(new[] { "출력: 컨트롤러" }, null), "both", "다른 키 태그는 무시");

            Func<string[], string[], string> players = (t, f) => GameRules.Players(t, f);
            Eq(players(new[] { "인원: 1~4" }, new[] { "Online PvP" }), "1~4", "태그 인원");
            Eq(players(new[] { "인원:" }, null), "1", "빈 인원 태그 → 기본");
            Eq(players(null, new[] { "Single-player", "Online PvP" }), "온라인", "온라인만");
            Eq(players(null, new[] { "MMO" }), "온라인", "MMO 는 온라인");
            Eq(players(null, new[] { "Shared/Split Screen Co-op" }), "같이", "분할 화면");
            Eq(players(null, new[] { "Online Co-op", "Co-op" }), "같이", "협동이 있으면 같이");
            Eq(players(null, new[] { "Multi-player" }), "같이", "멀티");
            Eq(players(null, new[] { "Single-player" }), "1", "혼자");
            Eq(players(null, null), "1", "기본 1");

            var a = Guid.Parse("64f78cd1-0b00-4271-a9e1-89abc55f66cc");
            var b = Guid.Parse("00000000-0000-0000-0000-000000000001");
            var ca = GameRules.ColorPair(a);
            Check(ca.SequenceEqual(GameRules.ColorPair(a)), "색: 같은 ID 같은 색");
            Check(!ca.SequenceEqual(GameRules.ColorPair(b)), "색: 다른 ID 다른 색");
            Check(ca.All(x => System.Text.RegularExpressions.Regex.IsMatch(x, "^#[0-9a-f]{6}$")), "색 형식 " + string.Join(",", ca));
            for (int i = 0; i < 200; i++)
            {
                var pair = GameRules.ColorPair(Guid.NewGuid());
                double l0 = Luma(pair[0]), l1 = Luma(pair[1]);
                if (!(l0 < 0.25 && l1 > l0 && l1 < 0.6 && Spread(pair[0]) < 0.25)) { Check(false, "색이 어둡고 차분하지 않음 " + string.Join(",", pair)); break; }
            }
            Eq(GameRules.Fnv1a("a"), 0xe40c292cu, "FNV-1a 표준값");

            var r = GameRules.SquareCrop(1200, 1600, 600);
            Check(r.X == 0 && r.Y == 200 && r.Side == 1200 && r.Out == 600, "세로 표지 가운데 자르기");
            r = GameRules.SquareCrop(300, 200, 600);
            Check(r.X == 50 && r.Y == 0 && r.Side == 200 && r.Out == 200, "작은 가로 표지는 키우지 않음");
            r = GameRules.SquareCrop(601, 600, 600);
            Check(r.X == 0 && r.Side == 600 && r.Out == 600, "1px 차이");
            Eq(GameRules.SquareCrop(0, 10, 600).Side, 0, "빈 그림");

            Eq(GameRules.IsoLocal(new DateTime(2026, 11, 4, 21, 10, 0, DateTimeKind.Local)), "2026-11-04T21:10:00", "ISO 로컬");
            Eq(GameRules.IsoLocal(null), null, "날짜 없음 null");
            var utc = new DateTime(2026, 3, 1, 3, 0, 0, DateTimeKind.Utc);
            Eq(GameRules.IsoLocal(utc), utc.ToLocalTime().ToString("yyyy-MM-dd'T'HH:mm:ss", CultureInfo.InvariantCulture), "UTC 는 로컬로");

            var g = new GameInfo { Id = a, Name = "엘든 링", Source = null, Genres = new List<string> { "액션 RPG", " " }, Tags = new List<string> { "입력: 컨트롤러" }, Features = new List<string>(), Playtime = 763200, LastActivity = new DateTime(2026, 11, 4, 21, 10, 0), Added = null };
            var json = Json.Write(GameRules.ToDto(g, "ELDEN RING", null));
            Eq(json, "{\"id\":\"64f78cd1-0b00-4271-a9e1-89abc55f66cc\",\"title\":\"엘든 링\",\"originalTitle\":\"ELDEN RING\",\"source\":\"PC\",\"genres\":[\"액션 RPG\"],\"input\":\"pad\",\"players\":\"1\",\"playtimeSec\":763200,\"lastPlayed\":\"2026-11-04T21:10:00\",\"added\":null,\"cover\":null,\"color\":[\"" + ca[0] + "\",\"" + ca[1] + "\"]}", "게임 DTO");
            Check(!GameRules.ToDto(g, "엘든 링", null).ContainsKey("originalTitle"), "originalTitle 은 이름과 같으면 빠짐");
        }

        static double Luma(string hex)
        {
            int r = Convert.ToInt32(hex.Substring(1, 2), 16), g = Convert.ToInt32(hex.Substring(3, 2), 16), b = Convert.ToInt32(hex.Substring(5, 2), 16);
            return (0.2126 * r + 0.7152 * g + 0.0722 * b) / 255;
        }

        static double Spread(string hex)
        {
            var c = new[] { hex.Substring(1, 2), hex.Substring(3, 2), hex.Substring(5, 2) }.Select(x => Convert.ToInt32(x, 16) / 255.0).ToArray();
            return c.Max() - c.Min();
        }

        static void PadChecks()
        {
            var b = PadRules.Buttons((ushort)(PadRules.A | PadRules.Start | PadRules.Left), 0, 255);
            Eq(b.Length, 17, "버튼 17개");
            Check(b[0] && b[9] && b[14] && b[7] && !b[6] && !b[16] && b.Count(x => x) == 4, "A·Menu·←·RT 자리");
            Check(PadRules.Buttons(PadRules.Back, 0, 0)[8], "View 는 8");
            Check(PadRules.Buttons((ushort)(PadRules.LB | PadRules.RB), 0, 0)[4] && PadRules.Buttons(PadRules.RB, 0, 0)[5], "LB 4 · RB 5");
            Check(!PadRules.Buttons(0, 30, 0)[6] && PadRules.Buttons(0, 31, 0)[6], "트리거 문턱 30 초과");
            var bits = PadRules.Bits(b);
            Check(PadRules.FromBits(bits).SequenceEqual(b), "비트 왕복");

            Check(PadRules.Axes(5000, -5000, 0, 0).All(x => x == 0), "원형 데드존 안은 0");
            Check(PadRules.Axes(7000, -7000, 0, 0)[0] > 0, "대각선 7000 은 원형 데드존 밖");
            var up = PadRules.Axes(0, 32767, 0, 0);
            Eq(up[1], -1.0, "위로 끝 = -1 (W3C 아래가 +)");
            Eq(PadRules.Axes(0, -32768, 0, 0)[1], 1.0, "아래로 끝 = +1");
            Eq(PadRules.Axes(0, 0, 32767, 0)[2], 1.0, "오른쪽 스틱 X");
            var diag = PadRules.Axes(32767, 32767, 0, 0);
            Check(Math.Sqrt(diag[0] * diag[0] + diag[1] * diag[1]) <= 1.01 && diag[0] > 0.7 && diag[1] < -0.7, "대각선 길이 1 이하");
            Eq(Json.Write(PadRules.Axes(0, 7900, 0, 0)), "[0,0,0,0]", "반올림한 -0 은 0 으로");
            var half = PadRules.Axes(16384, 0, 0, 0)[0];
            Check(half > 0.3 && half < 0.5, "중간은 다시 늘린 값 " + half);
        }

        static void StatusChecks()
        {
            const string en = "There is 1 interface on the system:\r\n\r\n    Name                   : Wi-Fi\r\n    State                  : connected\r\n    Receive rate (Mbps)    : 866.7\r\n    Signal                 : 87% \r\n    Profile                : Home\r\n";
            const string ko = "    이름                   : Wi-Fi\r\n    상태                   : 연결됨\r\n    신호                   : 99%\r\n";
            Eq(StatusRules.WifiSignalPercent(en), (int?)87, "netsh 영어 신호");
            Eq(StatusRules.WifiSignalPercent(ko), (int?)99, "netsh 한국어 신호");
            Eq(StatusRules.WifiSignalPercent("Network shell commands need location permission"), (int?)null, "신호 없음");
            Eq(StatusRules.WifiBars(100), 4, "막대 4");
            Eq(StatusRules.WifiBars(79), 3, "막대 3");
            Eq(StatusRules.WifiBars(5), 0, "막대 0");

            Func<string, bool, bool, string, StatusRules.NetIf> nic = (k, up, gw, d) => new StatusRules.NetIf { Kind = k, Up = up, HasGateway = gw, Description = d };
            Eq(StatusRules.NetworkKind(new[] { nic("wifi", true, true, "Intel Wi-Fi 6E"), nic("wired", true, true, "Realtek PCIe GbE") }), "wired", "둘 다면 유선");
            Eq(StatusRules.NetworkKind(new[] { nic("wifi", true, true, "Intel Wi-Fi 6E"), nic("wired", false, false, "Realtek PCIe GbE") }), "wifi", "와이파이만");
            Eq(StatusRules.NetworkKind(new[] { nic("wired", true, true, "Hyper-V Virtual Ethernet Adapter") }), "none", "가상 어댑터 제외");
            Eq(StatusRules.NetworkKind(new[] { nic("wifi", true, false, "Intel Wi-Fi 6E") }), "none", "게이트웨이 없으면 none");

            Check(StatusRules.IsVirtualInputDevice(Bs + Bs + "?" + Bs + "Root#RDP_KBD#0000#{884b96c3-56ef-11d1-bc8c-00a0c91405dd}"), "RDP 키보드 제외");
            Check(!StatusRules.IsVirtualInputDevice(Bs + Bs + "?" + Bs + "HID#VID_046D&PID_C52B&MI_00#7&2b3d3a0&0&0000#{884b96c3-56ef-11d1-bc8c-00a0c91405dd}"), "실제 HID 키보드");

            Eq(Json.Write(StatusRules.Battery(0x01, 0)), "{\"kind\":\"wired\"}", "배터리 유선");
            Eq(Json.Write(StatusRules.Battery(0x02, 2)), "{\"kind\":\"level\",\"value\":2}", "배터리 단계");
            Eq(Json.Write(StatusRules.Battery(0x03, 9)), "{\"kind\":\"level\",\"value\":3}", "배터리 단계 최대 3");
            Eq(Json.Write(StatusRules.Battery(0xFF, 3)), "{\"kind\":\"unknown\"}", "배터리 모름");
            Eq(Json.Write(StatusRules.Battery(0x00, 0)), "{\"kind\":\"unknown\"}", "배터리 끊김");
        }

        // P/Invoke 구조체 크기 (윈도우 헤더 기준. 64비트 / 32비트)
        static void NativeChecks()
        {
            bool x64 = IntPtr.Size == 8;
            Eq(Marshal.SizeOf(typeof(Native.INPUT)), x64 ? 40 : 28, "INPUT 크기");
            Eq(Marshal.SizeOf(typeof(Native.MOUSEINPUT)), x64 ? 32 : 24, "MOUSEINPUT 크기");
            Eq(Marshal.SizeOf(typeof(Native.KEYBDINPUT)), x64 ? 24 : 16, "KEYBDINPUT 크기");
            Eq(Marshal.SizeOf(typeof(Native.XINPUT_STATE)), 16, "XINPUT_STATE 크기");
            Eq(Marshal.SizeOf(typeof(Native.XINPUT_BATTERY_INFORMATION)), 2, "XINPUT_BATTERY_INFORMATION 크기");
            Eq(Marshal.SizeOf(typeof(Native.RAWINPUTDEVICELIST)), x64 ? 16 : 8, "RAWINPUTDEVICELIST 크기");
            Eq((int)Marshal.OffsetOf(typeof(Native.INPUT), "u"), x64 ? 8 : 4, "INPUT 공용체 위치");
            Eq(Native.Key(Native.VK_LEFT, false).u.ki.dwFlags, Native.KEYEVENTF_EXTENDEDKEY, "← 는 확장 키");
            Eq(Native.Key(Native.VK_TAB, true).u.ki.dwFlags, Native.KEYEVENTF_KEYUP, "Tab 떼기");
            Eq(Native.Mouse(Native.MOUSEEVENTF_WHEEL, 0, 0, -120).u.mi.mouseData, unchecked((uint)-120), "휠 음수");
        }
            static WebCommand Cmd(string type, string action = null)
        {
            return new WebCommand { Type = type, Action = action };
        }

        static GateContext Ctx(long now, bool visible = true, bool fg = true, bool game = false, bool ready = true, long input = 100)
        {
            return new GateContext { HomeVisible = visible, HomeForeground = fg, GameActive = game, PageReady = ready, NowMs = now, MsSinceUserInput = input };
        }

        static void GateChecks()
        {
            // 토큰 통
            var rl = new RateLimiter(50, 50);
            int ok = 0;
            for (int i = 0; i < 60; i++) if (rl.Allow(1000)) ok++;
            Eq(ok, 50, "비율: 같은 순간 50개까지");
            Check(!rl.Allow(1000), "비율: 51번째 거절");
            ok = 0;
            for (int i = 0; i < 10; i++) if (rl.Allow(1100)) ok++;
            Eq(ok, 5, "비율: 100ms 뒤 5개 다시");

            // 상태
            var g = new MessageGate();
            Check(g.Check(Cmd("ready"), Ctx(0, visible: false, fg: false, ready: false)) == null, "ready 는 숨어 있어도 통과");
            Check(g.Check(Cmd("ready"), Ctx(1, ready: true)) != null, "이미 준비됐으면 ready 무시");
            Check(g.Check(Cmd("log"), Ctx(2, visible: false, fg: false)) == null, "log 는 숨어 있어도 통과");
            Check(g.Check(Cmd("launch"), Ctx(3, visible: false, fg: false)) != null, "숨어 있으면 launch 거절");
            Check(g.Check(Cmd("volume"), Ctx(4, visible: true, fg: false)) != null, "앞 창이 아니면 volume 거절");
            Check(g.Check(Cmd("volume"), Ctx(5)) == null, "보이고 앞 창이면 volume 통과");
            Check(g.Check(Cmd("power", "desktop"), Ctx(6, game: true)) == null, "게임 중에도 desktop 통과");
            Check(g.Check(Cmd("power", "screenOff"), Ctx(7, game: true)) != null, "게임 중 screenOff 거절");
            Check(g.Check(Cmd("power", "sleep"), Ctx(8, game: true)) != null, "게임 중 sleep 거절");
            Check(g.Check(Cmd("power", "screenOff"), Ctx(9, input: -1)) == null, "screenOff 는 최근 입력 규칙 없음");

            // 다시 시작·종료·절전: 최근 3초 입력 + 5초 간격
            var pw = new MessageGate();
            Check(pw.Check(Cmd("power", "shutdown"), Ctx(10000, input: 4000)) != null, "입력이 4초 전이면 shutdown 거절");
            Check(pw.Check(Cmd("power", "restart"), Ctx(10001, input: -1)) != null, "입력을 모르면 restart 거절");
            Check(pw.Check(Cmd("power", "restart"), Ctx(10002, input: 1200)) == null, "1.2초 전 입력이면 restart 통과");
            Check(pw.Check(Cmd("power", "shutdown"), Ctx(12002, input: 100)) != null, "2초 뒤 shutdown 은 간격 때문에 거절");
            Check(pw.Check(Cmd("power", "sleep"), Ctx(15002, input: 100)) == null, "5초 뒤 sleep 통과");
            Check(pw.Check(Cmd("power", "sleep"), Ctx(15003, input: 3000)) != null, "통과 직후 또 sleep 거절");

            // openWeb 2초
            var ow = new MessageGate();
            Check(ow.Check(Cmd("openWeb"), Ctx(0)) == null, "openWeb 첫 번째 통과");
            Check(ow.Check(Cmd("openWeb"), Ctx(1500)) != null, "openWeb 1.5초 안에 또 거절");
            Check(ow.Check(Cmd("openWeb"), Ctx(2100)) == null, "openWeb 2초 뒤 통과");
            ow.ForgetOpenWeb();
            Check(ow.Check(Cmd("openWeb"), Ctx(2300)) == null, "열기 실패 뒤 ForgetOpenWeb 이면 바로 다시 통과");

            // 보이는데 앞 창이 아닐 때 다시 앞으로: 초당 1번
            var rf = new MessageGate();
            Check(rf.WantsRefocus(Ctx(0, visible: true, fg: false)), "보이는데 앞 창 아님 → 다시 앞으로");
            Check(!rf.WantsRefocus(Ctx(500, visible: true, fg: false)), "1초 안에 또는 안 함");
            Check(rf.WantsRefocus(Ctx(1100, visible: true, fg: false)), "1초 뒤 다시");
            Check(!rf.WantsRefocus(Ctx(5000, visible: true, fg: true)), "앞 창이면 안 함");
            Check(!rf.WantsRefocus(Ctx(9000, visible: false, fg: false)), "숨어 있으면 안 함");
            Check(!rf.WantsRefocus(Ctx(20000, visible: true, fg: false, game: true)), "게임이 켜지는 중·실행 중이면 안 함");
            Check(rf.WantsRefocus(Ctx(20001, visible: true, fg: false)), "게임 때문에 막혔던 차례는 쓰지 않음(바로 다음은 통과)");

            // 전체 50개/초, log 10개/초와 버린 수
            var fl = new MessageGate();
            ok = 0;
            for (int i = 0; i < 60; i++) if (fl.AllowAny(5000)) ok++;
            Eq(ok, 50, "전체 초당 50개(해석 전 AllowAny)");
            Check(fl.Check(Cmd("volume"), Ctx(5000)) == null, "Check 는 전체 비율을 다시 세지 않음");
            var lg = new MessageGate();
            ok = 0;
            string reason = null;
            for (int i = 0; i < 15; i++) { var r = lg.Check(Cmd("log"), Ctx(0)); if (r == null) ok++; else reason = r; }
            Eq(ok, 10, "log 초당 10개");
            Eq(reason, MessageGate.LogDropped, "log 넘침 이유");
            Eq(lg.TakeDroppedLogs(), 5, "버린 log 5개");
            Eq(lg.TakeDroppedLogs(), 0, "가져간 뒤 0");
        }

        static void CoverChecks()
        {
            var sep = System.IO.Path.DirectorySeparatorChar.ToString();
            var root = System.IO.Path.Combine(System.IO.Path.GetTempPath(), "pn", "library");
            var inside = System.IO.Path.Combine(root, "files", "abc", "cover.jpg");
            Eq(CoverRules.SafeCoverPath(inside, root), System.IO.Path.GetFullPath(inside), "DB 폴더 안 표지 통과");
            Check(CoverRules.SafeCoverPath(System.IO.Path.Combine(root, "files", "..", "..", "evil.png"), root) == null, "../ 로 빠져나가면 거절");
            Check(CoverRules.SafeCoverPath(root + "2" + sep + "x.png", root) == null, "이름만 비슷한 옆 폴더 거절");
            Check(CoverRules.SafeCoverPath(Bs + Bs + "server" + Bs + "share" + Bs + "x.png", root) == null, "UNC 거절");
            Check(CoverRules.SafeCoverPath("https://evil.example/x.png", root) == null, "http 거절");
            Check(CoverRules.SafeCoverPath("file:///etc/passwd", root) == null, "file:// 거절");
            Check(CoverRules.SafeCoverPath(inside, null) == null, "DB 폴더를 모르면 거절");

            Check(CoverRules.SizeOk(600, 900), "보통 크기 통과");
            Check(CoverRules.SizeOk(8192, 4096), "한 변 8192 통과");
            Check(!CoverRules.SizeOk(8193, 100), "한 변 8192 넘으면 거절");
            Check(!CoverRules.SizeOk(8000, 6000), "4천만 화소 넘으면 거절");
            Check(!CoverRules.SizeOk(0, 10), "빈 그림 거절");
        }

        static void WebRuleChecks()
        {
            Check(WebRules.IsAllowedRequest("https://orvio-app.example/app.js"), "앱 호스트 허용");
            Check(WebRules.IsAllowedRequest("https://ORVIO-COVERS.example/a.png?v=1"), "표지 호스트 허용(대소문자)");
            Check(!WebRules.IsAllowedRequest("https://orvio-app.example.evil.com/x"), "비슷한 다른 호스트 막음");
            Check(!WebRules.IsAllowedRequest("http://orvio-app.example/x"), "http 막음");
            Check(!WebRules.IsAllowedRequest("https://www.google.com/"), "외부 막음");
            Check(!WebRules.IsAllowedRequest(null), "null 막음");
            Eq(WebRules.SchemeHost("https://evil.example/path?secret=1"), "https://evil.example", "로그엔 스킴·호스트만");

            Eq(WebRules.FinalUrl("javascript:alert(1)"), null, "javascript: 거절");
            Eq(WebRules.FinalUrl("https://www.google.com/search?q=%EC%97%98"), "https://www.google.com/search?q=%EC%97%98", "인코딩된 주소 그대로");
            var q = WebRules.FinalUrl("https://x.com/a\"b c");
            Check(q != null && q.IndexOf('"') < 0 && q.IndexOf(' ') < 0, "AbsoluteUri 가 따옴표·공백을 인코딩 " + q);
            Eq(WebRules.QuoteArg("https://x.com/a"), "\"https://x.com/a\"", "인자로 감쌈");
            Check(WebRules.QuoteArg("https://x.com/a\"b") == null, "\" 가 남으면 거절");
            Check(WebRules.QuoteArg("https://x.com/a" + Bs + "b") == null, "\\ 가 남으면 거절");
            Check(WebRules.QuoteArg(null) == null, "null 거절");

            var edge = @"C:\Program Files (x86)\Microsoft\Edge\Application\msedge.exe".Replace("\\", Bs);
            Check(WebRules.IsEdgeProcess(edge, edge), "같은 Edge 경로");
            Check(WebRules.IsEdgeProcess(edge.ToUpperInvariant(), edge), "대소문자 무관");
            Check(WebRules.IsEdgeProcess(@"C:\Program Files (x86)\Microsoft\Edge\Application\130.0.1\msedge.exe".Replace("\\", Bs), edge), "설치 폴더 아래");
            Check(!WebRules.IsEdgeProcess(@"C:\Users\me\Downloads\msedge.exe".Replace("\\", Bs), edge), "다른 폴더의 msedge.exe 거절");
            Check(!WebRules.IsEdgeProcess(@"C:\Program Files (x86)\Microsoft\Edge\Application\cmd.exe".Replace("\\", Bs), edge), "같은 폴더 다른 exe 거절");
            Check(!WebRules.IsEdgeProcess(edge, null), "Edge 를 못 찾았으면 거절");
            Check(!WebRules.IsEdgeProcess("", edge), "앞 창을 모르면 거절");

            var s = new OrvioSettings { EdgePath = @"C:\Tools\chrome.exe".Replace("\\", Bs) };
            s.Normalize();
            Eq(s.EdgePath, "", "edgePath 가 msedge.exe 가 아니면 비움");
            var s2 = new OrvioSettings { EdgePath = @"D:\Edge\msedge.exe".Replace("\\", Bs) };
            s2.Normalize();
            Eq(s2.EdgePath, @"D:\Edge\msedge.exe".Replace("\\", Bs), "msedge.exe 는 그대로");
            foreach (var bad in new[] { "msedge.exe", @"Edge\msedge.exe", @"\\server\share\msedge.exe", @"C:msedge.exe", @"\Edge\msedge.exe" })
            {
                var s3 = new OrvioSettings { EdgePath = bad };
                s3.Normalize();
                Eq(s3.EdgePath, "", "드라이브 절대 경로가 아닌 edgePath 는 비움: " + bad);
            }
            Check(WebRules.IsDriveRooted(@"C:\x") && WebRules.IsDriveRooted("d:/x"), "드라이브 절대 경로");
            Check(!WebRules.IsDriveRooted(@"\\srv\x") && !WebRules.IsDriveRooted("C:x") && !WebRules.IsDriveRooted(null), "UNC·드라이브 상대·null 은 아님");

            var w1 = new WebCloseWatch();
            Check(!w1.Closed(true) && !w1.Closed(false) && w1.Closed(false), "Edge 창을 본 뒤 연속 2번 없으면 닫힘");
            var w2 = new WebCloseWatch();
            Check(!w2.Closed(true) && !w2.Closed(false) && !w2.Closed(true) && !w2.Closed(false) && w2.Closed(false), "창이 다시 잡히면 처음부터 셈");
            var w3 = new WebCloseWatch();
            bool early = false;
            for (int i = 0; i < 14; i++) early |= w3.Closed(false);
            Check(!early && w3.Closed(false), "창을 한 번도 못 보면 15번째에 닫힘");

            Check(SoundRules.Usable("navigation.wav", 11094) && SoundRules.Usable("activation.MP3", 1), "소리 파일: 형식·크기가 맞으면 씀");
            Check(!SoundRules.Usable("navigation.exe", 1) && !SoundRules.Usable("navigation.wav", 0) && !SoundRules.Usable("navigation.wav", SoundRules.MaxBytes + 1) && !SoundRules.Usable(null, 1), "소리 파일: 다른 형식·빈 파일·큰 파일·null 은 안 씀");
            var wav = SoundRules.KeepAwakeWav(1);
            Eq(wav.Length, 44 + 16000, "깨우기 신호 길이");
            Check(System.Text.Encoding.ASCII.GetString(wav, 0, 4) == "RIFF" && System.Text.Encoding.ASCII.GetString(wav, 36, 4) == "data" && BitConverter.ToInt32(wav, 40) == 16000, "깨우기 신호 WAV 머리");
            bool quiet = true, silent = true;
            for (int i = 44; i < wav.Length; i += 2) { int v = BitConverter.ToInt16(wav, i); quiet &= Math.Abs(v) <= 2; silent &= v == 0; }
            Check(quiet && !silent, "깨우기 신호는 ±2 안이고 완전한 무음은 아님");
            var sv = new OrvioSettings { SfxVolume = 150 };
            sv.Normalize();
            Eq(sv.SfxVolume, 100, "효과음 크기는 100까지");
        }

        static void LogChecks()
        {
            var raw = "a\nb\rc" + Ctrl1 + "d" + LineSep + "e" + ((char)0x2029) + "f" + ((char)0x85) + "g" + ((char)0x7f) + "h";
            Eq(Log.OneLine(raw), "a b c d e f g h", "로그 한 줄: 제어 문자·U+2028·U+2029 를 공백으로");
            Eq(Log.OneLine(null), "", "로그 null");
        }
    }
}
