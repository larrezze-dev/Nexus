#!/usr/bin/env node
// NEXUS — arnés de capturas para la revisión visual (skill nx-ui-visual-review).
//
// Uso:
//   NX_PW_DIR=<dir con node_modules/playwright> node shots.mjs \
//     --out <dir> [--langs de,es] [--widths 1440,390] [--routes routes.txt | --route /legal-entities ...] \
//     [--user admin --pass admin] [--base http://localhost:4200] [--tabs] [--no-firstload]
//
// routes.txt: una línea por ruta, "nombre  /ruta" (separado por espacios); '#' = comentario.
// Salida: <out>/<lang>-<width>/<nombre>.png (+ -tabN.png por pestaña) y report.json con
// texto visible, claves i18n crudas, fuentes calculadas, idioma de <html> y errores de consola.
//
// Playwright NO es dependencia del repo: instálalo en un scratch dir
//   (cd <scratch> && npm i playwright@1.65.0)
// y usa el headless shell en caché (NX_CHROME) si la build pedida no está descargada.
import { createRequire } from 'node:module';
import fs from 'node:fs';
import path from 'node:path';

const require = createRequire(import.meta.url);
const pwPath = require.resolve('playwright', { paths: [process.env.NX_PW_DIR ?? process.cwd(), process.cwd()] });
const { chromium } = require(pwPath);

const args = process.argv.slice(2);
const opt = (name, def) => { const i = args.indexOf('--' + name); return i >= 0 ? args[i + 1] : def; };
const flag = name => args.includes('--' + name);
const OUT = opt('out'); if (!OUT) { console.error('--out es obligatorio'); process.exit(2); }
const BASE = opt('base', 'http://localhost:4200');
const LANGS = opt('langs', 'de,es').split(',');
const WIDTHS = opt('widths', '1440,390').split(',').map(Number);
const USER = opt('user', 'admin'); const PASS = opt('pass', 'admin');
const CHROME = process.env.NX_CHROME ?? path.join(process.env.HOME, 'Library/Caches/ms-playwright/chromium_headless_shell-1228/chrome-headless-shell-mac-arm64/chrome-headless-shell');

let routes = [];
const rf = opt('routes');
if (rf) routes = fs.readFileSync(rf, 'utf8').split('\n').map(l => l.trim()).filter(l => l && !l.startsWith('#')).map(l => l.split(/\s+/));
args.forEach((a, i) => { if (a === '--route') { const r = args[i + 1]; if (!r || r.startsWith('--')) { console.error('--route sin valor'); process.exit(2); } routes.push([r.replace(/[^a-z0-9]+/gi, '-').replace(/^-|-$/g, '') || 'root', r]); } });
if (!routes.length) { console.error('sin rutas (--routes o --route)'); process.exit(2); }

// Claves i18n crudas: prefijo de un grupo conocido + al menos un segmento (p. ej. audit.summary).
const KEY_GROUPS = 'common|shared|nav|shell|breadcrumb|errors|httpPages|account|org|access|audit|events|pageTitle|locations|warehouses';
const KEY_RE = new RegExp(`\\b(?:${KEY_GROUPS})\\.[a-zA-Z][a-zA-Z0-9_]*(?:\\.[a-zA-Z0-9_]+)*\\b`, 'g');
const rawKeysOf = text => [...new Set((text ?? '').match(KEY_RE) ?? [])];

// El contenido scrollea dentro de mat-sidenav-content: fullPage del documento no basta.
async function full(page, file) {
  const h = await page.evaluate(() => {
    const c = document.querySelector('mat-sidenav-content, .mat-sidenav-content');
    return c ? c.scrollHeight + c.getBoundingClientRect().top : document.documentElement.scrollHeight;
  });
  const vp = page.viewportSize();
  if (h + 8 > 5000) console.warn(`  aviso: ${file} mide ${h}px; se recorta a 5000px`);
  const H = Math.max(vp.height, Math.min(h + 8, 5000));
  if (H !== vp.height) await page.setViewportSize({ width: vp.width, height: H });
  await page.waitForTimeout(250);
  await page.screenshot({ path: file, fullPage: true });
  if (H !== vp.height) await page.setViewportSize(vp);
}

const browser = await chromium.launch({ executablePath: fs.existsSync(CHROME) ? CHROME : undefined });
for (const lang of LANGS) {
  const ctx = await browser.newContext({ viewport: { width: WIDTHS[0], height: 900 }, locale: lang === 'de' ? 'de-CH' : lang, timezoneId: 'Europe/Zurich' });
  // El idioma de la UI sale de /me.preferredLanguage: lo forzamos sin tocar la BD.
  await ctx.route('**/api/platform/v1/me', async r => {
    if (r.request().method() !== 'GET') return r.continue();
    const res = await r.fetch();
    try { const j = await res.json(); j.preferredLanguage = lang; await r.fulfill({ response: res, json: j }); }
    catch { await r.fulfill({ response: res }); }
  });
  // El idioma elegido en el selector se guarda en localStorage y manda sobre /me: lo fijamos también.
  await ctx.addInitScript(l => { try { localStorage.setItem('nx.lang', l); } catch {} }, lang);
  const page = await ctx.newPage();
  const errs = []; page.on('console', m => { if (m.type() === 'error') errs.push(m.text().slice(0, 200)); });
  await page.goto(BASE + '/');
  await page.waitForURL(/8180/, { timeout: 20000 }).catch(async () => {
    const b = page.getByRole('button').first(); if (await b.count()) await b.click(); await page.waitForURL(/8180/, { timeout: 20000 });
  });
  await page.fill('#username', USER); await page.fill('#password', PASS); await page.click('#kc-login');
  await page.waitForURL(u => u.toString().startsWith(BASE), { timeout: 20000 });
  await page.waitForLoadState('networkidle');

  for (const width of WIDTHS) {
    const dir = path.join(OUT, `${lang}-${width}`); fs.mkdirSync(dir, { recursive: true });
    await page.setViewportSize({ width, height: 900 });
    const report = {};
    for (const [name, url] of routes) {
      errs.length = 0;
      await page.goto(BASE + url); await page.waitForLoadState('networkidle').catch(() => {});
      let rawKeysFirstLoad = [];
      if (!flag('no-firstload')) {
        await page.screenshot({ path: path.join(dir, `${name}-firstload.png`) });
        rawKeysFirstLoad = rawKeysOf(await page.evaluate(() => document.body.innerText));
      }
      await page.waitForTimeout(1200);
      await full(page, path.join(dir, `${name}.png`));
      const info = await page.evaluate(() => {
        const ff = s => { const e = document.querySelector(s); return e ? getComputedStyle(e).fontFamily : null; };
        return {
          path: location.pathname, title: document.title, htmlLang: document.documentElement.lang,
          fullText: document.body.innerText,
          fonts: { body: ff('body'), button: ff('.mdc-button'), field: ff('.mat-mdc-form-field'), gridHeader: ff('.ag-header-cell-text'), h1: ff('h1'), nav: ff('mat-sidenav') },
          tabs: [...document.querySelectorAll('[role=tab]')].map(t => t.innerText.trim()),
          horizontalOverflow: [document.documentElement, document.querySelector('mat-sidenav-content, .mat-sidenav-content')]
            .filter(Boolean).some(e => e.scrollWidth > e.clientWidth + 1),
        };
      });
      info.rawKeys = rawKeysOf(info.fullText);
      info.rawKeysFirstLoad = rawKeysFirstLoad;
      info.text = info.fullText.replace(/\s+/g, ' ').slice(0, 3000);
      delete info.fullText;
      if (flag('tabs')) {
        const tabs = page.getByRole('tab'); const n = await tabs.count();
        for (let i = 1; i < n; i++) {
          if ((await tabs.nth(i).getAttribute('aria-disabled')) === 'true') continue;
          await tabs.nth(i).click(); await page.waitForTimeout(1500);
          await full(page, path.join(dir, `${name}-tab${i}.png`));
        }
      }
      info.consoleErrors = [...errs];
      report[name] = info;
      console.log(`${lang}-${width} ${name} ${info.path} raw=${info.rawKeys.length} errs=${errs.length} overflow=${info.horizontalOverflow}`);
    }
    fs.writeFileSync(path.join(dir, 'report.json'), JSON.stringify(report, null, 1));
  }
  await ctx.close();
}
await browser.close();
