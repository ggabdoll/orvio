using System;
using System.Globalization;
using System.IO;
using System.Text;

namespace Orvio
{
    // orvio.log. 1MB 넘으면 orvio.log.1 로 밀고 하나만 남김. 절대 예외를 던지지 않음
    internal static class Log
    {
        const long MaxBytes = 1024 * 1024;
        static readonly object gate = new object();
        static string path;

        public static void Init(string dir)
        {
            try
            {
                Directory.CreateDirectory(dir);
                path = Path.Combine(dir, "orvio.log");
            }
            catch { }
        }

        public static void Info(string area, string msg) { Write("info", area, msg, null); }
        public static void Warn(string area, string msg) { Write("warn", area, msg, null); }
        public static void Error(string area, string msg, Exception ex = null) { Write("error", area, msg, ex); }

        public static void Write(string level, string area, string msg, Exception ex)
        {
            try
            {
                if (path == null) return;
                var line = new StringBuilder()
                    .Append(DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss.fff", CultureInfo.InvariantCulture))
                    .Append(" [").Append(level).Append("] [").Append(area).Append("] ")
                    .Append(OneLine(msg));
                if (ex != null) line.Append(" | ").Append(ex);
                line.Append(Environment.NewLine);
                lock (gate)
                {
                    var fi = new FileInfo(path);
                    if (fi.Exists && fi.Length > MaxBytes)
                    {
                        var old = path + ".1";
                        if (File.Exists(old)) File.Delete(old);
                        File.Move(path, old);
                    }
                    File.AppendAllText(path, line.ToString(), Encoding.UTF8);
                }
            }
            catch { }
        }

        // 화면이 보낸 글이 줄을 나누지 못하게: 제어 문자(C0·C1, NEL 포함)와 U+2028·U+2029 를 공백으로
        internal static string OneLine(string s)
        {
            if (s == null) return "";
            var sb = new StringBuilder(s.Length);
            foreach (var c in s) sb.Append(char.IsControl(c) || c == (char)0x2028 || c == (char)0x2029 ? ' ' : c);
            return sb.ToString();
        }
    }
}
