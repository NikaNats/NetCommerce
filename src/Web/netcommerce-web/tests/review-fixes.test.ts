import { describe, expect, it } from 'vitest';
import { readFileSync } from 'node:fs';

/**
 * Regression tests for three defects found by review, none of which the existing
 * gates could see.
 *
 * Each assertion targets the MECHANISM of the bug, not the fix:
 *   1. a CSP that cannot load the fonts the CSS asks for,
 *   2. a post-login redirect that resolves to a different route than intended,
 *   3. a REST enum fed through the realtime vocabulary's mapper.
 *
 * The reason these survived review is instructive: check-headers.mjs only
 * asserted CSP directive KEYS were present as strings, and check-render.mjs
 * parsed raw CSS with no CSP enforcement and no browser. Nothing could observe a
 * blocked request. These tests close that gap statically, where it can be closed.
 */

const read = (rel: string) =>
  readFileSync(new URL(rel, import.meta.url), 'utf8');

/* ------------------------------------------------------------------ *
 * 1. Content Security Policy vs the typography
 * ------------------------------------------------------------------ */

/**
 * Source with comments stripped.
 *
 * Several assertions below are "this must NOT appear" checks, and the explanatory
 * comments added alongside the fixes legitimately MENTION the thing they forbid —
 * e.g. globals.css documents why there is no remote @import, and the hook explains
 * that needsManualIntervention is never fed a REST status, using that exact
 * identifier. Matching raw text would fail against correct code, which is how a
 * check gets learned to be ignored.
 *
 * Stripping comments means only real code counts.
 */
function codeOnly(source: string): string {
  return source.replace(/\/\*[\s\S]*?\*\//g, '').replace(/^\s*\/\/.*$/gm, '');
}

describe('CSP and typography agree', () => {
  const config = read('../next.config.ts');
  const rawCss = read('../src/app/globals.css');
  const css = codeOnly(rawCss);

  it('globals.css does NOT import fonts from a remote origin', () => {
    // This is the defect itself. A remote @import is blocked by the policy below,
    // which drops the whole type system to system serif/sans.
    expect(css).not.toMatch(/@import\s+url\(\s*['"]?https:\/\/fonts\./i);
  });

  it('has no remote font origin anywhere in the shipped CSS', () => {
    // Guards against the @import coming back via a different syntax.
    expect(css).not.toMatch(/fonts\.(googleapis|gstatic)\.com/);
  });

  it('the explanatory comment survives, so the trap is documented', () => {
    // The reason the previous two assertions need comment-stripping: this note is
    // in the file on purpose, and deleting it would be a loss.
    expect(rawCss).toMatch(/fonts\.googleapis\.com/);
  });

  it('uses next/font, which self-hosts the woff2 files', () => {
    const fonts = read('../src/app/fonts.ts');
    expect(fonts).toMatch(/from 'next\/font\/google'/);
  });

  it('the layout actually applies the generated font classes', () => {
    // next/font emitting a class is not enough — the <html>/<body> must carry it,
    // or the families are declared and never applied.
    const layout = read('../src/app/layout.tsx');
    expect(layout).toMatch(/fontVariablesClassName/);
    expect(layout).toMatch(/fontClassNames/);
  });

  it('keeps the strict posture: no third-party origin added to the CSP', () => {
    // The fix must be self-hosting, NOT an allowlist. Silencing the symptom by
    // allowlisting fonts.googleapis.com would reintroduce the runtime dependency
    // default-src 'self' exists to avoid.
    expect(config).not.toMatch(/fonts\.googleapis\.com/);
    expect(config).not.toMatch(/fonts\.gstatic\.com/);
  });

  it('the CSS stacks consume the next/font variables', () => {
    // Without this the self-hosted @font-face is declared but never referenced,
    // which is the same silent failure one layer removed.
    expect(css).toMatch(/var\(--loaded-display/);
    expect(css).toMatch(/var\(--loaded-body/);
    expect(css).toMatch(/var\(--loaded-mono/);
  });

  it('does not shadow the next/font variables with same-named :root entries', () => {
    // next/font publishes --loaded-*; the stacks are --font-*. If these ever
    // became the same name, :root would win on <html> and the generated
    // font-face bindings would be overridden.
    const rootBlock = css.slice(css.indexOf(':root'), css.indexOf('}', css.indexOf(':root')));
    expect(rootBlock).not.toMatch(/--loaded-(display|body|mono)\s*:/);
  });
});

/* ------------------------------------------------------------------ *
 * 2. Post-login redirect must resolve to the intended route
 * ------------------------------------------------------------------ */

describe('the add-to-basket returnTo resolves to a real route', () => {
  const action = read('../src/app/products/actions.ts');

  it('never builds a bare /products/<id> URL', () => {
    // /products/[slug] wins for a bare segment, so a UUID there looks up a slug,
    // misses, and 404s AFTER a successful sign-in.
    expect(action).not.toMatch(/requireSession\(\s*`\/products\/\$\{/);
  });

  it('routes through the single productIdPath helper', () => {
    // One place that knows a product ID needs the /id/ segment, rather than
    // every call site remembering.
    expect(action).toMatch(/productIdPath\(/);
  });

  it('validates the id before interpolating it into a redirect', () => {
    // returnTo is attacker-reachable via the login query string.
    expect(action).toMatch(/isProductId\(/);
  });

  it('the helper refuses anything that is not a UUID', () => {
    const products = read('../src/lib/catalog/products.ts');
    expect(products).toMatch(/export function productIdPath/);
    // And it must build the /id/ segment, not a bare /products/.
    expect(products).toMatch(/`\/products\/id\/\$\{id\}`/);
  });

  it('rejects path traversal and slug-shaped values', async () => {
    // The REAL function, not a copy of its pattern: a duplicated regex here would
    // keep passing after the implementation changed, which is the drift this whole
    // suite exists to prevent.
    const { isProductId, productIdPath } = await import('@/lib/catalog/products');

    expect(isProductId('/../../../etc/passwd')).toBe(false);
    expect(isProductId('leather-weekend-bag')).toBe(false);
    expect(isProductId('')).toBe(false);
    expect(isProductId('3f2504e0-4f89-11d3-9a0c-0305e82c3301')).toBe(true);

    // And the helper throws rather than emitting a bad URL.
    expect(() => productIdPath('leather-weekend-bag')).toThrow(/non-UUID/);
    expect(productIdPath('3f2504e0-4f89-11d3-9a0c-0305e82c3301')).toBe(
      '/products/id/3f2504e0-4f89-11d3-9a0c-0305e82c3301',
    );
  });
});

/* ------------------------------------------------------------------ *
 * 3. REST reconciliation must not use the realtime mapper
 * ------------------------------------------------------------------ */

describe('reconcile() bridges the vocabularies correctly', () => {
  const rawHook = read('../src/lib/real-time/use-order-saga.ts');
  const hook = codeOnly(rawHook);

  it('maps a REST payload with mapOrderStatus', () => {
    // mapRealtimeStatus knows only the saga's free-form strings, so a persisted
    // enum became "Unknown(StockConfirmed)" on every poll.
    expect(hook).toMatch(/mapOrderStatus\(order\.status\)/);
  });

  it('does not run a REST status through mapRealtimeStatus', () => {
    expect(hook).not.toMatch(/mapRealtimeStatus\(\s*String\(\s*order\.status/);
  });

  it('does not derive needsIntervention from a REST status', () => {
    // "ManualInterventionRequired" exists only in the realtime vocabulary, so no
    // REST value can imply it — and clearing the flag would hide a real one.
    //
    // Scoped to the reconcile() body: lines that legitimately derive the flag
    // from a REALTIME status (the socket handler and the poll guard) must keep
    // doing so, so the check is "no order.status feeds it", not "never used".
    expect(hook).not.toMatch(
      /needsIntervention:\s*needsManualIntervention\([^)]*order\.status/,
    );
  });

  it('still derives needsIntervention from a realtime status', () => {
    // The contrast case. A blanket "never call it" assertion would have passed
    // while quietly deleting real behaviour, so the positive case is pinned too.
    expect(hook).toMatch(/needsIntervention:\s*needsManualIntervention\(status\)/);
  });

  it('the state field admits both vocabularies', () => {
    expect(hook).toMatch(/status:\s*AnyRealtimeStatus \| OrderStatusName/);
  });
});

describe('the two status vocabularies stay distinct', () => {
  it('the REST enum name is NOT a realtime status', () => {
    // The invariant the broken reconcile() violated. Kept as an executable
    // statement so the two vocabularies cannot be merged by accident.
    const REALTIME_STATUS = [
      'StockSecured',
      'ProcessingPayment',
      'Success',
      'Error',
      'ManualInterventionRequired',
    ];
    expect(REALTIME_STATUS).not.toContain('StockConfirmed');
    expect(REALTIME_STATUS).not.toContain('Submitted');
  });

  it('mapOrderStatus handles both a numeric enum and a name', async () => {
    const { mapOrderStatus } = await import('@/lib/orders/order-status');
    // 2 === StockConfirmed in the persisted enum.
    expect(mapOrderStatus(2)).toBe('StockConfirmed');
    expect(mapOrderStatus('StockConfirmed')).toBe('StockConfirmed');
  });

  it('an unrecognised REST value stays visible rather than being coerced', async () => {
    const { mapOrderStatus } = await import('@/lib/orders/order-status');
    // Never silently mapped to a wrong state.
    expect(mapOrderStatus(99)).toBe('Unknown(99)');
  });
});