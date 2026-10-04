import { describe, expect, it } from 'vitest';
import { readFileSync } from 'node:fs';

import { buildRequestHeaders } from '@/lib/api/headers';

const read = (rel: string) => readFileSync(new URL(rel, import.meta.url), 'utf8');

/**
 * Code without `//` comment lines.
 *
 * The guards below must inspect the DIRECTIVES, not the prose about them: the
 * config comments legitimately quote the old policy and explain why a rewrite
 * cannot proxy websockets, and a whole-file regex would flag that prose as a
 * violation.
 */
const code = (source: string) =>
  source
    .split('\n')
    .filter((line) => !line.trimStart().startsWith('//'))
    .join('\n');

/**
 * Regression tests for four CSP / cache / correctness findings.
 *
 * None of these are visible to a passing build. Every one produces a page that
 * renders, with parts silently missing or requests silently failing — which is
 * exactly why the gates missed them and why they are pinned statically here.
 */

describe('CSP img-src permits the configured storage origin', () => {
  const config = code(read('../next.config.ts'));

  it('does not hardcode an img-src that blocks cleartext origins', () => {
    // The API's development CdnBaseUrl is http://localhost:9000 (MinIO), CLEARTEXT.
    // `'self' data: https:` allows neither, so every product image was blocked.
    expect(config).not.toMatch(/`img-src 'self' data: https:`/);
  });

  it('reads the origin from configuration rather than widening to http:', () => {
    // A blanket `http:` would permit ANY cleartext origin, weakening production too.
    expect(config).toContain('readStorageOrigin()');
    expect(config).toMatch(/STORAGE_ORIGIN/);
  });

  it('does not unconditionally permit cleartext anywhere', () => {
    // A regression guard: the fix must be an allowlist, not a blanket scheme.
    expect(config).not.toMatch(/img-src[^`]*\bhttp:(?!\/\/)/);
  });
});

describe('CSP connect-src permits the realtime hub', () => {
  const config = code(read('../next.config.ts'));

  it('no longer pins connect-src to self alone', () => {
    // The SignalR client opens a websocket to the API origin. Under
    // `connect-src 'self'` the browser aborts it outright.
    expect(config).not.toMatch(/`connect-src 'self'`/);
  });

  it('allowlists the API origin for the hub', () => {
    expect(config).toContain('readApiOrigin()');
  });

  it('does not use a wildcard, which would permit exfiltration to any host', () => {
    // Scoped to the connect-src directive line (the one built from
    // readApiOrigin): a whole-file `*` search also matches JSDoc comment
    // asterisks and the unrelated `/:path*` route pattern below it.
    const directive = config
      .split('\n')
      .filter((line) => line.includes('connect-src') && line.includes('readApiOrigin'));
    expect(directive.length).toBeGreaterThan(0);
    for (const line of directive) expect(line).not.toContain('*');
  });

  it('does NOT try to rewrite /api/messages through Next', () => {
    // A Next Route Handler cannot perform the HTTP Upgrade handshake a websocket
    // needs, so a rewrite would still 404. Asserting its absence prevents someone
    // "fixing" this with a rewrite that cannot work. Matched as a method
    // definition so the comment explaining WHY is not flagged.
    expect(config).not.toMatch(/async\s+rewrites\s*\(/);
  });
});

describe('the hub points at the API origin, not a same-origin path', () => {
  const hook = read('../src/lib/real-time/use-order-saga.ts');

  it('does not connect to a bare /api/messages on this origin', () => {
    // That is a guaranteed 404: no such route exists under src/app/.
    expect(hook).not.toMatch(/\.withUrl\('\/api\/messages'/);
  });

  it('builds the URL from an explicit origin', () => {
    expect(hook).toMatch(/withUrl\(`\$\{hubOrigin\(\)\}\/api\/messages`/);
  });

  it('sends no bearer token over the socket', () => {
    // The socket carries the httpOnly session cookie only. A token here would put
    // it in the browser, which this architecture refuses to do.
    const urlBlock = hook.slice(hook.indexOf('new HubConnectionBuilder()'), hook.indexOf('.build()'));
    expect(urlBlock).not.toMatch(/accessToken|bearer/i);
  });
});

describe('cached GETs do not carry per-request-unique headers', () => {
  it('omits X-Correlation-ID entirely when it is not supplied', () => {
    // Next hashes ALL headers into the fetch cache key. A random correlation id per
    // call means a unique key per call: 0% hit rate and an orphan written per call.
    const headers = buildRequestHeaders({
      method: 'GET',
      accessToken: undefined,
      idempotencyKey: undefined,
      correlationId: undefined,
    });

    expect(headers.has('X-Correlation-ID')).toBe(false);
  });

  it('still sends it when explicitly supplied', () => {
    // Non-cached calls must remain traceable — the fix is not "stop tracing".
    const headers = buildRequestHeaders({
      method: 'GET',
      accessToken: undefined,
      idempotencyKey: undefined,
      correlationId: 'fixed-id',
    });

    expect(headers.get('X-Correlation-ID')).toBe('fixed-id');
  });

  it('never stringifies undefined into a literal "undefined" header', () => {
    const headers = buildRequestHeaders({
      method: 'GET',
      accessToken: undefined,
      idempotencyKey: undefined,
      correlationId: undefined,
    });

    expect(headers.get('X-Correlation-ID')).toBeNull();
  });

  it('produces IDENTICAL headers for two cached calls, so the cache key matches', () => {
    // The property that actually matters, stated as behaviour rather than as an
    // inspection of client.server.ts: two anonymous catalogue reads must produce
    // byte-identical header sets, which is what lets Next dedupe them.
    const build = () =>
      buildRequestHeaders({
        method: 'GET',
        accessToken: undefined,
        idempotencyKey: undefined,
        correlationId: undefined,
      });

    expect([...build().entries()]).toEqual([...build().entries()]);
  });

  it('client.server omits the bearer token on cacheable calls', () => {
    // Next treats an `authorization` header as making a fetch uncacheable outright
    // (patch-fetch.ts hasUnCacheableHeader). The catalogue GETs are public, so
    // sending a token there both breaks caching and grants nothing.
    const client = read('../src/lib/api/client.server.ts');
    expect(client).toMatch(/const isCached = Boolean\(/);
    expect(client).toMatch(/isCached \? null : await getSession\(\)/);
  });

  it('keeps the idempotency key on mutations', () => {
    // The cache fix must not weaken the mutation contract.
    const headers = buildRequestHeaders({
      method: 'POST',
      accessToken: undefined,
      idempotencyKey: 'key-1',
      correlationId: undefined,
    });

    expect(headers.get('X-Idempotency-Key')).toBe('key-1');
  });
});

describe('a lock loser is never handed an expired access token', () => {
  const session = read('../src/lib/auth/server-session.ts');

  it('checks needsRefresh() on the rehydrated session after losing the lock', () => {
    // waitForRotation exits early once the lock is released, which is right for the
    // thundering herd — but a transient failure by the winner means nothing was
    // written, so the loser gets the ORIGINAL pair with a stale access token. That
    // produced a 401 from the API on the request the lock exists to make succeed.
    const loserBlock = session.slice(
      session.indexOf('if (!outcome.acquired)'),
      session.indexOf('if (!outcome.value)'),
    );

    expect(loserBlock).toMatch(/needsRefresh\(\)/);
  });

  it('returns undefined rather than a session that still needs rotation', () => {
    const loserBlock = session.slice(
      session.indexOf('if (!outcome.acquired)'),
      session.indexOf('if (!outcome.value)'),
    );

    expect(loserBlock).toMatch(/needsRefresh\(\)[\s\S]{0,200}return undefined/);
  });
});