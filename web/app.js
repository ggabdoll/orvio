// ORVIO 홈 화면. 값은 확장 메시지로 받는다(docs/bridge-protocol.md).
import { bridge } from './bridge.js';
import * as L from './logic.js';

const PROTOCOL = 1; // 메시지 약속 판
const $ = (s, r = document) => r.querySelector(s);
const $$ = (s, r = document) => [...r.querySelectorAll(s)];
const esc = s => String(s).replace(/[&<>"']/g, c => ({ '&': '&amp;', '<': '&lt;', '>': '&gt;', '"': '&quot;', "'": '&#39;' }[c]));
const QS = new URLSearchParams(location.search); // ?demo= ?input=kbm ?idle=초 : 시안과 같은 시험 쿼리
const REDUCED = matchMedia('(prefers-reduced-motion: reduce)').matches;
const now = () => bridge.now();
const logAt = new Map(); // 같은 로그는 5초에 한 번만
const log = (level, msg) => {
  msg = String(msg).slice(0, 500);
  const k = level + msg, t = performance.now();
  if (t - (logAt.get(k) ?? -1e9) < 5000) return;
  if (logAt.size > 200) logAt.clear(); // ponytail: 종류가 많으면 통째로 비운다
  logAt.set(k, t); bridge.send('log', { level, msg });
};
const lum = h => { const n = parseInt(h.slice(1), 16), f = v => (v /= 255) <= .03928 ? v / 12.92 : ((v + .055) / 1.055) ** 2.4; return .2126 * f(n >> 16) + .7152 * f(n >> 8 & 255) + .0722 * f(n & 255); };
const ink = c => (lum(c) + .05) / .069 > 1.05 / (lum(c) + .05) ? '#262626' : '#fff'; // 대비가 더 큰 글자색 (#262626 휘도 ≈ .019)
const mod = (a, n) => ((a % n) + n) % n;

/* ---------- 아이콘 (선 아이콘 한 벌, 잉크색) ---------- */
const S = (vb, body) => `<svg class="ic" viewBox="${vb}" aria-hidden="true">${body}</svg>`;
const st = 'fill="none" stroke="currentColor" stroke-width="2.6" stroke-linecap="round" stroke-linejoin="round"';
const IC = {
  pad: S('0 0 48 32', `<path d="M13 3h22c6 0 10 6 11 14s-1 12-5 12c-3 0-5-2-7-6H14c-2 4-4 6-7 6-4 0-6-4-5-12S7 3 13 3z" fill="none" stroke="currentColor" stroke-width="3" stroke-linejoin="round"/><path d="M14 11v9M9.5 15.5h9" stroke="currentColor" stroke-width="3" stroke-linecap="round"/><circle cx="33" cy="12.5" r="2.6" fill="currentColor"/><circle cx="38" cy="18" r="2.6" fill="currentColor"/>`),
  kbm: S('0 0 56 32', `<rect x="2" y="7" width="36" height="22" rx="4" fill="none" stroke="currentColor" stroke-width="3"/><path d="M8 14h3M15 14h3M22 14h3M29 14h3M10 22h20" stroke="currentColor" stroke-width="3" stroke-linecap="round"/><rect x="43" y="3" width="11" height="26" rx="5.5" fill="none" stroke="currentColor" stroke-width="3"/><path d="M48.5 4v7" stroke="currentColor" stroke-width="2.5"/>`),
  key: S('0 0 40 32', '<rect x="2" y="6" width="36" height="22" rx="4" fill="none" stroke="currentColor" stroke-width="3"/><path d="M8 13h3M15 13h3M22 13h3M29 13h3M10 21h20" stroke="currentColor" stroke-width="3" stroke-linecap="round"/>'),
  wifi: S('0 0 32 26', `<path d="M2 9a20 20 0 0 1 28 0M7 14a13 13 0 0 1 18 0M12 19a6 6 0 0 1 8 0" ${st} stroke-width="3"/><circle cx="16" cy="23" r="2.4" fill="currentColor"/>`),
  search: S('0 0 32 32', `<circle cx="14" cy="14" r="9" ${st} stroke-width="3.2"/><path d="M21 21l8 8" ${st} stroke-width="3.2"/>`),
  menu: S('0 0 32 32', `<path d="M6 9h20M6 16h20M6 23h20" ${st} stroke-width="3.2"/>`),
  vol: S('0 0 32 32', `<path d="M5 12h5l7-6v20l-7-6H5z" fill="currentColor"/><path d="M21 11a6 6 0 0 1 0 10M24.5 7a11 11 0 0 1 0 18" ${st}/>`),
  screen: S('0 0 32 32', `<rect x="3" y="5" width="26" height="17" rx="2.5" ${st}/><path d="M11 27h10M6 3l20 22" ${st}/>`),
  moon: S('0 0 32 32', `<path d="M25 19.5A10.5 10.5 0 0 1 12.5 7a10.5 10.5 0 1 0 12.5 12.5z" ${st}/>`),
  restart: S('0 0 32 32', `<path d="M26 16a10 10 0 1 1-3-7.1" ${st}/><path d="M24 3.5v6h-6" ${st}/>`),
  power: S('0 0 32 32', `<path d="M16 4v11" ${st}/><path d="M9.5 8.5a10 10 0 1 0 13 0" ${st}/>`),
  desk: S('0 0 32 32', `<rect x="3" y="5" width="26" height="21" rx="2.5" ${st}/><path d="M3 11h26" ${st}/>`),
  mouse: S('0 0 20 30', `<rect x="2" y="2" width="16" height="26" rx="8" fill="none" stroke="currentColor" stroke-width="2.6"/><path d="M10 3v8" stroke="currentColor" stroke-width="2.4"/>`),
  dialic: S('0 0 32 32', `<circle cx="16" cy="16" r="12" ${st}/><circle cx="16" cy="16" r="3.5" fill="currentColor"/><path d="M24 16h5" ${st}/>`),
  half: S('0 0 32 32', `<circle cx="16" cy="16" r="11" ${st}/><path d="M16 5a11 11 0 0 1 0 22z" fill="currentColor"/>`),
  check: S('0 0 32 32', `<path d="M6 17l7 7L26 9" ${st} stroke-width="3.4"/>`),
};

/* ---------- CD 면: 표지가 있으면 사진, 없으면 두 색 평면 + 도형(글자 없음). 웹은 서비스 색 + 첫 글자, 시스템은 무채색 + 아이콘 ---------- */
const art = x => `<svg class="mo" viewBox="0 0 300 400" preserveAspectRatio="xMidYMid slice" aria-hidden="true"><circle cx="300" cy="0" r="240" fill="${x.color[1]}" opacity=".16"/></svg>`;
const tone = h => '#' + [1, 3, 5].map(i => Math.round(parseInt(h.slice(i, i + 2), 16) * .7 + 0x3a * .3).toString(16).padStart(2, '0')).join(''); // 채도·밝기 한 단계 아래 (16진 색 두 자리 맞춤이라 번호 아님: taste-skill-ignore)
function faceHTML(it) {
  if (it.kind === 'game') { const x = it.g; return x.cover ? `<div class="face" style="background-image:url('${esc(x.cover)}')"></div>` : `<div class="face" style="background:${x.color[0]}">${art(x)}</div>`; }
  if (it.kind === 'web' && it.q == null && it.w.id !== 'search') { const c = it.w.color, d = tone(c); return `<div class="face svc" style="--c:${c};--i:${ink(c)};--cd:${d};--id:${ink(d)}"><b class="lt">${esc([...it.w.title][0] || '')}</b></div>`; } // 웹 검색은 무채색(보라는 포커스에만)
  return `<div class="face" style="background:var(--sysface);color:var(--ink)"><b class="lt">${it.kind === 'sys' ? IC[it.s.ic] : IC.search}</b></div>`;
}
const cdHTML = (it, cls) => `<div class="cdw ${cls}">${faceHTML(it)}</div>`;

/* ---------- 원판에 올라갈 항목과 분류 ---------- */
const SYS = [
  { id: 'menu', title: '빠른 메뉴', sub: '볼륨 · 연결된 기기 · 전원', ic: 'menu', note: '볼륨, 컨트롤러 배터리, 전원을 한곳에서 다뤄요.' },
  { id: 'screen', title: '화면 끄기', sub: '버튼을 누르면 다시 켜져요', ic: 'screen', p: 'screenOff' },
  { id: 'sleep', title: '절전', sub: 'PC를 절전 상태로 둬요', ic: 'moon', p: 'sleep' },
  { id: 'restart', title: '다시 시작', sub: 'PC를 껐다 켜요', ic: 'restart', p: 'restart' },
  { id: 'power', title: '시스템 종료', sub: 'PC를 꺼요', ic: 'power', p: 'shutdown' },
  { id: 'desk', title: '윈도우 바탕화면으로', sub: '이 화면을 닫고 윈도우로', ic: 'desk', p: 'desktop' },
];
const POWER_MSG = { screenOff: '화면을 끄는 중…', sleep: '절전 모드로 들어가는 중…', restart: '다시 시작하는 중…', shutdown: '시스템을 종료하는 중…', desktop: '윈도우 바탕화면으로 나가는 중…' };
const HOLD = { restart: '다시 시작해요', shutdown: '종료해요' }; // D-12: 다시 시작·시스템 종료는 A(Enter)를 1초 눌러야 함
let games = new Map(), GAMES = [], WEB = [], CATS = L.categories([], [], SYS);
let loading = true; // SCR-09: library 를 기다리는 동안
let status = { network: null, pads: [], kbm: {}, volume: 40 };
const asGame = g => ({ kind: 'game', g });
const byId = id => games.get(id);
const webById = id => WEB.find(w => w.id === id);
const inputChip = x => x.input === 'kbm' ? `<span class="chip">${IC.kbm}키보드·마우스</span>` : x.input === 'both' ? `<span class="chip">${IC.pad}+${IC.kbm}</span>` : '';
function itemHTML(it) {
  let t, m;
  if (it.kind === 'game') { t = it.g.title; m = `<span>${esc(L.cardSub(it.g, now()))}</span>${inputChip(it.g)}`; }
  else if (it.kind === 'web') { t = it.w.title; m = esc(L.host(it.w)); }
  else { t = it.s.title; m = it.s.sub; }
  return cdHTML(it, 'rd') + `<div class="cd"><div class="tt">${esc(t)}</div><div class="mt">${m}</div></div>`;
}

/* ---------- 원판 회전 ----------
   칸 번호 k는 끝없이 늘어나는 정수, 항목은 k mod N. 가운데(3시)가 P[cat].
   가운데에서 d칸 떨어진 칸은 원판 각도 A[d]에 놓이고 S[d]로 작아지며 O[d]로 흐려진다(40% 아래로는 안 흐림). */
const R_ANG = [0, 24, 36, 46, 54], R_SC = [1, .84, .72, .6, .5], R_OP = [1, 1, .6, .45, 0];
const MARK_W = [2.2, .84, .72, .6, .5]; // '처음' 눈금을 두 판 사이 빈틈에 두는 무게(가운데 판은 크게 빠져나와 있다)
const CAT_STEP = 13;
const ang = d => Math.sign(d) * R_ANG[Math.min(Math.abs(d), 4)];
let cat = 0, spinAcc = 0, instant = true;
const P = CATS.map(() => 0);
const slots = new Map(), marks = new Map();
const tv = $('#tv'), itemsEl = $('#items'), catsEl = $('#cats'), disc = $('#disc');
const N = () => CATS[cat].items.length;
const VIS = () => Math.max(0, Math.min(3, Math.floor((N() - 1) / 2))); // 같은 항목이 한 화면에 두 번 보이지 않게
const itemAt = k => CATS[cat].items[mod(k, N())];
const center = () => { const sp = slots.get(P[cat]); return sp ? sp.firstElementChild : null; };
const catLabel = () => $(`.cl[data-c="${cat}"]`);
const homeFocus = () => loading ? null : N() ? center() : catLabel(); // 빈 분류면 분류 링(SCR-10). 포커스 없는 상태를 만들지 않는다
function goHome() { const el = homeFocus(); if (el) return focusEl(el); if (cur) cur.classList.remove('focus'); cur = null; renderGuide(); } // 불러오는 중이면 포커스 없음

function spokeEl(html, d, v) {
  const sp = document.createElement('div');
  sp.className = 'spoke';
  sp.innerHTML = html;
  if (!instant) { sp.style.transition = 'none'; place(sp, Math.sign(d || 1) * (v + 1), v); } // 안 보이는 가장자리에서 출발
  itemsEl.appendChild(sp);
  if (!instant) { sp.offsetWidth; sp.style.transition = ''; }
  return sp;
}
function place(sp, d, v, a) {
  const ad = Math.abs(d), hid = ad > v, e = hid ? Math.sign(d) * (v + 1) : d;
  sp.style.transform = `rotate(${a ?? ang(e)}deg)`;
  sp.style.opacity = hid ? 0 : R_OP[ad];
  if (sp.firstElementChild.classList.contains('it')) sp.firstElementChild.style.transform = `scale(${R_SC[Math.min(ad, 4)]})`;
  sp.classList.toggle('hid', hid);
  sp.classList.toggle('nb', !hid && ad > 0);
  sp.classList.toggle('far', ad >= 2);
  sp.classList.toggle('c', d === 0);
  sp.style.zIndex = 10 - Math.min(ad, 9);
}
function keep(map, lo, hi, p, v) { // 범위를 벗어난 칸은 가장자리로 숨긴 뒤 지운다
  for (const [k, sp] of map) if (k < lo || k > hi) {
    place(sp, k - p, v);
    sp.style.opacity = 0; sp.classList.add('hid');
    if (!sp._rm) sp._rm = setTimeout(() => { sp.remove(); map.delete(k); }, 460);
  }
}
function renderDial() {
  const p = P[cat], v = VIS(), n = N(), lo = p - v - 1, hi = p + v + 1;
  disc.style.transform = `rotate(${spinAcc}deg)`;
  if (!n) { $('#cnt').textContent = ''; instant = false; return; } // SCR-10: 원판에 CD 없음
  for (let k = lo; k <= hi; k++) {
    let sp = slots.get(k);
    if (!sp) { sp = spokeEl(`<div class="it" data-f data-g="dial" data-k="${k}">${itemHTML(itemAt(k))}</div>`, k - p, v); slots.set(k, sp); }
    clearTimeout(sp._rm); sp._rm = 0;
    place(sp, k - p, v);
    if (mod(k, n) === 0) { // '처음' 눈금: 마지막 항목(k-1)과 첫 항목(k) 사이
      let m = marks.get(k);
      if (!m) { m = spokeEl('<div class="fm"><b>처음</b></div>', k - p, v); marks.set(k, m); }
      clearTimeout(m._rm); m._rm = 0;
      const d = k - p, far = Math.max(Math.abs(d), Math.abs(d - 1)), near = Math.min(Math.abs(d), Math.abs(d - 1));
      const cl = x => Math.max(-v - 1, Math.min(v + 1, x)), s0 = MARK_W[Math.min(Math.abs(d), 4)], s1 = MARK_W[Math.min(Math.abs(d - 1), 4)];
      place(m, far > v ? d * 9 : 0, v, ang(cl(d)) + (ang(cl(d - 1)) - ang(cl(d))) * s0 / (s0 + s1)); // 두 아이콘 크기에 맞춘 틈
      m.style.opacity = far > v ? 0 : R_OP[near];
    }
  }
  keep(slots, lo, hi, p, v);
  keep(marks, lo, hi, p, v);
  $('#cnt').textContent = `${mod(p, n) + 1} / ${n}`;
  instant = false;
}
function wipeDial() { for (const sp of [...slots.values(), ...marks.values()]) { clearTimeout(sp._rm); sp.remove(); } slots.clear(); marks.clear(); instant = true; }
/* ---------- 효과음: 소리는 확장이 튼다(이 화면은 이름만 보낸다) ---------- */
const sfxAt = {};
function sfx(name) {
  const t = performance.now();
  if (t - (sfxAt[name] ?? -1e9) < 70) return; // 키보드 자동 반복이 메시지를 쌓지 않게
  sfxAt[name] = t;
  bridge.send('sfx', { name });
}

/* 오른쪽 글자 전환. 누르고 있어 빠르게 넘어갈 때는 글자가 깜빡이지 않게 건너뛴다 */
let detailAnims = [], lastStep = -1e9;
function slideDetail(dir) {
  const t = performance.now(), fast = t - lastStep < 200;
  lastStep = t;
  detailAnims.forEach(a => a.cancel());
  detailAnims = REDUCED || fast ? [] : ['#dT', '#dM', '#dC', '#dR', '#dN'].map(q => $(q).animate(
    [{ opacity: 0, transform: `translateY(${Math.sign(dir) * 14}px)` }, { opacity: 1, transform: 'none' }],
    { duration: 260, easing: 'cubic-bezier(.22,1,.36,1)' }));
  if (REDUCED || fast) return;
  // 새로 온 CD 의 표지가 구르다가 똑바로 멈춘다. 바로 다음 칸으로 넘겨도 튀지 않게 취소하지 않고 끝까지 둔다
  const face = center()?.querySelector('.face');
  if (face) face.animate([{ transform: `rotate(${Math.sign(dir) * -150}deg)` }, { transform: 'none' }], { duration: 1040, easing: 'cubic-bezier(.22,1,.36,1)' });
}
function userTurn(steps) { // 사용자가 돌릴 때만 소리와 글자 전환(대기 회전은 조용히)
  if (!N()) return;
  turn(steps); sfx('navigation'); slideDetail(steps);
}

function turn(steps) {
  if (!steps || !N()) return;
  P[cat] += steps; spinAcc -= steps * 15; // 원판 무늬도 항목과 같은 각도로 돈다
  renderDial();
  if (cur && cur.dataset.g === 'dial') focusEl(center());
  showItem(itemAt(P[cat]));
}
function updateCats() {
  catsEl.style.transform = `rotate(${-cat * CAT_STEP}deg)`;
  $$('.cs', catsEl).forEach((sp, j) => {
    const o = Math.abs(j - cat);
    sp.style.opacity = [1, 1, .85][o] ?? 0;
    sp.classList.toggle('hid', o > 2);
    sp.firstElementChild.nextElementSibling.classList.toggle('on', o === 0);
  });
}
function setCat(i) {
  i = Math.max(0, Math.min(CATS.length - 1, i)); // 분류 링은 끝에서 멈춘다
  if (i === cat) return;
  const dir = Math.sign(i - cat), g = cur && cur.dataset.g;
  cat = i;
  wipeDial();
  itemsEl.style.transition = 'none'; itemsEl.style.transform = `rotate(${dir * 30}deg)`; itemsEl.style.opacity = 0;
  spinAcc -= dir * 30;
  renderDial();
  itemsEl.offsetWidth;
  itemsEl.style.transition = ''; itemsEl.style.transform = ''; itemsEl.style.opacity = '';
  updateCats();
  const cnt = $('#cnt'); cnt.classList.add('swap'); clearTimeout(cnt._t); cnt._t = setTimeout(() => cnt.classList.remove('swap'), 380); // 링 글자가 개수 위를 지나는 동안 숨김
  if (g === 'ring' || (!N() && (g === 'dial' || g === 'act'))) focusEl(catLabel()); else if (g === 'dial') focusEl(center());
  showItem(itemAt(P[cat]));
  sfx('navigation'); slideDetail(dir);
}
function redraw() { // 목록이 바뀌었거나 날짜 낱말을 다시 셀 때: 보던 칸 그대로 다시 그린다(화면을 새로 고치지 않음, FR-06)
  const g = cur && cur.dataset.g;
  wipeDial(); renderDial(); updateCats();
  if (g === 'dial' || (g === 'act' && !N())) focusEl(homeFocus());
  showItem(itemAt(P[cat]));
}

/* ---------- 홈 배경 (명세 4.4, FR-29) ---------- */
// 3시 항목이 마지막으로 바뀌고 260ms 뒤에 바꾼다. 방향을 누르고 있는 동안은 미룬다. 웹·시스템·표지 없는 게임은 회색 바탕 그대로
// 층 3장을 돌려 쓴다: 빠르게 넘겨도 아직 사라지는 중인 장의 그림을 갈아 끼우지 않게(그림이 툭 바뀜)
let bgT = 0, bgFlip = 0, bgCur = null, arrowHeld = false;
function applyBg(it) {
  clearTimeout(bgT);
  const c = it && it.kind === 'game' ? it.g.cover : null;
  bgT = setTimeout(function fire() {
    if (held || arrowHeld) { bgT = setTimeout(fire, 60); return; } // 떼는 즉시 이어지게 짧게 다시 본다
    if (c === bgCur) return;
    bgCur = c;
    const ps = $$('.bgp');
    if (c) { bgFlip = (bgFlip + 1) % 3; ps[bgFlip].style.backgroundImage = `url(${JSON.stringify(c)})`; }
    ps.forEach((p, i) => p.classList.toggle('on', !!c && i === bgFlip));
  }, 260);
}

/* ---------- 오른쪽 상세 ---------- */
const verb = it => !it ? '' : it.kind === 'game' ? '플레이' : it.kind === 'web' ? '열기' : '실행';
function showItem(it) {
  const T = $('#dT'), set = (id, v) => { $(id).innerHTML = v; };
  $('#acts').style.visibility = it ? '' : 'hidden'; // SCR-10: [플레이] 자리도 비운다
  applyBg(it);
  if (!it) { // SCR-10 빈 분류 · 빈 라이브러리
    const none = !GAMES.length;
    T.textContent = none ? '아직 게임이 없어요' : '이 분류에 맞는 게임이 없어요';
    set('#dM', none ? 'Playnite에서 Steam·Epic 같은 라이브러리를 연결하면 여기에 나와요.' : 'Playnite에서 게임에 태그를 달면 여기에 나와요.');
    set('#dC', ''); set('#dR', ''); set('#dN', '');
    return renderGuide();
  }
  $('#bPlayL').textContent = verb(it);
  if (it.kind === 'game') {
    const x = it.g;
    T.textContent = x.title;
    set('#dM', esc(L.metaLine(x)));
    set('#dC', `<span class="chip">${esc(x.source)}</span>` + (x.input === 'kbm' ? `<span class="chip">${IC.kbm}키보드·마우스 필요</span>` : x.input === 'both' ? `<span class="chip">${IC.pad}컨트롤러 · ${IC.kbm}키보드·마우스</span>` : `<span class="chip">${IC.pad}컨트롤러</span>`));
    set('#dR', esc(L.recordLine(x, now())).replace(/^[\d,]+/, '<span class="num">$&</span>'));
    set('#dN', x.input === 'kbm' ? '시작하기 전에 키보드·마우스 연결을 확인해 드려요.' : `${esc(x.source)}에서 바로 시작해요.`);
  } else if (it.kind === 'web') {
    const w = it.w, s = w.id === 'search';
    T.textContent = w.title;
    set('#dM', s ? '화면 키보드로 검색어를 넣어요' : `${esc(L.host(w))} · 브라우저 전체화면`);
    set('#dC', `<span class="chip">${IC.pad}컨트롤러로 조작</span>`);
    set('#dR', '왼쪽 스틱이 마우스 커서가 돼요.');
    set('#dN', '열면 조작법을 먼저 보여 드려요.');
  } else {
    const x = it.s;
    T.textContent = x.title;
    set('#dM', x.sub);
    set('#dC', '');
    set('#dR', x.note || '');
    set('#dN', '실행하면 바로 적용돼요.');
  }
  renderGuide();
}

/* ---------- 화면 테마: 라이트 · 다크 · 자동(PC 시계로 19:00~06:59 다크, 정각에 바뀜) ---------- */
const THEMES = ['light', 'dark', 'auto'];
let themePref = 'auto', themeT = 0;
function setTheme(p, anim) {
  themePref = p;
  const t = L.themeOf(p, now());
  if (anim && !REDUCED && tv.dataset.theme !== t) { tv.classList.add('theming'); clearTimeout(themeT); themeT = setTimeout(() => tv.classList.remove('theming'), 320); } // 짧게 전환
  tv.dataset.theme = t;
  $$('#seg i').forEach(i => i.classList.toggle('on', i.dataset.t === p));
}
function userTheme(p) { if (p === themePref) return; setTheme(p, true); bridge.send('setSetting', { key: 'theme', value: p }); }

/* ---------- 버튼 모양 (마지막 입력 장치에 따라) ---------- */
let mode = QS.get('input') === 'kbm' ? 'kbm' : 'pad';
const gb = l => `<span class="gb">${l}</span>`;
const kc = t => `<span class="kc">${t}</span>`;
const dpad = hi => `<span class="gd"><svg viewBox="0 0 30 30" aria-hidden="true"><path d="M11 2h8v9h9v8h-9v9h-8v-9H2v-8h9z" style="fill:var(--dpad0)"/>${hi.includes('u') ? '<path d="M11 2h8v9h-8z" style="fill:var(--ink)"/>' : ''}${hi.includes('d') ? '<path d="M11 19h8v9h-8z" style="fill:var(--ink)"/>' : ''}${hi.includes('l') ? '<path d="M2 11h9v8H2z" style="fill:var(--ink)"/>' : ''}${hi.includes('r') ? '<path d="M19 11h9v8h-9z" style="fill:var(--ink)"/>' : ''}</svg></span>`; // 눌리는 팔만 잉크색
const GLYPH = {
  pad: { ok: gb('A'), back: gb('B'), x: gb('X'), search: gb('Y'), lb: '<span class="gs">LB</span>', rb: '<span class="gs">RB</span>', menu: gb(IC.menu), lr: dpad('lr'), ud: dpad('ud'), left: dpad('l'), ls: gb('L'), rs: gb('R') },
  kbm: { ok: kc('Enter'), back: kc('Esc'), x: kc('Backspace'), search: kc('/'), lb: kc('Q'), rb: kc('E'), menu: kc('M'), lr: kc('←') + kc('→'), ud: kc('↑') + kc('↓'), left: kc('←'), type: kc('가A'), click: kc(IC.mouse + '클릭'), wheel: kc(IC.mouse + '휠') },
};
const gl = (a, m = mode) => GLYPH[m][a] || '';

/* ---------- 고정 조각 ---------- */
function renderStatic() {
  $$('[data-ic]').forEach(e => e.innerHTML = IC[e.dataset.ic]);
  catsEl.innerHTML = CATS.map((c, j) => `<div class="spoke cs" style="transform:rotate(${j * CAT_STEP}deg)"><div class="ctk"></div><div class="cl" data-f data-g="ring" data-c="${j}"><span>${c.name}</span></div></div>`).join('');
  $('#qmUser').textContent = '빠른 메뉴';
  $('#whoMk').innerHTML = $('#loadNote .mk').innerHTML;
  $('#webHelp').innerHTML = [[['ls'], '마우스 커서 움직이기', '왼쪽 스틱'], [['ok'], '클릭', ''], [['rs'], '스크롤', '오른쪽 스틱'], [['search'], '화면 키보드 열기', ''], [['back'], '뒤로 가기', ''], [['lb', 'rb'], '브라우저 탭 이동', ''], [['menu'], '이 홈 화면으로 돌아오기', '길게 누르기']]
    .map(([as, d, n]) => `<li><span class="wg">${as.map(a => gl(a, 'pad')).join('')}</span><b>${d}</b>${n ? `<span class="wl">${n}</span>` : ''}</li>`).join('');
  const KB = [[...'ㄱㄴㄷㄹㅁㅂㅅㅇㅈㅊ'], [...'ㅋㅌㅍㅎ', ['space', '띄어쓰기'], ['del', '지우기'], ['clear', '모두 지우기']], [...'ABCDEFGHIJ'], [...'KLMNOPQRST'], [...'UVWXYZ', "'", ':', '-', '.'], [...'1234567890']];
  $('#kb').innerHTML = KB.map((row, r) => `<div class="kr${r === 2 || r === 5 ? ' gap' : ''}">${row.map(k => {
    const [v, lab] = Array.isArray(k) ? k : [k, k];
    return `<div class="key${Array.isArray(k) ? ' w2' : ''}" data-f data-g="keys" data-act="key" data-k="${esc(v)}" data-r="${r}">${esc(lab)}</div>`;
  }).join('')}</div>`).join('');
}
/* ---------- 상태 줄 · 키마 확인 칸 · 빠른 메뉴 기기 (status 메시지) ---------- */
function renderStatus() {
  const s = status, net = s.network, k = s.kbm, kw = L.kbmWord(k);
  $('#bar').innerHTML =
    (!net || net.kind === 'wired' ? '' : `<span class="st" data-tip="${net.kind === 'none' ? '인터넷 연결 안 됨' : '와이파이 신호 ' + L.wifiWord(net.strength)}">${IC.wifi}</span>`) + // 유선이면 아이콘을 숨김(D-14)
    s.pads.map(p => { const b = L.battery(p.battery); return `<span class="st num" data-tip="${esc(p.name ?? '컨트롤러')} · ${b.tip}">${IC.pad}${b.label}</span>`; }).join('') +
    `<span class="st" data-tip="키보드·마우스 ${kw}">${IC.kbm}${k.keyboard ? '<span class="okdot"></span>' : ''}</span>` +
    `<span class="clock num">${L.clock(now())}</span>` +
    `<span class="ib" data-cmd="search" role="button" aria-label="검색" data-tip="검색 · 컨트롤러 Y · 키보드 /">${IC.search}</span><span class="ib" data-cmd="menu" role="button" aria-label="빠른 메뉴" data-tip="빠른 메뉴 · 컨트롤러 메뉴 버튼 · 키보드 M">${IC.menu}</span>`; // 마우스만 쓰는 사람용. 방향키 포커스 경로에는 넣지 않는다
  $('#kbmConn').innerHTML = [[IC.key, '키보드', k.keyboard], [IC.mouse, '마우스', k.mouse]].map(([ic, n, on]) => `<div>${ic}${n}<span class="ok">${on ? IC.check + '연결됨' : '연결 안 됨'}</span></div>`).join('');
  $('#qmDev').innerHTML = s.pads.map(p => { const b = L.battery(p.battery); return `<div class="dev">${IC.pad}${esc(p.name ?? '컨트롤러')}<span class="r num">${b.fill == null ? '' : `<span class="bbar"><i style="width:${b.fill}%"></i></span>`}${b.label}</span></div>`; }).join('') +
    `<div class="dev">${IC.kbm}키보드·마우스<span class="r">${k.keyboard && k.mouse ? IC.check + '연결됨' : kw}</span></div>`;
}

/* ---------- 포커스 · 이동 ---------- */
let cur = null, lastKey = null, stick = null;
const stack = [];
const layer = () => stack[stack.length - 1];
const OV = { launch: $('#ovLaunch'), kbm: $('#ovKbm'), web: $('#ovWeb'), search: $('#ovSearch'), menu: $('#ovMenu') };
const q = $('#q'), resList = $('#resList');
const resItems = () => $$('[data-f]', resList);
const keysInRow = r => $$(`#kb [data-r="${r}"]`);
const step = (Ls, el, d) => Ls[Ls.indexOf(el) + d] || null; // 끝에서는 멈춘다
const cx = e => e.offsetLeft + e.offsetWidth / 2;
const G = {
  dial(d) {
    if (d === 'up' || d === 'down') { userTurn(d === 'down' ? 1 : -1); return null; } // 원판은 순환한다
    return d === 'left' ? catLabel() : $('#acts [data-f]');
  },
  ring(d) {
    if (d === 'up' || d === 'down') { setCat(cat + (d === 'down' ? 1 : -1)); return catLabel(); }
    return d === 'right' ? center() : null;
  },
  act(d, el) {
    const Ls = $$('#acts [data-f]'), i = Ls.indexOf(el);
    if (d === 'left') return i ? Ls[i - 1] : center();
    return d === 'right' ? Ls[i + 1] || null : null;
  },
  keys(d, el) {
    const r = +el.dataset.r, row = keysInRow(r), i = row.indexOf(el);
    if (d === 'left') return row[i - 1] || null;
    if (d === 'right') return row[i + 1] || resItems()[0] || null;
    const x = stick && stick.el === el ? stick.x : cx(el), nr = keysInRow(r + (d === 'down' ? 1 : -1));
    if (!nr.length) return null;
    const nx = nr.reduce((b, k) => Math.abs(cx(k) - x) < Math.abs(cx(b) - x) ? k : b);
    stick = { el: nx, x }; // 넓은 키를 지나도 처음 열로 돌아오게
    return nx;
  },
  res: (d, el) => d === 'up' || d === 'down' ? step(resItems(), el, d === 'down' ? 1 : -1) : d === 'left' ? (lastKey && lastKey.isConnected ? lastKey : keysInRow(0)[0]) : null,
  menu(d, el) {
    if (d === 'up' || d === 'down') return step($$('[data-f]', OV.menu), el, d === 'down' ? 1 : -1);
    if (el.id === 'vol') setVol(vol + (d === 'right' ? 5 : -5), true);
    if (el.id === 'thm') { const t = THEMES[THEMES.indexOf(themePref) + (d === 'right' ? 1 : -1)]; if (t) userTheme(t); renderGuide(); }
    if (el.id === 'idl') userIdle(d === 'right');
    return null;
  },
  dlg: (d, el) => d === 'left' || d === 'right' ? step($$('[data-f]', layer().el), el, d === 'right' ? 1 : -1) : null,
};
function nav(d) { const nx = cur && G[cur.dataset.g](d, cur); if (nx) focusEl(nx); }
function focusEl(el, mouse) {
  if (!el) return;
  if (cur !== el) { if (cur) cur.classList.remove('focus'); cur = el; el.classList.add('focus'); holdCancel(); }
  const g = el.dataset.g;
  if (g === 'keys') lastKey = el;
  else if (g === 'res' && !mouse) scrollRes(el);
  renderGuide();
}
function scrollRes(el) {
  const y0 = el.offsetTop - 8, bot = el.offsetTop + el.offsetHeight + 8;
  if (y0 < resList.scrollTop) resList.scrollTop = y0;
  else if (bot > resList.scrollTop + resList.clientHeight) resList.scrollTop = bot - resList.clientHeight;
}

/* ---------- 열린 창 (스택: 닫으면 연 자리로 돌아감) ---------- */
const inTop = el => layer() ? layer().el.contains(el) : !el.closest('.ov');
const usable = el => el && el.isConnected && inTop(el) && !(el.dataset.g === 'act' && !N());
function openLayer(name, ret = cur) {
  stack.push({ name, el: OV[name], ret });
  OV[name].style.zIndex = 50 + stack.length; // 나중에 연 창이 늘 위 (안내 줄 90보다 아래)
  OV[name].classList.add('open');
  syncInput();
}
function closeTop() {
  const t = stack.pop();
  if (!t) return;
  t.el.classList.remove('open');
  syncInput();
  if (usable(t.ret)) focusEl(t.ret); else if (layer()) focusEl($('[data-f]', layer().el)); else goHome();
}
function dropLayer(name) { // 맨 위가 아니어도 그 창만 뺀다
  const i = stack.findIndex(s => s.name === name);
  if (i < 0) return;
  if (i === stack.length - 1) return closeTop();
  stack.splice(i, 1)[0].el.classList.remove('open');
}
function closeAll() { while (stack.length) stack.pop().el.classList.remove('open'); syncInput(); }
function syncInput() { if (layer() && layer().name === 'search') q.focus({ preventScroll: true }); else q.blur(); }

/* ---------- 실행 (SCR-03 실행 중 · SCR-04 키마 확인 · SCR-11 실행 실패) ---------- */
let kbmGame = null, launch = null, launchFailed = false; // launch: { id, cat } 켠 게임과 켠 분류(D-15)
let running = null, lastStop = null; // running: 켜는 중이거나 도는 게임 하나 { id, state }. gameState 로만 바뀐다
const isLaunching = id => !!running && running.id === id && running.state === 'launching';
let watchT = 0; // launch 를 보낸 뒤 5초 안에 gameState 가 없으면 실패로 본다 (확장이 조용히 거절한 경우)
function openGame(x) {
  if (x.input !== 'kbm') return openLaunch(x);
  kbmGame = x;
  $('#kbmGame').innerHTML = `<div><b>${esc(x.title)}</b><span>${esc([x.source, x.genres[0], L.kbmLine(x)].filter(Boolean).join(' · '))}</span></div>`;
  openLayer('kbm');
  focusEl($('#ovKbm [data-act="run"]'));
}
function runKbm() { const t = stack.pop(); t.el.classList.remove('open'); openLaunch(kbmGame, t.ret); }
function startGame(x) { // 상태는 확장이 보낸 gameState 만 믿는다
  launch = { id: x.id, cat };
  sfx('activation');
  bridge.send('launch', { id: x.id });
  clearTimeout(watchT);
  watchT = setTimeout(() => { log('warn', `launch 답이 없어요: ${x.id}`); showFailed(x.id); }, 5000);
}
function showFailed(id) { const x = byId(id); if (x && launch && launch.id === id && stack.some(s => s.name === 'launch')) launchView(x, true); else toast('게임이 시작되지 않았어요'); }
function openLaunch(x, ret) {
  if (!isLaunching(x.id)) startGame(x); // 이미 켜는 중이면 화면만 다시 보여 주고 launch 는 또 보내지 않는다
  else if (!launch || launch.id !== x.id) launch = { id: x.id, cat };
  $('#launchCov').innerHTML = cdHTML(asGame(x), 'bigcd');
  $('#launchT').textContent = x.title;
  launchView(x, false);
  openLayer('launch', ret);
  focusEl($('#ovLaunch [data-f]'));
}
const btnHTML = (pri, act, g, label) => `<div class="btn${pri ? ' pri' : ''}" data-f data-g="dlg" data-act="${act}"><span data-gl="${g}">${gl(g)}</span>${label}</div>`;
function launchView(x, failed) { // 실패는 실행 중 화면 그 자리에서 글과 버튼만 바뀐다
  launchFailed = failed;
  $('#launchSpin').style.display = failed ? 'none' : '';
  $('#launchMsg').textContent = failed ? '게임이 시작되지 않았어요' : `${x.source}에서 실행 중…`;
  $('#launchP').textContent = failed ? `${x.source} 창을 열어 로그인이나 업데이트가 남았는지 확인해 주세요.` : '게임 창이 뜨면 이 화면은 뒤로 물러나요.';
  $('#launchActs').innerHTML = (failed ? btnHTML(true, 'retry', 'ok', '다시 시도') : '') + btnHTML(false, 'close', 'back', '홈으로 돌아가기'); // D-7: 실행을 멈출 수 없으니 '취소'라 쓰지 않음
  if (layer() && layer().name === 'launch') focusEl($('#ovLaunch [data-f]'));
}
function retry() {
  const x = launch && byId(launch.id);
  if (!x) return closeTop();
  if (isLaunching(x.id)) return toast('아직 켜는 중이에요'); // 두 번 실행되지 않게
  startGame(x);
  launchView(x, false);
}
let webAt = -1e9;
function openWeb(w, qv) {
  if (performance.now() - webAt >= 2000) { webAt = performance.now(); sfx('activation'); bridge.send('openWeb', qv != null ? { query: qv } : { id: w.id }); } // 2초 안에 또 누르면 화면만 (확장이 조용히 버림)
  const it = { kind: 'web', w, q: qv };
  $('#webCov').innerHTML = cdHTML(it, 'bigcd');
  $('#webT').textContent = qv != null ? `“${qv}” 검색` : w.title;
  openLayer('web');
  focusEl($('#ovWeb [data-f]'));
}
function openSearch() { if (loading) return; q.value = ''; renderResults(); openLayer('search'); focusEl(keysInRow(0)[0]); }
function openMenu() { openLayer('menu'); focusEl($('#vol')); }
let vol = 40, volAt = -1e9, volT = 0;
function setVol(v, user) {
  v = Math.max(0, Math.min(100, Math.round(v)));
  if (user) { volAt = performance.now(); if (v !== vol && !volT) volT = setTimeout(() => { volT = 0; bridge.send('volume', { value: vol }); }, 100); } // 100ms 에 한 번, 마지막 값만
  vol = v; $('#volV').textContent = vol; $('#volBar').style.width = vol + '%'; renderGuide();
}
let toastT = 0;
function toast(m) { const t = $('#toast'); t.textContent = m; t.classList.add('on'); clearTimeout(toastT); toastT = setTimeout(() => t.classList.remove('on'), 2400); }

/* ---------- 전원: 다시 시작·시스템 종료는 1초 길게 눌러야 실행 (D-12) ---------- */
let hold = null;
const runningId = () => running && running.id; // 켜는 중이거나 도는 게임
const gameBlocks = p => p !== 'desktop' && runningId() && (toast('게임을 끈 뒤에 할 수 있어요'), true); // 게임 중에는 확장이 바탕화면 말고는 조용히 거절한다: 대신 알린다
function runPower(p) { if (gameBlocks(p)) return; toast(POWER_MSG[p]); bridge.send('power', { action: p }); }
function power(p) {
  if (gameBlocks(p)) return; // 길게 누르기도 시작하지 않는다
  if (!HOLD[p]) return runPower(p);
  holdCancel();
  hold = { p, mouse: mouseHold, t: setTimeout(() => { hold = null; runPower(p); }, 1000) };
}
function holdEnd() { // 1초 전에 뗐으면 알림으로 알려 준다
  if (!hold) return;
  const { p, mouse } = hold;
  holdCancel();
  toast(mouse ? `1초 누르고 있으면 ${HOLD[p]}` : `${mode === 'kbm' ? 'Enter를' : 'A를'} 길게 누르면 ${HOLD[p]}`);
}
function holdCancel() { if (hold) { clearTimeout(hold.t); hold = null; } }
function holdsOn(el) { // 마우스로 누를 때 '누른 채 1초'가 되는 항목인지
  const a = el.dataset.act || el.dataset.g, it = a === 'dial' ? itemAt(+el.dataset.k) : a === 'play' ? itemAt(P[cat]) : null;
  return !!HOLD[a === 'sys' ? el.dataset.p : it && it.kind === 'sys' ? it.s.p : ''];
}

/* ---------- 검색 (규칙은 logic.js) ---------- */
let results = [];
function renderResults() {
  const v = q.value.trim(), r = L.search(GAMES, WEB, v), sw = webById('search');
  results = [...r.games.map(asGame), ...r.webs.map(w => ({ kind: 'web', w })), ...(v && sw ? [{ kind: 'web', w: sw, q: v }] : [])];
  $('#resH').textContent = v ? `게임 ${r.total}개${r.webs.length ? ` · 웹 ${r.webs.length}개` : ''}` : '최근 플레이';
  resList.innerHTML = results.map((it, i) => {
    let t, s;
    if (it.kind === 'game') { const x = it.g; t = esc(x.title); s = esc([x.source, x.genres[0]].filter(Boolean).join(' · ')) + inputChip(x); }
    else if (it.q != null) { t = `웹에서 “${esc(it.q)}” 찾기`; s = '브라우저로 검색 결과 열기'; }
    else { t = esc(it.w.title); s = `웹 바로가기 · ${esc(L.host(it.w))}`; }
    return `<div class="ri" data-f data-g="res" data-act="res" data-i="${i}">${cdHTML(it, 'rc')}<div class="rt"><b>${t}</b><span>${s}</span></div></div>`;
  }).join('');
  resList.scrollTop = 0;
}
function typeKey(k) {
  if (k === 'del') q.value = [...q.value].slice(0, -1).join('');
  else if (k === 'clear') q.value = '';
  else q.value += k === 'space' ? ' ' : k;
  renderResults();
  if (!cur.isConnected) focusEl(resItems()[0] || keysInRow(0)[0]);
}
q.addEventListener('input', () => { renderResults(); focusEl((q.value.trim() && resItems()[0]) || (lastKey && lastKey.isConnected ? lastKey : keysInRow(0)[0])); }); // 결과가 0개면 화면 키보드로 // 실제 키보드로 치면 첫 결과로. 다 지우면 화면 키보드로 (빈 검색어 + Enter = 아무 일 없음)

/* ---------- 실행 ---------- */
function actItem(it) {
  if (!it) return;
  if (it.kind === 'game') return openGame(it.g);
  if (it.kind === 'web') return openWeb(it.w);
  return it.s.id === 'menu' ? openMenu() : power(it.s.p);
}
function activate(el) {
  if (!el || (loading && !layer())) return;
  const a = el.dataset.act || el.dataset.g;
  if (a === 'dial') { turn(+el.dataset.k - P[cat]); return actItem(itemAt(P[cat])); } // 옆 칸을 누르면 그 칸이 가운데로 돌아온 뒤 실행
  if (a === 'ring') { setCat(+el.dataset.c); return focusEl(homeFocus()); }
  if (a === 'play') return actItem(itemAt(P[cat]));
  if (a === 'search') return openSearch();
  if (a === 'menu') return openMenu();
  if (a === 'vol') return; // 볼륨 줄은 ←→로 (Enter가 메뉴를 또 열지 않게)
  if (a === 'thm') return userTheme(THEMES[(THEMES.indexOf(themePref) + 1) % 3]);
  if (a === 'idl') return userIdle(!idleOn);
  if (a === 'key') return typeKey(el.dataset.k);
  if (a === 'res') { const r = results[+el.dataset.i]; if (!r) return; return r.kind === 'game' ? openGame(r.g) : openWeb(r.w, r.q); }
  if (a === 'run') return runKbm();
  if (a === 'retry') return retry();
  if (a === 'close') return closeTop();
  if (a === 'sys') return power(el.dataset.p);
}
function backMain() {
  const g = cur && cur.dataset.g;
  if (g !== 'dial') return focusEl(center());
  const i = mod(P[cat], N()); // 원판에서 B = 처음 항목으로 (가까운 쪽으로 돈다)
  if (i) userTurn(i <= N() / 2 ? -i : N() - i);
}
function handle(a) {
  const Ly = layer();
  if (loading && !Ly && a !== 'menu') return; // SCR-09: 불러오는 동안은 메뉴만
  if (a === 'up' || a === 'down' || a === 'left' || a === 'right') return nav(a);
  if (a === 'ok') return activate(cur);
  if (a === 'back') return Ly ? closeTop() : backMain();
  if (a === 'x') { if (Ly && Ly.name === 'search') typeKey('del'); return; }
  if (a === 'search') { if (!Ly) openSearch(); return; }
  if (a === 'lb' || a === 'rb') { if (!Ly) setCat(cat + (a === 'rb' ? 1 : -1)); return; }
  if (a === 'menu') { if (Ly && Ly.name === 'menu') closeTop(); else openMenu(); }
}

/* ---------- 조작 안내 줄 ---------- */
function hintsFor() {
  const Ly = layer() && layer().name, gn = cur && cur.dataset.g;
  if (!Ly && loading) return [[['menu'], '메뉴']]; // SCR-09
  if (Ly === 'launch') return launchFailed ? [[['lr'], '고르기'], [['ok'], '확인'], [['back'], '홈으로 돌아가기']] : [[['back'], '홈으로 돌아가기'], [['menu'], '메뉴']];
  if (Ly === 'kbm') return [[['lr'], '고르기'], [['ok'], '확인'], [['back'], '취소']];
  if (Ly === 'web') return [[['back'], '홈으로 돌아가기'], [['menu'], '메뉴']];
  if (Ly === 'menu' && cur.id === 'idl') return [[['lr'], '끄기 · 켜기'], [['ud'], '이동'], [['back'], '닫기']];
  if (Ly === 'menu' && cur.id === 'thm') return [[['lr'], '테마 고르기'], [['ud'], '이동'], [['back'], '닫기']];
  if (Ly === 'menu') return cur.id === 'vol' ? [[['lr'], '볼륨'], [['ud'], '이동'], [['back'], '닫기']] : [[['ud'], '이동'], [['ok'], '선택'], [['back'], '닫기']];
  if (Ly === 'search') {
    if (gn === 'res') return [[['ok'], '실행'], [['left'], '키보드로'], [['back'], '닫기']];
    return mode === 'kbm' ? [[['type'], '바로 입력'], [['ok'], '첫 결과 실행'], [['back'], '닫기']] : [[['ok'], '입력'], [['x'], '지우기'], [['back'], '닫기']];
  }
  if (gn === 'ring' && !N()) return [[['ud'], '분류 돌리기'], [['search'], '검색'], [['menu'], '메뉴']]; // SCR-10
  if (gn === 'ring') return [[['ud'], '분류 돌리기'], [['ok'], '고르기'], [['back'], '원판으로']];
  if (gn === 'act') return [[['ok'], verb(itemAt(P[cat]))], [['left'], '원판으로'], [['search'], '검색'], [['menu'], '메뉴']];
  const H = [[['ok'], verb(itemAt(P[cat]))], [['ud'], '돌리기'], [['lb', 'rb'], '분류']];
  if (mod(P[cat], N())) H.push([['back'], '처음으로']);
  H.push([['search'], '검색'], [['menu'], '메뉴']);
  H.noClick = true;
  return H;
}
function renderGuide() {
  if (tv.classList.contains('idle')) { $('#guide').innerHTML = '<span class="h idle-note">대기 중 · 아무 버튼이나 누르면 멈춰요</span>'; return fitBrand(); }
  let H = hintsFor();
  if (mode === 'kbm' && !H.noClick && !H.some(h => h[0].includes('click') || h[0].includes('type'))) H = H.map(([as, l]) => [as[0] === 'ok' ? [...as, 'click'] : as, l]);
  $('#guide').innerHTML = H.map(([as, l]) => `<span class="h"><span>${as.map(a => gl(a)).join('')}</span>${l}</span>`).join('');
  fitBrand();
}
function fitBrand() { // 왼쪽 아래 로고: 안내 줄(키보드 안내 등)이 24px 안으로 다가오면 숨긴다
  const b = $('#brandW');
  b.classList.toggle('off', $('#guide').offsetLeft < b.offsetLeft + b.offsetWidth + 24);
}
function applyMode() {
  tv.classList.toggle('nocursor', mode === 'pad');
  $$('[data-gl]').forEach(e => e.innerHTML = gl(e.dataset.gl));
  renderGuide();
}
function setMode(m) { if (m !== mode) { mode = m; applyMode(); } }

/* ---------- 말풍선 (브라우저 title 대신 직접 그림) ---------- */
const tip = $('#tip');
function showTip(el) {
  if (!el) { tip.classList.remove('on'); return; }
  const tr = tv.getBoundingClientRect(), s = tr.width / 1920, r = el.getBoundingClientRect();
  tip.textContent = el.dataset.tip;
  const x = (r.left + r.width / 2 - tr.left) / s, w = tip.offsetWidth;
  const left = Math.max(96, Math.min(1824 - w, x - w / 2));
  tip.style.left = left + 'px'; tip.style.top = (r.bottom - tr.top) / s + 16 + 'px';
  tip.style.setProperty('--ax', (x - left) + 'px');
  tip.classList.add('on');
}

/* ---------- 대기 모드: 30초 동안 입력이 없을 때만 원판이 저절로 돈다 (설정, 기본 꺼짐) ---------- */
const IDLE_MS = (+QS.get('idle') || 30) * 1000;
let idleOn = false, lastIn = performance.now(), idleT = 0, idleHome = 0;
function setIdleOn(v) { idleOn = v; lastIn = performance.now(); $$('#idlSeg i').forEach(i => i.classList.toggle('on', (i.dataset.v === 'on') === v)); }
function userIdle(v) { if (v !== idleOn) bridge.send('setSetting', { key: 'idle', value: v }); setIdleOn(v); }
// 입력이 오면 호출. 대기 중이었으면 회전을 멈추고 대기 전 칸으로 되돌린 뒤 true를 돌려준다 → 부른 쪽은 그 입력을 버린다(실행하지 않음)
function poke() {
  lastIn = performance.now();
  if (!idleT) return false;
  clearInterval(idleT); idleT = 0; tv.classList.remove('idle');
  const back = mod(idleHome - P[cat], N());
  turn(back <= N() / 2 ? back : back - N()); // 가까운 쪽으로 돌아온다
  renderGuide();
  return true;
}
function idleCheck() {
  if (!idleOn || idleT || REDUCED || loading || !N() || layer() || document.hidden || performance.now() - lastIn < IDLE_MS) return;
  idleHome = P[cat];
  tv.classList.add('idle'); renderGuide();
  idleT = setInterval(() => { if (!document.hidden && !layer()) turn(1); }, 2400); // 창이 열려 있으면 돌지 않는다
  turn(1);
}

/* ---------- 입력: 키보드 · 마우스 · 컨트롤러 ---------- */
addEventListener('keydown', e => {
  if (e.isComposing || e.keyCode === 229) return; // 한글 조합 중에는 손대지 않는다
  if (poke()) { e.preventDefault(); setMode('kbm'); return; } // 대기 모드를 깨운 키는 버린다
  const typing = e.target === q, k = e.key;
  if (k.startsWith('Arrow')) arrowHeld = true; // 홈 배경: 누르고 있는 동안은 바꾸지 않는다
  setMode('kbm');
  if (e.repeat && !k.startsWith('Arrow')) { if (!typing) e.preventDefault(); return; } // 누르고 있어도 방향키만 반복
  if (!typing && layer() && layer().name === 'search' && k.length === 1) { q.focus({ preventScroll: true }); return; }
  let a = null;
  if (k.startsWith('Arrow')) a = k.slice(5).toLowerCase();
  else if (k === 'Enter') {
    if (typing) { // 검색창: 고른 결과, 없으면 첫 결과. 빈 검색어는 아무 일 없음
      e.preventDefault();
      if (cur.dataset.g === 'res') return activate(cur);
      if (q.value.trim()) activate(resItems()[0]);
      return;
    }
    a = 'ok';
  }
  else if (k === 'Escape') a = 'back';
  else if (k === 'Backspace') a = typing && q.value ? null : 'back';
  else if (!typing && !e.ctrlKey && !e.metaKey && !e.altKey) a = { Slash: 'search', KeyQ: 'lb', KeyE: 'rb', KeyM: 'menu' }[e.code] || null;
  if (a) { e.preventDefault(); handle(a); }
  else if (k === 'Tab') e.preventDefault();
});
addEventListener('keyup', e => { if (e.key === 'Enter') holdEnd(); if (e.key.startsWith('Arrow')) arrowHeld = false; });
const NOHOVER = new Set(['dial', 'ring']); // 원판·링은 호버로 돌리지 않는다(커서 밑 항목이 바뀌지 않게). 클릭으로 고른다
let mx = -1, my = -1, hadFocus = true, eatClick = false, wokeAt = -1e9, mouseHold = false;
tv.addEventListener('mousemove', e => {
  if (Math.abs(e.clientX - mx) + Math.abs(e.clientY - my) < 3) return; // 목록이 움직여 생긴 가짜 이동은 무시
  mx = e.clientX; my = e.clientY;
  if (poke()) wokeAt = performance.now(); // 마우스를 움직여 깨우면 원판이 제자리로 돌아오는 동안(600ms) 클릭을 받지 않는다
  setMode('kbm');
  const el = e.target.closest('[data-f]');
  if (el && el !== cur && inTop(el) && !NOHOVER.has(el.dataset.g)) focusEl(el, true);
  const t = e.target.closest('[data-tip]');
  showTip(t && inTop(t) ? t : null);
});
tv.addEventListener('mousedown', e => {
  eatClick = poke() || performance.now() - wokeAt < 600; hadFocus = document.hasFocus() || !(bridge.test || QS.has('demo')); // 첫 클릭 삼키기는 맥 미리보기(시험·시연)에서만
  if (layer() && layer().name === 'search') e.preventDefault();
  const el = e.target.closest('[data-f]');
  if (e.button === 0 && !eatClick && hadFocus && el && inTop(el) && !el.closest('.hid') && holdsOn(el)) { // 다시 시작·종료: 누른 채 1초
    eatClick = mouseHold = true;
    if (!NOHOVER.has(el.dataset.g) && el !== cur) focusEl(el, true);
    activate(el);
  }
});
addEventListener('mouseup', () => { if (mouseHold) { mouseHold = false; holdEnd(); } });
const dropHold = () => { holdCancel(); mouseHold = false; arrowHeld = false; }; // 창을 떠나면 누르고 있던 것은 없던 일로
addEventListener('blur', dropHold);
document.addEventListener('visibilitychange', dropHold);
tv.addEventListener('click', e => {
  if (eatClick) { eatClick = false; return; } // 대기 모드를 깨운 클릭·길게 누른 클릭은 실행하지 않는다
  if (!hadFocus) return; // 미리보기 창에서 첫 클릭은 키보드 포커스만 가져온다
  const cmd = e.target.closest('[data-cmd]');
  if (cmd && !layer()) { showTip(null); return cmd.dataset.cmd === 'search' ? openSearch() : openMenu(); }
  const el = e.target.closest('[data-f]');
  if (el && inTop(el) && !el.closest('.hid')) {
    if (el.id === 'thm') { focusEl(el, true); const t = e.target.closest('[data-t]'); if (t) userTheme(t.dataset.t); return; }
    if (el.id === 'idl') { focusEl(el, true); const t = e.target.closest('[data-v]'); userIdle(t ? t.dataset.v === 'on' : !idleOn); return; }
    if (el.id === 'vol') { focusEl(el, true); const r = $('#vbar').getBoundingClientRect(); return setVol(Math.round((e.clientX - r.left) / r.width * 20) * 5, true); }
    if (!NOHOVER.has(el.dataset.g) && el !== cur) focusEl(el, true);
    return activate(el);
  }
  if (layer() && e.target === layer().el && e.target.hasAttribute('data-close')) closeTop();
});
let wAcc = 0, wLast = 0, wStep = 0;
tv.addEventListener('wheel', e => {
  e.preventDefault();
  if (poke()) { wAcc = 0; wStep = e.timeStamp; return; } // 깨운 휠은 돌리지 않는다
  setMode('kbm');
  const dv = Math.abs(e.deltaY) >= Math.abs(e.deltaX) ? e.deltaY : e.deltaX;
  if (e.timeStamp - wLast > 250) wAcc = 0;
  wLast = e.timeStamp;
  wAcc += e.deltaMode === 1 ? dv * 40 : dv; // 트랙패드의 잔 움직임은 모아서 한 칸
  if (Math.abs(wAcc) < 50 || e.timeStamp - wStep < 90) return;
  const dir = Math.sign(wAcc); wAcc = 0; wStep = e.timeStamp;
  if (!layer()) {
    if (loading) return;
    if (e.target.closest('.cats')) { setCat(cat + dir); if (cur.dataset.g === 'ring') focusEl(catLabel()); }
    else userTurn(dir);
    return;
  }
  const g = cur && cur.dataset.g;
  if (['keys', 'res', 'menu'].includes(g)) { const nx = G[g](dir > 0 ? 'down' : 'up', cur); if (nx && nx.dataset.g === g) focusEl(nx); }
}, { passive: false });

// 컨트롤러: inputMode 가 xinput 이면 확장이 보낸 pad 메시지를, 아니면 Gamepad API 를 같은 꼴로 읽는다 (기획서 6.2)
const PADMAP = { 0: 'ok', 1: 'back', 2: 'x', 3: 'search', 4: 'lb', 5: 'rb', 8: 'menu', 9: 'menu' };
let prevBtn = {}, held = null, nextRep = 0, padRaf = 0, muteUntil = 0;
let xinput = false, xpads = [], latch = {}; // latch: 두 프레임 사이에 눌렀다 뗀 버튼도 한 번은 읽히게
const oddPads = new Set(); // 표준 배치가 아닌 패드는 읽지 않고, 패드마다 한 번만 알린다
const standard = p => { if (p && p.mapping !== 'standard' && !oddPads.has(p.id)) { oddPads.add(p.id); log('warn', `표준 배치가 아닌 컨트롤러는 읽지 않아요: ${p.id}`); } return p && p.mapping === 'standard'; };
const getPads = () => xinput ? xpads : navigator.getGamepads ? [...navigator.getGamepads()].filter(standard) : [];
function padLoop(t) {
  padRaf = requestAnimationFrame(padLoop);
  const pr = {};
  let dir = null;
  for (const p of getPads()) { // 여러 대를 합쳐 읽고 같은 프레임 눌림은 한 번만
    p.buttons.forEach((b, i) => { if (b.pressed) pr[i] = true; });
    const ax = p.axes[0] || 0, ay = p.axes[1] || 0;
    if (Math.abs(ax) > .5 || Math.abs(ay) > .5) dir = Math.abs(ax) > Math.abs(ay) ? (ax > 0 ? 'right' : 'left') : (ay > 0 ? 'down' : 'up'); // 대각선은 큰 축 하나만
  }
  for (const i in latch) pr[i] = true;
  latch = {};
  if (pr[12]) dir = 'up'; else if (pr[13]) dir = 'down'; else if (pr[14]) dir = 'left'; else if (pr[15]) dir = 'right';
  if (t < muteUntil) { prevBtn = pr; held = dir; nextRep = Infinity; return; } // resume 직후 눌려 있던 버튼(Menu 길게로 돌아옴 등)은 뗄 때까지 버린다
  if ((dir || Object.keys(pr).length) && poke()) { prevBtn = pr; held = dir; nextRep = Infinity; setMode('pad'); return; } // 깨운 버튼·방향은 버리고, 떼기 전에는 반복하지 않는다
  const was = prevBtn;
  prevBtn = pr; // 먼저 바꿔 둔다: handle 이 던져도 누른 버튼이 매 프레임 다시 실행되지 않게
  if (was[0] && !pr[0]) holdEnd();
  for (const i in PADMAP) if (pr[i] && !was[i]) { setMode('pad'); handle(PADMAP[i]); }
  if (!dir) held = null;
  else if (dir !== held) { held = dir; nextRep = t + 300; setMode('pad'); handle(dir); }
  else if (t >= nextRep) { nextRep += 120; if (nextRep < t) nextRep = t + 120; handle(dir); } // 누적: 300ms 뒤 120ms 간격. 프레임이 멈췄다 와도 몰아서 돌지 않게
}

/* ---------- 시계 · 멈춤과 다시 시작 (pause / resume) ---------- */
let awake = false, idleChk = 0, clockT = 0, lastDay = null; // awake: 홈이 앞에 있어 그리기·입력 읽기가 도는 중
function tick() { // 분이 바뀔 때마다: 시계, 자동 테마(7시·19시 정각), 자정이면 '오늘'·'어제'
  clearTimeout(clockT);
  const d = now(), t = L.clock(d);
  $$('.clock').forEach(e => e.textContent = t);
  if (themePref === 'auto') setTheme('auto', lastDay != null);
  if (lastDay != null && lastDay !== d.getDate() && !loading) redraw();
  lastDay = d.getDate();
  if (awake) clockT = setTimeout(tick, 60000 - d.getSeconds() * 1000 - d.getMilliseconds() + 20);
}
function start() {
  if (awake) return;
  awake = true; tv.classList.remove('paused');
  padRaf = requestAnimationFrame(padLoop);
  idleChk = setInterval(idleCheck, 500);
}
function stop() { // pause: 창을 숨기기 직전. 그리기·입력 읽기·타이머를 멈춘다 (NFR-05)
  if (!awake) return;
  awake = false; tv.classList.add('paused');
  cancelAnimationFrame(padRaf); clearInterval(idleChk); clearTimeout(clockT);
  if (idleT) poke(); // 대기 회전 중이었으면 멈추고 대기 전 칸으로
  if (volT) { clearTimeout(volT); volT = 0; bridge.send('volume', { value: vol }); } // 기다리던 볼륨은 지금 보낸다
  holdCancel(); xpads = []; latch = {}; prevBtn = {}; held = null; arrowHeld = false;}

/* ---------- 확장 → 화면 메시지 ---------- */
const keyOf = it => it ? it.kind + ':' + (it.g || it.w || it.s).id : '';
function rebuild() { // 정렬·분류를 다시 하고, 분류마다 보던 칸을 그대로 둔다
  const before = CATS.map((c, j) => [keyOf(c.items[mod(P[j], c.items.length)]), mod(P[j], c.items.length) || 0]);
  GAMES = L.sortGames([...games.values()]);
  CATS = L.categories(GAMES, WEB, SYS);
  CATS.forEach((c, j) => { const i = c.items.findIndex(it => keyOf(it) === before[j][0]); P[j] = i >= 0 ? i : Math.min(before[j][1], Math.max(0, c.items.length - 1)); });
  if (!loading) redraw();
  renderRun();
}
function renderRun() { // SCR-01: 게임이 도는 동안 홈을 부르면 상세 위에 한 줄
  const id = runningId(), x = id && byId(id);
  $('#dRun').textContent = id ? '게임 실행 중' + (x ? ' · ' + x.title : '') : '';
}
const gameList = list => (Array.isArray(list) ? list : []).filter(x => x && x.id != null).map(x => L.normGame(x, bridge.test)); // data: 표지는 시험 모드만
function boot() { // 첫 library: 불러오는 중을 끝내고 처음 켤 때 회전을 한 번 (SCR-09)
  loading = false;
  tv.classList.remove('loading');
  redraw();
  if (!layer()) goHome(); // 불러오는 동안 연 메뉴의 포커스는 뺏지 않는다
  if (QS.has('demo') || REDUCED) { itemsEl.style.transition = 'none'; tv.classList.remove('boot'); itemsEl.offsetWidth; itemsEl.style.transition = ''; } // 시연·움직임 줄이기: 회전 없이 바로
  else { tv.classList.add('booting'); requestAnimationFrame(() => requestAnimationFrame(() => { tv.classList.remove('boot'); setTimeout(() => tv.classList.remove('booting'), 900); })); }
  runDemo();
}
function backFrom(id) { // 게임이 끝남: 켠 분류로 돌아가 그 게임을 3시에, 기록 줄은 새 값 (FR-09 · D-15)
  let c = launch && launch.id === id ? launch.cat : cat;
  if (launch && launch.id === id) launch = null;
  closeAll();
  const at = j => CATS[j].items.findIndex(it => it.kind === 'game' && it.g.id === id);
  if (at(c) < 0) c = 0;
  if (!loading && at(c) >= 0) { cat = c; P[c] = at(c); redraw(); }
  goHome();
}
const isObj = v => v && typeof v === 'object';
const STATES = ['launching', 'started', 'stopped', 'failed'];
const ON = {
  init(m) {
    clearInterval(readyT); // init 이 왔으니 ready 다시 보내기 멈춤
    if (m.version !== PROTOCOL) log('warn', `메시지 약속 판이 달라요: 확장 ${m.version}, 화면 ${PROTOCOL}`);
    const s = m.settings || {};
    WEB = (Array.isArray(s.web) ? s.web : []).filter(w => w && w.id != null).map(L.normWeb);
    xinput = m.inputMode === 'xinput';
    setIdleOn(!!s.idle);
    setTheme(THEMES.includes(s.theme) ? s.theme : 'auto');
    rebuild();
    tick();
  },
  library(m) { games = new Map(gameList(m.games).map(g => [g.id, g])); rebuild(); if (loading) boot(); },
  libraryDelta(m) {
    for (const id of Array.isArray(m.removes) ? m.removes : []) games.delete(String(id));
    for (const g of gameList(m.upserts)) games.set(g.id, g);
    rebuild();
  },
  status(m) { // 다 다듬은 뒤 한 번에 바꾼다(반쯤 적용되는 일 없게)
    const next = { network: isObj(m.network) ? m.network : null, pads: Array.isArray(m.pads) ? m.pads.filter(isObj) : [], kbm: isObj(m.kbm) ? m.kbm : {}, volume: m.volume };
    status = next;
    renderStatus();
    if (Number.isFinite(m.volume) && performance.now() - volAt > 3000) setVol(m.volume); // 막 바꾼 볼륨을 늦게 온 값이 되돌리지 않게
  },
  gameState(m) {
    if (m.id == null || !STATES.includes(m.state)) return log('warn', `gameState 값이 이상해요: ${JSON.stringify(m).slice(0, 200)}`);
    const id = String(m.id), busy = m.state === 'failed' && m.reason === 'busy';
    if (launch && launch.id === id) clearTimeout(watchT); // 답이 왔다
    if (m.state === 'launching' || m.state === 'started') { running = { id, state: m.state }; lastStop = null; } // 창이 다시 만들어진 뒤 확장이 started 를 다시 보내도 그대로 받는다
    else if (!busy && running && running.id === id) running = null; // busy 는 다른 게임 때문이라 지금 상태를 바꾸지 않는다
    renderRun();
    if (m.state === 'stopped') { if (lastStop === id) return; lastStop = id; return backFrom(id); } // 같은 stopped 가 거푸 오면 한 번만 (started 없이 온 stopped 는 받는다)
    if (m.state !== 'failed') return; // launching·started: 보이는 변화 없음 (started 면 확장이 창을 숨긴다)
    if (busy) { dropLayer('launch'); return toast('이미 실행 중인 게임이 있어요'); }
    showFailed(id);
  },
  pad(m) {
    if (!xinput || !awake) return; // Gamepad API 로 읽는 중이거나 홈이 숨어 있으면 무시
    xpads = (Array.isArray(m.pads) ? m.pads : []).filter(p => p && Array.isArray(p.buttons)).map(p => ({ index: p.index, mapping: 'standard',
      buttons: p.buttons.map((b, i) => { if (b) latch[i] = true; return { pressed: !!b }; }), axes: Array.isArray(p.axes) ? p.axes : [] }));
  },
  pause: stop,
  resume() { // 창이 다시 앞에: 멈춘 것을 다시 돌리고 포커스는 원판 3시 (NFR-16)
    closeAll();
    start();
    tick(); // 시계와 자동 테마를 지금 시각으로 (FR-23)
    if (!loading) redraw(); // '오늘'·'어제' 다시 셈
    muteUntil = performance.now() + 500;
    lastIn = performance.now();
    volAt = -1e9; // 다음 status 의 볼륨을 그대로 받는다
    goHome();
  },
  toast(m) { dropLayer('web'); toast(String(m.text || '')); }, // 웹을 열다 실패하면 확장이 알림을 보낸다: 여는 중 화면을 걷는다
};
const unknown = new Set();
bridge.on(m => {
  const f = m && Object.hasOwn(ON, m.type) && ON[m.type], t = String(m && m.type).slice(0, 80);
  if (f) f(m); else if (!unknown.has(t)) { unknown.add(t); log('warn', `모르는 메시지: ${t}`); } // 모르는 type 은 무시하고, type 마다 한 번만 로그
});
addEventListener('error', e => log('error', `${e.message} (${String(e.filename).split('/').pop()}:${e.lineno})`));
addEventListener('unhandledrejection', e => log('error', String(e.reason && e.reason.stack || e.reason)));
document.addEventListener('securitypolicyviolation', e => log('error', `CSP ${e.violatedDirective} ${e.blockedURI}`)); // 같은 위반은 log 가 5초에 한 번으로 줄인다

/* ---------- 시연 (시안과 같은 ?demo= : launch · kbm · web · search · menu) ---------- */
function runDemo() {
  const demo = QS.get('demo');
  if (demo === 'launch' && byId('elden')) openGame(byId('elden'));
  else if (demo === 'kbm' && byId('lol')) { turn(2); openGame(byId('lol')); }
  else if (demo === 'web' && WEB[0]) { setCat(4); openWeb(WEB[0]); }
  else if (demo === 'search') { openSearch(); typeKey('ㅇ'); typeKey('ㄹ'); focusEl(keysInRow(0)[3]); }
  else if (demo === 'menu') openMenu();
}

/* ---------- 무대 맞춤: 1920×1080 을 화면에 맞춰 통째로 (NFR-07) ---------- */
function fit() {
  const s = Math.min(innerWidth / 1920, innerHeight / 1080);
  Object.assign($('#tvWrap').style, { left: (innerWidth - 1920 * s) / 2 + 'px', top: (innerHeight - 1080 * s) / 2 + 'px', width: 1920 * s + 'px', height: 1080 * s + 'px' });
  tv.style.transform = `scale(${s})`;
}

/* ---------- 시작: 불러오는 중(SCR-09)을 그리고 확장에 ready ---------- */
addEventListener('resize', fit); fit();
setTheme(themePref);
renderStatic();
renderStatus();
setVol(vol);
renderDial();
updateCats();
applyMode();
document.fonts.ready.then(fitBrand); // 글꼴이 늦게 도착해 안내 줄 폭이 바뀌면 왼쪽 아래 로고를 다시 잰다
start();
tick();
let readyN = 0;
const sendReady = () => { if (readyN++ === 12) log('error', 'init 없음: ready 를 12번 보냈어요'); bridge.send('ready', { version: PROTOCOL }); };
const readyT = setInterval(sendReady, 5000); // init 이 올 때까지 5초마다 (확장은 겹쳐 받아도 됨). 12번 뒤에도 계속
sendReady();
