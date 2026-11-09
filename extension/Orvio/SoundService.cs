using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Windows.Media;
using System.Windows.Threading;
using Orvio.Core;

namespace Orvio
{
    // 효과음. Playnite 전체화면 기본 테마의 소리를 이 PC 에 깔린 파일에서 바로 튼다(ORVIO 에 복사해 넣지 않음). 파일이 없으면 소리 없이 동작
    internal sealed class SoundService
    {
        // 입력이 없으면 깨우기를 멈춤: 소리가 흐르는 동안은 PC 가 자동 절전에 들어가지 못함
        static readonly TimeSpan KeepAwakeFor = TimeSpan.FromMinutes(10);

        readonly Dictionary<string, MediaPlayer> players = new Dictionary<string, MediaPlayer>();
        bool played;

        // TV 로 가는 소리 출력은 조용하면 잠들어 다시 울릴 때 앞부분이 잘리고, 짧은 효과음은 통째로 사라짐. 들리지 않는 신호를 흘려 깨워 둠
        MediaPlayer keepAwake;
        DispatcherTimer idle;
        DateTime lastUse;
        bool awake;

        public SoundService(string appDir, string dataDir, int volume)
        {
            if (volume == 0) return;
            try
            {
                var dir = new DirectoryInfo(Path.Combine(appDir, "Themes", "Fullscreen", "Default", "Audio"));
                if (!dir.Exists) { Log.Warn("sfx", "효과음 폴더 없음"); return; }
                foreach (var name in Messages.SoundNames)
                {
                    var f = dir.GetFiles(name + ".*").FirstOrDefault(x => SoundRules.Usable(x.Name, x.Length));
                    if (f == null) { Log.Warn("sfx", "파일 없음 " + name); continue; }
                    var n = name;
                    var m = new MediaPlayer { Volume = volume / 100.0 };
                    m.MediaOpened += (s, e) => Log.Info("sfx", "열림 " + n + " 소리 " + m.HasAudio);
                    m.MediaFailed += (s, e) => Log.Error("sfx", "열기 실패 " + n, e.ErrorException);
                    m.Open(new Uri(f.FullName));
                    players[name] = m;
                }
                if (players.Count == 0) return;

                var file = Path.Combine(dataDir, "sfx-keepawake.wav");
                var wav = SoundRules.KeepAwakeWav(60);
                if (!File.Exists(file) || new FileInfo(file).Length != wav.Length) File.WriteAllBytes(file, wav);   // 쓰다 끊긴 파일이면 다시
                var k = new MediaPlayer { Volume = 1 };
                k.MediaFailed += (s, e) => Log.Error("sfx", "깨우기 신호 실패", e.ErrorException);
                k.MediaEnded += (s, e) => { if (!awake) return; k.Position = TimeSpan.Zero; k.Play(); };
                k.Open(new Uri(file));
                idle = new DispatcherTimer { Interval = TimeSpan.FromSeconds(30) };
                idle.Tick += (s, e) => { if (DateTime.UtcNow - lastUse > KeepAwakeFor) Sleep(); };
                keepAwake = k;   // 다 만들어진 뒤에만: Wake 는 이 값으로 준비 여부를 봄
            }
            catch (Exception e) { Log.Error("sfx", "효과음 준비 실패", e); }
        }

        // 홈이 보일 때와 소리를 낼 때 부름 (UI 스레드)
        public void Wake()
        {
            lastUse = DateTime.UtcNow;
            if (keepAwake == null || awake) return;
            try
            {
                awake = true;
                keepAwake.Play();
                idle.Start();
                Log.Info("sfx", "출력 깨우기 시작");
            }
            catch (Exception e) { Log.Error("sfx", "출력 깨우기 실패", e); }   // 소리 때문에 홈이 안 뜨면 안 됨
        }

        // 홈이 숨을 때(게임 중에는 조용히)와 입력이 오래 없을 때 (UI 스레드)
        public void Sleep()
        {
            if (keepAwake == null || !awake) return;
            try
            {
                awake = false;
                keepAwake.Pause();
                idle.Stop();
                Log.Info("sfx", "출력 깨우기 멈춤");
            }
            catch (Exception e) { Log.Error("sfx", "출력 깨우기 멈춤 실패", e); }
        }

        // UI 스레드에서 부름
        public void Play(string name)
        {
            Wake();
            MediaPlayer m;
            if (!players.TryGetValue(name, out m)) return;
            if (!played) { played = true; Log.Info("sfx", "첫 재생 " + name); }
            m.Stop();   // 처음 위치로
            m.Play();
        }
    }
}
