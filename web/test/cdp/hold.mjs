// 다시 시작·시스템 종료 '1초 길게 누르기'(D-12)를 실제 입력으로 확인. tools/cdp.mjs 는 키를 누른 채 둘 수 없어서 따로 둔다.
// 사용: node --experimental-websocket web/test/cdp/hold.mjs [http://127.0.0.1:8765]
import { spawn } from 'node:child_process'; import fs from 'node:fs'; import os from 'node:os'; import path from 'node:path';
const BASE = (process.argv[2] || 'http://127.0.0.1:8765') + '/index.html';
const port = 9300 + Math.floor(Math.random() * 500), sleep = ms => new Promise(r => setTimeout(r, ms));
const chrome = spawn('/Applications/Google Chrome.app/Contents/MacOS/Google Chrome', ['--headless=new', '--disable-gpu', `--remote-debugging-port=${port}`, `--user-data-dir=${fs.mkdtempSync(path.join(os.tmpdir(), 'cdp-'))}`, '--window-size=1920,1080', 'about:blank'], { stdio: 'ignore' });
let wsUrl; for (let i = 0; i < 50 && !wsUrl; i++) { await sleep(200); try { wsUrl = (await (await fetch(`http://127.0.0.1:${port}/json/list`)).json()).find(t => t.type === 'page')?.webSocketDebuggerUrl; } catch {} }
const ws = new WebSocket(wsUrl); await new Promise(r => ws.onopen = r);
let id = 0; const pending = new Map(); const logs = [];
ws.onmessage = m => { const d = JSON.parse(m.data); if (d.id && pending.has(d.id)) { pending.get(d.id)(d); pending.delete(d.id); }
  if (d.method === 'Runtime.exceptionThrown') logs.push('EXC ' + d.params.exceptionDetails.exception?.description);
  if (d.method === 'Log.entryAdded' && ['error', 'warning'].includes(d.params.entry.level) && !/^file:/.test(d.params.entry.url || '')) logs.push('LOG ' + d.params.entry.text.slice(0, 160)); }; // CSP 위반·404 같은 브라우저 메시지
const send = (method, params = {}) => new Promise(r => { const i = ++id; pending.set(i, r); ws.send(JSON.stringify({ id: i, method, params })); });
const ev = async e => (await send('Runtime.evaluate', { expression: e, returnByValue: true, awaitPromise: true })).result?.result?.value;
const KEYS = { Enter: [13, 'Enter', '\r'], ArrowDown: [40, 'ArrowDown'], Escape: [27, 'Escape'], m: [77, 'KeyM', 'm'], e: [69, 'KeyE', 'e'] };
const down = k => { const [vk, code, text] = KEYS[k]; return send('Input.dispatchKeyEvent', { type: text ? 'keyDown' : 'rawKeyDown', key: k, code, windowsVirtualKeyCode: vk, text }); };
const up = k => { const [vk, code] = KEYS[k]; return send('Input.dispatchKeyEvent', { type: 'keyUp', key: k, code, windowsVirtualKeyCode: vk }); };
const tap = async (k, n = 1) => { for (let i = 0; i < n; i++) { await down(k); await up(k); await sleep(250); } };
const mouse = (type, [x, y], button = 'left') => send('Input.dispatchMouseEvent', { type, x, y, button, clickCount: 1 });
const center = sel => ev(`(r => [r.x + r.width / 2, r.y + r.height / 2])(document.querySelector(${JSON.stringify(sel)}).getBoundingClientRect())`);
const powers = () => ev(`fakeHost.got.filter(m => m.type === 'power').map(m => m.action).join()`);
const toastNow = () => ev(`toast.classList.contains('on') ? toast.textContent : ''`);
const results = [];
const check = async (label, cond) => { const v = { powers: await powers(), toast: await toastNow() }; results.push(`${cond(v) ? 'PASS' : 'FAIL'} ${label} ${JSON.stringify(v)}`); };
const open = async q => { await send('Page.navigate', { url: BASE + q }); await sleep(2500); };
await send('Runtime.enable'); await send('Log.enable');

await open('');
await tap('m'); await sleep(400); await tap('ArrowDown', 5); // 볼륨 → 테마 → 대기 → 화면 끄기 → 절전 → 다시 시작
await down('Enter'); await sleep(400); await up('Enter'); await sleep(200);
await check('1 Enter 0.4초 -> 실행 안 함, 안내', v => v.powers === '' && v.toast === 'Enter를 길게 누르면 다시 시작해요');
await down('Enter'); await sleep(1200); await check('2 Enter 1.2초 누르는 중 -> restart', v => v.powers === 'restart' && v.toast === '다시 시작하는 중…'); await up('Enter');
const sd = await center('#ovMenu [data-p="shutdown"]');
await mouse('mouseMoved', sd); await sleep(200); await mouse('mousePressed', sd); await sleep(300); await mouse('mouseReleased', sd); await sleep(300);
await check('3 마우스 짧게 -> 실행 안 함, 안내', v => v.powers === 'restart' && v.toast === '1초 누르고 있으면 종료해요');
await mouse('mousePressed', sd); await sleep(1200); await check('4 마우스 1.2초 -> shutdown', v => v.powers === 'restart,shutdown' && v.toast === '시스템을 종료하는 중…'); await mouse('mouseReleased', sd); await sleep(300);
await tap('Escape'); await tap('e', 5); await sleep(400); await tap('ArrowDown', 3); await sleep(400); // 원판 '시스템' 분류의 다시 시작 CD
await down('Enter'); await sleep(1200); await up('Enter'); await sleep(200);
await check('5 원판 시스템 분류 다시 시작 CD: Enter 1.2초 -> restart', v => v.powers === 'restart,shutdown,restart');
await sleep(2600); // 알림이 사라질 때까지
await down('Enter'); await sleep(300); await ev(`window.dispatchEvent(new Event('blur')); 1`); await sleep(1100); await up('Enter'); await sleep(200);
await check('8 누르는 중 창이 포커스를 잃음(blur: 합성 이벤트) -> 실행도 안내도 없음', v => v.powers === 'restart,shutdown,restart' && v.toast === '');
await tap('m'); await sleep(400); const sd2 = await center('#ovMenu [data-p="shutdown"]');
await mouse('mouseMoved', sd2); await sleep(200); await mouse('mousePressed', sd2, 'right'); await sleep(1200); await mouse('mouseReleased', sd2, 'right'); await sleep(300);
await check('9 오른쪽 버튼으로 1.2초 -> 실행 안 함', v => v.powers === 'restart,shutdown,restart');
await ev(`fakeHost.send({ type: 'gameState', id: 'elden', state: 'started' }); 1`); await sleep(300); // 게임 중
await tap('ArrowDown', 6); // 볼륨 → … → 시스템 종료
await down('Enter'); await sleep(1200); await up('Enter'); await sleep(200);
await check('10 게임 중 시스템 종료 Enter 1.2초 -> 보내지 않고 알림', v => v.powers === 'restart,shutdown,restart' && v.toast === '게임을 끈 뒤에 할 수 있어요');
await ev(`fakeHost.send({ type: 'gameState', id: 'elden', state: 'stopped', elapsedSec: 60 }); 1`); await sleep(300);

await open('?xinput=1'); // 컨트롤러 A (확장이 보낸 pad 메시지)
await ev(`window.P = b => fakeHost.send({ type: 'pad', pads: [{ index: 0, buttons: Array.from({ length: 17 }, (_, i) => b.includes(i)), axes: [0, 0, 0, 0] }] }); 1`);
await ev('P([9])'); await sleep(150); await ev('P([])'); await sleep(300); // Menu
for (let i = 0; i < 5; i++) { await ev('P([13])'); await sleep(80); await ev('P([])'); await sleep(150); }
await ev('P([0])'); await sleep(300); await ev('P([])'); await sleep(200);
await check('6 패드 A 0.3초 -> 안내 (A를)', v => v.powers === '' && v.toast === 'A를 길게 누르면 다시 시작해요');
await ev('P([0])'); await sleep(1200); await check('7 패드 A 1.2초 -> restart', v => v.powers === 'restart'); await ev('P([])');

console.log(results.join('\n'));
console.log(`PASS ${results.filter(r => r.startsWith('PASS')).length} / ${results.length}  exceptions ${logs.length} ${logs.join(' ')}`);
ws.close(); chrome.kill(); process.exit(0);
