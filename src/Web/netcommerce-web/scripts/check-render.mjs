import { writeFileSync } from 'node:fs';

/**
 * Assert design invariants against the page AND its stylesheets.
 *
 * Dev serves CSS as a separate file, so checking only the HTML reports a FALSE
 * FAILURE for anything defined in a stylesheet (fonts, breakpoints, clamp()).
 * Follow the stylesheet <link>s and check them too.
 */
const BASE = process.env.RENDER_BASE_URL ?? 'http://localhost:3000';

/**
 * Fetch with retry.
 *
 * `next dev` reports "Ready" before the first route has compiled, and that first
 * request can close the socket mid-response. A single unguarded fetch made this
 * gate flaky — it failed with UND_ERR_SOCKET on a clean tree, which is worse than
 * no gate, because a red build trains people to ignore it.
 *
 * Bounded and explicit: a genuine failure still fails, just not on the very first
 * cold compile.
 */
async function fetchWithRetry(url, attempts = 5, delayMs = 1000) {
  let lastError;

  for (let attempt = 1; attempt <= attempts; attempt += 1) {
    try {
      return await fetch(url);
    } catch (cause) {
      lastError = cause;
      if (attempt < attempts) {
        console.log(
          `  (retry ${attempt}/${attempts - 1} after ${cause.message})`,
        );
        await new Promise((resolve) => setTimeout(resolve, delayMs));
      }
    }
  }

  throw lastError;
}

const res = await fetchWithRetry(BASE + '/');
const html = await res.text();

console.log(`=== HTTP ${res.status}, ${html.length} bytes of HTML ===`);

const hrefs = [...html.matchAll(/<link[^>]+rel="stylesheet"[^>]*>/g)]
  .map((m) => m[0].match(/href="([^"]+)"/)?.[1])
  .filter(Boolean);

const cssChunks = [];
for (const href of hrefs) {
  const url = href.startsWith('http') ? href : BASE + href;
  try {
    const cssRes = await fetch(url);
    if (cssRes.ok) cssChunks.push(await cssRes.text());
  } catch {
    /* stylesheet unreachable; surfaces as a failure below via empty css */
  }
}

const css = cssChunks.join('\n');
console.log(`=== ${hrefs.length} stylesheet(s), ${css.length} bytes of CSS ===`);
if (!css) console.log('!! NO CSS FETCHED — invariant checks below are UNRELIABLE');

const blob = `${html}\n${css}`;

let failures = 0;
const check = (pattern, label, on = blob) => {
  const ok = new RegExp(pattern, 'i').test(on);
  console.log(`  ${ok ? 'PASS' : 'FAIL'}  ${label}`);
  if (!ok) failures += 1;
};
const refute = (pattern, label, on = blob) => {
  const found = new RegExp(pattern, 'i').test(on);
  console.log(`  ${found ? 'FAIL' : 'PASS'}  ${label}`);
  if (found) failures += 1;
};

console.log('\n=== typography (Bodoni Moda + IBM Plex Mono, never AI-default) ===');
check('Bodoni\\+Moda|Bodoni Moda', 'Bodoni Moda display face referenced');
check('IBM\\+Plex\\+Mono|IBM Plex Mono', 'IBM Plex Mono referenced');
// The detector flags Fraunces/Inter/Roboto as overused AI-convergent faces.
refute('Fraunces', 'Fraunces NOT used (detector-flagged overused face)', css);

// Banned-font checks run against the AUTHORED stylesheet only. Next's dev-mode
// error overlay ships its own inline `fontFamily: system-ui, ..., Roboto,
// Helvetica, Arial, sans-serif` for the dev-tools panel. That is framework
// boilerplate in a development-only affordance — asserting on the whole HTML
// would flag someone else's code. The authored CSS is what this app ships.
refute('font-family:[^;}]*\\bInter\\b', 'Inter NOT used in authored CSS', css);
refute('\\bRoboto\\b', 'Roboto NOT used in authored CSS', css);
refute('\\bArial\\b', 'Arial NOT used in authored CSS', css);
refute('font-family:[^;}]*system-ui', 'no system-ui font stack in authored CSS', css);

console.log('\n=== structure / accessibility ===');
check('skip-link', 'skip link present');
check('id="main"', 'main landmark target');
check('lang="en"', 'html lang attribute');
check('<main', 'semantic <main> element');
check('prefers-reduced-motion', 'reduced-motion honoured');
check(':focus-visible', 'visible keyboard focus styles');

console.log('\n=== anti-slop ===');
refute('linear-gradient', 'no linear-gradient');
refute('radial-gradient', 'no radial-gradient');
refute('\\b(purple|violet|indigo)\\b', 'no purple/violet/indigo palette');

const EMOJI = /[\u{1F300}-\u{1FAFF}\u{2600}-\u{27BF}\u{FE0F}]/u;
const emoji = EMOJI.test(blob);
console.log(`  ${emoji ? 'FAIL' : 'PASS'}  no emoji glyphs`);
if (emoji) failures += 1;

console.log('\n=== refused patterns (design system bans) ===');
// A kicker/eyebrow above a heading is a ban, not a default — the heading must
// carry its own weight. The class was deleted from the stylesheet; assert the
// ban so it cannot creep back.
refute('class="[^"]*\\beyebrow\\b', 'no .eyebrow kicker class in markup', html);
refute('\\.eyebrow\\s*\\{', 'no .eyebrow rule in CSS', css);
// A >1px coloured rule on a callout/card is refused; depth must come from an
// offset + soft-blur shadow.
const thickAccent = /border-(left|right):\s*(?:[2-9]|\d{2,})px/.exec(css);
console.log(`  ${thickAccent ? 'FAIL' : 'PASS'}  no >1px accent border (${thickAccent?.[0] ?? 'none found'})`);
if (thickAccent) failures += 1;
check('box-shadow', 'depth via offset + soft-blur shadow', css);

console.log('\n=== browser surfaces themed ===');
check('::selection', 'text selection themed', css);
check('caret-color', 'caret themed', css);
check('scrollbar', 'scrollbars themed', css);

console.log('\n=== mono discipline ===');
// Mono is for real data (ids, prices, codes), not costume for "technical".
const monoCostume = /font-family:[^;}]*font-mono[^;}]*;[^}]*(eyebrow|status|label)/i.exec(css);
console.log(`  ${monoCostume ? 'FAIL' : 'PASS'}  mono not applied to prose labels/status`);
if (monoCostume) failures += 1;

console.log('\n=== responsive ===');
check('64rem', 'desktop breakpoint in CSS');
check('clamp\\(', 'fluid type via clamp()');

console.log('\n=== error path (API deliberately unreachable) ===');
check('API unreachable', 'unreachable-API notice rendered', html);

writeFileSync('/tmp/rendered.html', html);
writeFileSync('/tmp/rendered.css', css);
console.log('\nsaved /tmp/rendered.html + /tmp/rendered.css');

console.log(failures === 0 ? '\nALL INVARIANTS HELD' : `\n${failures} INVARIANT FAILURE(S)`);
process.exit(failures === 0 ? 0 : 1);