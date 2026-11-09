using System;

namespace Orvio.Core
{
    // 초당 n개 토큰 통. 처음엔 가득 참
    public sealed class RateLimiter
    {
        readonly double perMs;
        readonly double capacity;
        double tokens;
        long last = long.MinValue;

        public RateLimiter(double perSecond, double burst)
        {
            perMs = perSecond / 1000.0;
            capacity = burst;
            tokens = burst;
        }

        public bool Allow(long nowMs)
        {
            if (last != long.MinValue && nowMs > last) tokens = Math.Min(capacity, tokens + (nowMs - last) * perMs);
            last = nowMs;
            if (tokens < 1) return false;
            tokens -= 1;
            return true;
        }
    }

    // 메시지를 받을 때의 확장 상태 (UI 스레드에서 채움)
    public sealed class GateContext
    {
        public bool HomeVisible;
        public bool HomeForeground;
        public bool GameActive;
        public bool PageReady;
        public long NowMs;              // 단조 시계
        public long MsSinceUserInput;   // 마지막 실제 입력(패드·키보드·마우스) 뒤 지난 ms. 모르면 -1
    }

    // 상태에 따른 거절 (protocol 3절 '상태에 따른 거절'). 검사(Messages)를 통과한 명령만 받음
    public sealed class MessageGate
    {
        public const double MaxPerSecond = 50;
        public const double MaxLogPerSecond = 10;
        public const long OpenWebCooldownMs = 2000;
        public const long PowerCooldownMs = 5000;
        // 1초 길게 누르기 끝의 마지막 입력은 약 1초 전, 마우스를 누르고 있는 동안엔 이벤트가 없으므로 3초
        public const long PowerInputRecencyMs = 3000;
        // 이 이유는 로그를 한 줄씩 남기지 않고 개수만 셈
        public const string LogDropped = "log 비율 넘음";

        readonly RateLimiter all = new RateLimiter(MaxPerSecond, MaxPerSecond);
        readonly RateLimiter logs = new RateLimiter(MaxLogPerSecond, MaxLogPerSecond);
        readonly RateLimiter refocus = new RateLimiter(1, 1);
        long lastOpenWeb = long.MinValue;
        long lastPower = long.MinValue;
        int droppedLogs;

        // 전체 비율(초당 약 50개). 해석(Parse)보다 먼저 불러 큰 메시지를 풀기 전에 거름
        public bool AllowAny(long nowMs)
        {
            return all.Allow(nowMs);
        }

        // 통과면 null, 아니면 거절 이유. 전체 비율은 AllowAny 에서 이미 셈
        public string Check(WebCommand c, GateContext x)
        {
            switch (c.Type)
            {
                case "ready":
                    return x.PageReady ? "이미 준비됨(중복 ready 무시)" : null;
                case "log":
                    if (logs.Allow(x.NowMs)) return null;
                    droppedLogs++;
                    return LogDropped;
            }

            if (!x.HomeVisible || !x.HomeForeground) return "홈이 " + (x.HomeVisible ? "앞 창이 아님" : "숨어 있음");

            if (c.Type == "openWeb")
            {
                if (Within(lastOpenWeb, x.NowMs, OpenWebCooldownMs)) return "openWeb " + OpenWebCooldownMs + "ms 안에 또 옴";
                lastOpenWeb = x.NowMs;
                return null;
            }

            if (c.Type == "power" && c.Action != "desktop")
            {
                if (x.GameActive) return "게임 중 전원 명령";
                if (c.Action == "restart" || c.Action == "shutdown" || c.Action == "sleep")
                {
                    if (Within(lastPower, x.NowMs, PowerCooldownMs)) return c.Action + " " + PowerCooldownMs + "ms 안에 또 옴";
                    if (x.MsSinceUserInput < 0 || x.MsSinceUserInput > PowerInputRecencyMs)
                        return c.Action + ": " + PowerInputRecencyMs + "ms 안에 실제 입력 없음(" + x.MsSinceUserInput + "ms)";
                    lastPower = x.NowMs;
                }
            }
            return null;
        }

        // 열기에 실패하면 간격을 지움: 2초 안에 다시 시도해도 조용히 막히지 않게
        public void ForgetOpenWeb()
        {
            lastOpenWeb = long.MinValue;
        }

        // 홈이 보이는데 앞 창이 아닐 때 다시 앞으로 올지(초당 1번). 게임이 켜지는 중·실행 중이면 절대 안 함
        public bool WantsRefocus(GateContext x)
        {
            return x.HomeVisible && !x.HomeForeground && !x.GameActive && refocus.Allow(x.NowMs);
        }

        // 버린 log 개수를 가져가고 0으로
        public int TakeDroppedLogs()
        {
            int n = droppedLogs;
            droppedLogs = 0;
            return n;
        }

        static bool Within(long last, long now, long ms)
        {
            return last != long.MinValue && now - last < ms;
        }
    }
}
