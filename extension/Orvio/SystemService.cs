using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.NetworkInformation;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Orvio.Core;

namespace Orvio
{
    // 상태 줄(네트워크·패드 배터리·키마·볼륨), 볼륨, 전원
    internal sealed class SystemService : IDisposable
    {
        const int TickMs = 2000;
        const int WifiEveryMs = 10000;

        readonly OrvioPlugin p;
        readonly Timer timer;
        readonly Stopwatch clock = Stopwatch.StartNew();
        int busy;
        volatile string lastJson;
        long lastSentMs = -100000;
        int? wifiPercent;
        long wifiAt = -100000;
        string lastDeviceNames;
        volatile bool screenOff;
        readonly object volumeGate = new object();
        volatile int wantedVolume;

        public SystemService(OrvioPlugin p)
        {
            this.p = p;
            timer = new Timer(_ => Tick(), null, TickMs, TickMs);
        }

        public void Dispose() { try { timer.Dispose(); } catch { } }

        public void Kick() { try { timer.Change(0, TickMs); } catch { } }

        public void ResetStatus()
        {
            lastJson = null;
            lastSentMs = -100000;
            Kick();
        }

        // ── 상태 (타이머 스레드) ──

        void Tick()
        {
            if (Interlocked.Exchange(ref busy, 1) == 1) return;
            try
            {
                var home = p.Home;
                if (!p.HomeVisible || home == null || !home.PageReady) return;
                var json = Json.Write(Build());
                long now = clock.ElapsedMilliseconds;
                // 바뀔 때만, 최소 2초 간격 (조금 이르면 다음 틱에)
                if (json == lastJson || now - lastSentMs < TickMs - 100) return;
                lastJson = json;
                lastSentMs = now;
                p.Ui(() => p.PostRaw(json), "status");
            }
            catch (Exception e) { Log.Error("status", "상태 만들기 실패", e); }
            finally { Interlocked.Exchange(ref busy, 0); }
        }

        Dictionary<string, object> Build()
        {
            var kind = NetworkKind();
            var network = new Dictionary<string, object> { { "kind", kind } };
            if (kind == "wifi")
            {
                RefreshWifi();
                if (wifiPercent != null) network["strength"] = StatusRules.WifiBars(wifiPercent.Value);
            }
            var d = new Dictionary<string, object>
            {
                { "type", "status" },
                { "network", network },
                { "pads", Pads() },
                { "kbm", Kbm() },
            };
            var vol = GetVolume();
            if (vol != null) d["volume"] = vol.Value;
            return d;
        }

        static string NetworkKind()
        {
            var list = new List<StatusRules.NetIf>();
            foreach (var n in NetworkInterface.GetAllNetworkInterfaces())
            {
                var t = n.NetworkInterfaceType;
                string kind = t == NetworkInterfaceType.Wireless80211 ? "wifi"
                    : t == NetworkInterfaceType.Ethernet || t == NetworkInterfaceType.GigabitEthernet || t == NetworkInterfaceType.FastEthernetT || t == NetworkInterfaceType.FastEthernetFx ? "wired"
                    : "other";
                bool gateway = false;
                try
                {
                    gateway = n.GetIPProperties().GatewayAddresses.Any(g => g.Address != null && !g.Address.Equals(IPAddress.Any) && !g.Address.Equals(IPAddress.IPv6Any));
                }
                catch { }
                list.Add(new StatusRules.NetIf { Kind = kind, Up = n.OperationalStatus == OperationalStatus.Up, HasGateway = gateway, Description = n.Description + " " + n.Name });
            }
            return StatusRules.NetworkKind(list);
        }

        // netsh 는 10초에 한 번. 3초 안에 안 끝나면 버림
        void RefreshWifi()
        {
            long now = clock.ElapsedMilliseconds;
            if (now - wifiAt < WifiEveryMs) return;
            wifiAt = now;
            try
            {
                var psi = new ProcessStartInfo(SystemExe("netsh.exe"), "wlan show interfaces")
                {
                    UseShellExecute = false,
                    CreateNoWindow = true,
                    RedirectStandardOutput = true,
                };
                using (var proc = Process.Start(psi))
                {
                    var output = proc.StandardOutput.ReadToEndAsync();
                    if (!proc.WaitForExit(3000)) { try { proc.Kill(); } catch { } return; }
                    wifiPercent = StatusRules.WifiSignalPercent(output.Result);
                }
            }
            catch (Exception e) { Log.Warn("status", "netsh 실패: " + e.Message); }
        }

        static List<object> Pads()
        {
            var r = new List<object>();
            for (uint i = 0; i < 4; i++)
            {
                Native.XINPUT_STATE st;
                if (Native.XInputGetState(i, out st) != Native.ERROR_SUCCESS) continue;
                Native.XINPUT_BATTERY_INFORMATION bi;
                var battery = Native.XInputGetBatteryInformation(i, Native.BATTERY_DEVTYPE_GAMEPAD, out bi) == Native.ERROR_SUCCESS
                    ? StatusRules.Battery(bi.BatteryType, bi.BatteryLevel)
                    : StatusRules.Battery(0xFF, 0);
                r.Add(new Dictionary<string, object> { { "index", (int)i }, { "name", "컨트롤러 " + (i + 1) }, { "battery", battery } });
            }
            return r;
        }

        // 윈도우가 아는 키보드·마우스. 진단이 켜져 있으면 장치 이름을 로그로(휴리스틱 다듬기용)
        Dictionary<string, object> Kbm()
        {
            bool keyboard = false, mouse = false;
            var names = new List<string>();
            uint count = 0;
            uint size = (uint)Marshal.SizeOf(typeof(Native.RAWINPUTDEVICELIST));
            if (Native.GetRawInputDeviceList(null, ref count, size) == 0 && count > 0)
            {
                var list = new Native.RAWINPUTDEVICELIST[count];
                uint got = Native.GetRawInputDeviceList(list, ref count, size);
                if (got != uint.MaxValue)
                {
                    for (int i = 0; i < got; i++)
                    {
                        var dev = list[i];
                        if (dev.dwType != Native.RIM_TYPEKEYBOARD && dev.dwType != Native.RIM_TYPEMOUSE) continue;
                        var name = DeviceName(dev.hDevice);
                        bool fake = StatusRules.IsVirtualInputDevice(name);
                        names.Add((dev.dwType == Native.RIM_TYPEKEYBOARD ? "K " : "M ") + (fake ? "(제외) " : "") + name);
                        if (fake) continue;
                        if (dev.dwType == Native.RIM_TYPEKEYBOARD) keyboard = true;
                        else mouse = true;
                    }
                }
            }
            if (p.Settings.Diagnostics)
            {
                var joined = string.Join(" ; ", names);
                if (joined != lastDeviceNames)
                {
                    lastDeviceNames = joined;
                    Log.Info("diag", "입력 장치: " + joined);
                }
            }
            return new Dictionary<string, object> { { "keyboard", keyboard }, { "mouse", mouse } };
        }

        static string DeviceName(IntPtr h)
        {
            uint size = 0;
            Native.GetRawInputDeviceInfo(h, Native.RIDI_DEVICENAME, null, ref size);
            if (size == 0) return "";
            var sb = new StringBuilder((int)size + 1);
            size = (uint)sb.Capacity;
            return Native.GetRawInputDeviceInfo(h, Native.RIDI_DEVICENAME, sb, ref size) == uint.MaxValue ? "" : sb.ToString();
        }

        // ── 볼륨 (Core Audio) ──

        public static int? GetVolume()
        {
            IAudioEndpointVolume v = null;
            try
            {
                v = Endpoint();
                float level;
                if (v.GetMasterVolumeLevelScalar(out level) != 0) return null;
                return (int)Math.Round(level * 100);
            }
            catch (Exception e) { Log.Warn("volume", "읽기 실패: " + e.Message); return null; }
            finally { Release(v); }
        }

        // 막대를 빠르게 움직여도 마지막 값이 남도록: 작업마다 잠금 안에서 '지금 원하는 값'을 다시 읽음
        public void SetVolumeAsync(int value)
        {
            wantedVolume = Math.Max(0, Math.Min(100, value));
            Task.Run(() =>
            {
                lock (volumeGate)
                {
                    IAudioEndpointVolume v = null;
                    try
                    {
                        v = Endpoint();
                        var ctx = Guid.Empty;
                        Marshal.ThrowExceptionForHR(v.SetMasterVolumeLevelScalar(wantedVolume / 100f, ref ctx));
                    }
                    catch (Exception e) { Log.Error("volume", "바꾸기 실패", e); }
                    finally { Release(v); }
                }
                Kick();
            });
        }

        static IAudioEndpointVolume Endpoint()
        {
            var enumerator = (IMMDeviceEnumerator)new MMDeviceEnumeratorCom();
            try
            {
                IMMDevice device;
                Marshal.ThrowExceptionForHR(enumerator.GetDefaultAudioEndpoint(0 /* eRender */, 0 /* eConsole */, out device));
                try
                {
                    var iid = typeof(IAudioEndpointVolume).GUID;
                    object o;
                    Marshal.ThrowExceptionForHR(device.Activate(ref iid, 23 /* CLSCTX_ALL */, IntPtr.Zero, out o));
                    return (IAudioEndpointVolume)o;
                }
                finally { Release(device); }
            }
            finally { Release(enumerator); }
        }

        static void Release(object com)
        {
            try { if (com != null && Marshal.IsComObject(com)) Marshal.ReleaseComObject(com); } catch { }
        }

        // ── 전원 ──

        public void Power(string action)
        {
            if (action == "desktop")
            {
                p.HideHome("desktop");
                // 홈 뒤에 Playnite 창(전체화면 모드 포함)이 있으면 바탕화면이 안 보이므로 같이 내림
                var w = System.Windows.Application.Current.MainWindow;
                if (w != null && !(w is HomeWindow) && w.IsVisible) w.WindowState = System.Windows.WindowState.Minimized;
                return;
            }
            Log.Info("power", action + " (1초 뒤)");
            Task.Delay(1000).ContinueWith(_ =>
            {
                try
                {
                    switch (action)
                    {
                        case "screenOff":
                            screenOff = true;
                            // 응답 없는 창이 있어도 멈추지 않게 SendMessageTimeout
                            IntPtr result;
                            Native.SendMessageTimeout(Native.HWND_BROADCAST, Native.WM_SYSCOMMAND, (IntPtr)Native.SC_MONITORPOWER, (IntPtr)2, Native.SMTO_ABORTIFHUNG, 2000, out result);
                            break;
                        case "sleep":
                            Native.EnableShutdownPrivilege();
                            if (!Native.SetSuspendState(false, false, false)) Log.Error("power", "SetSuspendState 실패 " + Marshal.GetLastWin32Error());
                            break;
                        // /t 는 늘 0. 0보다 크면 /f(강제 종료)가 함께 걸림(다발 74)
                        case "restart": RunShutdown("/r /t 0"); break;
                        case "shutdown": RunShutdown("/s /t 0"); break;
                    }
                }
                catch (Exception e) { Log.Error("power", action + " 실패", e); }
            });
        }

        static void RunShutdown(string args)
        {
            var psi = new ProcessStartInfo(SystemExe("shutdown.exe"), args) { UseShellExecute = false, CreateNoWindow = true };
            using (Process.Start(psi)) { }
        }

        // 32비트 Playnite 는 System32 가 SysWOW64 로 돌려지므로 진짜 64비트 도구는 sysnative 로
        static string SystemExe(string name)
        {
            if (Environment.Is64BitOperatingSystem && !Environment.Is64BitProcess)
            {
                var native = Path.Combine(Environment.GetEnvironmentVariable("windir") ?? @"C:\Windows", "sysnative", name);
                if (File.Exists(native)) return native;
            }
            return Path.Combine(Environment.SystemDirectory, name);
        }

        // 화면을 끈 뒤 패드를 누르면 다시 켬. 패드 입력은 윈도우가 사용자 활동으로 안 볼 수 있어서
        // 움직이지 않는 마우스 이동 하나를 넣음 — 홈이 앞 창이고 게임이 없을 때만
        public void OnPadActivity()
        {
            if (!screenOff) return;
            screenOff = false;
            try
            {
                Native.SetThreadExecutionState(Native.ES_DISPLAY_REQUIRED);
                var home = p.Home;
                if (!p.GameActive && p.HomeVisible && home != null && home.IsForeground())
                    Native.SendInput(1, new[] { Native.Mouse(Native.MOUSEEVENTF_MOVE) }, Marshal.SizeOf(typeof(Native.INPUT)));
                Log.Info("power", "화면 다시 켬");
            }
            catch (Exception e) { Log.Error("power", "화면 켜기 실패", e); }
        }

        // ── Core Audio COM (mmdeviceapi.h · endpointvolume.h 의 vtable 순서) ──

        [ComImport, Guid("BCDE0395-E52F-467C-8E3D-C4579291692E")]
        class MMDeviceEnumeratorCom { }

        [ComImport, Guid("A95664D2-9614-4F35-A746-DE8DB63617E6"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
        interface IMMDeviceEnumerator
        {
            int EnumAudioEndpoints();   // 안 씀(자리만)
            [PreserveSig] int GetDefaultAudioEndpoint(int dataFlow, int role, out IMMDevice device);
        }

        [ComImport, Guid("D666063F-1587-4E43-81F1-B948E807363F"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
        interface IMMDevice
        {
            [PreserveSig] int Activate(ref Guid iid, int clsCtx, IntPtr activationParams, [MarshalAs(UnmanagedType.IUnknown)] out object instance);
        }

        [ComImport, Guid("5CDF2C82-841E-4546-9722-0CF74078229A"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
        interface IAudioEndpointVolume
        {
            int RegisterControlChangeNotify();     // 자리만
            int UnregisterControlChangeNotify();   // 자리만
            int GetChannelCount();                 // 자리만
            int SetMasterVolumeLevel();            // 자리만
            [PreserveSig] int SetMasterVolumeLevelScalar(float level, ref Guid eventContext);
            int GetMasterVolumeLevel();            // 자리만
            [PreserveSig] int GetMasterVolumeLevelScalar(out float level);
        }
    }

    // 웹 바로가기는 늘 Edge 로(홈 WebView2 에는 외부 웹을 띄우지 않음, 6.7)
    internal static class EdgeLauncher
    {
        static volatile string edgePath;

        // 시작할 때 한 번 찾아 둠. 이중 잠금 ①이 이 경로와 앞 창 프로세스 경로를 비교함
        public static void Resolve(OrvioSettings s)
        {
            edgePath = Find(s.EdgePath);
            Log.Info("web", "Edge: " + (edgePath ?? "찾지 못함"));
        }

        // 앞 창이 찾아 둔 Edge 설치 폴더의 msedge.exe 인가(파일 이름만 같은 다른 exe 는 아님)
        public static bool IsForegroundEdge()
        {
            var e = edgePath;
            return e != null && WebRules.IsEdgeProcess(Native.ForegroundProcessPath(), e);
        }

        // 화면에 Edge 창이 하나라도 있는가 (창을 닫아도 뒤에 남는 msedge.exe 프로세스는 세지 않음)
        public static bool HasEdgeWindow()
        {
            var e = edgePath;
            return e != null && Native.AnyVisibleWindowOf(path => WebRules.IsEdgeProcess(path, e));
        }

        public static bool Open(string url, OrvioSettings s)
        {
            try
            {
                var arg = WebRules.QuoteArg(WebRules.FinalUrl(url));
                if (arg == null) { Log.Warn("web", "열 수 없는 주소(http(s) 아님, 또는 \" \\ 포함)"); return false; }
                if (edgePath == null) Resolve(s);
                var exe = edgePath;
                if (exe == null) { Log.Error("web", "msedge.exe 를 찾지 못함"); return false; }
                var args = (s.EdgeArgs ?? "").Trim();
                args = (args.Length > 0 ? args + " " : "") + arg;
                using (Process.Start(new ProcessStartInfo(exe, args) { UseShellExecute = false })) { }
                Log.Info("web", "Edge 열기 " + WebRules.SchemeHost(url));
                return true;
            }
            catch (Exception e)
            {
                Log.Error("web", "Edge 열기 실패", e);
                return false;
            }
        }

        static string Find(string configured)
        {
            if (!string.IsNullOrWhiteSpace(configured))
            {
                // 이름은 Normalize 가 이미 msedge.exe 로 걸렀지만 한 번 더
                if (WebRules.IsEdgeExeName(configured) && Path.IsPathRooted(configured) && WebRules.IsDriveRooted(configured) && File.Exists(configured))
                    return Path.GetFullPath(configured);
                Log.Warn("web", "설정의 edgePath 를 쓸 수 없음, 자동으로 찾음");
            }
            foreach (var env in new[] { "ProgramFiles(x86)", "ProgramW6432", "ProgramFiles", "LOCALAPPDATA" })
            {
                var root = Environment.GetEnvironmentVariable(env);
                if (string.IsNullOrEmpty(root)) continue;
                var path = Path.Combine(root, "Microsoft", "Edge", "Application", "msedge.exe");
                if (File.Exists(path)) return path;
            }
            return null;
        }
    }
}
