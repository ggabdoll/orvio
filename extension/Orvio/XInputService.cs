using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Threading;
using Orvio.Core;

namespace Orvio
{
    // 컨트롤러를 뒤 스레드에서 읽음
    // 홈이 보일 때(xinput 모드): 약 60Hz, 바뀌면 pad 보냄
    // 홈이 숨었을 때: 20Hz, Menu 길게 → 홈 / Edge 가 앞이면 컨트롤러 마우스(60Hz)
    internal sealed class XInputService : IDisposable
    {
        const double SendIntervalMs = 1000.0 / 60;   // pad 평균 최대 60번/초
        const int ProbeDisconnectedMs = 2000; // 끊긴 칸은 가끔만 확인(XInput 은 빈 칸 읽기가 느림)

        readonly OrvioPlugin p;
        readonly MouseEmulator mouse;
        readonly Stopwatch clock = Stopwatch.StartNew();
        volatile bool stop;
        volatile bool showRequested;
        int packetChanges;
        volatile string pendingPad;
        int padScheduled;
        int refocusScheduled;
        // 실제 입력(버튼·데드존 밖 스틱)이 마지막으로 바뀐 때. Environment.TickCount 기준
        volatile int lastInputTick;
        volatile bool hasInput;

        public XInputService(OrvioPlugin p)
        {
            this.p = p;
            mouse = new MouseEmulator(p);
            new Thread(Run) { IsBackground = true, Name = "orvio-xinput" }.Start();
        }

        public void Dispose() { stop = true; }

        // 진단: 마지막 호출 뒤 XInput 패킷 번호가 바뀐 횟수
        public int TakePacketChanges() { return Interlocked.Exchange(ref packetChanges, 0); }

        // 전원 명령의 '최근 실제 입력' 판정용. 지난 ms, 모르면 -1
        public long MsSinceInput()
        {
            if (!hasInput) return -1;
            int last = lastInputTick;   // 먼저 읽음: 그 뒤에 '지금'을 읽어야 음수(→ 아주 큰 값)가 안 나옴
            return (long)unchecked((uint)(Environment.TickCount - last));
        }

        // 홈을 보인 직후. 그때 눌려 있던 버튼은 뗄 때까지 화면에 안 보냄(Menu 길게 → 빠른 메뉴 열림 방지)
        public void OnShown() { showRequested = true; }

        // UI 스레드가 바쁘면 쌓지 않고 마지막 상태 하나만 보냄
        void PostPad(string json)
        {
            pendingPad = json;
            if (Interlocked.Exchange(ref padScheduled, 1) == 1) return;
            p.Ui(() =>
            {
                Interlocked.Exchange(ref padScheduled, 0);
                p.PostRaw(pendingPad);
            }, "pad");
        }

        // 다시 앞으로 오기 요청도 UI 에 쌓지 않고 하나만(초당 1번 제한은 UI 쪽 RefocusIfBehind)
        void RequestRefocus()
        {
            if (Interlocked.Exchange(ref refocusScheduled, 1) == 1) return;
            p.Ui(() =>
            {
                Interlocked.Exchange(ref refocusScheduled, 0);
                p.RefocusIfBehind("pad");
            }, "refocus");
        }

        void Run()
        {
            var states = new Native.XINPUT_STATE[4];
            var connected = new bool[4];
            var nextProbe = new long[4];
            var lastPacket = new uint[4];
            var mask = new int[4];
            var menuSince = new long[] { -1, -1, -1, -1 };
            var menuFired = new bool[4];
            var prevSig = new string[4];
            string lastJson = null, baselineJson = null, behindJson = null;
            long shownAt = -1, lastTick = clock.ElapsedMilliseconds;
            double nextSend = 0;
            int errors = 0;

            while (!stop)
            {
                int sleep = 50;
                try
                {
                    long now = clock.ElapsedMilliseconds;
                    double dt = Math.Min(0.1, (now - lastTick) / 1000.0);
                    lastTick = now;

                    for (uint i = 0; i < 4; i++)
                    {
                        if (!connected[i] && now < nextProbe[i]) continue;
                        Native.XINPUT_STATE st;
                        connected[i] = Native.XInputGetState(i, out st) == Native.ERROR_SUCCESS;
                        if (!connected[i]) { nextProbe[i] = now + ProbeDisconnectedMs; mask[i] = 0; continue; }
                        if (st.dwPacketNumber != lastPacket[i])
                        {
                            lastPacket[i] = st.dwPacketNumber;
                            Interlocked.Increment(ref packetChanges);
                        }
                        states[i] = st;
                        // 패킷 번호는 스틱 잡음에도 바뀌므로, 매핑한 값(버튼·데드존 뒤 축)이 바뀔 때만 '입력'으로 셈
                        var g0 = st.Gamepad;
                        var sig = PadRules.Bits(PadRules.Buttons(g0.wButtons, g0.bLeftTrigger, g0.bRightTrigger)) + ":" + Json.Write(PadRules.Axes(g0.sThumbLX, g0.sThumbLY, g0.sThumbRX, g0.sThumbRY));
                        if (prevSig[i] != null && sig != prevSig[i])
                        {
                            lastInputTick = Environment.TickCount;
                            hasInput = true;
                        }
                        prevSig[i] = sig;
                    }

                    bool visible = p.HomeVisible, active = p.GameActive;
                    if (visible)
                    {
                        for (int i = 0; i < 4; i++) { menuSince[i] = -1; menuFired[i] = false; }
                        mouse.Reset();
                        if (p.Settings.InputMode != "xinput") { sleep = 100; }
                        else
                        {
                            bool justShown = showRequested;
                            if (justShown) { showRequested = false; shownAt = now; }
                            var pads = new List<object>();
                            for (int i = 0; i < 4; i++)
                            {
                                if (!connected[i]) continue;
                                var g = states[i].Gamepad;
                                int bits = PadRules.Bits(PadRules.Buttons(g.wButtons, g.bLeftTrigger, g.bRightTrigger));
                                if (justShown) mask[i] = bits;
                                mask[i] &= bits;   // 뗀 버튼은 가리기에서 뺌
                                pads.Add(new Dictionary<string, object>
                                {
                                    { "index", i },
                                    { "buttons", PadRules.FromBits(bits & ~mask[i]) },
                                    { "axes", PadRules.Axes(g.sThumbLX, g.sThumbLY, g.sThumbRX, g.sThumbRY) },
                                });
                            }
                            var json = Json.Write(new Dictionary<string, object> { { "type", "pad" }, { "pads", pads } });
                            if (justShown) baselineJson = json;
                            // 홈이 보이지만 앞 창이 아니면 화면에 보내지 않음(보이지 않게 화면을 움직이지 않게).
                            // 대신 패드가 바뀌면 다시 앞으로 오게 요청(UI 에서 초당 1번). lastJson 을 비워 두면
                            // 앞 창이 된 다음 틱에 아직 누르고 있는 버튼까지 그대로 보내므로 첫 입력이 사라지지 않음(K-3)
                            var home = p.Home;   // 한 번만 읽음
                            bool front = home != null && home.IsForeground();
                            if (!front)
                            {
                                if (behindJson != null && json != behindJson) RequestRefocus();
                                behindJson = json;
                                lastJson = null;
                            }
                            else behindJson = null;
                            // 루프는 윈도우 타이머(15.6ms) 단위로 돌므로 고정 간격 대신 몫을 쌓아 평균 60번/초를 넘지 않게
                            if (front && json != lastJson && now >= nextSend)
                            {
                                lastJson = json;
                                nextSend = Math.Max(nextSend + SendIntervalMs, now);
                                PostPad(json);
                                if (shownAt >= 0 && json != baselineJson)
                                {
                                    // K-3: 보인 뒤 첫 입력이 들어오기까지
                                    if (p.Settings.Diagnostics) Log.Info("diag", "보인 뒤 첫 패드 변화 " + (now - shownAt) + "ms");
                                    shownAt = -1;
                                }
                                p.Sys.OnPadActivity();
                            }
                            sleep = 15;
                        }
                    }
                    else
                    {
                        lastJson = null;   // 다시 보일 때 지금 상태를 한 번 보냄
                        behindJson = null; // 숨었다 보이면 '앞 창 아님' 기준도 새로
                        for (int i = 0; i < 4; i++)
                        {
                            bool menu = connected[i] && (states[i].Gamepad.wButtons & PadRules.Start) != 0;
                            if (!menu) { menuSince[i] = -1; menuFired[i] = false; continue; }
                            if (menuSince[i] < 0) menuSince[i] = now;
                            else if (!menuFired[i] && !active && now - menuSince[i] >= p.Settings.MenuHoldMs)
                            {
                                menuFired[i] = true;
                                Log.Info("input", "Menu 길게(컨트롤러 " + (i + 1) + ") → 홈");
                                p.Ui(() => p.ShowHome("menuHold"), "menuHold");
                            }
                        }

                        if (!active && EdgeLauncher.IsForegroundEdge())
                        {
                            short lx = 0, ly = 0, rx = 0, ry = 0;
                            ushort buttons = 0;
                            for (int i = 0; i < 4; i++)
                            {
                                if (!connected[i]) continue;
                                var g = states[i].Gamepad;
                                buttons |= g.wButtons;
                                if (Math.Abs((int)g.sThumbLX) + Math.Abs((int)g.sThumbLY) > Math.Abs((int)lx) + Math.Abs((int)ly)) { lx = g.sThumbLX; ly = g.sThumbLY; }
                                if (Math.Abs((int)g.sThumbRX) + Math.Abs((int)g.sThumbRY) > Math.Abs((int)rx) + Math.Abs((int)ry)) { rx = g.sThumbRX; ry = g.sThumbRY; }
                            }
                            mouse.Tick(lx, ly, rx, ry, buttons, dt);
                            sleep = 15;
                        }
                        else
                        {
                            mouse.Reset();
                        }
                    }
                    errors = 0;
                }
                catch (DllNotFoundException e)
                {
                    Log.Error("input", "xinput1_4.dll 없음, 컨트롤러 읽기 멈춤", e);
                    return;
                }
                catch (Exception e)
                {
                    if (errors++ < 5) Log.Error("input", "읽기 실패", e);
                    sleep = 500;
                }
                Thread.Sleep(sleep);
            }
        }
    }
}
