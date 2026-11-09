// 실제 입력 확인: 헤드리스 크롬에 CDP Input.* 로 키·마우스를 넣는다 (isTrusted=true)
// 사용: node --experimental-websocket cdp.mjs <url> <steps.json> <outdir>
import { spawn } from 'node:child_process';
import fs from 'node:fs';
import os from 'node:os';
import path from 'node:path';

const [url, stepsFile, outdir] = process.argv.slice(2);
const steps = JSON.parse(fs.readFileSync(stepsFile, 'utf8'));
fs.mkdirSync(outdir, { recursive: true });
const port = 9300 + Math.floor(Math.random() * 500);
const chrome = spawn('/Applications/Google Chrome.app/Contents/MacOS/Google Chrome', [
  '--headless=new', '--disable-gpu', '--hide-scrollbars', `--remote-debugging-port=${port}`,
  `--user-data-dir=${fs.mkdtempSync(path.join(os.tmpdir(), 'cdp-'))}`, '--window-size=1920,1250', 'about:blank'], { stdio: 'ignore' });
const sleep = ms => new Promise(r => setTimeout(r, ms));

let wsUrl;
for (let i = 0; i < 50 && !wsUrl; i++) {
  await sleep(200);
  try { wsUrl = (await (await fetch(`http://127.0.0.1:${port}/json/list`)).json()).find(t => t.type === 'page')?.webSocketDebuggerUrl; } catch {}
}
const ws = new WebSocket(wsUrl);
await new Promise(r => ws.onopen = r);
let id = 0; const pending = new Map(); const logs = [];
ws.onmessage = m => { const d = JSON.parse(m.data);
  if (d.id && pending.has(d.id)) { pending.get(d.id)(d); pending.delete(d.id); }
  if (d.method === 'Runtime.exceptionThrown') logs.push('EXC ' + d.params.exceptionDetails.exception?.description);
  if (d.method === 'Runtime.consoleAPICalled' && d.params.type === 'error') logs.push('ERR ' + d.params.args.map(a => a.value ?? a.description).join(' '));
};
const send = (method, params = {}) => new Promise(r => { const i = ++id; pending.set(i, r); ws.send(JSON.stringify({ id: i, method, params })); });

const KEYS = { ArrowUp: 38, ArrowDown: 40, ArrowLeft: 37, ArrowRight: 39, Enter: 13, Escape: 27, Backspace: 8, Tab: 9 };
async function key(k) {
  const code = KEYS[k] ? k : (k.length === 1 ? (/[a-z]/i.test(k) ? 'Key' + k.toUpperCase() : k === '/' ? 'Slash' : '') : k);
  const vk = KEYS[k] ?? (k === '/' ? 191 : k.length === 1 ? k.toUpperCase().charCodeAt(0) : 0);
  const text = k.length === 1 ? k : (k === 'Enter' ? '\r' : undefined);
  await send('Input.dispatchKeyEvent', { type: text ? 'keyDown' : 'rawKeyDown', key: k, code, windowsVirtualKeyCode: vk, text });
  await send('Input.dispatchKeyEvent', { type: 'keyUp', key: k, code, windowsVirtualKeyCode: vk });
  if (k === 'Escape') await send('Page.bringToFront'); // 헤드리스 Esc 뒤 hidden 되는 함정
}
async function mouse(type, x, y) {
  await send('Input.dispatchMouseEvent', { type, x, y, button: type === 'mouseMoved' ? 'none' : 'left', clickCount: 1 });
}

await send('Runtime.enable'); await send('Page.enable');
await send('Emulation.setDeviceMetricsOverride', { width: 1920, height: 1250, deviceScaleFactor: 1, mobile: false });
await send('Page.navigate', { url });
await sleep(2500);
const evalv = async expr => (await send('Runtime.evaluate', { expression: expr, returnByValue: true })).result?.result?.value;
// 무대(.tv-wrap) 위치: 스크린샷 자르기와 무대 좌표 → 화면 좌표 변환에 씀
const box = await evalv(`(() => { const r = document.getElementById('tvWrap').getBoundingClientRect(); return { x: r.x, y: r.y + scrollY, w: r.width, h: r.height }; })()`);
const s = box.w / 1920;
const out = [];
for (const st of steps) {
  if (st.key) for (let n = 0; n < (st.n || 1); n++) { await key(st.key); await sleep(st.gap ?? 260); }
  if (st.type) for (const ch of st.type) { await key(ch); await sleep(120); }
  if (st.hold) { // 누르고 있기: keyDown 뒤 50ms마다 autoRepeat, ms 뒤 keyUp (방향키만)
    const k = st.hold, vk = KEYS[k]; const ev = r => send('Input.dispatchKeyEvent', { type: 'rawKeyDown', key: k, code: k, windowsVirtualKeyCode: vk, autoRepeat: r });
    await ev(false); const end = Date.now() + (st.ms || 1000); await sleep(500);
    while (Date.now() < end) { await ev(true); await sleep(50); }
    await send('Input.dispatchKeyEvent', { type: 'keyUp', key: k, code: k, windowsVirtualKeyCode: vk });
  }
  if (st.ime) { await send('Input.insertText', { text: st.ime }); await sleep(300); }
  if (st.move) { await mouse('mouseMoved', box.x + st.move[0] * s, box.y + st.move[1] * s); await sleep(300); }
  if (st.click) { const [x, y] = [box.x + st.click[0] * s, box.y + st.click[1] * s];
    await mouse('mouseMoved', x, y); await sleep(150); await mouse('mousePressed', x, y); await mouse('mouseReleased', x, y); await sleep(400); }
  if (st.wait) await sleep(st.wait);
  if (st.eval) out.push({ step: st.label || st.eval.slice(0, 40), value: await evalv(st.eval) });
  if (st.shot) {
    await sleep(500);
    const r = await send('Page.captureScreenshot', { format: 'jpeg', quality: 70, clip: st.clip ? { x: box.x + st.clip[0] * s, y: box.y + st.clip[1] * s, width: st.clip[2] * s, height: st.clip[3] * s, scale: st.scale ?? 1 } : { x: box.x, y: box.y, width: box.w, height: box.h, scale: st.scale ?? 0.5 }, captureBeyondViewport: true });
    fs.writeFileSync(path.join(outdir, st.shot + '.jpg'), Buffer.from(r.result.data, 'base64'));
    out.push({ shot: st.shot });
  }
}
console.log(JSON.stringify({ stageScale: s, out, logs }, null, 1));
ws.close(); chrome.kill();
process.exit(0);
