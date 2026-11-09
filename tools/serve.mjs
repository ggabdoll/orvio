import http from 'node:http'; import fs from 'node:fs'; import path from 'node:path';
const root = process.argv[2], port = +process.argv[3];
http.createServer((q, r) => {
  const p = path.join(root, decodeURIComponent(new URL(q.url, 'http://x').pathname));
  if (!p.startsWith(root)) { r.writeHead(403); return r.end(); }
  fs.readFile(p, (e, b) => { if (e) { r.writeHead(404); return r.end(); }
    r.writeHead(200, { 'content-type': ({ '.html': 'text/html; charset=utf-8', '.js': 'text/javascript', '.mjs': 'text/javascript', '.json': 'application/json', '.css': 'text/css', '.svg': 'image/svg+xml', '.png': 'image/png', '.webp': 'image/webp', '.woff2': 'font/woff2' })[path.extname(p)] || 'application/octet-stream', 'cache-control': 'no-store' }); r.end(b); });
}).listen(port, '127.0.0.1', () => console.log('ok', port));
