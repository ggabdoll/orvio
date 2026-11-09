using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Threading;
using Microsoft.Win32;
using Orvio.Core;
using Playnite.SDK;
using Playnite.SDK.Data;
using Playnite.SDK.Events;
using Playnite.SDK.Plugins;

namespace Orvio
{
    public class OrvioPlugin : GenericPlugin
    {
        public const int ProtocolVersion = 1;

        public override Guid Id { get; } = Guid.Parse("a936dbac-b13c-47cc-9278-ae1a453fd946");

        internal readonly string DataDir;
        internal readonly string PluginDir;
        internal OrvioSettings Settings;
        internal volatile HomeWindow Home;   // 다른 스레드는 한 번 지역 변수로 읽어 씀
        internal LibraryService Library;
        internal XInputService Input;
        internal SystemService Sys;
        internal Diagnostics Diag;
        SoundService sound;

        // 다른 스레드(XInput·상태·진단)가 읽는 값. UI 스레드에서만 씀
        internal volatile bool HomeVisible;
        internal volatile bool GameActive;

        // 실행 상태 (UI 스레드 전용)
        Guid? launchingId;
        bool launchFromPage;
        DispatcherTimer launchTimer;
        bool hiddenDuringLaunch;   // 실행 중 화면이 포커스를 잃어 숨음 → 실패하면 다시 보임
        readonly HashSet<Guid> running = new HashSet<Guid>();

        // 화면 메시지 상태 검사와 거절 로그 비율 제한 (UI 스레드 전용)
        readonly MessageGate gate = new MessageGate();
        readonly Stopwatch clock = Stopwatch.StartNew();
        readonly RateLimiter rejectLog = new RateLimiter(5, 10);
        int rejectUnlogged;
        bool openingWeb;   // Edge 가 앞으로 오기를 기다리는 중: 이때는 홈을 다시 앞으로 끌지 않음
        DispatcherTimer webWatch;   // Edge 로 나가 홈이 숨은 동안 창이 모두 닫혔는지 봄
        bool startLogged;

        public OrvioPlugin(IPlayniteAPI api) : base(api)
        {
            Properties = new GenericPluginProperties { HasSettings = false };
            DataDir = GetPluginUserDataPath();
            PluginDir = Path.GetDirectoryName(typeof(OrvioPlugin).Assembly.Location);
            Log.Init(DataDir);
            LoadSettings();
        }

        internal Dispatcher UiDispatcher
        {
            get
            {
                try { return PlayniteApi.MainView.UIDispatcher ?? Application.Current.Dispatcher; }
                catch { return Application.Current != null ? Application.Current.Dispatcher : null; }
            }
        }

        // UI 스레드로 넘겨 실행. 어떤 예외도 Playnite 로 새지 않게
        internal void Ui(Action a, string what)
        {
            try
            {
                var d = UiDispatcher;
                if (d == null) { Log.Warn("ui", "디스패처 없음: " + what); return; }
                d.BeginInvoke(new Action(() =>
                {
                    try { a(); }
                    catch (Exception e) { Log.Error("ui", what + " 실패", e); }
                }));
            }
            catch (Exception e) { Log.Error("ui", what + " 넘기기 실패", e); }
        }

        internal T UiInvoke<T>(Func<T> f, T fallback)
        {
            try
            {
                var d = UiDispatcher;
                if (d == null) return fallback;
                var r = d.Invoke(f, DispatcherPriority.Normal, CancellationToken.None, TimeSpan.FromSeconds(2));
                return r == null ? fallback : r;   // 시간 초과면 기본값이 옴
            }
            catch { return fallback; }
        }

        // ── 시작과 끝 ──

        public override void OnApplicationStarted(OnApplicationStartedEventArgs args)
        {
            Ui(StartHome, "시작");
        }

        void StartHome()
        {
            var runtime = HomeWindow.PrepareRuntime(PluginDir);
            Log.Info("start", string.Format("ORVIO {0} · Playnite {1} ({2}) · WebView2 {3} · {4}비트",
                typeof(OrvioPlugin).Assembly.GetName().Version, PlayniteApi.ApplicationInfo.ApplicationVersion,
                PlayniteApi.ApplicationInfo.Mode, runtime ?? "없음", Environment.Is64BitProcess ? 64 : 32));
            Log.Info("start", "ORVIO 시작: " + StartupElapsed());

            EdgeLauncher.Resolve(Settings);
            launchTimer = new DispatcherTimer();
            launchTimer.Tick += (s, e) =>
            {
                try { OnLaunchTimeout(); }
                catch (Exception ex) { Log.Error("launch", "시간 초과 처리 실패", ex); }
            };

            Library = new LibraryService(this);
            Sys = new SystemService(this);
            sound = new SoundService(PlayniteApi.Paths.ApplicationPath, DataDir, Settings.SfxVolume);
            sound.Wake();
            Input = new XInputService(this);
            Diag = new Diagnostics(this);
            Home = new HomeWindow(this);

            PlayniteApi.UriHandler.RegisterSource("orvio", OnUri);
            SystemEvents.PowerModeChanged += OnPowerModeChanged;

            Input.OnShown();
            HomeVisible = true;
            Home.Show();
            Home.Start(runtime != null);
            Home.ShowAndFocus();
            CheckForeground();
            if (runtime == null)
                PlayniteApi.Notifications.Add("orvio-webview2", "ORVIO: WebView2 런타임이 없어 홈 화면을 띄우지 못했습니다.", NotificationType.Error);
        }

        // NFR-04 측정: 로그인(탐색기 시작)과 Playnite 시작에서 몇 초 지났나
        static string StartupElapsed()
        {
            try
            {
                var now = DateTime.Now;
                var explorer = Process.GetProcessesByName("explorer");
                var login = explorer.Min(x => x.StartTime);
                foreach (var x in explorer) x.Dispose();
                using (var me = Process.GetCurrentProcess())
                    return string.Format("로그인 후 {0:0.0}초 · Playnite 시작 후 {1:0.0}초", (now - login).TotalSeconds, (now - me.StartTime).TotalSeconds);
            }
            catch (Exception e) { return "재지 못함 " + e.Message; }
        }

        public override void OnApplicationStopped(OnApplicationStoppedEventArgs args)
        {
            try
            {
                SystemEvents.PowerModeChanged -= OnPowerModeChanged;
                if (Input != null) Input.Dispose();
                if (Sys != null) Sys.Dispose();
                if (Diag != null) Diag.Dispose();
                if (Library != null) Library.Dispose();
                var d = UiDispatcher;
                if (Home != null && d != null)
                {
                    d.Invoke(new Action(() => { if (webWatch != null) webWatch.Stop(); Home.AllowClose = true; Home.Close(); }));
                }
                Log.Info("stop", "Playnite 종료");
            }
            catch (Exception e) { Log.Error("stop", "정리 실패", e); }
        }

        void OnUri(PlayniteUriEventArgs args)
        {
            Ui(() =>
            {
                var a = args.Arguments;
                // 게임 중에도 받음: SCR-01 '게임 실행 중' 상태이자 K-10(이른 종료 신호 못 받음)·D-13 의 탈출구.
                // 게임 중 전원은 MessageGate 가 막음
                if (a != null && a.Length > 0 && a[0] == "show") ShowHome("shortcut");
                else Log.Warn("uri", "모르는 명령 " + (a == null ? "(없음)" : string.Join("/", a)));
            }, "uri");
        }

        void OnPowerModeChanged(object sender, PowerModeChangedEventArgs e)
        {
            if (e.Mode != PowerModes.Resume) return;
            Ui(() =>
            {
                Log.Info("power", "절전에서 깨어남");
                sound.Sleep();   // 절전 중 끊긴 소리 출력을 아래 ShowHome 의 Wake 가 다시 열게
                if (HomeVisible) ShowHome("wake");
            }, "wake");
        }

        // ── 창 보이기·숨기기 ──

        internal void ShowHome(string reason)
        {
            if (Home == null) return;
            if (webWatch != null) webWatch.Stop();
            Log.Info("home", "보이기 " + reason);
            sound.Wake();
            hiddenDuringLaunch = false;
            Input.OnShown();   // HomeVisible 보다 먼저: 눌려 있던 버튼이 한 번도 새지 않게
            HomeVisible = true;
            Home.ShowAndFocus();
            Library.FlushQueued();
            Send("resume", new Dictionary<string, object> { { "reason", reason } });
            Sys.Kick();
            CheckForeground();
        }

        internal void HideHome(string reason)
        {
            if (Home == null || !HomeVisible) return;
            Log.Info("home", "숨기기 " + reason);
            Send("pause");
            HomeVisible = false;
            Home.Hide();
            sound.Sleep();
        }

        // 보인 뒤 +200ms 에 앞 창이 아니면 한 번 더 시도(K-3). 진단이 켜져 있으면 +1000ms 도 기록
        async void CheckForeground()
        {
            try
            {
                await Task.Delay(200);
                if (!HomeVisible) return;
                bool fg = Home.IsForeground();
                if (!fg)
                {
                    Log.Warn("focus", "+200ms 앞 창 아님(앞 창=" + Native.ForegroundProcessName() + ") → 다시 시도");
                    Home.ShowAndFocus();
                }
                else if (Settings.Diagnostics) Log.Info("diag", "+200ms 앞 창=예");
                await Task.Delay(800);
                if (Settings.Diagnostics && HomeVisible)
                    Log.Info("diag", "+1000ms 앞 창=" + (Home.IsForeground() ? "예" : "아니오(" + Native.ForegroundProcessName() + ")"));
            }
            catch (Exception e) { Log.Error("focus", "확인 실패", e); }
        }

        // WebView2 가 1분에 5번 넘게 죽음: 바탕화면으로 비키고 알림. 다음 보이기에서 HomeWindow 가 새로 만듦
        internal void OnHomeBroken()
        {
            HideHome("webviewFailed");
            try { PlayniteApi.Notifications.Add("orvio-webview2-failed", "ORVIO: 홈 화면이 계속 멈춰 숨겼습니다. 컨트롤러 Menu 길게나 바로가기로 다시 열 수 있습니다.", NotificationType.Error); }
            catch (Exception e) { Log.Error("home", "알림 실패", e); }
        }

        // 실행 중·게임 중에 홈이 포커스를 잃으면 숨김(기획서 6.4 '게임이 홈 뒤에서 뜸')
        internal void OnHomeDeactivated()
        {
            if (!HomeVisible || !GameActive) return;
            if (launchingId != null && launchFromPage) hiddenDuringLaunch = true;
            HideHome("lostFocus");
        }

        // ── 화면으로 보내기 (UI 스레드) ──

        // 보냈으면 true
        internal bool Send(string type, Dictionary<string, object> fields = null)
        {
            if (Home == null || !Home.PageReady) return false;
            var m = new Dictionary<string, object> { { "type", type } };
            if (fields != null) foreach (var kv in fields) m[kv.Key] = kv.Value;
            return Home.PostJson(Json.Write(m));
        }

        // pad·status 용. 숨은 뒤(pause 뒤)에는 버림
        internal void PostRaw(string json)
        {
            if (Home != null && HomeVisible) Home.PostJson(json);
        }

        void SendGameState(Guid id, string state, string reason = null, ulong? elapsed = null)
        {
            var f = new Dictionary<string, object> { { "id", id.ToString("D") }, { "state", state } };
            if (reason != null) f["reason"] = reason;
            if (elapsed != null) f["elapsedSec"] = elapsed.Value;
            Log.Info("game", "gameState " + state + (reason != null ? " " + reason : "") + " " + id);
            Send("gameState", f);
        }

        // ── 화면에서 온 메시지 (WebView2 이벤트 = UI 스레드) ──

        internal void OnWebMessage(string json)
        {
            // 전체 비율(초당 약 50개)은 해석보다 먼저
            if (!gate.AllowAny(clock.ElapsedMilliseconds))
            {
                LogReject("거절: 초당 " + MessageGate.MaxPerSecond + "개 넘음");
                return;
            }
            string err;
            var c = Messages.Parse(json, out err);
            if (c == null)
            {
                LogReject("거절: " + err + " | " + (json != null && json.Length > 200 ? json.Substring(0, 200) + "…" : json));
                return;
            }
            // 상태 검사: ready·log 말고는 홈이 보이고 앞 창일 때만. 게임 중 전원, 비율, 전원 명령의 최근 입력
            var ctx = new GateContext
            {
                HomeVisible = HomeVisible,
                HomeForeground = Home != null && Home.IsForeground(),
                GameActive = GameActive,
                PageReady = Home != null && Home.PageReady,
                NowMs = clock.ElapsedMilliseconds,
                MsSinceUserInput = MsSinceUserInput(),
            };
            var why = gate.Check(c, ctx);
            if (why != null)
            {
                if (c.Type == "sfx") return;   // 소리는 조용히 버림: 거절이 홈을 앞으로 끌어와 뒤따르는 실행 명령을 통과시키면 안 됨
                if (why != MessageGate.LogDropped) LogReject(c.Type + " 거절: " + why);
                // 보이는데 앞 창이 아니라서 거절됐다면 조용히 끝내지 않고 다시 앞으로(초당 1번)
                if (ctx.HomeVisible && !ctx.HomeForeground) RefocusIfBehind("message");
                return;
            }
            if (c.Type == "log")
            {
                int dropped = gate.TakeDroppedLogs();
                if (dropped > 0) Log.Warn("bridge", "화면 log " + dropped + "개 버림(초당 " + MessageGate.MaxLogPerSecond + "개 넘음)");
            }
            switch (c.Type)
            {
                case "ready": OnPageReady(c.Version); break;
                case "launch": Launch(c.GameId); break;
                case "openWeb": OpenWeb(c); break;
                case "power": Sys.Power(c.Action); break;
                case "volume": Sys.SetVolumeAsync(c.Volume); break;
                case "setSetting": SetSetting(c); break;
                case "log": Log.Write(c.Level, "web", c.Msg, null); break;
                case "sfx": sound.Play(c.Sound); break;
            }
        }

        // 홈이 보이는데 앞 창이 아님: 다시 앞으로(초당 1번, 로그). 화면 메시지 거절과 패드 변화(XInputService)가 부름
        internal void RefocusIfBehind(string why)
        {
            // 게임이 켜지는 중·실행 중이면 절대 앞으로 끌지 않음(게임 창의 포커스를 뺏지 않게)
            if (Home == null || openingWeb || GameActive) return;
            var ctx = new GateContext { HomeVisible = HomeVisible, HomeForeground = Home.IsForeground(), GameActive = GameActive, NowMs = clock.ElapsedMilliseconds };
            if (!gate.WantsRefocus(ctx)) return;
            Log.Info("focus", "홈이 보이는데 앞 창 아님(" + why + ", 앞 창=" + Native.ForegroundProcessName() + ") → 다시 앞으로");
            Home.ShowAndFocus();
        }

        // 거절 로그도 비율 제한. 건너뛴 수는 다음 줄에 붙임
        void LogReject(string msg)
        {
            if (!rejectLog.Allow(clock.ElapsedMilliseconds)) { rejectUnlogged++; return; }
            if (rejectUnlogged > 0) { msg += " (그사이 " + rejectUnlogged + "건 더 거절)"; rejectUnlogged = 0; }
            Log.Warn("bridge", msg);
        }

        // 패드(XInput 매핑 값 변화)와 키보드·마우스(GetLastInputInfo) 중 더 최근 것. 모르면 -1
        long MsSinceUserInput()
        {
            long pad = Input != null ? Input.MsSinceInput() : -1;
            long kbm = -1;
            try { kbm = Native.MsSinceLastInput(); } catch (Exception e) { Log.Warn("bridge", "GetLastInputInfo 실패 " + e.Message); }
            if (pad < 0) return kbm;
            if (kbm < 0) return pad;
            return Math.Min(pad, kbm);
        }

        void OnPageReady(string version)
        {
            Home.PageReady = true;
            Log.Info("bridge", "ready 화면 판 " + (version ?? "(없음)"));
            if (version != ProtocolVersion.ToString()) Log.Warn("bridge", "메시지 판이 다름: 화면 " + (version ?? "(없음)") + " · 확장 " + ProtocolVersion);

            bool sent = Send("init", new Dictionary<string, object>
            {
                { "version", ProtocolVersion },
                { "settings", Settings.ToInitPayload() },
                { "inputMode", Settings.InputMode },
            });
            if (!sent)
            {
                // 준비 안 된 것으로 돌려 두면 화면이 5초마다 보내는 ready 로 다시 시작함
                Home.PageReady = false;
                Log.Warn("bridge", "init 보내기 실패, 화면의 다음 ready 를 기다림");
                return;
            }
            Sys.ResetStatus();
            if (!HomeVisible) Send("pause");

            Library.SendFull(() =>
            {
                if (!startLogged) { startLogged = true; Log.Info("start", "화면 준비 끝: " + StartupElapsed()); }
                // init·library 뒤, 켜지는 중·실행 중인 게임의 상태를 한 번 더(다시 만든 화면도 '게임 실행 중' 줄과 전원 막기를 이어가게)
                if (running.Count > 0) SendGameState(running.First(), "started");
                else if (launchingId != null) SendGameState(launchingId.Value, "launching");
                if (HomeVisible)
                {
                    Home.ShowAndFocus();
                    Input.OnShown();
                    Send("resume", new Dictionary<string, object> { { "reason", "start" } });
                    CheckForeground();
                }
            });
        }

        void OpenWeb(WebCommand c)
        {
            var url = c.WebId != null ? Settings.UrlFor(c.WebId) : Settings.SearchUrl(c.Query);
            // 실패하면 2초 간격을 지우고 토스트: 바로 다시 시도할 수 있고, 화면의 '여는 중' 겹침도 닫힘
            if (url == null) { Log.Warn("web", "모르는 바로가기 " + c.WebId); OpenWebFailed(); return; }
            if (!EdgeLauncher.Open(url, Settings)) { OpenWebFailed(); return; }
            // 먼저 숨기면 앞 창 권한이 다른 창으로 넘어가 Edge 가 뒤에 뜰 수 있음. Edge 가 앞에 오면 숨김(5초까지)
            int tries = 0;
            var wait = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(100) };
            wait.Tick += (s, e) =>
            {
                try
                {
                    bool edge = EdgeLauncher.IsForegroundEdge();
                    if (!edge && ++tries < 50 && HomeVisible) return;
                    wait.Stop();
                    openingWeb = false;
                    if (!edge) Log.Warn("web", "5초 안에 Edge 가 앞으로 오지 않음");
                    HideHome("web");
                    WatchWebClosed();
                }
                catch (Exception ex) { wait.Stop(); openingWeb = false; Log.Error("web", "Edge 기다리기 실패", ex); }
            };
            wait.Start();
            openingWeb = true;   // 타이머가 실제로 돈 다음에만(시작이 실패해 표시만 남는 일이 없게)
        }

        // Edge 창을 모두 닫으면 홈으로. 다른 창을 쓰던 중에 닫혔으면(작업 표시줄에서 닫기 등) 그 앞을 가리지 않고 그만둠
        void WatchWebClosed()
        {
            if (webWatch != null) webWatch.Stop();
            var watch = new WebCloseWatch();
            bool edgeFront = true;   // 창을 한 번도 못 봤으면 브라우저가 안 뜬 것: 홈으로 돌아옴
            var t = new DispatcherTimer { Interval = TimeSpan.FromSeconds(1) };
            webWatch = t;
            t.Tick += (s, e) =>
            {
                try
                {
                    if (GameActive) { t.Stop(); return; }
                    bool open = EdgeLauncher.HasEdgeWindow();
                    if (open) edgeFront = EdgeLauncher.IsForegroundEdge();
                    if (!watch.Closed(open)) return;
                    t.Stop();
                    Log.Info("web", "Edge 창이 모두 닫힘" + (edgeFront ? "" : " (다른 창을 쓰는 중이라 홈은 부르지 않음)"));
                    if (edgeFront) ShowHome("webClosed");
                }
                catch (Exception ex) { t.Stop(); Log.Error("web", "Edge 닫힘 확인 실패", ex); }
            };
            t.Start();
        }

        void OpenWebFailed()
        {
            gate.ForgetOpenWeb();
            Send("toast", new Dictionary<string, object> { { "text", "브라우저를 열지 못했어요" } });
        }

        // 값이 실제로 바뀔 때만 저장
        void SetSetting(WebCommand c)
        {
            bool changed = false;
            if (c.Key == "theme" && Settings.Theme != c.Theme) { Settings.Theme = c.Theme; changed = true; }
            else if (c.Key == "idle" && Settings.Idle != c.Idle) { Settings.Idle = c.Idle; changed = true; }
            if (!changed) return;
            SaveSettings();
            Log.Info("settings", c.Key + " 바꿈");
        }

        // ── 실행 ──

        void UpdateActive()
        {
            GameActive = launchingId != null || running.Count > 0;
        }

        void Launch(Guid id)
        {
            var g = PlayniteApi.Database.Games.Get(id);
            if (g == null || !g.IsInstalled || g.Hidden)
            {
                // 화면은 launch 를 보내며 '실행 중'으로 들어가므로 풀어 줌
                Log.Warn("launch", "라이브러리에 없는 ID 거절 " + id);
                SendGameState(id, "failed", "error");
                return;
            }
            if (GameActive || PlayniteApi.Database.Games.Any(x => x.IsRunning || x.IsLaunching))
            {
                SendGameState(id, "failed", "busy");
                return;
            }
            launchingId = id;
            launchFromPage = true;
            UpdateActive();
            StartLaunchTimer();
            SendGameState(id, "launching");
            try
            {
                Log.Info("launch", "StartGame " + g.Name);
                PlayniteApi.StartGame(id);
            }
            catch (Exception e)
            {
                Log.Error("launch", "StartGame 실패", e);
                EndLaunch();
                SendGameState(id, "failed", "error");
            }
        }

        void StartLaunchTimer()
        {
            launchTimer.Stop();
            launchTimer.Interval = TimeSpan.FromSeconds(Settings.LaunchTimeoutSec);
            launchTimer.Start();
        }

        void EndLaunch()
        {
            if (launchTimer != null) launchTimer.Stop();
            launchingId = null;
            launchFromPage = false;
            UpdateActive();
        }

        // StartGame 은 결과를 돌려주지 않으므로 실패는 시간 초과로만 앎
        void OnLaunchTimeout()
        {
            launchTimer.Stop();
            if (launchingId == null) return;
            var id = launchingId.Value;
            bool fromPage = launchFromPage;
            EndLaunch();
            Log.Warn("launch", Settings.LaunchTimeoutSec + "초 안에 시작 안 됨 " + id);
            if (!fromPage) return;
            BackAfterFailedLaunch();
            SendGameState(id, "failed", "timeout");
        }

        // 실행 중 화면이 포커스를 잃어 숨었다면 실패를 알리기 전에 홈을 다시 보임(안 그러면 TV에서 돌아올 길이 없음)
        void BackAfterFailedLaunch()
        {
            if (hiddenDuringLaunch && !HomeVisible) ShowHome("gameStopped");
            hiddenDuringLaunch = false;
        }

        public override void OnGameStarting(OnGameStartingEventArgs args)
        {
            var g = args.Game;
            Ui(() =>
            {
                Log.Info("game", "starting " + g.Name);
                // Playnite 에서 직접 켠 게임도 '실행 중'으로 셈(이중 잠금·Menu 길게)
                if (launchingId == null)
                {
                    launchingId = g.Id;
                    launchFromPage = false;
                    UpdateActive();
                    StartLaunchTimer();
                }
            }, "OnGameStarting");
        }

        public override void OnGameStartupCancelled(OnGameStartupCancelledEventArgs args)
        {
            var g = args.Game;
            Ui(() =>
            {
                Log.Info("game", "startup cancelled " + g.Name);
                if (launchingId != g.Id) return;
                bool fromPage = launchFromPage;
                EndLaunch();
                if (!fromPage) return;
                BackAfterFailedLaunch();
                SendGameState(g.Id, "failed", "error");
            }, "OnGameStartupCancelled");
        }

        public override void OnGameStarted(OnGameStartedEventArgs args)
        {
            var g = args.Game;
            var pid = args.StartedProcessId;
            Ui(() =>
            {
                Log.Info("game", "started " + g.Name + " pid " + pid);
                if (launchingId == g.Id) EndLaunch();
                hiddenDuringLaunch = false;
                running.Add(g.Id);
                UpdateActive();
                SendGameState(g.Id, "started");
                HideHome("gameStarted");
            }, "OnGameStarted");
        }

        public override void OnGameStopped(OnGameStoppedEventArgs args)
        {
            var g = args.Game;
            var sec = args.ElapsedSeconds;
            Ui(() =>
            {
                running.Remove(g.Id);
                if (launchingId == g.Id) EndLaunch();
                UpdateActive();
                SendGameState(g.Id, "stopped", null, sec);
                // 런처형 게임은 런처가 끝날 때 신호가 일찍 옴(K-10). 홈을 앞으로 끌지 않음
                if (sec < (ulong)Settings.EarlyStopSec)
                {
                    Log.Info("game", "early stop " + g.Name + " " + sec + "초 < " + Settings.EarlyStopSec + "초, 홈은 그대로");
                    return;
                }
                Log.Info("game", "stopped " + g.Name + " " + sec + "초");
                if (running.Count > 0) return;
                Library.RefreshNow(g.Id, () => ShowHome("gameStopped"));
            }, "OnGameStopped");
        }

        public override void OnLibraryUpdated(OnLibraryUpdatedEventArgs args)
        {
            Ui(() =>
            {
                Log.Info("library", "라이브러리 갱신");
                if (Library != null) Library.MarkAll();
            }, "OnLibraryUpdated");
        }

        // ── 설정 ──

        string SettingsPath { get { return Path.Combine(DataDir, "settings.json"); } }

        void LoadSettings()
        {
            OrvioSettings s = null;
            try
            {
                Exception err;
                if (File.Exists(SettingsPath) && !Serialization.TryFromJsonFile(SettingsPath, out s, out err))
                {
                    Log.Error("settings", "settings.json 을 읽지 못해 기본값을 씀. settings.bad.json 으로 복사", err);
                    File.Copy(SettingsPath, Path.Combine(DataDir, "settings.bad.json"), true);
                    s = null;
                }
            }
            catch (Exception e) { Log.Error("settings", "읽기 실패", e); s = null; }

            bool fresh = !File.Exists(SettingsPath);
            Settings = s ?? new OrvioSettings();
            Settings.Normalize();
            if (fresh) SaveSettings();
        }

        internal void SaveSettings()
        {
            try { WriteAtomic(SettingsPath, Serialization.ToJson(Settings, true)); }
            catch (Exception e) { Log.Error("settings", "저장 실패", e); }
        }

        // 임시 파일에 쓰고 바꿔 끼움. 대상이 있으면 File.Replace(한 번에 바뀜), 없으면 Move
        internal static void WriteAtomic(string path, string text)
        {
            var tmp = path + ".tmp";
            File.WriteAllText(tmp, text, Encoding.UTF8);
            if (File.Exists(path)) File.Replace(tmp, path, null);
            else File.Move(tmp, path);
        }
    }
}
