// Renders a reel page to an Instagram-ready MP4 (1080x1920, 30 fps, H.264).
//
//   node marketing/reel/render.cjs                    -> reel.html        -> calling-bell-services-reel.mp4
//   node marketing/reel/render.cjs --page portal      -> portal-reel.html -> calling-bell-portal-reel.mp4
//   node marketing/reel/render.cjs --stills 1,4,6.5   -> PNG stills for review (combine with --page)
//
// Needs Chrome installed and FFMPEG (env var) or ffmpeg on PATH. Uses playwright-core from tests/node_modules.
const http = require('http');
const fs = require('fs');
const path = require('path');
const { spawn } = require('child_process');
const { chromium } = require(path.join(__dirname, '../../tests/node_modules/playwright-core'));

const ROOT = __dirname;
const FPS = 30;
const REELS = {
  services: { page: 'reel.html', out: 'calling-bell-services-reel.mp4' },
  portal: { page: 'portal-reel.html', out: 'calling-bell-portal-reel.mp4' },
};
const pageArg = process.argv.indexOf('--page');
const REEL = REELS[pageArg > -1 ? process.argv[pageArg + 1] : 'services'];
if (!REEL) throw new Error(`--page must be one of: ${Object.keys(REELS).join(', ')}`);
const OUT = path.join(ROOT, REEL.out);
const TYPES = { '.html': 'text/html', '.woff2': 'font/woff2', '.js': 'text/javascript' };

function serve() {
  const server = http.createServer((req, res) => {
    const file = path.join(ROOT, decodeURIComponent(new URL(req.url, 'http://x').pathname));
    if (!file.startsWith(ROOT) || !fs.existsSync(file) || fs.statSync(file).isDirectory()) { res.writeHead(404); return res.end(); }
    res.writeHead(200, { 'Content-Type': TYPES[path.extname(file)] || 'application/octet-stream' });
    fs.createReadStream(file).pipe(res);
  });
  return new Promise(r => server.listen(0, '127.0.0.1', () => r(server)));
}

async function main() {
  const stillsArg = process.argv.indexOf('--stills');
  const stills = stillsArg > -1 ? process.argv[stillsArg + 1].split(',').map(Number) : null;

  const server = await serve();
  const browser = await chromium.launch({ channel: 'chrome', headless: true });
  const page = await browser.newPage({ viewport: { width: 1080, height: 1920 }, deviceScaleFactor: 1 });
  page.on('pageerror', e => console.error('page error:', e.message));
  await page.goto(`http://127.0.0.1:${server.address().port}/${REEL.page}?render`);
  await page.evaluate(() => window.reelReady);
  const dur = await page.evaluate(() => window.REEL.DUR);

  const frame = t => page.evaluate(t => {
    window.REEL.renderFrame(t);
    return document.getElementById('c').toDataURL('image/png').slice(22);
  }, t);

  if (stills) {
    const dir = path.join(ROOT, 'stills', path.basename(REEL.page, '.html'));
    fs.mkdirSync(dir, { recursive: true });
    for (const t of stills) fs.writeFileSync(path.join(dir, `t${t.toFixed(2)}.png`), Buffer.from(await frame(t), 'base64'));
    console.log(`wrote ${stills.length} stills to ${dir}`);
  } else {
    const ffmpeg = spawn(process.env.FFMPEG || 'ffmpeg', [
      '-y', '-f', 'image2pipe', '-framerate', String(FPS), '-c:v', 'png', '-i', '-',
      '-f', 'lavfi', '-i', 'anullsrc=r=48000:cl=stereo',
      '-c:v', 'libx264', '-preset', 'slow', '-crf', '16', '-profile:v', 'high', '-pix_fmt', 'yuv420p',
      '-c:a', 'aac', '-b:a', '128k', '-shortest', '-movflags', '+faststart', OUT,
    ], { stdio: ['pipe', 'ignore', 'pipe'] });
    let log = '';
    ffmpeg.stderr.on('data', d => { log += d; });
    const done = new Promise((res, rej) => ffmpeg.on('close', c => (c === 0 ? res() : rej(new Error(log.slice(-2000))))));
    const total = Math.round(dur * FPS);
    for (let i = 0; i < total; i++) {
      const buf = Buffer.from(await frame(i / FPS), 'base64');
      if (!ffmpeg.stdin.write(buf)) await new Promise(r => ffmpeg.stdin.once('drain', r));
      if (i % 60 === 0) process.stdout.write(`frame ${i}/${total}\r`);
    }
    ffmpeg.stdin.end();
    await done;
    console.log(`\nwrote ${OUT}`);
  }
  await browser.close();
  server.close();
}

main().catch(e => { console.error(e); process.exit(1); });
