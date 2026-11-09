using System;
using System.Collections.Generic;
using System.Text.RegularExpressions;

namespace Orvio.Core
{
    // 화면 → 확장 명령 하나. 검사를 통과한 값만 담김
    public sealed class WebCommand
    {
        public string Type;
        public string Version;   // ready
        public Guid GameId;      // launch (DB 확인은 확장이)
        public string WebId;     // openWeb
        public string Query;     // openWeb
        public string Action;    // power
        public int Volume;       // volume 0~100
        public string Key;       // setSetting
        public string Theme;     // setSetting theme
        public bool Idle;        // setSetting idle
        public string Level;     // log
        public string Msg;       // log (500자 안)
        public string Sound;     // sfx
    }

    // bridge-protocol.md 3절의 허용 목록
    public static class Messages
    {
        public const int MaxRawLength = 8192;
        public const int MaxLogLength = 500;
        public const int MaxQueryLength = 200;

        static readonly Regex WebIdRe = new Regex("^[a-z0-9_-]{1,32}$");
        static readonly HashSet<string> PowerActions = new HashSet<string> { "screenOff", "sleep", "restart", "shutdown", "desktop" };
        static readonly HashSet<string> Themes = new HashSet<string> { "auto", "light", "dark" };
        static readonly HashSet<string> Levels = new HashSet<string> { "info", "warn", "error" };
        public static readonly string[] SoundNames = { "navigation", "activation" };

        // 타입별 허용 필드 (type 제외)
        static readonly Dictionary<string, string[]> Fields = new Dictionary<string, string[]>
        {
            { "ready", new[] { "version" } },
            { "launch", new[] { "id" } },
            { "openWeb", new[] { "id", "query" } },
            { "power", new[] { "action" } },
            { "volume", new[] { "value" } },
            { "setSetting", new[] { "key", "value" } },
            { "log", new[] { "level", "msg" } },
            { "sfx", new[] { "name" } },
        };

        // 거절이면 null, 이유는 error 에
        public static WebCommand Parse(string json, out string error)
        {
            error = null;
            if (json == null) { error = "빈 메시지"; return null; }
            if (json.Length > MaxRawLength) { error = "너무 김 " + json.Length; return null; }

            Dictionary<string, object> m;
            // net462 의 double.Parse("1e999") 는 OverflowException 이라 모든 예외를 잡음
            try { m = Json.Parse(json) as Dictionary<string, object>; }
            catch (Exception e) { error = "JSON 오류 " + e.Message; return null; }
            if (m == null) { error = "객체가 아님"; return null; }

            object t;
            var type = m.TryGetValue("type", out t) ? t as string : null;
            string[] allowed;
            if (type == null || !Fields.TryGetValue(type, out allowed)) { error = "모르는 type " + (type ?? "(없음)"); return null; }
            foreach (var k in m.Keys)
            {
                if (k != "type" && Array.IndexOf(allowed, k) < 0) { error = type + ": 모르는 필드 " + k; return null; }
            }

            var c = new WebCommand { Type = type };
            switch (type)
            {
                case "ready":
                    object v;
                    if (m.TryGetValue("version", out v) && v != null)
                    {
                        if (v is double) c.Version = ((double)v).ToString(System.Globalization.CultureInfo.InvariantCulture);
                        else if (v is string && ((string)v).Length <= 20) c.Version = (string)v;
                        else { error = "ready: version 형식"; return null; }
                    }
                    return c;

                case "launch":
                    var id = Str(m, "id");
                    if (id == null || !Guid.TryParse(id, out c.GameId)) { error = "launch: id 가 GUID 아님"; return null; }
                    return c;

                case "openWeb":
                    var wid = Str(m, "id");
                    var q = Str(m, "query");
                    if ((wid == null) == (q == null)) { error = "openWeb: id 와 query 중 하나만"; return null; }
                    if (wid != null)
                    {
                        if (!WebIdRe.IsMatch(wid)) { error = "openWeb: id 형식"; return null; }
                        c.WebId = wid;
                    }
                    else
                    {
                        q = q.Trim();
                        if (q.Length == 0) { error = "openWeb: 빈 검색어"; return null; }
                        c.Query = q.Length > MaxQueryLength ? q.Substring(0, MaxQueryLength) : q;
                    }
                    return c;

                case "power":
                    var a = Str(m, "action");
                    if (a == null || !PowerActions.Contains(a)) { error = "power: action " + (a ?? "(없음)"); return null; }
                    c.Action = a;
                    return c;

                case "volume":
                    // 문서: 0~100 정수. 벗어나면 자르지 않고 거절
                    object n;
                    if (!m.TryGetValue("value", out n) || !(n is double)) { error = "volume: 수가 아님"; return null; }
                    double vol = (double)n;
                    if (!(vol >= 0 && vol <= 100) || vol != Math.Floor(vol)) { error = "volume: 0~100 정수가 아님"; return null; }
                    c.Volume = (int)vol;
                    return c;

                case "setSetting":
                    var key = Str(m, "key");
                    object val;
                    m.TryGetValue("value", out val);
                    if (key == "theme")
                    {
                        var th = val as string;
                        if (th == null || !Themes.Contains(th)) { error = "setSetting: theme 값"; return null; }
                        c.Key = key; c.Theme = th;
                        return c;
                    }
                    if (key == "idle")
                    {
                        if (!(val is bool)) { error = "setSetting: idle 은 bool"; return null; }
                        c.Key = key; c.Idle = (bool)val;
                        return c;
                    }
                    error = "setSetting: 모르는 key " + (key ?? "(없음)");
                    return null;

                case "log":
                    var lv = Str(m, "level");
                    var msg = Str(m, "msg");
                    if (lv == null || !Levels.Contains(lv)) { error = "log: level"; return null; }
                    if (msg == null) { error = "log: msg 없음"; return null; }
                    c.Level = lv;
                    c.Msg = msg.Length > MaxLogLength ? msg.Substring(0, MaxLogLength) : msg;
                    return c;

                case "sfx":
                    c.Sound = Str(m, "name");
                    if (Array.IndexOf(SoundNames, c.Sound) < 0) { error = "sfx: name"; return null; }
                    return c;
            }
            error = "처리 안 된 type " + type;
            return null;
        }

        static string Str(Dictionary<string, object> m, string k)
        {
            object v;
            return m.TryGetValue(k, out v) ? v as string : null;
        }
    }
}
