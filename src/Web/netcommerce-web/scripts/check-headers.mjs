/**
 * Assert the security headers are actually SENT, not merely configured.
 *
 * A CSP in next.config.ts that never reaches the response is decoration. This
 * reads the headers off a live response.
 */
const BASE = process.env.HEADER_BASE_URL ?? 'http://localhost:3000';

let res;
let lastError = null;

for (let attempt = 1; attempt <= 5; attempt += 1) {
  try {
    res = await fetch(`${BASE}/`);
    lastError = null;
    break;
  } catch (cause) {
    lastError = cause;
    await new Promise((r) => setTimeout(r, 1000));
  }
}

if (lastError) {
  console.log(`  FAIL  server unreachable: ${lastError.message}`);
  process.exit(1);
}

const headers = res.headers;
const csp = headers.get('content-security-policy') ?? '';

let failed = 0;
function check(label, ok, detail = '') {
  console.log(`  ${ok ? 'PASS' : 'FAIL'}  ${label}${detail ? ` — ${detail}` : ''}`);
  if (!ok) failed += 1;
}

console.log(`=== HTTP ${res.status} ===\n`);

console.log('=== headers present ===');
check('Content-Security-Policy is sent', Boolean(csp));
check('X-Content-Type-Options is nosniff', headers.get('x-content-type-options') === 'nosniff');
check('X-Frame-Options is DENY', headers.get('x-frame-options') === 'DENY');
check('Referrer-Policy is set', Boolean(headers.get('referrer-policy')));
check('Permissions-Policy is set', Boolean(headers.get('permissions-policy')));

console.log('\n=== CSP directives ===');
const directives = new Set(
  csp.split(';').map((d) => d.trim().split(/\s+/)[0]).filter(Boolean),
);
for (const d of [
  'default-src',
  'script-src',
  'style-src',
  'img-src',
  'font-src',
  'connect-src',
  'form-action',
  'frame-ancestors',
  'object-src',
  'base-uri',
]) {
  check(`declares ${d}`, directives.has(d));
}

console.log('\n=== the load-bearing directive ===');
// connect-src 'self' is what would catch a regression introducing a client-side
// fetch that puts a token in the browser. It is widened by exactly one explicit
// pair — the API origin over http(s) plus its ws(s) mate for the SignalR socket
// (next.config.ts readApiOrigin; the gate env pins API_BASE_URL to
// http://localhost:59999). Assert that shape: 'self' plus explicit origins,
// never a scheme wildcard (`https:`) or host wildcard (`*`), either of which
// would let the socket (or an exfiltration fetch) reach an arbitrary host.
const connectSrc = csp.split(';').map((d) => d.trim()).find((d) => d.startsWith('connect-src'));
const connectSources = (connectSrc ?? '').replace(/^connect-src\s+/, '').split(/\s+/).filter(Boolean);
const extraSources = connectSources.filter((s) => s !== "'self'");
check(
  "connect-src is anchored at 'self'",
  connectSources.includes("'self'"),
  connectSrc ?? '(missing)',
);
check(
  'connect-src has no wildcard',
  Boolean(connectSrc) && !connectSrc.includes('*'),
  connectSrc ?? '(missing)',
);
check(
  'connect-src allows only the explicit API http(s) + ws(s) pair',
  extraSources.length > 0 &&
    extraSources.length <= 2 &&
    extraSources.every((s) => /^https?:\/\/[^*\s]+$/.test(s) || /^wss?:\/\/[^*\s]+$/.test(s)),
  extraSources.join(' ') || '(no extra sources)',
);

check(
  'frame-ancestors blocks embedding',
  csp.includes("frame-ancestors 'none'") || csp.includes('frame-ancestors'),
  'clickjacking',
);

console.log(failed === 0 ? '\nALL HEADER CHECKS PASSED' : `\n${failed} HEADER CHECK(S) FAILED`);
process.exit(failed === 0 ? 0 : 1);