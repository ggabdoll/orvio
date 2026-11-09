// 화면 ↔ 확장 통로 (docs/bridge-protocol.md).
// WebView2 안이면 window.chrome.webview, 아니면 시험 모드 가짜 다리(test/fake-bridge.js)가 같은 꼴을 흉내 낸다.
const handlers = [];
const queue = []; // 통로가 열리기 전에 보낸 메시지
let wv = window.chrome && window.chrome.webview;

export const bridge = {
  now: () => new Date(), // 시험 모드는 정해 둔 시각으로 바꾼다
  test: false, // 가짜 다리로 도는 시험 모드인지
  send(type, payload = {}) { const m = { type, ...payload }; if (wv) wv.postMessage(m); else queue.push(m); },
  on(fn) { handlers.push(fn); },
};

function open() {
  wv.addEventListener('message', e => {
    let d = e.data;
    if (typeof d === 'string') try { d = JSON.parse(d); } catch { d = null; } // PostWebMessageAsString 으로 와도 받는다. 깨진 JSON 은 null (화면이 로그로 남김)
    for (const fn of handlers) fn(d);
  });
  for (const m of queue.splice(0)) wv.postMessage(m);
}

if (wv) open();
else import('./test/fake-bridge.js').then(m => {
  wv = window.chrome.webview;
  if (m.now) bridge.now = m.now;
  bridge.test = true;
  open();
}, () => {
  document.body.insertAdjacentHTML('beforeend', '<p role="status" style="position:fixed;left:24px;bottom:24px;z-index:99;margin:0;padding:12px 20px;border-radius:6px;background:#262626;color:#f3f3f3;font:24px/1.4 Pretendard,system-ui,sans-serif">확장과 연결되지 않았어요. Playnite의 ORVIO 화면에서 열어 주세요.</p>');
});
