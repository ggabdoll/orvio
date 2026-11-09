// 시험 모드 가짜 다리: 확장(Playnite)을 흉내 내 window.chrome.webview 를 만든다. 배포 확장에는 넣지 않는다(web/test).
// 쿼리: ?now=2026-11-04T21:47:00 (이 시각에서 흐름) · ?theme=light|dark|auto · ?fail=1 (실행이 timeout 으로 실패)
//       ?empty=1 (게임 0개) · ?loading=1 (library 를 보내지 않음) · ?xinput=1 (inputMode xinput, 패드는 fakeHost.send 로)
//       ?mute=1 (launch 에 답하지 않음: 확장이 조용히 거절한 경우)
// 시험 손잡이: window.fakeHost.send(msg) 로 확장 메시지를 보내고, window.fakeHost.got 에 화면이 보낸 메시지가 쌓인다.
await import('./covers.js').catch(() => {}); // window.COVERS: 시안 표지(data URL). 저작권 때문에 저장소에 없으므로 없어도 돈다

const QS = new URLSearchParams(location.search);
const NOW0 = new Date(QS.get('now') || '2026-11-04T21:47:00'), T0 = Date.now();
export const now = () => new Date(NOW0.getTime() + Date.now() - T0);
const iso = d => new Date(d.getTime() - d.getTimezoneOffset() * 6e4).toISOString().slice(0, 19); // 로컬 시각 ISO (오프셋 없음)

// 시안 DATA 그대로. lastPlayed 낱말과 hours 는 아래에서 날짜·초로 바꾼다
const DATA = [
  { id: 'elden', title: '엘든 링', en: 'ELDEN RING', launcher: 'Steam', input: 'pad', players: '1', genre: '액션 RPG', lastPlayed: '오늘', hours: 212, color: ['#3a3324', '#8c7a4b'] },
  { id: 'mhw', title: '몬스터 헌터 와일즈', en: 'Monster Hunter Wilds', launcher: 'Steam', input: 'pad', players: '1~4', genre: '헌팅 액션', lastPlayed: '어제', hours: 148, color: ['#2a3a2f', '#6f8f5a'] },
  { id: 'lol', title: '리그 오브 레전드', en: 'League of Legends', launcher: 'Riot', input: 'kbm', players: '온라인', genre: 'MOBA', lastPlayed: '어제', hours: 1830, color: ['#1d2a3a', '#c8aa6e'] },
  { id: 'split', title: '스플릿 픽션', en: 'Split Fiction', launcher: 'Steam', input: 'pad', players: '2', genre: '협동 액션', lastPlayed: '3일 전', hours: 21, color: ['#3a2340', '#e0679a'] },
  { id: 'forza', title: '포르자 호라이즌 5', en: 'Forza Horizon 5', launcher: 'Xbox', input: 'pad', players: '1', genre: '레이싱', lastPlayed: '5일 전', hours: 96, color: ['#1f3346', '#f08a24'] },
  { id: 'valorant', title: '발로란트', en: 'VALORANT', launcher: 'Riot', input: 'kbm', players: '온라인', genre: 'FPS', lastPlayed: '1주 전', hours: 340, color: ['#2b1d22', '#ff4655'] },
  { id: 'hades2', title: '하데스 II', en: 'Hades II', launcher: 'Steam', input: 'both', players: '1', genre: '로그라이트', lastPlayed: '1주 전', hours: 44, color: ['#24203a', '#7d6bd6'] },
  { id: 'tekken', title: '철권 8', en: 'TEKKEN 8', launcher: 'Steam', input: 'pad', players: '1~2', genre: '대전 격투', lastPlayed: '2주 전', hours: 77, color: ['#331c1c', '#d94b2b'] },
  { id: 'bg3', title: '발더스 게이트 3', en: "Baldur's Gate 3", launcher: 'Steam', input: 'both', players: '1~4', genre: 'RPG', lastPlayed: '3주 전', hours: 160, color: ['#2a2433', '#b08d57'] },
  { id: 'sc', title: '스타크래프트: 리마스터', en: 'StarCraft Remastered', launcher: 'Battle.net', input: 'kbm', players: '온라인', genre: 'RTS', lastPlayed: '1달 전', hours: 520, color: ['#16242e', '#3fa7d6'] },
  { id: 'rocket', title: '로켓 리그', en: 'Rocket League', launcher: 'Epic', input: 'pad', players: '1~4', genre: '스포츠', lastPlayed: '1달 전', hours: 63, color: ['#1c2a44', '#3d8bff'] },
  { id: 'silk', title: '할로우 나이트: 실크송', en: 'Hollow Knight: Silksong', launcher: 'Steam', input: 'pad', players: '1', genre: '메트로배니아', lastPlayed: '2달 전', hours: 38, color: ['#2a2626', '#d9c7b8'] },
  { id: 'civ7', title: '문명 VII', en: 'Civilization VII', launcher: 'Epic', input: 'kbm', players: '1', genre: '전략', lastPlayed: '3달 전', hours: 55, color: ['#1f2d2a', '#d4b25a'] },
  { id: 'fc', title: 'EA SPORTS FC 26', en: 'EA SPORTS FC 26', launcher: 'Steam', input: 'pad', players: '1~4', genre: '축구', lastPlayed: '3달 전', hours: 29, color: ['#13261c', '#33d17a'] },
];
const daysOf = w => w === '오늘' ? 0 : w === '어제' ? 1 : +w.match(/\d+/)[0] * { 일: 1, 주: 7, 달: 30 }[w.match(/[일주달]/)[0]];
// 표지 파일이 없을 때 쓰는 색 그림(CD와 홈 배경을 그대로 시험할 수 있게)
const placeholder = ([a, b]) => 'data:image/svg+xml;base64,' + btoa(`<svg xmlns="http://www.w3.org/2000/svg" viewBox="0 0 440 440"><defs><linearGradient id="g" x2="1" y2="1"><stop stop-color="${a}"/><stop offset="1" stop-color="${b}"/></linearGradient></defs><rect width="440" height="440" fill="url(#g)"/><circle cx="330" cy="110" r="150" fill="${b}" opacity=".5"/></svg>`);
const GAMES = DATA.map((x, i) => {
  const d = new Date(NOW0); d.setDate(d.getDate() - daysOf(x.lastPlayed)); d.setSeconds(d.getSeconds() - i); // 같은 낱말끼리도 시안 순서가 남게 1초씩
  return { id: x.id, title: x.title, ...(x.en !== x.title && { originalTitle: x.en }), source: x.launcher, genres: [x.genre], input: x.input, players: x.players,
    playtimeSec: x.hours * 3600, lastPlayed: iso(d), added: '2026-03-01T12:00:00', cover: (window.COVERS || {})[x.id] || placeholder(x.color), color: x.color };
});
const WEB = [
  { id: 'youtube', title: '유튜브', url: 'https://www.youtube.com', color: '#ff3d3d' },
  { id: 'netflix', title: '넷플릭스', url: 'https://www.netflix.com', color: '#e50914' },
  { id: 'chzzk', title: '치지직', url: 'https://chzzk.naver.com', color: '#00ffa3' },
  { id: 'disney', title: '디즈니+', url: 'https://www.disneyplus.com', color: '#1f6fff' },
  { id: 'tving', title: '티빙', url: 'https://www.tving.com', color: '#ff153c' },
  { id: 'naver', title: '네이버', url: 'https://www.naver.com', color: '#03c75a' },
  { id: 'search', title: '웹 검색', url: 'https://search.naver.com/search.naver?query={q}', color: '#8a5cf5' },
];
const STATUS = { network: { kind: 'wifi', strength: 3 }, pads: [{ index: 0, name: '컨트롤러 1', battery: { kind: 'percent', value: 82 } }, { index: 1, name: '컨트롤러 2', battery: { kind: 'percent', value: 34 } }], kbm: { keyboard: true, mouse: true }, volume: 40 };
const settings = { theme: QS.get('theme') || 'auto', idle: false, web: WEB, launchTimeoutSec: 90 };

const listeners = [];
const got = [];
const post = m => setTimeout(() => { if (m.type === 'gameState') running = ['started', 'launching'].includes(m.state) ? m.id : m.state === 'failed' && m.reason === 'busy' ? running : null; const data = JSON.parse(JSON.stringify(m)); for (const fn of listeners) fn({ data }); }, 0); // PostWebMessageAsJson 처럼 복사본. gameState 로 실행 중인 게임을 기억(확장처럼)
let running = null; // 실행 중인 게임 ID
function handle(m) {
  got.push({ ...m, at: performance.now() }); // at: 받은 시각(시험이 순서·시간을 본다)
  console.log('[가짜 확장] 받음', m.type, m);
  if (m.type === 'ready') {
    post({ type: 'init', version: 1, settings, inputMode: QS.has('xinput') ? 'xinput' : 'gamepadApi' });
    post({ type: 'status', ...STATUS });
    if (!QS.has('loading')) post({ type: 'library', games: QS.has('empty') ? [] : GAMES });
  } else if (m.type === 'launch') {
    const g = GAMES.find(x => x.id === m.id);
    if (!g) return console.log('[가짜 확장] 거절: 라이브러리에 없는 ID', m.id);
    if (QS.has('mute')) return console.log('[가짜 확장] 답하지 않음(?mute=1)', m.id);
    if (running) return post({ type: 'gameState', id: m.id, state: 'failed', reason: 'busy' });
    running = m.id;
    post({ type: 'gameState', id: m.id, state: 'launching' });
    setTimeout(() => {
      if (QS.has('fail')) { running = null; return post({ type: 'gameState', id: m.id, state: 'failed', reason: 'timeout' }); }
      post({ type: 'gameState', id: m.id, state: 'started' });
      post({ type: 'pause' }); // 확장은 여기서 창을 숨긴다
      setTimeout(() => {
        running = null;
        g.playtimeSec += 3; g.lastPlayed = iso(now());
        post({ type: 'gameState', id: m.id, state: 'stopped', elapsedSec: 3 });
        post({ type: 'libraryDelta', upserts: [g], removes: [] }); // 숨어 있는 동안 모은 변경을 resume 직전에 한 번
        post({ type: 'resume', reason: 'gameStopped' });
      }, 3000);
    }, 2000);
  } else if (m.type === 'power' && running && m.action !== 'desktop') console.log('[가짜 확장] 게임 중이라 power 거절', m.action); // 확장도 조용히 거절
  else if (m.type === 'setSetting') settings[m.key] = m.value;
  else if (m.type === 'volume') STATUS.volume = m.value;
}

window.chrome = window.chrome || {};
window.chrome.webview = {
  addEventListener(type, fn) { if (type === 'message') listeners.push(fn); },
  postMessage: handle,
};
window.fakeHost = { send: post, got };
