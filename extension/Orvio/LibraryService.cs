using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using Orvio.Core;
using Playnite.SDK;
using Playnite.SDK.Data;
using Playnite.SDK.Models;

namespace Orvio
{
    // 게임 목록 → 화면. DB 읽기는 UI 스레드, 표지 자르기는 STA 작업 스레드 하나
    internal sealed class LibraryService : IDisposable
    {
        const int CoverMax = 600;

        sealed class Snap
        {
            public GameInfo Info;
            public string Original;
        }

        // 작업 스레드가 만든 결과: DTO 와 그 JSON(바뀌었는지 비교용)
        sealed class Built
        {
            public Guid Id;
            public Dictionary<string, object> Dto;
            public string Json;
        }

        readonly OrvioPlugin p;
        public readonly string CoversDir;
        readonly string namesPath;
        readonly string dbRoot;   // Playnite DB 폴더. 표지는 이 안의 파일만
        readonly Dictionary<string, string> names;
        bool namesDirty;

        // 아래는 모두 UI 스레드 전용
        // 화면에 보낸(또는 보낼 차례인) 게임과 그 DTO JSON. 같으면 다시 안 보냄(OnLibraryUpdated 때 원판을 다시 그리지 않게)
        readonly Dictionary<Guid, string> sent = new Dictionary<Guid, string>();
        readonly HashSet<string> rejectedCoverPaths = new HashSet<string>();
        readonly HashSet<Guid> dirty = new HashSet<Guid>();
        readonly Dictionary<Guid, Dictionary<string, object>> queuedUpserts = new Dictionary<Guid, Dictionary<string, object>>();
        readonly HashSet<Guid> queuedRemoves = new HashSet<Guid>();
        readonly DispatcherTimer debounce;
        readonly StaWorker worker = new StaWorker("orvio-covers");
        readonly HashSet<string> badCovers = new HashSet<string>();   // 작업 스레드 전용

        public LibraryService(OrvioPlugin p)
        {
            this.p = p;
            CoversDir = Path.Combine(p.DataDir, "covers");
            Directory.CreateDirectory(CoversDir);
            namesPath = Path.Combine(p.DataDir, "original-names.json");
            try { dbRoot = p.PlayniteApi.Database.DatabasePath; }
            catch (Exception e) { Log.Error("library", "DatabasePath 를 못 읽음, 표지 없이", e); }
            names = LoadNames();
            debounce = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(400) };
            debounce.Tick += (s, e) => { try { ProcessDirty(null); } catch (Exception ex) { Log.Error("library", "변경 처리 실패", ex); } };

            var games = p.PlayniteApi.Database.Games;
            games.ItemCollectionChanged += OnCollectionChanged;
            games.ItemUpdated += OnItemUpdated;
        }

        public void Dispose()
        {
            try
            {
                var games = p.PlayniteApi.Database.Games;
                games.ItemCollectionChanged -= OnCollectionChanged;
                games.ItemUpdated -= OnItemUpdated;
            }
            catch { }
            worker.Dispose();
        }

        static bool Eligible(Game g)
        {
            return g != null && g.IsInstalled && !g.Hidden;
        }

        // ── DB 이벤트 (어느 스레드에서 올지 몰라 UI 로 넘김) ──

        void OnCollectionChanged(object sender, ItemCollectionChangedEventArgs<Game> e)
        {
            try
            {
                var ids = (e.AddedItems ?? new List<Game>()).Concat(e.RemovedItems ?? new List<Game>()).Where(g => g != null).Select(g => g.Id).ToList();
                p.Ui(() => MarkDirty(ids), "games changed");
            }
            catch (Exception ex) { Log.Error("library", "ItemCollectionChanged", ex); }
        }

        void OnItemUpdated(object sender, ItemUpdatedEventArgs<Game> e)
        {
            try
            {
                var ids = (e.UpdatedItems ?? new List<ItemUpdateEvent<Game>>())
                    .Select(u => u.NewData ?? u.OldData).Where(g => g != null).Select(g => g.Id).ToList();
                p.Ui(() => MarkDirty(ids), "games updated");
            }
            catch (Exception ex) { Log.Error("library", "ItemUpdated", ex); }
        }

        void MarkDirty(IEnumerable<Guid> ids)
        {
            foreach (var id in ids) dirty.Add(id);
            if (dirty.Count > 0 && !debounce.IsEnabled) debounce.Start();
        }

        public void MarkAll()
        {
            MarkDirty(p.PlayniteApi.Database.Games.Where(Eligible).Select(g => g.Id).Concat(sent.Keys.ToList()));
        }

        // ── 보내기 ──

        public void SendFull(Action after)
        {
            debounce.Stop();
            dirty.Clear();
            var snaps = p.PlayniteApi.Database.Games.Where(Eligible).Select(Take).ToList();
            SaveNamesIfDirty();
            worker.Post(() =>
            {
                var built = new List<Built>();
                foreach (var s in snaps)
                {
                    var b = Build(s);
                    if (b != null) built.Add(b);
                }
                p.Ui(() =>
                {
                    built = StillEligible(built);
                    sent.Clear();
                    foreach (var b in built) sent[b.Id] = b.Json;
                    queuedUpserts.Clear();
                    queuedRemoves.Clear();
                    p.Send("library", new Dictionary<string, object> { { "games", built.Select(b => (object)b.Dto).ToList() } });
                    Log.Info("library", "library 보냄 " + built.Count + "개");
                    if (after != null) after();
                }, "library");
            });
        }

        // 게임 종료 직후: 그 게임을 바로 다시 읽고(플레이 시간), 끝나면 then. 작업 스레드가 막혀도 2초 뒤엔 then
        public void RefreshNow(Guid id, Action then)
        {
            bool done = false;
            Action once = () => { if (done) return; done = true; then(); };
            var guard = new DispatcherTimer { Interval = TimeSpan.FromSeconds(2) };
            guard.Tick += (s, e) =>
            {
                try { guard.Stop(); Log.Warn("library", "갱신이 늦어 먼저 보임"); once(); }
                catch (Exception ex) { Log.Error("library", "늦은 보이기 실패", ex); }
            };
            guard.Start();
            dirty.Add(id);
            ProcessDirty(() => { guard.Stop(); once(); });
        }

        void ProcessDirty(Action after)
        {
            debounce.Stop();
            var ups = new List<Snap>();
            var removes = new List<Guid>();
            foreach (var id in dirty)
            {
                var g = p.PlayniteApi.Database.Games.Get(id);
                if (Eligible(g)) ups.Add(Take(g));
                else if (sent.ContainsKey(id)) removes.Add(id);
            }
            dirty.Clear();
            SaveNamesIfDirty();
            if (ups.Count == 0 && removes.Count == 0) { if (after != null) after(); return; }

            worker.Post(() =>
            {
                var built = new List<Built>();
                foreach (var s in ups)
                {
                    var b = Build(s);
                    if (b != null) built.Add(b);
                }
                p.Ui(() =>
                {
                    Deliver(built, removes);
                    if (after != null) after();
                }, "libraryDelta");
            });
        }

        void Deliver(List<Built> built, List<Guid> removes)
        {
            built = StillEligible(built);
            // 보낸 JSON 과 같으면 뺌(플레이 시간·태그 등이 실제로 바뀐 게임만)
            var changed = new List<Built>();
            foreach (var b in built)
            {
                string old;
                if (sent.TryGetValue(b.Id, out old) && old == b.Json) continue;
                sent[b.Id] = b.Json;
                changed.Add(b);
            }
            removes = removes.Where(r => sent.Remove(r)).ToList();
            if (changed.Count == 0 && removes.Count == 0) return;
            if (p.HomeVisible && p.Home != null && p.Home.PageReady)
            {
                SendDelta(changed.Select(b => (object)b.Dto).ToList(), removes);
                return;
            }
            // 숨어 있는 동안은 모았다가 resume 직전에 한 번
            foreach (var b in changed)
            {
                queuedUpserts[b.Id] = b.Dto;
                queuedRemoves.Remove(b.Id);
            }
            foreach (var r in removes)
            {
                queuedUpserts.Remove(r);
                queuedRemoves.Add(r);
            }
        }

        // 표지를 만드는 사이 숨기거나 지운 게임은 뺌(유령 게임 방지). 빠진 게임은 그 변경 이벤트가 따로 정리함
        List<Built> StillEligible(List<Built> built)
        {
            var keep = built.Where(b => Eligible(p.PlayniteApi.Database.Games.Get(b.Id))).ToList();
            if (keep.Count != built.Count) Log.Info("library", "만드는 사이 바뀐 게임 " + (built.Count - keep.Count) + "개 뺌");
            return keep;
        }

        public void FlushQueued()
        {
            if (queuedUpserts.Count == 0 && queuedRemoves.Count == 0) return;
            SendDelta(queuedUpserts.Values.Cast<object>().ToList(), queuedRemoves.ToList());
            queuedUpserts.Clear();
            queuedRemoves.Clear();
        }

        void SendDelta(List<object> upserts, List<Guid> removes)
        {
            p.Send("libraryDelta", new Dictionary<string, object>
            {
                { "upserts", upserts },
                { "removes", removes.Select(r => (object)r.ToString("D")).ToList() },
            });
            Log.Info("library", "libraryDelta +" + upserts.Count + " -" + removes.Count);
        }

        // ── 베끼기 (UI 스레드) ──

        Snap Take(Game g)
        {
            var info = new GameInfo
            {
                Id = g.Id,
                Name = g.Name ?? "",
                Source = g.Source != null ? g.Source.Name : null,
                Playtime = g.Playtime,
                LastActivity = g.LastActivity,
                Added = g.Added,
                CoverPath = CoverPath(g),
            };
            if (g.Genres != null) info.Genres = g.Genres.Where(x => x != null).Select(x => x.Name).ToList();
            if (g.Tags != null) info.Tags = g.Tags.Where(x => x != null).Select(x => x.Name).ToList();
            if (g.Features != null) info.Features = g.Features.Where(x => x != null).Select(x => x.Name).ToList();

            // 처음 본 이름을 기억 (D-5: 한글로 바꾸기 전 원래 이름)
            var key = g.Id.ToString("D");
            string orig;
            if (!names.TryGetValue(key, out orig))
            {
                orig = info.Name;
                names[key] = orig;
                namesDirty = true;
            }
            return new Snap { Info = info, Original = orig };
        }

        // 로컬 경로이고 Playnite DB 폴더 안일 때만(UNC·http 거절). 아니면 cover: null
        string CoverPath(Game g)
        {
            var c = g.CoverImage;
            if (string.IsNullOrEmpty(c)) return null;
            string full = null;
            try
            {
                if (c.IndexOf("://", StringComparison.Ordinal) < 0 && !c.StartsWith(@"\\", StringComparison.Ordinal))
                    full = p.PlayniteApi.Database.GetFullFilePath(c);
            }
            catch { }
            var safe = CoverRules.SafeCoverPath(full, dbRoot);
            if (safe == null && rejectedCoverPaths.Add(c)) Log.Warn("cover", "DB 폴더 밖이거나 로컬이 아닌 표지 거절: " + g.Name);
            return safe;
        }

        // ── 표지 (작업 스레드) ──

        // 한 게임이 실패해도 목록 전체가 멈추지 않게 그 게임만 뺌
        Built Build(Snap s)
        {
            try
            {
                var dto = GameRules.ToDto(s.Info, s.Original, EnsureCover(s.Info));
                return new Built { Id = s.Info.Id, Dto = dto, Json = Json.Write(dto) };
            }
            catch (Exception e)
            {
                Log.Error("library", "게임 변환 실패 " + s.Info.Name, e);
                return null;
            }
        }

        // 원본이 바뀐 때만 다시 자름. 잘라 둔 파일의 수정 시각을 원본과 같게 맞춰 비교함
        string EnsureCover(GameInfo g)
        {
            var src = g.CoverPath;
            if (src == null || !File.Exists(src)) return null;
            var id = g.Id.ToString("D");
            var dst = Path.Combine(CoversDir, id + ".png");
            var stamp = File.GetLastWriteTimeUtc(src);
            var tag = src + "|" + stamp.Ticks;
            if (badCovers.Contains(tag)) return null;
            try
            {
                if (!File.Exists(dst) || File.GetLastWriteTimeUtc(dst) != stamp)
                {
                    MakeCover(src, dst);
                    File.SetLastWriteTimeUtc(dst, stamp);
                }
            }
            catch (Exception e)
            {
                try { File.Delete(dst + ".tmp"); } catch { }
                // 파일 잠김 같은 일시 오류(IOException)는 다음에 다시. 크기 초과·해독 실패는 원본이 바뀔 때까지 건너뜀
                if (!(e is IOException)) badCovers.Add(tag);
                Log.Warn("cover", "표지 자르기 실패 " + g.Name + ": " + e.GetType().Name + " " + e.Message);
                return null;
            }
            return "https://" + HomeWindow.CoverHost + "/" + id + ".png?v=" + GameRules.Fnv1a(tag).ToString("x8");
        }

        static void MakeCover(string src, string dst)
        {
            var tmp = dst + ".tmp";
            using (var fs = new FileStream(src, FileMode.Open, FileAccess.Read, FileShare.ReadWrite))
            {
                // 지연 생성 + 캐시 없음: 머리글의 크기만 먼저 읽고, 너무 크면 픽셀을 풀기 전에 거절
                var dec = BitmapDecoder.Create(fs, BitmapCreateOptions.DelayCreation | BitmapCreateOptions.IgnoreColorProfile, BitmapCacheOption.None);
                var frame = dec.Frames[0];
                if (!CoverRules.SizeOk(frame.PixelWidth, frame.PixelHeight))
                    throw new InvalidDataException("그림이 너무 큼 " + frame.PixelWidth + "x" + frame.PixelHeight);
                var r = GameRules.SquareCrop(frame.PixelWidth, frame.PixelHeight, CoverMax);
                BitmapSource img = new CroppedBitmap(frame, new Int32Rect(r.X, r.Y, r.Side, r.Side));
                if (r.Out != r.Side)
                {
                    double k = r.Out / (double)r.Side;
                    img = new TransformedBitmap(img, new ScaleTransform(k, k));
                }
                var enc = new PngBitmapEncoder();
                enc.Frames.Add(BitmapFrame.Create(img));
                // 캐시가 없으므로 원본 스트림이 열려 있는 동안 저장까지 끝냄
                using (var o = File.Create(tmp)) enc.Save(o);
            }
            if (File.Exists(dst)) File.Replace(tmp, dst, null);
            else File.Move(tmp, dst);
        }

        // ── 원래 이름 저장소 ──

        Dictionary<string, string> LoadNames()
        {
            try
            {
                Dictionary<string, string> d;
                Exception err;
                if (File.Exists(namesPath))
                {
                    if (Serialization.TryFromJsonFile(namesPath, out d, out err) && d != null) return d;
                    Log.Error("library", "original-names.json 읽기 실패", err);
                }
            }
            catch (Exception e) { Log.Error("library", "original-names.json", e); }
            return new Dictionary<string, string>();
        }

        void SaveNamesIfDirty()
        {
            if (!namesDirty) return;
            namesDirty = false;
            try { OrvioPlugin.WriteAtomic(namesPath, Serialization.ToJson(names, true)); }
            catch (Exception e) { Log.Error("library", "original-names.json 저장 실패", e); }
        }
    }

    // STA 스레드 하나에서 차례로 실행 (WPF 이미징용)
    internal sealed class StaWorker : IDisposable
    {
        readonly BlockingCollection<Action> queue = new BlockingCollection<Action>();

        public StaWorker(string name)
        {
            var t = new Thread(Run) { IsBackground = true, Name = name, Priority = ThreadPriority.BelowNormal };
            t.SetApartmentState(ApartmentState.STA);
            t.Start();
        }

        public void Post(Action a)
        {
            try { queue.Add(a); }
            catch (InvalidOperationException) { }
        }

        void Run()
        {
            foreach (var a in queue.GetConsumingEnumerable())
            {
                try { a(); }
                catch (Exception e) { Log.Error("worker", "작업 실패", e); }
            }
        }

        public void Dispose()
        {
            try { queue.CompleteAdding(); } catch { }
        }
    }
}
