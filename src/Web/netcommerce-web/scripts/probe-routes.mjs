/**
 * Probe the storefront routes from INSIDE the sandbox.
 *
 * curl is not installed there (`curl: command not found`, exit 127) — which is why
 * an earlier version of this probe reported "DEV SERVER NEVER BECAME READY" while
 * Next was serving 200s the whole time. Node's fetch is the working probe; the
 * self-request is kept as a positive control.
 *
 * The API is deliberately DOWN for these probes: data pages must degrade with a
 * notice, not crash, and must never claim a product is "not found" when the truth
 * is that the API is unreachable.
 */
const BASE = process.argv[2];

if (!BASE) {
  console.error('usage: node probe-routes.mjs <baseUrl>');
  process.exit(2);
}

const PATHS = [
  '/',
  '/catalog',
  '/basket',
  '/products/walnut-desk',
  '/products/id/00000000-0000-0000-0000-000000000000',
];

const results = [];
let failed = 0;

function check(label, ok, detail = '') {
  console.log(`  ${ok ? 'PASS' : 'FAIL'}  ${label}${detail ? ` — ${detail}` : ''}`);
  if (!ok) failed += 1;
}

console.log('=== control: is the server actually up? ===');
let controlStatus = 0;
try {
  controlStatus = (await fetch(`${BASE}/`)).status;
} catch (cause) {
  console.log(`  FAIL  server unreachable: ${cause.message}`);
  process.exit(1);
}
check('GET / returns 200', controlStatus === 200, `status=${controlStatus}`);

console.log('\n=== route status (API DOWN: must degrade, not 500) ===');
for (const path of PATHS) {
  let status = 0;
  let body = '';
  try {
    const res = await fetch(`${BASE}${path}`, { redirect: 'manual' });
    status = res.status;
    body = await res.text();
  } catch (cause) {
    console.log(`  FAIL  ${path} threw: ${cause.message}`);
    failed += 1;
    continue;
  }
  results.push({ path, status, body });
  // 3xx is legitimate: /basket must redirect an anonymous visitor to login.
  const ok = status < 500;
  check(`${path} -> ${status}`, ok, ok ? '' : 'server error');
}

const find = (p) => results.find((r) => r.path === p);
const catalog = find('/catalog');
const basket = find('/basket');
const product = find('/products/walnut-desk');

console.log('\n=== /catalog renders its shell and the search form ===');
if (catalog) {
  check('heading present', catalog.body.includes('Catalog'));
  check('search form present', /name="q"/.test(catalog.body));
  check(
    'shows a graceful API-unavailable notice',
    /unavailable|API/i.test(catalog.body),
    'no crash, no empty silence',
  );
  // The critical one: an outage must not read as "no products exist".
  check(
    'does NOT claim the catalog is empty',
    !/Nothing matches that search yet/.test(catalog.body),
  );
}

console.log('\n=== /basket guards an anonymous visitor ===');
if (basket) {
  check(
    'redirects (3xx) or renders a sign-in path',
    (basket.status >= 300 && basket.status < 400) ||
      /sign in|login/i.test(basket.body),
    `status=${basket.status}`,
  );
  check(
    'does NOT render an empty-basket state to an anonymous visitor',
    !/Nothing here yet/.test(basket.body),
    'an empty basket implies "you own nothing", not "you are signed out"',
  );
}

console.log('\n=== product page with the API down ===');
if (product) {
  check('did not 500', product.status < 500, `status=${product.status}`);
  check(
    'shows the outage notice',
    /Catalog temporarily unavailable/.test(product.body),
  );
  // Assert on the VISIBLE heading and title, not a raw substring. An earlier
  // version grepped for "not found" and matched (a) the phrase "has not been
  // withdrawn" in the notice and (b) a "Not found" <title> from a metadata
  // fallback — both false positives that hid a genuine metadata bug.
  const title = product.body.match(/<title>([^<]*)<\/title>/)?.[1] ?? '';
  check(
    'title does NOT claim the product is missing',
    !/not found/i.test(title),
    `title="${title}"`,
  );
  check(
    'page does not render a not-found state',
    !/>([^<]*)\bnot found\b/i.test(product.body.replace(/<title>[\s\S]*?<\/title>/g, '')),
  );
}

console.log(
  failed === 0
    ? '\nALL ROUTE CHECKS PASSED'
    : `\n${failed} ROUTE CHECK(S) FAILED`,
);
process.exit(failed === 0 ? 0 : 1);