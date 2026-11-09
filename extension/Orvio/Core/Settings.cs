using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;
using Playnite.SDK.Data;

namespace Orvio.Core
{
    // 웹 바로가기 하나 (protocol 4절 '웹 바로가기')
    public sealed class WebShortcut
    {
        [SerializationPropertyName("id")] public string Id { get; set; }
        [SerializationPropertyName("title")] public string Title { get; set; }
        [SerializationPropertyName("url")] public string Url { get; set; }
        [SerializationPropertyName("color")] public string Color { get; set; }

        public WebShortcut() { }
        public WebShortcut(string id, string title, string url, string color) { Id = id; Title = title; Url = url; Color = color; }

        public Dictionary<string, object> ToDto()
        {
            return new Dictionary<string, object> { { "id", Id }, { "title", Title }, { "url", Url }, { "color", Color } };
        }
    }

    // settings.json. 사용자가 손으로 고칠 수 있으므로 읽은 뒤 Normalize 로 바로잡음
    public sealed class OrvioSettings
    {
        public const string SearchId = "search";
        const string DefaultEdgeArgs = "--start-fullscreen";

        [SerializationPropertyName("theme")] public string Theme { get; set; } = "auto";
        [SerializationPropertyName("idle")] public bool Idle { get; set; }
        // 기본값을 여기서 채우지 않음: 역직렬화가 목록에 덧붙이는 것을 막으려고. Normalize 가 채움
        [SerializationPropertyName("web")] public List<WebShortcut> Web { get; set; }
        [SerializationPropertyName("launchTimeoutSec")] public int LaunchTimeoutSec { get; set; } = 90;
        [SerializationPropertyName("earlyStopSec")] public int EarlyStopSec { get; set; } = 30;
        [SerializationPropertyName("menuHoldMs")] public int MenuHoldMs { get; set; } = 1000;
        [SerializationPropertyName("inputMode")] public string InputMode { get; set; } = "xinput";
        [SerializationPropertyName("edgePath")] public string EdgePath { get; set; } = "";
        [SerializationPropertyName("edgeArgs")] public string EdgeArgs { get; set; } = DefaultEdgeArgs;
        // 컨트롤러 마우스 속도. 실제 TV에서 맞추는 손잡이 (px/초, 휠 단위/초)
        [SerializationPropertyName("cursorSpeed")] public int CursorSpeed { get; set; } = 1800;
        [SerializationPropertyName("wheelSpeed")] public int WheelSpeed { get; set; } = 1500;
        // 효과음 크기 0~100. 0 이면 끔. 실제 TV에서 맞추는 손잡이
        [SerializationPropertyName("sfxVolume")] public int SfxVolume { get; set; } = 50;
        [SerializationPropertyName("devMode")] public bool DevMode { get; set; }
        // 지금은 켬: TV PC 에서 K-1·K-2·K-3 진단 로그가 필요. v1.0 에서 false 로 바꿈
        [SerializationPropertyName("diagnostics")] public bool Diagnostics { get; set; } = true;

        static readonly Regex IdRe = new Regex("^[a-z0-9_-]{1,32}$");
        static readonly Regex ColorRe = new Regex("^#[0-9a-fA-F]{6}$");
        static readonly string[] ThemeValues = { "auto", "light", "dark" };
        static readonly string[] InputValues = { "xinput", "gamepadApi" };

        public static OrvioSettings CreateDefault()
        {
            var s = new OrvioSettings();
            s.Normalize();
            return s;
        }

        // 색은 시안 h-dial.html DATA.web 그대로
        public static List<WebShortcut> DefaultWeb()
        {
            return new List<WebShortcut>
            {
                new WebShortcut("youtube", "유튜브", "https://www.youtube.com", "#ff3d3d"),
                new WebShortcut("netflix", "넷플릭스", "https://www.netflix.com", "#e50914"),
                new WebShortcut("chzzk", "치지직", "https://chzzk.naver.com", "#00ffa3"),
                new WebShortcut("disney", "디즈니+", "https://www.disneyplus.com", "#1f6fff"),
                new WebShortcut("tving", "티빙", "https://www.tving.com", "#ff153c"),
                new WebShortcut("naver", "네이버", "https://www.naver.com", "#03c75a"),
                new WebShortcut(SearchId, "웹 검색", "https://www.google.com/search?q={q}", "#8a5cf5"),
            };
        }

        public void Normalize()
        {
            if (Array.IndexOf(ThemeValues, Theme) < 0) Theme = "auto";
            if (Array.IndexOf(InputValues, InputMode) < 0) InputMode = "xinput";
            LaunchTimeoutSec = Clamp(LaunchTimeoutSec, 10, 900);
            EarlyStopSec = Clamp(EarlyStopSec, 0, 600);
            MenuHoldMs = Clamp(MenuHoldMs, 300, 5000);
            CursorSpeed = Clamp(CursorSpeed, 200, 8000);
            WheelSpeed = Clamp(WheelSpeed, 100, 8000);
            SfxVolume = Clamp(SfxVolume, 0, 100);
            if (EdgePath == null) EdgePath = "";
            // 지정한다면 드라이브 절대 경로의 msedge.exe 여야 함(다른 exe·상대 경로·네트워크 경로를 띄우지 않게). 아니면 자동 찾기로
            var ep = EdgePath.Trim();
            if (ep.Length > 0 && (!WebRules.IsEdgeExeName(ep) || !WebRules.IsDriveRooted(ep))) EdgePath = "";
            if (EdgeArgs == null) EdgeArgs = DefaultEdgeArgs;

            var good = new List<WebShortcut>();
            foreach (var w in Web ?? new List<WebShortcut>())
            {
                if (w == null || w.Id == null || !IdRe.IsMatch(w.Id) || good.Any(g => g.Id == w.Id)) continue;
                if (!IsHttpUrl(w.Id == SearchId ? (w.Url ?? "").Replace("{q}", "x") : w.Url)) continue;
                if (w.Id == SearchId && (w.Url ?? "").IndexOf("{q}", StringComparison.Ordinal) < 0) continue;
                var title = (w.Title ?? "").Trim();
                if (title.Length == 0) title = w.Id;
                if (title.Length > 20) title = title.Substring(0, 20);
                good.Add(new WebShortcut(w.Id, title, w.Url, w.Color != null && ColorRe.IsMatch(w.Color) ? w.Color : "#8a5cf5"));
            }
            if (good.Count == 0) good = DefaultWeb();
            // 검색 칸은 늘 있어야 함(검색 화면의 '웹에서 찾기')
            if (!good.Any(g => g.Id == SearchId)) good.Add(DefaultWeb().Last());
            Web = good;
        }

        // 바로가기 ID → 열 주소. 없으면 null
        public string UrlFor(string webId)
        {
            var w = Web == null ? null : Web.FirstOrDefault(x => x.Id == webId);
            if (w == null) return null;
            return w.Id == SearchId ? w.Url.Replace("{q}", "") : w.Url;
        }

        public string SearchUrl(string query)
        {
            var w = Web == null ? null : Web.FirstOrDefault(x => x.Id == SearchId);
            if (w == null) return null;
            return w.Url.Replace("{q}", Uri.EscapeDataString(query ?? ""));
        }

        // init.settings (protocol 4절)
        public Dictionary<string, object> ToInitPayload()
        {
            return new Dictionary<string, object>
            {
                { "theme", Theme },
                { "idle", Idle },
                { "web", Web.Select(w => (object)w.ToDto()).ToList() },
                { "launchTimeoutSec", LaunchTimeoutSec },
            };
        }

        public static bool IsHttpUrl(string s)
        {
            Uri u;
            return s != null && Uri.TryCreate(s, UriKind.Absolute, out u) && (u.Scheme == Uri.UriSchemeHttps || u.Scheme == Uri.UriSchemeHttp);
        }

        static int Clamp(int v, int lo, int hi) { return v < lo ? lo : v > hi ? hi : v; }
    }
}
