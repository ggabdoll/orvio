using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using Microsoft.Web.WebView2.Core;
using Microsoft.Web.WebView2.Wpf;
using Orvio.Core;

namespace Orvio
{
    // 테두리 없는 전체화면 창 + WebView2. XAML 없이 코드로만
    // 지킬 것(기획서 6.1): Owner 없음 · AllowsTransparency 없음 · Topmost 는 잠깐만 · 숨길 때 Hide
    internal sealed class HomeWindow : Window
    {
        public const string AppHost = "orvio-app.example";
        public const string CoverHost = "orvio-covers.example";
        const string StartUrl = "https://orvio-app.example/index.html";

        readonly OrvioPlugin p;
        WebView2 web;
        CoreWebView2Environment env;
        readonly List<DateTime> failures = new List<DateTime>();
        bool broken;   // 1분에 5번 넘게 죽음 → 숨기고, 다음에 보일 때 새로 만듦
        bool recovering;   // 복구가 도는 중이면 겹쳐 들어온 신호는 버림
        bool recoverAgain; // 복구 중에 브라우저 프로세스가 또 죽음 → 끝나고 한 번 더 새로 만듦
        DateTime lastReload = DateTime.MinValue;
        // 요청 막기 통계(진단)와 막은 요청 로그 비율 제한
        readonly Stopwatch clock = Stopwatch.StartNew();
        readonly RateLimiter blockedLog = new RateLimiter(2, 5);
        int allowedRequests, blockedRequests, blockedUnlogged;
        bool firstLoadLogged;

        public bool PageReady { get; set; }
        public bool AllowClose { get; set; }
        public IntPtr Hwnd { get; private set; }

        public HomeWindow(OrvioPlugin p)
        {
            this.p = p;
            Title = "ORVIO";
            WindowStyle = WindowStyle.None;
            ResizeMode = ResizeMode.NoResize;
            Background = Brushes.Black;
            WindowStartupLocation = WindowStartupLocation.Manual;
            // 주 모니터에 놓고 최대화
            Left = 0;
            Top = 0;
            Width = SystemParameters.PrimaryScreenWidth;
            Height = SystemParameters.PrimaryScreenHeight;
            WindowState = WindowState.Maximized;
            try { Icon = BitmapFrame.Create(new Uri(Path.Combine(p.PluginDir, "icon.png"))); } catch { }

            SourceInitialized += (s, e) => Hwnd = new WindowInteropHelper(this).Handle;
            Deactivated += (s, e) => Safe(p.OnHomeDeactivated, "deactivated");
            // 윈도우 로그오프·종료를 이 창이 막지 않게
            if (Application.Current != null) Application.Current.SessionEnding += (s, e) => AllowClose = true;
            Closing += (s, e) =>
            {
                // Alt+F4 등으로 닫히면 홈을 잃으므로 숨기기로 바꿈
                if (AllowClose) return;
                e.Cancel = true;
                Safe(() => p.HideHome("close"), "closing");
            };
        }

        static void Safe(Action a, string what)
        {
            try { a(); } catch (Exception e) { Log.Error("window", what + " 실패", e); }
        }

        // 로더 DLL 폴더를 프로세스 비트에 맞춰 지정하고 런타임 판을 돌려줌. 없으면 null
        public static string PrepareRuntime(string pluginDir)
        {
            try
            {
                var dir = Path.Combine(pluginDir, "runtimes", Environment.Is64BitProcess ? "win-x64" : "win-x86", "native");
                if (Directory.Exists(dir)) CoreWebView2Environment.SetLoaderDllFolderPath(dir);
            }
            catch (Exception e) { Log.Warn("web", "로더 경로 지정 못 함: " + e.Message); }
            try { return CoreWebView2Environment.GetAvailableBrowserVersionString(); }
            catch (Exception e) { Log.Error("web", "WebView2 런타임 없음", e); return null; }
        }

        public async void Start(bool runtimeOk)
        {
            if (!runtimeOk) { ShowMessage("WebView2 런타임이 필요합니다.\nMicrosoft Edge WebView2 런타임을 설치한 뒤 Playnite 를 다시 켜 주세요."); return; }
            try
            {
                env = await CoreWebView2Environment.CreateAsync(null, Path.Combine(p.DataDir, "webview2"));
                await CreateView();
            }
            catch (Exception e)
            {
                Log.Error("web", "WebView2 시작 실패", e);
                ShowMessage("홈 화면을 띄우지 못했습니다.\n로그: " + Path.Combine(p.DataDir, "orvio.log"));
            }
        }

        async Task CreateView()
        {
            PageReady = false;
            var old = web;
            var view = new WebView2 { DefaultBackgroundColor = System.Drawing.Color.Black };
            web = view;
            Content = view;
            if (old != null) { try { old.Dispose(); } catch { } }

            await view.EnsureCoreWebView2Async(env);
            if (web != view) return;   // 기다리는 사이 다시 만들어짐
            var core = view.CoreWebView2;
            allowedRequests = blockedRequests = blockedUnlogged = 0;
            firstLoadLogged = false;
            var s = core.Settings;
            bool dev = p.Settings.DevMode;
            s.AreDefaultContextMenusEnabled = false;
            s.IsZoomControlEnabled = false;
            s.IsPinchZoomEnabled = false;
            s.AreBrowserAcceleratorKeysEnabled = dev;   // F5·F12·Ctrl+휠 등. 개발 모드에서만
            s.IsStatusBarEnabled = false;
            s.AreDevToolsEnabled = dev;
            s.AreDefaultScriptDialogsEnabled = false;
            s.AreHostObjectsAllowed = false;
            s.IsWebMessageEnabled = true;
            s.IsSwipeNavigationEnabled = false;
            s.IsGeneralAutofillEnabled = false;
            s.IsPasswordAutosaveEnabled = false;

            var webDir = Path.Combine(p.PluginDir, "web");
            if (!File.Exists(Path.Combine(webDir, "index.html"))) Log.Error("web", "화면 파일 없음: " + webDir);
            Directory.CreateDirectory(webDir);
            core.SetVirtualHostNameToFolderMapping(AppHost, webDir, CoreWebView2HostResourceAccessKind.Deny);
            // 표지는 다른 출처(orvio-covers)라 Deny 면 <img src> 도 막힘(SDK 문서 표). img 는 되고 fetch/XHR 은 막는 DenyCors
            core.SetVirtualHostNameToFolderMapping(CoverHost, p.Library.CoversDir, CoreWebView2HostResourceAccessKind.DenyCors);

            core.NavigationStarting += (snd, e) => Safe(() => OnNavigationStarting(e, true), "navigation");
            core.FrameNavigationStarting += (snd, e) => Safe(() => OnNavigationStarting(e, false), "frameNavigation");
            // 두 가상 호스트 밖으로 나가는 요청은 403. 허용한 요청은 손대지 않아 가상 호스트 매핑이 응답함
            // 주의: 필터("*")와 SetVirtualHostNameToFolderMapping 이 함께 잘 도는지는 윈도우에서 확인해야 함
            //       → 첫 화면 로드 뒤 '요청 허용 N · 막음 M' 진단 줄로 봄(허용 0 이거나 화면이 안 뜨면 충돌)
            AddRequestFilter(core);
            core.WebResourceRequested += (snd, e) => Safe(() => OnResourceRequested(e), "resourceRequested");
            core.PermissionRequested += (snd, e) =>
            {
                e.State = CoreWebView2PermissionState.Deny;
                Log.Warn("web", "권한 요청 거절 " + e.PermissionKind + " " + WebRules.SchemeHost(e.Uri));
            };
            core.DownloadStarting += (snd, e) =>
            {
                e.Cancel = true;
                Log.Warn("web", "다운로드 막음 " + WebRules.SchemeHost(e.DownloadOperation != null ? e.DownloadOperation.Uri : null));
            };
            core.NavigationCompleted += (snd, e) => Safe(() => OnFirstLoad(e.IsSuccess, e.WebErrorStatus.ToString()), "navigationCompleted");
            core.NewWindowRequested += (snd, e) =>
            {
                e.Handled = true;
                Log.Warn("web", "새 창 막음 " + Short(e.Uri));
            };
            core.ProcessFailed += (snd, e) => Safe(() => OnProcessFailed(e), "processFailed");
            core.WebMessageReceived += (snd, e) => Safe(() =>
            {
                if (!WebRules.IsAppUrl(e.Source))
                {
                    Log.Warn("bridge", "출처 거절 " + Short(e.Source));
                    return;
                }
                p.OnWebMessage(e.WebMessageAsJson);
            }, "webMessage");

            core.Navigate(StartUrl);
            Log.Info("web", "화면 여는 중 " + StartUrl);
        }

        // 인자 3개(모든 요청 출처)가 먼저. 오래된 런타임이 못 받으면 2개(사용 중단 표시, iframe 에서 덜 맞음)로
        static void AddRequestFilter(CoreWebView2 core)
        {
            try
            {
                core.AddWebResourceRequestedFilter("*", CoreWebView2WebResourceContext.All, CoreWebView2WebResourceRequestSourceKinds.All);
                Log.Info("web", "요청 필터: 인자 3개(모든 요청 출처)");
                return;
            }
            catch (Exception e) { Log.Warn("web", "요청 필터 인자 3개 실패(" + e.Message + "), 인자 2개로"); }
            try
            {
#pragma warning disable CS0618, CS0612
                core.AddWebResourceRequestedFilter("*", CoreWebView2WebResourceContext.All);
#pragma warning restore CS0618, CS0612
                Log.Info("web", "요청 필터: 인자 2개");
            }
            catch (Exception e) { Log.Error("web", "요청 필터를 걸지 못함(탐색 잠금만 남음)", e); }
        }

        void OnResourceRequested(CoreWebView2WebResourceRequestedEventArgs e)
        {
            var uri = e.Request.Uri;
            if (WebRules.IsAllowedRequest(uri)) { allowedRequests++; return; }
            blockedRequests++;
            e.Response = env.CreateWebResourceResponse(new MemoryStream(), 403, "Forbidden", "");
            if (!blockedLog.Allow(clock.ElapsedMilliseconds)) { blockedUnlogged++; return; }
            Log.Warn("web", "요청 막음 " + WebRules.SchemeHost(uri) + (blockedUnlogged > 0 ? " (그사이 " + blockedUnlogged + "건 더)" : ""));
            blockedUnlogged = 0;
        }

        // 진단: 첫 화면 로드 직후와 10초 뒤(표지까지) 허용·막은 요청 수
        void OnFirstLoad(bool ok, string status)
        {
            if (firstLoadLogged) return;
            firstLoadLogged = true;
            if (!p.Settings.Diagnostics) return;
            Log.Info("diag", "첫 화면 로드 " + (ok ? "성공" : "실패 " + status) + " · 요청 허용 " + allowedRequests + " · 막음 " + blockedRequests);
            var later = new DispatcherTimer { Interval = TimeSpan.FromSeconds(10) };
            later.Tick += (s, e) =>
            {
                later.Stop();
                Log.Info("diag", "로드 10초 뒤 · 요청 허용 " + allowedRequests + " · 막음 " + blockedRequests);
            };
            later.Start();
        }

        void OnNavigationStarting(CoreWebView2NavigationStartingEventArgs e, bool topLevel)
        {
            if (WebRules.IsAppUrl(e.Uri))
            {
                // 새 탐색(새로 고침 포함)이면 화면이 다시 ready 를 보내야 함
                if (topLevel) PageReady = false;
                return;
            }
            e.Cancel = true;
            Log.Warn("web", "탐색 막음 " + Short(e.Uri));
        }

        // 브라우저 프로세스가 죽으면 다시 만들고, 렌더러면 새로 고침. 1분에 5번 넘으면 멈춤
        void OnProcessFailed(CoreWebView2ProcessFailedEventArgs e)
        {
            var kind = e.ProcessFailedKind;
            Log.Error("web", "프로세스 실패 " + kind + " " + e.Reason + " exit=" + e.ExitCode);
            if (kind != CoreWebView2ProcessFailedKind.BrowserProcessExited &&
                kind != CoreWebView2ProcessFailedKind.RenderProcessExited &&
                kind != CoreWebView2ProcessFailedKind.RenderProcessUnresponsive)
                return;   // GPU·유틸리티 등은 저절로 회복
            Dispatcher.BeginInvoke(new Action(() => Recover(
                kind == CoreWebView2ProcessFailedKind.BrowserProcessExited,
                kind == CoreWebView2ProcessFailedKind.RenderProcessUnresponsive)));
        }

        // recreate: 환경부터 새로 만듦. 실패하면 2초 뒤 다시(1분에 5번까지)
        // repeatable: 몇 초마다 거듭 오는 신호(RenderProcessUnresponsive)
        async void Recover(bool recreate, bool repeatable = false)
        {
            if (recovering)
            {
                // 브라우저 프로세스가 또 죽었으면 버리지 않고 지금 복구가 끝난 뒤 한 번 더
                if (recreate) recoverAgain = true;
                Log.Info("web", "이미 복구 중" + (recreate ? ", 끝나면 한 번 더 새로 만듦" : ", 이번 신호는 건너뜀"));
                return;
            }
            recovering = true;
            bool retry = false, again = false;
            try
            {
                if (AllowClose) return;
                var now = DateTime.UtcNow;
                // '응답 없음'만: 방금 새로 고쳤으면(3초 안) 같은 렌더러의 남은 신호로 보고 세지 않음.
                // 진짜 렌더러 종료(RenderProcessExited)는 3초 안이어도 복구하고 셈
                if (repeatable && now - lastReload < TimeSpan.FromSeconds(3)) return;
                failures.RemoveAll(t => now - t > TimeSpan.FromMinutes(1));
                if (failures.Count >= 5)
                {
                    // 멈춘 화면을 띄워 두지 않고 바탕화면으로. Menu 길게·바로가기로 다시 부르면 새로 만듦
                    Log.Error("web", "1분에 5번 실패, 홈을 숨기고 다음에 보일 때 새로 만듦");
                    broken = true;
                    p.OnHomeBroken();
                    return;
                }
                failures.Add(now);
                PageReady = false;
                if (recreate || web == null || web.CoreWebView2 == null)
                {
                    env = await CoreWebView2Environment.CreateAsync(null, Path.Combine(p.DataDir, "webview2"));
                    await CreateView();
                    Log.Info("web", "WebView2 다시 만듦");
                }
                else
                {
                    lastReload = now;
                    web.CoreWebView2.Reload();
                    Log.Info("web", "화면 새로 고침");
                }
            }
            catch (Exception ex)
            {
                Log.Error("web", "다시 띄우기 실패, 2초 뒤 다시", ex);
                retry = true;
            }
            finally
            {
                again = recoverAgain;
                recoverAgain = false;
                recovering = false;
            }
            if (again && !broken && !AllowClose) { Recover(true); return; }
            if (!retry) return;
            var timer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(2) };
            timer.Tick += (s, e) => { timer.Stop(); Recover(true); };
            timer.Start();
        }

        // 보냈으면 true
        public bool PostJson(string json)
        {
            if (!PageReady || web == null || web.CoreWebView2 == null) return false;
            try
            {
                web.CoreWebView2.PostWebMessageAsJson(json);
                return true;
            }
            catch (Exception e)
            {
                Log.Error("bridge", "보내기 실패", e);
                return false;
            }
        }

        // 보이고, 앞으로, 포커스. 윈도우가 막으면 입력 큐 붙이기 + 잠깐 맨 위
        public void ShowAndFocus()
        {
            if (broken)
            {
                broken = false;
                failures.Clear();
                Recover(true);
            }
            if (!IsVisible) Show();
            if (WindowState != WindowState.Maximized) WindowState = WindowState.Maximized;
            Activate();
            if (Hwnd != IntPtr.Zero && !Native.ForceForeground(Hwnd)) Log.Warn("focus", "SetForegroundWindow 거절됨");
            Topmost = true;
            Topmost = false;
            if (web != null) web.Focus();
        }

        public bool IsForeground()
        {
            return Hwnd != IntPtr.Zero && Native.GetForegroundWindow() == Hwnd;
        }

        // 진단: 이 WebView2 의 프로세스들(msedgewebview2)
        public List<int> WebViewProcessIds()
        {
            var r = new List<int>();
            try
            {
                if (env != null) foreach (var info in env.GetProcessInfos()) r.Add(info.ProcessId);
            }
            catch (Exception e) { Log.Warn("diag", "GetProcessInfos 실패 " + e.Message); }
            return r;
        }

        void ShowMessage(string text)
        {
            Content = new TextBlock
            {
                Text = text,
                Foreground = Brushes.White,
                FontSize = 32,
                TextAlignment = TextAlignment.Center,
                TextWrapping = TextWrapping.Wrap,
                HorizontalAlignment = HorizontalAlignment.Center,
                VerticalAlignment = VerticalAlignment.Center,
                Margin = new Thickness(96),
            };
        }

        static string Short(string s)
        {
            if (s == null) return "(없음)";
            return s.Length > 120 ? s.Substring(0, 120) + "…" : s;
        }
    }
}
