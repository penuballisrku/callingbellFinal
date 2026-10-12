/**
 * Browser checks for the AI search assistant: the animated "Ask AI" button (and reduced motion), suggestions while typing (keyboard,
 * mouse, clear, Escape, click outside), place suggestions changing the location, device location (allowed and refused), one request
 * per pick, and the phone layout.
 *
 *   cd tests; npm install; node e2e/ai-search.cjs
 *
 * Settings (environment): CB_WEB (default http://localhost:5173), CB_CHROME (default: the installed Chrome).
 * Device location is simulated at Kukatpally, Hyderabad (17.4948, 78.3996). Screenshots go to tests/e2e/output.
 */
const path = require('path');
const fs = require('fs');
const { chromium } = require('playwright-core');
const BASE = (process.env.CB_WEB || 'http://localhost:5173').replace(/\/$/, '');
const CHROME = process.env.CB_CHROME || 'C:/Program Files/Google/Chrome/Application/chrome.exe';
const OUT = path.join(__dirname, 'output');
fs.mkdirSync(OUT, { recursive: true });
const results = [];
const check = (name, ok, detail = '') => { results.push(!!ok); console.log(`${ok ? 'PASS' : 'FAIL'} ${name}${detail ? `  [${detail}]` : ''}`); };
const KUKATPALLY = { latitude: 17.4948, longitude: 78.3996 };

async function page(browser, options = {}) {
  const ctx = await browser.newContext({ viewport: { width: 1366, height: 860 }, ...options });
  const p = await ctx.newPage();
  p.errors = [];
  p.assistantPosts = 0;
  p.on('pageerror', (e) => p.errors.push(e.message));
  p.on('console', (m) => { if (m.type() === 'error' && !/WebSocket|negotiat|Failed to load resource/.test(m.text())) p.errors.push(m.text().slice(0, 160)); });
  p.on('request', (r) => { if (r.method() === 'POST' && r.url().includes('/api/search/assistant')) p.assistantPosts += 1; });
  return { ctx, p };
}

const box = (p) => p.getByRole('combobox', { name: 'Message the AI search assistant' });
const listbox = (p) => p.locator('[role="dialog"] [role="listbox"][aria-label="Search suggestions"]');
async function openAssistant(p) {
  await p.goto(BASE + '/', { waitUntil: 'networkidle' });
  await p.getByRole('button', { name: /Open AI search assistant/ }).first().click();
  await box(p).waitFor({ timeout: 10_000 });
}
async function typeAndWait(p, text) {
  await box(p).fill('');
  await box(p).pressSequentially(text, { delay: 40 });
  await listbox(p).locator('[role="option"]').first().waitFor({ timeout: 10_000 });
}

(async () => {
  const browser = await chromium.launch({ executablePath: CHROME });

  // ---------- Ask AI button ----------
  {
    const { ctx, p } = await page(browser);
    await p.goto(BASE + '/', { waitUntil: 'networkidle' });
    const btn = p.getByRole('button', { name: /Open AI search assistant/ }).first();
    const anim = await btn.evaluate((el) => [getComputedStyle(el).animationName, getComputedStyle(el.querySelector('.cb-ai-spark')).animationName]);
    check('Ask AI glows and its sparkle twinkles', anim[0] === 'cb-ai-glow' && anim[1] === 'cb-ai-twinkle', anim.join(', '));
    await ctx.close();
    const reduced = await page(browser, { reducedMotion: 'reduce' });
    await reduced.p.goto(BASE + '/', { waitUntil: 'networkidle' });
    const still = await reduced.p.getByRole('button', { name: /Open AI search assistant/ }).first()
      .evaluate((el) => [getComputedStyle(el).animationName, getComputedStyle(el.querySelector('.cb-ai-spark')).animationName]);
    check('Reduced motion: no animation', still.every((a) => a === 'none'), still.join(', '));
    await reduced.ctx.close();
  }

  // ---------- Suggestions, keyboard, device location allowed ----------
  {
    const { ctx, p } = await page(browser, { permissions: ['geolocation'], geolocation: KUKATPALLY });
    await openAssistant(p);
    await p.waitForFunction(() => sessionStorage.getItem('cb-device-location') === 'located', null, { timeout: 15_000 }).catch(() => undefined);
    const located = await p.evaluate(() => [sessionStorage.getItem('cb-device-location'), JSON.parse(sessionStorage.getItem('cb-location-v2') || '{}').state]);
    check('Device location used automatically when allowed', located[0] === 'located' && located[1]?.source === 'device',
      `${located[1]?.areaName ?? ''}, ${located[1]?.cityName ?? ''}`);
    check('Location row says it is from the device', await p.locator('[role="dialog"]').getByText(/from your device/).count() > 0);

    await typeAndWait(p, 'ac');
    const opts = await listbox(p).locator('[role="option"]').count();
    const boxRect = await box(p).boundingBox();
    const listRect = await listbox(p).boundingBox();
    check('Two letters show suggestions', opts > 0, `${opts} options`);
    check('List opens above the box, inside the screen', listRect.y + listRect.height <= boxRect.y && listRect.y >= 0);
    check('Matching text is highlighted', await listbox(p).locator('mark').count() > 0);
    check('Combobox is expanded', (await box(p).getAttribute('aria-expanded')) === 'true');

    await typeAndWait(p, 'ac rep');
    const nearYou = await listbox(p).locator('[role="option"]').allInnerTexts();
    const combined = nearYou.find((t) => / in /.test(t));
    check('Service-in-your-area suggestion with a real count', !!combined && /(business|None listed)/.test(combined), combined?.replace(/\s+/g, ' '));

    await box(p).press('ArrowDown');
    const activeId = await box(p).getAttribute('aria-activedescendant');
    const activeText = activeId ? (await p.locator(`[id="${activeId}"]`).innerText()).split('\n')[0] : '';
    check('Arrow keys move the highlight', !!activeId, activeText);
    await p.screenshot({ path: path.join(OUT, 'ai-search-suggestions.png') });
    const before = p.assistantPosts;
    await box(p).press('Enter');
    await p.locator('[role="dialog"] p.bg-navy').first().waitFor({ timeout: 10_000 });
    await p.waitForTimeout(1500);
    const sent = await p.locator('[role="dialog"] p.bg-navy').last().innerText();
    check('Enter picks the highlighted suggestion and asks the assistant', sent.trim().length > 0, sent);
    check('Exactly one assistant request for the pick', p.assistantPosts - before === 1, `${p.assistantPosts - before} request(s)`);

    await typeAndWait(p, 'plumb');
    await box(p).press('Escape');
    await p.waitForTimeout(400);
    check('Escape closes the list', (await box(p).getAttribute('aria-expanded')) === 'false');
    check('…but not the assistant', await box(p).isVisible());
    await box(p).press('ArrowDown');
    await listbox(p).waitFor();
    await p.locator('[role="dialog"] h2#assistant-title').click();
    check('Clicking outside closes the list', (await box(p).getAttribute('aria-expanded')) === 'false');
    await p.getByRole('button', { name: 'Clear search' }).click({ force: true }).catch(() => undefined);
    check('Clear empties the box', (await box(p).inputValue()) === '');

    // A place suggestion changes the location, and suggestions follow it.
    await typeAndWait(p, 'Ameerpet');
    const place = listbox(p).locator('[role="option"]', { hasText: 'Area ·' }).first();
    const placeName = (await place.innerText()).split('\n')[0];
    await place.click();
    const store = await p.evaluate(() => JSON.parse(sessionStorage.getItem('cb-location-v2') || '{}').state);
    check('Choosing a place selects it as the location', store?.areaName === placeName && store?.source === 'user', `${store?.areaName}, ${store?.citySlug}`);
    check('The box is ready for what to search', (await box(p).inputValue()) === '');
    await typeAndWait(p, 'ac rep');
    await listbox(p).locator('[role="option"]', { hasText: `in ${placeName}` }).first().waitFor({ timeout: 10_000 }).catch(() => undefined);
    const after = await listbox(p).locator('[role="option"]').allInnerTexts();
    check('Suggestions follow the new place', after.some((t) => t.includes(`in ${placeName}`)), after.find((t) => t.includes(' in '))?.replace(/\s+/g, ' '));
    check('No suggestion for the previous place', !after.some((t) => t.includes('in KPHB Colony')));
    check('No page errors', p.errors.length === 0, p.errors.join(' | '));
    await ctx.close();
  }

  // ---------- Device location refused: the existing location stays, search still works ----------
  {
    const { ctx, p } = await page(browser);
    await openAssistant(p);
    // A browser that will ask shows "Near me" with the reason; one already set to block shows that location access is off.
    const permission = await p.evaluate(async () => (await navigator.permissions.query({ name: 'geolocation' })).state);
    await p.waitForTimeout(800);
    if (permission === 'prompt') {
      const nearMe = p.getByRole('button', { name: 'Near me' });
      check('"Near me" offered with a reason', await nearMe.count() > 0 && await p.getByText(/only picks your area/).count() > 0);
      await nearMe.click();
    } else {
      check('Blocked location: no prompt, says access is off', await p.getByRole('button', { name: 'Near me' }).count() === 0
        && await p.getByText(/Location access is off/).count() > 0, `permission ${permission}`);
    }
    await p.waitForFunction(() => ['denied', 'unavailable'].includes(sessionStorage.getItem('cb-device-location') ?? '')
      || document.body.innerText.includes('Location access is off'), null, { timeout: 15_000 }).catch(() => undefined);
    const state = await p.evaluate(() => [sessionStorage.getItem('cb-device-location') ?? 'denied (not asked)', JSON.parse(sessionStorage.getItem('cb-location-v2') || '{}').state?.source]);
    check('Refused location is handled', /denied|unavailable/.test(state[0] ?? ''), `${state[0]}, location from ${state[1]}`);
    check('Approximate location kept', state[1] === 'ip' || state[1] === 'user');
    await typeAndWait(p, 'doctor');
    check('Suggestions still work', await listbox(p).locator('[role="option"]').count() > 0);
    await ctx.close();
  }

  // ---------- Phone ----------
  {
    const { ctx, p } = await page(browser, { viewport: { width: 390, height: 844 }, isMobile: true, hasTouch: true });
    await openAssistant(p);
    await typeAndWait(p, 'elec');
    const fits = await p.evaluate(() => {
      const list = document.querySelector('[role="dialog"] [role="listbox"]').getBoundingClientRect();
      return document.documentElement.scrollWidth <= innerWidth && list.left >= 0 && list.right <= innerWidth && list.top >= 0;
    });
    check('Phone: list fits the screen, no sideways scroll', fits);
    await p.screenshot({ path: path.join(OUT, 'ai-search-phone.png') });
    await listbox(p).locator('[role="option"]').first().tap();
    await p.locator('[role="dialog"] p.bg-navy').first().waitFor({ timeout: 10_000 }).then(() => check('Phone: tapping a suggestion asks it', true))
      .catch(() => check('Phone: tapping a suggestion asks it', false));
    check('Phone: no page errors', p.errors.length === 0, p.errors.join(' | '));
    await ctx.close();
  }

  await browser.close();
  const failed = results.filter((r) => !r).length;
  console.log(`\n${results.length - failed}/${results.length} checks passed`);
  process.exit(failed ? 1 : 0);
})().catch((e) => { console.error(e); process.exit(1); });
