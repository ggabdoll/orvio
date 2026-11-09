using System;
using System.Runtime.InteropServices;
using Orvio.Core;

namespace Orvio
{
    // Edge 가 앞일 때 컨트롤러 → 마우스·키 (FR-17)
    // 이중 잠금: 넣기 직전마다 ① 앞 창 프로세스가 Edge 설치 폴더의 msedge.exe ② 실행·추적 중인 게임 없음. 하나라도 아니면 넣지 않음(C-5)
    internal sealed class MouseEmulator
    {
        readonly OrvioPlugin p;
        bool armed;
        ushort prev;
        double accX, accY, accWheel;

        public MouseEmulator(OrvioPlugin p) { this.p = p; }

        public void Reset()
        {
            armed = false;
            accX = accY = accWheel = 0;
        }

        public void Tick(short lx, short ly, short rx, short ry, ushort buttons, double dt)
        {
            // 켜지는 순간 이미 눌려 있던 버튼(홈에서 A 로 웹 열기 등)은 무시
            if (!armed) { armed = true; prev = buttons; return; }

            // 왼쪽 스틱: 가속(제곱 곡선) 커서
            double x, y;
            PadRules.Stick(lx, ly, PadRules.LeftDeadzone, out x, out y);
            double mag = Math.Sqrt(x * x + y * y);
            if (mag > 0)
            {
                double speed = p.Settings.CursorSpeed * mag * mag;
                accX += x / mag * speed * dt;
                accY += y / mag * speed * dt;
            }
            int dx = (int)accX, dy = (int)accY;
            accX -= dx;
            accY -= dy;
            if (dx != 0 || dy != 0) MoveBy(dx, dy);

            // 오른쪽 스틱 세로: 휠 (위로 밀면 위로)
            double wx, wy;
            PadRules.Stick(rx, ry, PadRules.RightDeadzone, out wx, out wy);
            if (wy == 0) accWheel = 0;
            else accWheel += -wy * Math.Abs(wy) * p.Settings.WheelSpeed * dt;
            int wheel = (int)accWheel;
            if (wheel != 0)
            {
                accWheel -= wheel;
                Inject(Native.Mouse(Native.MOUSEEVENTF_WHEEL, 0, 0, wheel));
            }

            // 버튼: 눌린 순간만
            ushort down = (ushort)(buttons & ~prev);
            prev = buttons;
            if ((down & PadRules.A) != 0)
                Inject(Native.Mouse(Native.MOUSEEVENTF_LEFTDOWN), Native.Mouse(Native.MOUSEEVENTF_LEFTUP));
            if ((down & PadRules.B) != 0)
                Inject(Native.Key(Native.VK_MENU, false), Native.Key(Native.VK_LEFT, false), Native.Key(Native.VK_LEFT, true), Native.Key(Native.VK_MENU, true));
            if ((down & PadRules.LB) != 0)
                Inject(Native.Key(Native.VK_CONTROL, false), Native.Key(Native.VK_SHIFT, false), Native.Key(Native.VK_TAB, false),
                       Native.Key(Native.VK_TAB, true), Native.Key(Native.VK_SHIFT, true), Native.Key(Native.VK_CONTROL, true));
            if ((down & PadRules.RB) != 0)
                Inject(Native.Key(Native.VK_CONTROL, false), Native.Key(Native.VK_TAB, false), Native.Key(Native.VK_TAB, true), Native.Key(Native.VK_CONTROL, true));
            if ((down & PadRules.Y) != 0)
                Inject(Native.Key(Native.VK_LWIN, false), Native.Key(Native.VK_CONTROL, false), Native.Key(Native.VK_O, false),
                       Native.Key(Native.VK_O, true), Native.Key(Native.VK_CONTROL, true), Native.Key(Native.VK_LWIN, true));
        }

        // 절대 좌표로 옮김(윈도우 포인터 가속의 영향을 받지 않게)
        void MoveBy(int dx, int dy)
        {
            Native.POINT pt;
            if (!Native.GetCursorPos(out pt)) return;
            int vx = Native.GetSystemMetrics(Native.SM_XVIRTUALSCREEN), vy = Native.GetSystemMetrics(Native.SM_YVIRTUALSCREEN);
            int vw = Native.GetSystemMetrics(Native.SM_CXVIRTUALSCREEN), vh = Native.GetSystemMetrics(Native.SM_CYVIRTUALSCREEN);
            if (vw <= 1 || vh <= 1) return;
            int nx = Math.Max(vx, Math.Min(vx + vw - 1, pt.X + dx));
            int ny = Math.Max(vy, Math.Min(vy + vh - 1, pt.Y + dy));
            int ax = (int)Math.Round((nx - vx) * 65535.0 / (vw - 1));
            int ay = (int)Math.Round((ny - vy) * 65535.0 / (vh - 1));
            Inject(Native.Mouse(Native.MOUSEEVENTF_MOVE | Native.MOUSEEVENTF_ABSOLUTE | Native.MOUSEEVENTF_VIRTUALDESK, ax, ay));
        }

        // 모든 입력은 여기를 지남. 잠금은 매번 다시 확인(① 앞 창 실행 파일의 전체 경로가 찾아 둔 Edge 설치 폴더의 msedge.exe)
        void Inject(params Native.INPUT[] inputs)
        {
            if (p.GameActive || !EdgeLauncher.IsForegroundEdge()) return;
            Native.SendInput((uint)inputs.Length, inputs, Marshal.SizeOf(typeof(Native.INPUT)));
        }
    }
}
