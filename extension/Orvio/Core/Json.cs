using System;
using System.Collections;
using System.Collections.Generic;
using System.Globalization;
using System.Text;

namespace Orvio.Core
{
    // 다리 메시지용 작은 JSON. 들어오는 쪽은 엄격하게, 나가는 쪽은 그대로.
    // 객체 → Dictionary<string, object>, 배열 → List<object>, 수 → double
    public static class Json
    {
        public static object Parse(string text, int maxDepth = 8)
        {
            if (text == null) throw new FormatException("null");
            var p = new Parser(text, maxDepth);
            p.SkipWs();
            var v = p.Value(0);
            p.SkipWs();
            if (p.Pos != text.Length) throw new FormatException("뒤에 남은 글자 @" + p.Pos);
            return v;
        }

        sealed class Parser
        {
            readonly string s;
            readonly int maxDepth;
            public int Pos;

            public Parser(string s, int maxDepth) { this.s = s; this.maxDepth = maxDepth; }

            public void SkipWs()
            {
                while (Pos < s.Length && (s[Pos] == ' ' || s[Pos] == '\t' || s[Pos] == '\n' || s[Pos] == '\r')) Pos++;
            }

            FormatException Err(string what) { return new FormatException(what + " @" + Pos); }

            static bool Digit(char c) { return c >= '0' && c <= '9'; }

            public object Value(int depth)
            {
                if (depth > maxDepth) throw Err("너무 깊음");
                if (Pos >= s.Length) throw Err("끝남");
                char c = s[Pos];
                if (c == '{') return Obj(depth);
                if (c == '[') return Arr(depth);
                if (c == '"') return Str();
                if (c == '-' || Digit(c)) return Num();
                if (Lit("true")) return true;
                if (Lit("false")) return false;
                if (Lit("null")) return null;
                throw Err("모르는 값");
            }

            bool Lit(string w)
            {
                if (string.CompareOrdinal(s, Pos, w, 0, w.Length) != 0) return false;
                Pos += w.Length;
                return true;
            }

            Dictionary<string, object> Obj(int depth)
            {
                var d = new Dictionary<string, object>(StringComparer.Ordinal);
                Pos++;
                SkipWs();
                if (Pos < s.Length && s[Pos] == '}') { Pos++; return d; }
                while (true)
                {
                    SkipWs();
                    if (Pos >= s.Length || s[Pos] != '"') throw Err("키가 아님");
                    var k = Str();
                    if (d.ContainsKey(k)) throw Err("키 중복 " + k);
                    SkipWs();
                    if (Pos >= s.Length || s[Pos] != ':') throw Err(": 없음");
                    Pos++;
                    SkipWs();
                    d[k] = Value(depth + 1);
                    SkipWs();
                    if (Pos >= s.Length) throw Err("끝남");
                    if (s[Pos] == ',') { Pos++; continue; }
                    if (s[Pos] == '}') { Pos++; return d; }
                    throw Err(", 또는 } 없음");
                }
            }

            List<object> Arr(int depth)
            {
                var l = new List<object>();
                Pos++;
                SkipWs();
                if (Pos < s.Length && s[Pos] == ']') { Pos++; return l; }
                while (true)
                {
                    SkipWs();
                    l.Add(Value(depth + 1));
                    SkipWs();
                    if (Pos >= s.Length) throw Err("끝남");
                    if (s[Pos] == ',') { Pos++; continue; }
                    if (s[Pos] == ']') { Pos++; return l; }
                    throw Err(", 또는 ] 없음");
                }
            }

            string Str()
            {
                Pos++;
                var sb = new StringBuilder();
                while (true)
                {
                    if (Pos >= s.Length) throw Err("문자열 끝남");
                    char c = s[Pos++];
                    if (c == '"') return sb.ToString();
                    if (c < 0x20) throw Err("제어 문자");
                    if (c != '\\') { sb.Append(c); continue; }
                    if (Pos >= s.Length) throw Err("문자열 끝남");
                    char e = s[Pos++];
                    switch (e)
                    {
                        case '"': sb.Append('"'); break;
                        case '\\': sb.Append('\\'); break;
                        case '/': sb.Append('/'); break;
                        case 'b': sb.Append('\b'); break;
                        case 'f': sb.Append('\f'); break;
                        case 'n': sb.Append('\n'); break;
                        case 'r': sb.Append('\r'); break;
                        case 't': sb.Append('\t'); break;
                        case 'u':
                            int code;
                            if (Pos + 4 > s.Length ||
                                !int.TryParse(s.Substring(Pos, 4), NumberStyles.AllowHexSpecifier, CultureInfo.InvariantCulture, out code))
                                throw Err("\\u 틀림");
                            sb.Append((char)code);
                            Pos += 4;
                            break;
                        default: throw Err("모르는 이스케이프");
                    }
                }
            }

            double Num()
            {
                int start = Pos;
                if (s[Pos] == '-') Pos++;
                if (Pos >= s.Length) throw Err("수 끝남");
                if (s[Pos] == '0') Pos++;
                else if (Digit(s[Pos])) { while (Pos < s.Length && Digit(s[Pos])) Pos++; }
                else throw Err("수 아님");
                if (Pos < s.Length && s[Pos] == '.')
                {
                    Pos++;
                    int f = Pos;
                    while (Pos < s.Length && Digit(s[Pos])) Pos++;
                    if (Pos == f) throw Err("소수점 뒤 없음");
                }
                if (Pos < s.Length && (s[Pos] == 'e' || s[Pos] == 'E'))
                {
                    Pos++;
                    if (Pos < s.Length && (s[Pos] == '+' || s[Pos] == '-')) Pos++;
                    int f = Pos;
                    while (Pos < s.Length && Digit(s[Pos])) Pos++;
                    if (Pos == f) throw Err("지수 없음");
                }
                // net462 는 범위를 넘으면 OverflowException, 새 .NET 은 무한대. 둘 다 형식 오류로
                double d;
                try { d = double.Parse(s.Substring(start, Pos - start), NumberStyles.Float, CultureInfo.InvariantCulture); }
                catch (OverflowException) { throw Err("수 범위"); }
                if (double.IsInfinity(d) || double.IsNaN(d)) throw Err("수 범위");
                return d;
            }
        }

        public static string Write(object v)
        {
            var sb = new StringBuilder();
            WriteValue(sb, v, 0);
            return sb.ToString();
        }

        static void WriteValue(StringBuilder sb, object v, int depth)
        {
            if (depth > 32) throw new InvalidOperationException("너무 깊음");
            if (v == null) { sb.Append("null"); return; }
            var str = v as string;
            if (str != null) { WriteString(sb, str); return; }
            if (v is bool) { sb.Append((bool)v ? "true" : "false"); return; }
            if (v is double || v is float)
            {
                double d = Convert.ToDouble(v, CultureInfo.InvariantCulture);
                if (double.IsNaN(d) || double.IsInfinity(d)) sb.Append("null");
                else sb.Append(d.ToString("R", CultureInfo.InvariantCulture));
                return;
            }
            if (v is int || v is long || v is uint || v is ulong || v is short || v is ushort || v is byte || v is sbyte || v is decimal)
            {
                sb.Append(Convert.ToString(v, CultureInfo.InvariantCulture));
                return;
            }
            var dict = v as IDictionary;
            if (dict != null)
            {
                sb.Append('{');
                bool first = true;
                foreach (DictionaryEntry kv in dict)
                {
                    if (!first) sb.Append(',');
                    first = false;
                    WriteString(sb, Convert.ToString(kv.Key, CultureInfo.InvariantCulture));
                    sb.Append(':');
                    WriteValue(sb, kv.Value, depth + 1);
                }
                sb.Append('}');
                return;
            }
            var list = v as IEnumerable;
            if (list != null)
            {
                sb.Append('[');
                bool first = true;
                foreach (var item in list)
                {
                    if (!first) sb.Append(',');
                    first = false;
                    WriteValue(sb, item, depth + 1);
                }
                sb.Append(']');
                return;
            }
            throw new InvalidOperationException("쓸 수 없는 형식: " + v.GetType().Name);
        }

        static void WriteString(StringBuilder sb, string s)
        {
            sb.Append('"');
            foreach (char c in s)
            {
                switch (c)
                {
                    case '"': sb.Append("\\\""); break;
                    case '\\': sb.Append("\\\\"); break;
                    case '\n': sb.Append("\\n"); break;
                    case '\r': sb.Append("\\r"); break;
                    case '\t': sb.Append("\\t"); break;
                    default:
                        if (c < 0x20 || c == (char)0x2028 || c == (char)0x2029) sb.Append("\\u").Append(((int)c).ToString("x4", CultureInfo.InvariantCulture));
                        else sb.Append(c);
                        break;
                }
            }
            sb.Append('"');
        }
    }
}
