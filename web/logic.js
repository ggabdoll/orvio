// ORVIO 화면 규칙. DOM 없이 돌아서 node 로 시험한다 (node web/test/logic.test.mjs).
// 정렬 FR-03 · 분류 FR-04 · 기록 줄 FR-12 · 검색 FR-19 · 상태 줄 문구 FR-21

/* ---------- 확장이 준 값 다듬기 (믿는 경계: 빠지거나 틀린 칸은 기본값) ---------- */
const HEX = /^#[0-9a-f]{6}$/i;
const hex = (c, d) => HEX.test(c) ? c : d;
const SAFE_URL = /^[^"'()\\\s<>]+$/; // CSS url('…') 안에 그대로 넣어도 깨지지 않는 주소만
const coverOk = (c, allowData) => typeof c === 'string' && SAFE_URL.test(c) && (c.startsWith('https://orvio-covers.example/') || (allowData && c.startsWith('data:image/'))); // data: 표지는 시험 모드만

export function normGame(x, allowDataCover = false) {
  const title = String(x.title ?? '');
  return {
    id: String(x.id),
    title,
    originalTitle: x.originalTitle && x.originalTitle !== title ? String(x.originalTitle) : null,
    source: x.source ? String(x.source) : 'PC',
    genres: Array.isArray(x.genres) ? x.genres.filter(Boolean).map(String) : [],
    input: ['pad', 'kbm', 'both'].includes(x.input) ? x.input : 'both', // FR-05: 모르면 '둘 다'
    players: x.players ? String(x.players) : '1', // FR-05: 모르면 '혼자'
    playtimeSec: Math.max(0, Math.floor(+x.playtimeSec || 0)),
    lastPlayed: x.lastPlayed || null,
    added: x.added || null,
    cover: coverOk(x.cover, allowDataCover) ? x.cover : null,
    color: [hex(x.color && x.color[0], '#3a3a3a'), hex(x.color && x.color[1], '#8a8a8a')],
  };
}
export const normWeb = w => ({ id: String(w.id), title: String(w.title || w.id), url: String(w.url || ''), color: hex(w.color, '#8a8a8a') });

/* ---------- FR-03 정렬: 마지막 플레이, 없으면 추가한 날. 최근 것부터 ---------- */
export const sortTime = g => Date.parse(g.lastPlayed) || Date.parse(g.added) || 0;
export const sortGames = list => [...list].sort((a, b) => sortTime(b) - sortTime(a) || a.title.localeCompare(b.title, 'ko')); // 같은 시각이면 이름순

/* ---------- FR-04 분류 (시안 CATS 규칙 그대로) ---------- */
export const CAT_NAMES = ['이어서 하기', '컨트롤러로', '키보드·마우스', '같이 하기', '웹', '시스템'];
export function categories(sorted, web, sys) {
  const g = x => ({ kind: 'game', g: x });
  return [
    sorted.map(g),
    sorted.filter(x => x.input !== 'kbm').map(g),
    sorted.filter(x => x.input !== 'pad').map(g),
    sorted.filter(x => x.players !== '1').map(g),
    web.map(w => ({ kind: 'web', w })),
    sys.map(s => ({ kind: 'sys', s })),
  ].map((items, i) => ({ name: CAT_NAMES[i], items }));
}

/* ---------- 날짜 낱말 (화면기획서 SCR-01 표). now 는 넣어 준다 ---------- */
const dayNo = d => Date.UTC(d.getFullYear(), d.getMonth(), d.getDate()) / 864e5; // 달력 날짜 차이 (같은 날 = 0)
export function relDay(iso, now) {
  const t = new Date(iso);
  if (!iso || isNaN(t)) return null;
  const d = Math.round(dayNo(now) - dayNo(t));
  if (d <= 0) return '오늘'; // 시계가 어긋나 미래 날짜가 와도 오늘
  if (d === 1) return '어제';
  if (d < 7) return `${d}일 전`;
  if (d < 30) return `${Math.floor(d / 7)}주 전`;
  if (d < 360) return `${Math.floor(d / 30)}달 전`; // 360일부터 1년 전: '12달 전'은 쓰지 않는다
  return `${Math.max(1, Math.floor(d / 365))}년 전`;
}

/* ---------- FR-12 기록 줄 ---------- */
function playtime(sec, tail) { // tail: '했어요'(상세) · '플레이'(키마 확인 창)
  if (sec >= 3600) return `${Math.floor(sec / 3600).toLocaleString('ko-KR')}시간 ${tail}`;
  if (sec >= 60) return `${Math.floor(sec / 60)}분 ${tail}`;
  if (sec >= 1) return `1분 안 되게 ${tail}`;
  return null;
}
export function recordLine(g, now) {
  const t = playtime(g.playtimeSec, '했어요'), last = relDay(g.lastPlayed, now);
  if (!t) return last ? `기록 없음 · 마지막 플레이 ${last}` : '아직 안 해 봤어요';
  return last ? `${t} · 마지막 플레이 ${last}` : t;
}
export const kbmLine = g => playtime(g.playtimeSec, '플레이') || '플레이 기록 없음';
export const cardSub = (g, now) => relDay(g.lastPlayed, now) || '새 게임'; // 이웃 카드 둘째 줄

/* ---------- 상세 메타 줄 (FR-27: 값이 없는 항목만 뺀다) ---------- */
export const players = p => p === '1' ? '혼자' : /^\d/.test(p) ? p + '인' : p;
export const metaLine = g => [g.originalTitle, g.genres[0], players(g.players)].filter(Boolean).join(' · ');
export const host = w => w.id === 'search' ? '검색 엔진' : w.url.replace(/^[a-z][\w+.-]*:\/\//i, '').replace(/^www\./, '').replace(/[/?#].*$/, '');

/* ---------- 상태 줄 · 빠른 메뉴 문구 ---------- */
export const clock = d => `${String(d.getHours()).padStart(2, '0')}:${String(d.getMinutes()).padStart(2, '0')}`;
export const night = d => { const h = d.getHours(); return h >= 19 || h < 7; }; // 19:00~06:59 다크
export const themeOf = (pref, d) => pref === 'auto' ? (night(d) ? 'dark' : 'light') : pref;
export const wifiWord = s => s >= 3 ? '강함' : s === 2 ? '보통' : '약함';
const LEVEL = ['거의 없음', '낮음', '보통', '높음'];
export function battery(b) { // label: 상태 줄 글자 · fill: 막대 채움(%), null 이면 막대 없음 · tip: 말풍선
  const v = b && Math.round(b.value);
  if (b && b.kind === 'percent' && v >= 0 && v <= 100) return { label: v + '%', fill: v, tip: `배터리 ${v}%` };
  if (b && b.kind === 'level' && v >= 0 && v <= 3) return { label: LEVEL[v], fill: Math.round(v / 3 * 100), tip: `배터리 ${LEVEL[v]}` };
  if (b && b.kind === 'wired') return { label: '유선', fill: null, tip: '유선 연결' };
  return { label: '', fill: null, tip: '배터리 정보 없음' };
}
export const kbmWord = k => k.keyboard && k.mouse ? '연결됨' : k.keyboard ? '키보드만 연결됨' : k.mouse ? '마우스만 연결됨' : '연결 안 됨';

/* ---------- FR-19 검색: 제목 > 머리글자(ㅇㄹ, lol, fh5) > 띄엄띄엄. 시안 알고리즘 그대로 ---------- */
const CHO = 'ㄱㄲㄴㄷㄸㄹㅁㅂㅃㅅㅆㅇㅈㅉㅊㅋㅌㅍㅎ';
const dec = ch => { const c = ch.charCodeAt(0) - 0xAC00; return c < 0 || c > 11171 ? null : { cho: CHO[Math.floor(c / 588)], jung: Math.floor(c % 588 / 28), jong: c % 28 }; };
function charMatch(qc, t) {
  if (qc === t) return true;
  const dt = dec(t);
  if (!dt) return false;
  if (CHO.includes(qc)) return dt.cho === qc;
  const dq = dec(qc); // 받침 없이 친 글자(드)는 받침 있는 글자(든)와도 맞는다: 한글 조합 중간 상태
  return !!dq && dq.jong === 0 && dq.cho === dt.cho && dq.jung === dt.jung;
}
const norm = s => [...s.toLowerCase().replace(/[\s:'.\-·+]/g, '')];
const JONG = ' ㄱㄲㄳㄴㄵㄶㄷㄹㄺㄻㄼㄽㄾㄿㅀㅁㅂㅄㅅㅆㅇㅈㅊㅋㅌㅍㅎ';
const JSPLIT = { 3: [1, 'ㅅ'], 5: [4, 'ㅈ'], 6: [4, 'ㅎ'], 9: [8, 'ㄱ'], 10: [8, 'ㅁ'], 11: [8, 'ㅂ'], 12: [8, 'ㅅ'], 13: [8, 'ㅌ'], 14: [8, 'ㅍ'], 15: [8, 'ㅎ'], 18: [17, 'ㅅ'] };
function unjong(qq) { // '릭' → '리'+'ㄱ', '몬슽' → '몬스'+'ㅌ', '발롤' → '발로'+'ㄹ'
  const last = dec(qq[qq.length - 1] || '');
  if (!last || !last.jong) return null;
  const [jn, ch] = JSPLIT[last.jong] || [0, JONG[last.jong]];
  return [...qq.slice(0, -1), String.fromCharCode(0xAC00 + (CHO.indexOf(last.cho) * 21 + last.jung) * 28 + jn), ch];
}
const initials = s => s.toLowerCase().split(/[\s:]+/).filter(Boolean).map(w => [...w][0]);
export function rank(texts, query) {
  const q0 = norm(query);
  if (!q0.length) return 0;
  const alt = unjong(q0);
  return Math.max(rankQ(texts, q0), alt ? rankQ(texts, alt) : 0);
}
function rankQ(texts, qq) {
  const seq = t => { for (let s = 0; s + qq.length <= t.length; s++) if (qq.every((c, i) => charMatch(c, t[s + i]))) return true; return false; };
  const sub = t => { let j = 0; for (const c of t) if (j < qq.length && charMatch(qq[j], c)) j++; return j === qq.length; };
  if (texts.some(t => seq(norm(t)))) return 4; // 제목에 이어서 들어 있음
  if (texts.some(t => { const I = initials(t); return qq.length <= I.length && qq.every((c, i) => charMatch(c, I[i])); })) return 3; // 낱말 머리글자
  if (texts.some(t => sub(norm(t)))) return 1; // 순서대로 띄엄띄엄
  return 0;
}
export const SEARCH_MAX = 20; // 화면기획서 SCR-06: 위 20개만 그림
// games 는 정렬된 목록. 같은 순위면 최근 플레이 순(정렬이 안정적이라 그대로 남는다)
export function search(games, web, query) {
  const v = query.trim();
  if (!v) return { games: games.slice(0, 6), total: Math.min(6, games.length), webs: [] }; // 최근 플레이 6개
  const hits = games.map(g => [rank([g.title, g.originalTitle].filter(Boolean), v), g]).filter(p => p[0]).sort((a, b) => b[0] - a[0]).map(p => p[1]);
  const webs = web.filter(w => w.id !== 'search' && rank([w.title, host(w)], v) >= 3);
  return { games: hits.slice(0, SEARCH_MAX), total: hits.length, webs };
}
