/**
 * Browser checks for chat, team and video (two signed-in people at once, real WebRTC with Chrome's fake camera).
 *
 *   cd tests; npm install; node e2e/phase2.cjs
 *
 * Settings (environment): CB_WEB (default http://localhost:5173), CB_API (default http://localhost:5080),
 * CB_CHROME (default: the installed Chrome), CB_PASSWORD. Screenshots go to tests/e2e/output.
 * Messages it sends carry [cb-test]; tests/sql/CleanupTestData.sql removes them.
 */
const path = require('path');
const fs = require('fs');
const { chromium } = require('playwright-core');
const BASE = (process.env.CB_WEB || 'http://localhost:5173').replace(/\/$/, '');
const API = (process.env.CB_API || 'http://localhost:5080').replace(/\/$/, '');
const PASSWORD = process.env.CB_PASSWORD || 'CallingBell@2026';
const CHROME = process.env.CB_CHROME || 'C:/Program Files/Google/Chrome/Application/chrome.exe';
const OUT = path.join(__dirname, 'output');
fs.mkdirSync(OUT, { recursive: true });
const shot = (name) => path.join(OUT, name);
const MARK = '[cb-test]';
const CUSTOMER = 'naveen.menon@demo.callingbell.in';
const OWNER = 'nagaraj.gowda@demo.callingbell.in';          // GreenLeaf Gardening Services
const VIDEO_OWNER = 'jignesh.desai@demo.callingbell.in';    // Akhada Fitness Studio (offers video)
const results = [];
const check = (name, ok, detail = '') => { results.push(!!ok); console.log(`${ok ? 'PASS' : 'FAIL'} ${name}${detail ? `  [${detail}]` : ''}`); };

// The 'auth' rate limit allows 10 sign-ins a minute per IP; when the earlier test steps used them up, wait for the window.
async function login(email, retries = 2) {
  const res = await fetch(`${API}/api/auth/login`, { method: 'POST', headers: { 'Content-Type': 'application/json' }, body: JSON.stringify({ email, password: PASSWORD }) });
  if (res.status === 429 && retries > 0) {
    const wait = Number(res.headers.get('Retry-After') || 60);
    console.log(`  (rate limited signing in; waiting ${wait}s)`);
    await new Promise((r) => setTimeout(r, (wait + 1) * 1000));
    return login(email, retries - 1);
  }
  const r = await res.json();
  if (!r.success) throw new Error(`login failed for ${email}: ${r.message}`);
  return r.data;
}
async function session(browser, auth, viewport = { width: 1366, height: 860 }) {
  const ctx = await browser.newContext({ viewport, permissions: ['camera', 'microphone'] });
  const page = await ctx.newPage();
  page.errors = [];
  page.on('pageerror', (e) => page.errors.push(e.message));
  page.on('console', (m) => { if (m.type() === 'error' && !/WebSocket|negotiat/.test(m.text())) page.errors.push(m.text().slice(0, 160)); });
  await page.goto(BASE + '/login');
  await page.evaluate((d) => localStorage.setItem('cb-auth', JSON.stringify({ state: { user: d.user, accessToken: d.accessToken, refreshToken: d.refreshToken }, version: 0 })), auth);
  return { ctx, page };
}

(async () => {
  const browser = await chromium.launch({
    executablePath: CHROME,
    args: ['--use-fake-ui-for-media-stream', '--use-fake-device-for-media-stream', '--autoplay-policy=no-user-gesture-required'],
  });
  const [cAuth, oAuth, vAuth] = [await login(CUSTOMER), await login(OWNER), await login(VIDEO_OWNER)];
  const cust = await session(browser, cAuth);
  const owner = await session(browser, oAuth);

  // ---------- chat: customer starts from the business page ----------
  await cust.page.goto(BASE + '/business/greenleaf-gardening-services', { waitUntil: 'networkidle' });
  const teamShown = await cust.page.getByRole('heading', { name: 'Our team' }).isVisible();
  check('business page shows "Our team"', teamShown);
  await owner.page.goto(BASE + '/owner/messages', { waitUntil: 'networkidle' });
  await cust.page.getByRole('button', { name: 'Chat' }).first().click();
  await cust.page.getByRole('textbox', { name: 'Message' }).waitFor({ timeout: 20000 });
  const stamp = Date.now().toString().slice(-5);
  await cust.page.getByRole('textbox', { name: 'Message' }).fill(`Hello, do you service terrace gardens? (${stamp}) ${MARK}`);
  await cust.page.keyboard.press('Enter');
  await cust.page.getByText(`(${stamp})`).waitFor({ timeout: 10000 });
  check('customer message appears in the drawer', true);

  // owner sees it live in the list and opens it
  await owner.page.getByText(`(${stamp})`).first().waitFor({ timeout: 15000 });
  check('owner inbox updates live (no reload)', true);
  await owner.page.locator('aside[aria-label="Conversations"] button').filter({ hasText: `(${stamp})` }).first().click();
  await owner.page.getByRole('textbox', { name: 'Message' }).waitFor();
  // customer's ticks turn to "read" once the owner opens it
  await cust.page.locator('[aria-label="Read"]').last().waitFor({ timeout: 10000 }).then(() => check('read receipt (✓✓) reaches the customer live', true)).catch(() => check('read receipt (✓✓) reaches the customer live', false));
  // typing indicator, then reply
  await owner.page.getByRole('textbox', { name: 'Message' }).type('Yes we do', { delay: 40 });
  const typingSeen = await cust.page.getByText('typing…').waitFor({ timeout: 6000 }).then(() => true).catch(() => false);
  check('customer sees "typing…"', typingSeen);
  await owner.page.getByRole('textbox', { name: 'Message' }).fill(`Yes, terrace gardens are our speciality. (${stamp}-r) ${MARK}`);
  await owner.page.getByRole('button', { name: 'Send' }).click();
  await cust.page.getByText(`(${stamp}-r)`).waitFor({ timeout: 10000 }).then(() => check('owner reply reaches the customer live', true)).catch(() => check('owner reply reaches the customer live', false));
  await cust.page.screenshot({ path: shot('p2-chat-customer.png') });
  await owner.page.screenshot({ path: shot('p2-chat-owner.png') });

  // ---------- team ----------
  await owner.page.goto(BASE + '/owner/team', { waitUntil: 'networkidle' });
  const cards = await owner.page.locator('main li.card').count();
  await owner.page.getByRole('button', { name: 'Add team member' }).first().click();
  await owner.page.getByLabel('Full name').fill('Ritu Sharma');
  await owner.page.getByLabel('Role / title').fill('Landscape Designer [cb-test]');
  await owner.page.getByRole('button', { name: 'Add to team' }).click();
  await owner.page.getByText('Ritu Sharma').waitFor({ timeout: 10000 });
  check('owner adds a team member', (await owner.page.locator('main li.card').count()) === cards + 1, `${cards} → ${cards + 1}`);
  await owner.page.screenshot({ path: shot('p2-team.png'), fullPage: true });
  const ritu = owner.page.locator('main li.card').filter({ hasText: 'Ritu Sharma' });
  await ritu.getByRole('button', { name: 'Remove Ritu Sharma' }).click();
  await owner.page.getByRole('button', { name: 'Remove', exact: true }).click();
  await owner.page.getByText('Ritu Sharma').first().waitFor({ state: 'detached', timeout: 10000 }).catch(() => {});

  // ---------- bookings: professional column ----------
  await owner.page.goto(BASE + '/owner/bookings', { waitUntil: 'networkidle' });
  const proHeader = await owner.page.getByRole('columnheader', { name: 'Professional' }).isVisible();
  const pickers = await owner.page.locator('[aria-label^="Professional for"]').count();
  check('owner bookings show a Professional column with pickers', proHeader && pickers > 0, `${pickers} pickers`);
  await owner.page.screenshot({ path: shot('p2-bookings.png') });

  // ---------- booking dialog: choose a professional ----------
  await cust.page.goto(BASE + '/business/greenleaf-gardening-services', { waitUntil: 'networkidle' });
  await cust.page.getByRole('button', { name: 'Book now' }).first().click().catch(() => {});
  const radios = await cust.page.getByRole('radiogroup', { name: 'Professional' }).getByRole('radio').count().catch(() => 0);
  check('booking dialog lets the customer pick a professional', radios >= 2, `${radios} choices`);
  await cust.page.screenshot({ path: shot('p2-booking-dialog.png') });
  await cust.page.keyboard.press('Escape');

  // ---------- video: chat → start call → both join ----------
  const videoBiz = (await (await fetch(`${API}/api/businesses/akhada-fitness-studio`)).json()).data.card.id;
  const conv = await (await fetch(`${API}/api/chat/conversations`, { method: 'POST', headers: { 'Content-Type': 'application/json', Authorization: `Bearer ${cAuth.accessToken}` }, body: JSON.stringify({ businessId: videoBiz }) })).json();
  const room = await (await fetch(`${API}/api/chat/conversations/${conv.data.id}/video`, { method: 'POST', headers: { Authorization: `Bearer ${cAuth.accessToken}` } })).json();
  check('customer starts a video call from chat', room.success, room.data);
  const vo = await session(browser, vAuth);
  await cust.page.goto(`${BASE}/video/${room.data}`, { waitUntil: 'networkidle' });
  await vo.page.goto(`${BASE}/video/${room.data}`, { waitUntil: 'networkidle' });
  await cust.page.getByRole('button', { name: 'Join call' }).click();
  await cust.page.getByText(/Waiting for/).first().waitFor({ timeout: 15000 });
  await vo.page.getByRole('button', { name: 'Join call' }).click();
  const connected = async (p) => p.waitForFunction(() => { const v = [...document.querySelectorAll('video')].find((x) => !x.muted); return v && v.videoWidth > 0 && !v.paused; }, null, { timeout: 30000 }).then(() => true).catch(() => false);
  const [a, b] = await Promise.all([connected(cust.page), connected(vo.page)]);
  check('both sides receive each other\'s video (WebRTC)', a && b, `customer ${a}, business ${b}`);
  await cust.page.waitForTimeout(2500);
  const timer = await cust.page.locator('header p.text-xs').first().textContent();
  check('call timer runs', /\d:\d\d/.test(timer ?? ''), timer);
  await cust.page.getByRole('button', { name: 'Mute' }).click();
  check('mute toggles', await cust.page.getByRole('button', { name: 'Unmute' }).isVisible());
  await cust.page.screenshot({ path: shot('p2-video-customer.png') });
  await vo.page.screenshot({ path: shot('p2-video-business.png') });
  // business leaves → customer sees it
  await vo.page.getByRole('button', { name: 'End call' }).click();
  const leftSeen = await cust.page.getByText(/left the call/).first().waitFor({ timeout: 10000 }).then(() => true).catch(() => false);
  check('the other side is told when someone leaves', leftSeen);

  // mobile video layout
  const mob = await session(browser, cAuth, { width: 390, height: 844 });
  await mob.page.goto(`${BASE}/video/${room.data}`, { waitUntil: 'networkidle' });
  await mob.page.waitForTimeout(1500);
  await mob.page.screenshot({ path: shot('p2-video-mobile.png') });
  const mobOk = await mob.page.evaluate(() => document.documentElement.scrollWidth - innerWidth);
  check('video page (ended call) renders on mobile without overflow', mobOk <= 0, `${mobOk}px`);

  for (const [n, s] of [['customer', cust], ['owner', owner], ['video owner', vo], ['mobile', mob]]) {
    if (s.page.errors.length) console.log(`errors (${n}):`, s.page.errors.slice(0, 4).join(' | '));
  }
  console.log(`\n${results.filter(Boolean).length}/${results.length} passed`);
  await browser.close();
  process.exit(results.every(Boolean) ? 0 : 1);
})().catch((e) => { console.error(e); process.exit(1); });
