// node web/test/logic.test.mjs  — logic.js 규칙 확인 (화면기획서 SCR-01·SCR-04·SCR-06, 요구사항 FR-03·04·05·12·19)
import assert from 'node:assert/strict';
import * as L from '../logic.js';

let n = 0;
const t = (name, fn) => { fn(); n++; };
const NOW = new Date('2026-11-04T21:47:00');
const ago = (days, hm = '12:00:00') => { const d = new Date(NOW); d.setDate(d.getDate() - days); return `${d.getFullYear()}-${String(d.getMonth() + 1).padStart(2, '0')}-${String(d.getDate()).padStart(2, '0')}T${hm}`; };
const G = o => L.normGame({ id: 'x', title: '게임', ...o });

t('날짜 낱말: SCR-01 경계', () => {
  const r = d => L.relDay(ago(d), NOW);
  assert.equal(L.relDay('2026-11-04T00:00:01', NOW), '오늘');
  assert.equal(L.relDay('2026-11-04T23:59:00', NOW), '오늘'); // 같은 날이면 시각이 지금보다 뒤여도 오늘
  assert.equal(L.relDay('2026-11-03T23:59:59', NOW), '어제'); // 달력 날짜로 센다
  assert.equal(r(1), '어제');
  assert.equal(r(2), '2일 전'); assert.equal(r(6), '6일 전');
  assert.equal(r(7), '1주 전'); assert.equal(r(13), '1주 전'); assert.equal(r(14), '2주 전'); assert.equal(r(29), '4주 전');
  assert.equal(r(30), '1달 전'); assert.equal(r(59), '1달 전'); assert.equal(r(60), '2달 전'); assert.equal(r(359), '11달 전');
  assert.equal(r(360), '1년 전'); assert.equal(r(364), '1년 전'); // 360일부터 1년 전, '12달 전'은 없다
  for (let d = 0; d < 800; d++) assert.notEqual(r(d), '12달 전');
  assert.equal(r(365), '1년 전'); assert.equal(r(729), '1년 전'); assert.equal(r(730), '2년 전');
  assert.equal(L.relDay('2026-11-10T10:00:00', NOW), '오늘'); // 미래(시계 어긋남)
  assert.equal(L.relDay(null, NOW), null); assert.equal(L.relDay('엉터리', NOW), null);
  assert.equal(L.relDay('2026-11-04T21:47:00', new Date('2026-11-05T00:00:00')), '어제'); // 자정이 지나면 바뀜
});

t('기록 줄: SCR-01 플레이 시간 표', () => {
  const rec = (sec, last) => L.recordLine(G({ playtimeSec: sec, lastPlayed: last }), NOW);
  assert.equal(rec(1830 * 3600, ago(0)), '1,830시간 했어요 · 마지막 플레이 오늘');
  assert.equal(rec(212 * 3600 + 3599, ago(1)), '212시간 했어요 · 마지막 플레이 어제'); // 시간은 내림
  assert.equal(rec(3600, ago(3)), '1시간 했어요 · 마지막 플레이 3일 전');
  assert.equal(rec(3599, ago(0)), '59분 했어요 · 마지막 플레이 오늘');
  assert.equal(rec(40 * 60 + 59, ago(0)), '40분 했어요 · 마지막 플레이 오늘');
  assert.equal(rec(60, ago(0)), '1분 했어요 · 마지막 플레이 오늘');
  assert.equal(rec(59, ago(0)), '1분 안 되게 했어요 · 마지막 플레이 오늘');
  assert.equal(rec(1, ago(0)), '1분 안 되게 했어요 · 마지막 플레이 오늘');
  assert.equal(rec(0, null), '아직 안 해 봤어요'); // '마지막 플레이' 붙이지 않음
  assert.equal(rec(0, ago(1)), '기록 없음 · 마지막 플레이 어제'); // 다른 런처에서 켠 게임
  assert.equal(rec(212 * 3600, null), '212시간 했어요'); // 가져온 기록
  assert.equal(rec(undefined, null), '아직 안 해 봤어요');
});

t('키마 확인 창 N시간 플레이: SCR-04', () => {
  const k = sec => L.kbmLine(G({ playtimeSec: sec }));
  assert.equal(k(1830 * 3600), '1,830시간 플레이');
  assert.equal(k(40 * 60), '40분 플레이');
  assert.equal(k(30), '1분 안 되게 플레이');
  assert.equal(k(0), '플레이 기록 없음');
});

t('이웃 카드 둘째 줄: 마지막 플레이 없으면 새 게임', () => {
  assert.equal(L.cardSub(G({ lastPlayed: ago(1) }), NOW), '어제');
  assert.equal(L.cardSub(G({ lastPlayed: null, added: ago(0) }), NOW), '새 게임');
});

t('인원·메타 줄 (시안 players(), FR-27 빈 칸만 뺌)', () => {
  assert.equal(L.players('1'), '혼자'); assert.equal(L.players('1~4'), '1~4인'); assert.equal(L.players('2'), '2인');
  assert.equal(L.players('온라인'), '온라인'); assert.equal(L.players('같이'), '같이');
  assert.equal(L.metaLine(G({ title: '엘든 링', originalTitle: 'ELDEN RING', genres: ['액션 RPG'], players: '1' })), 'ELDEN RING · 액션 RPG · 혼자');
  assert.equal(L.metaLine(G({ title: 'EA SPORTS FC 26', originalTitle: 'EA SPORTS FC 26', genres: ['축구'], players: '1~4' })), '축구 · 1~4인');
  assert.equal(L.metaLine(G({ genres: [] })), '혼자');
});

t('값 다듬기: FR-05 기본값, 믿을 수 없는 표지·색은 버림', () => {
  const g = L.normGame({ id: 7, title: 'T' });
  assert.equal(g.id, '7'); assert.equal(g.input, 'both'); assert.equal(g.players, '1'); assert.equal(g.source, 'PC');
  assert.deepEqual(g.genres, []); assert.equal(g.playtimeSec, 0); assert.equal(g.cover, null); assert.deepEqual(g.color, ['#3a3a3a', '#8a8a8a']);
  assert.equal(L.normGame({ id: 1, input: 'joystick' }).input, 'both');
  assert.equal(L.normGame({ id: 1, cover: 'https://orvio-covers.example/a.png?v=1' }).cover, 'https://orvio-covers.example/a.png?v=1');
  assert.equal(L.normGame({ id: 1, cover: 'https://evil.test/a.png' }).cover, null);
  assert.equal(L.normGame({ id: 1, cover: "https://orvio-covers.example/a.png');x:url('" }).cover, null);
  assert.equal(L.normGame({ id: 1, color: ['red;}', '#123456'] }).color[0], '#3a3a3a');
  assert.equal(L.normGame({ id: 1, title: 'A', originalTitle: 'A' }).originalTitle, null);
  assert.equal(L.normGame({ id: 1, cover: 'data:image/webp;base64,AAAA' }).cover, null); // data: 표지는 기본으로 버림
  assert.equal(L.normGame({ id: 1, cover: 'data:image/webp;base64,AAAA' }, true).cover, 'data:image/webp;base64,AAAA'); // 시험 모드만
});

t('FR-03 정렬: 마지막 플레이, 없으면 추가한 날', () => {
  const list = [
    G({ id: 'old', lastPlayed: ago(90) }),
    G({ id: 'new-install', lastPlayed: null, added: ago(0, '09:00:00') }),
    G({ id: 'yesterday', lastPlayed: ago(1) }),
    G({ id: 'today', lastPlayed: ago(0, '20:00:00') }),
    G({ id: 'nothing' }),
    G({ id: 'old-install', added: ago(400) }),
  ];
  assert.deepEqual(L.sortGames(list).map(x => x.id), ['today', 'new-install', 'yesterday', 'old', 'old-install', 'nothing']);
  const tie = [G({ id: 'c', title: '하데스', lastPlayed: ago(2) }), G({ id: 'a', title: '가디언', lastPlayed: ago(2) }), G({ id: 'b', title: 'Apex', lastPlayed: ago(2) }), G({ id: 'd', title: '나중' })];
  assert.deepEqual(L.sortGames(tie).map(x => x.id), ['a', 'c', 'b', 'd']); // 같은 시각이면 이름순(ko 정렬: 가디언 → 하데스 → Apex), 기록 없는 게임은 끝
});

t('FR-04 분류: 시안 CATS 규칙', () => {
  const list = L.sortGames([
    G({ id: 'p', input: 'pad', players: '1' }), G({ id: 'k', input: 'kbm', players: '온라인' }),
    G({ id: 'b', input: 'both', players: '1~4' }), G({ id: 'u' }), // u: 태그 없음 → both, 혼자
  ]);
  const C = L.categories(list, [L.normWeb({ id: 'youtube', title: '유튜브', url: 'https://www.youtube.com' })], [{ id: 'menu' }]);
  const ids = i => C[i].items.map(it => (it.g || it.w || it.s).id).sort().join(',');
  assert.deepEqual(C.map(c => c.name), ['이어서 하기', '컨트롤러로', '키보드·마우스', '같이 하기', '웹', '시스템']);
  assert.equal(ids(0), 'b,k,p,u'); assert.equal(ids(1), 'b,p,u'); assert.equal(ids(2), 'b,k,u'); assert.equal(ids(3), 'b,k');
  assert.equal(ids(4), 'youtube'); assert.equal(ids(5), 'menu');
  assert.equal(C[0].items[0].kind, 'game'); assert.equal(C[4].items[0].kind, 'web'); assert.equal(C[5].items[0].kind, 'sys');
});

t('웹 주소 표시 (시안 WEB_URL 과 같게)', () => {
  const h = (id, url) => L.host(L.normWeb({ id, url }));
  assert.equal(h('youtube', 'https://www.youtube.com'), 'youtube.com');
  assert.equal(h('chzzk', 'https://chzzk.naver.com/'), 'chzzk.naver.com');
  assert.equal(h('disney', 'https://www.disneyplus.com/ko-kr?x=1'), 'disneyplus.com');
  assert.equal(h('search', 'https://www.google.com/search?q={q}'), '검색 엔진');
});

t('검색: 시안 자체 점검 10개 그대로', () => {
  assert.equal(L.rank(['엘든 링'], 'ㅇㄹ'), 3); assert.equal(L.rank(['발로란트'], 'ㅂㄹㄹㅌ'), 4); assert.equal(L.rank(['엘든 링'], '엘드'), 4);
  assert.equal(L.rank(['리그 오브 레전드', 'League of Legends'], 'lol'), 3); assert.equal(L.rank(['포르자 호라이즌 5', 'Forza Horizon 5'], 'fh5'), 3);
  assert.equal(L.rank(['할로우 나이트: 실크송', 'Hollow Knight: Silksong'], 'lol'), 1); assert.equal(L.rank(['철권 8', 'TEKKEN 8'], 'ㅇㄹ'), 0);
  assert.equal(L.rank(['리그 오브 레전드'], '릭'), 4); assert.equal(L.rank(['몬스터 헌터 와일즈'], '몬슽'), 4); assert.equal(L.rank(['발로란트'], '발롤'), 4);
});

t('검색: originalTitle 로도 찾고, 순위·최근 순, 20개까지', () => {
  const list = L.sortGames([
    G({ id: 'hk', title: '할로우 나이트: 실크송', originalTitle: 'Hollow Knight: Silksong', lastPlayed: ago(0) }),
    G({ id: 'lol', title: '리그 오브 레전드', originalTitle: 'League of Legends', lastPlayed: ago(5) }),
    G({ id: 'er', title: '엘든 링', originalTitle: 'ELDEN RING', lastPlayed: ago(1) }),
  ]);
  const web = [{ id: 'youtube', title: '유튜브', url: 'https://www.youtube.com' }, { id: 'search', title: '웹 검색', url: '' }].map(L.normWeb);
  const r = L.search(list, web, 'lol');
  assert.deepEqual(r.games.map(x => x.id), ['lol', 'hk']); // 순위 3 이 1 보다 앞 (hk 가 더 최근이어도)
  assert.equal(r.total, 2);
  assert.deepEqual(L.search(list, web, 'elden').games.map(x => x.id), ['er']); // 원래 이름으로
  assert.deepEqual(L.search(list, web, 'ㅇㄹ').games.map(x => x.id), ['er', 'lol']); // 머리글자 3, 띄엄 1
  assert.deepEqual(L.search(list, web, 'ㅇㅌㅂ').webs.map(w => w.id), ['youtube']); // 웹 바로가기는 순위 3 이상
  assert.deepEqual(L.search(list, web, '  ').games.map(x => x.id), ['hk', 'er', 'lol']); // 빈 검색어: 최근 플레이
  const many = L.sortGames(Array.from({ length: 30 }, (_, i) => G({ id: 'g' + i, title: '게임 ' + i, lastPlayed: ago(i) })));
  const m = L.search(many, web, '게임');
  assert.equal(m.games.length, L.SEARCH_MAX); assert.equal(m.total, 30); assert.equal(m.games[0].id, 'g0');
  assert.equal(L.search(many, web, '').games.length, 6);
});

t('상태 줄 문구: 배터리·네트워크·키마·시계·테마', () => {
  assert.deepEqual(L.battery({ kind: 'percent', value: 82 }), { label: '82%', fill: 82, tip: '배터리 82%' });
  assert.deepEqual(L.battery({ kind: 'level', value: 0 }), { label: '거의 없음', fill: 0, tip: '배터리 거의 없음' });
  assert.equal(L.battery({ kind: 'level', value: 1 }).label, '낮음'); assert.equal(L.battery({ kind: 'level', value: 2 }).label, '보통');
  assert.deepEqual(L.battery({ kind: 'level', value: 3 }), { label: '높음', fill: 100, tip: '배터리 높음' });
  assert.equal(L.battery({ kind: 'level', value: 2 }).fill, 67);
  assert.equal(L.battery({ kind: 'wired' }).fill, null); assert.equal(L.battery({ kind: 'unknown' }).label, ''); assert.equal(L.battery(undefined).label, '');
  assert.equal(L.battery({ kind: 'percent', value: 140 }).label, ''); // 범위 밖은 모름으로
  assert.equal(L.wifiWord(4), '강함'); assert.equal(L.wifiWord(3), '강함'); assert.equal(L.wifiWord(2), '보통'); assert.equal(L.wifiWord(0), '약함');
  assert.equal(L.kbmWord({ keyboard: true, mouse: true }), '연결됨'); assert.equal(L.kbmWord({}), '연결 안 됨');
  assert.equal(L.kbmWord({ keyboard: true, mouse: false }), '키보드만 연결됨');
  assert.equal(L.clock(NOW), '21:47'); assert.equal(L.clock(new Date('2026-11-04T07:05:00')), '07:05');
  const at = hm => new Date(`2026-11-04T${hm}`);
  assert.equal(L.themeOf('auto', at('06:59:59')), 'dark'); assert.equal(L.themeOf('auto', at('07:00:00')), 'light');
  assert.equal(L.themeOf('auto', at('18:59:59')), 'light'); assert.equal(L.themeOf('auto', at('19:00:00')), 'dark');
  assert.equal(L.themeOf('light', at('23:00:00')), 'light'); assert.equal(L.themeOf('dark', at('12:00:00')), 'dark');
});

console.log(`logic.js: ${n}개 묶음 모두 통과`);
