using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Threading;

namespace Orvio
{
    // 진단(설정 diagnostics). 숨김 동안 5초마다 앞 창·XInput 변화(K-2), 60초마다 CPU(NFR-05)
    // 보인 뒤 앞 창 여부(+200/+1000ms)와 첫 패드 변화(K-3)는 OrvioPlugin·XInputService 가 기록
    internal sealed class Diagnostics : IDisposable
    {
        readonly OrvioPlugin p;
        readonly Timer timer;
        int busy;
        int hiddenTicks;
        bool visibleSinceCpu = true;
        Dictionary<int, TimeSpan> lastCpu = new Dictionary<int, TimeSpan>();
        DateTime lastCpuAt;

        public Diagnostics(OrvioPlugin p)
        {
            this.p = p;
            timer = new Timer(_ => Tick(), null, 5000, 5000);
        }

        public void Dispose() { try { timer.Dispose(); } catch { } }

        void Tick()
        {
            if (Interlocked.Exchange(ref busy, 1) == 1) return;
            try
            {
                if (!p.Settings.Diagnostics) return;
                if (p.HomeVisible) { visibleSinceCpu = true; hiddenTicks = 0; p.Input.TakePacketChanges(); return; }
                int changes = p.Input.TakePacketChanges();
                Log.Info("diag", "숨김: 앞 창=" + Native.ForegroundProcessName() + " XInput 바뀜=" + (changes > 0 ? "예(" + changes + ")" : "아니오") + " 게임=" + p.GameActive);
                if (++hiddenTicks % 12 == 0) Cpu();
            }
            catch (Exception e) { Log.Error("diag", "진단 실패", e); }
            finally { Interlocked.Exchange(ref busy, 0); }
        }

        void Cpu()
        {
            int self;
            using (var me = Process.GetCurrentProcess()) self = me.Id;
            var web = p.UiInvoke(() => p.Home != null ? p.Home.WebViewProcessIds() : new List<int>(), new List<int>());
            var now = DateTime.UtcNow;
            var cpu = new Dictionary<int, TimeSpan>();
            foreach (var pid in new[] { self }.Concat(web))
            {
                try { using (var pr = Process.GetProcessById(pid)) cpu[pid] = pr.TotalProcessorTime; }
                catch { }
            }
            if (!visibleSinceCpu && lastCpu.Count > 0)
            {
                double wall = (now - lastCpuAt).TotalMilliseconds * Environment.ProcessorCount;
                Func<int, double> used = pid =>
                {
                    TimeSpan a, b;
                    return cpu.TryGetValue(pid, out a) && lastCpu.TryGetValue(pid, out b) ? (a - b).TotalMilliseconds : 0;
                };
                double selfPct = used(self) / wall * 100;
                double webPct = web.Sum(used) / wall * 100;
                Log.Info("diag", string.Format("CPU(숨김 {0:0}초) Playnite {1:0.00}% · msedgewebview2 {2:0.00}% ({3}개)",
                    (now - lastCpuAt).TotalSeconds, selfPct, webPct, web.Count));
            }
            lastCpu = cpu;
            lastCpuAt = now;
            visibleSinceCpu = false;
        }
    }
}
