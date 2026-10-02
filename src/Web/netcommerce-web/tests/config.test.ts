import { describe, expect, it } from 'vitest';

import { ConfigError, publicOrigin, readConfig } from '@/lib/config';

/**
 * A missing or misspelled env var must fail loudly, not silently fall back to
 * localhost. A production deploy that boots "healthy" and then 500s per request
 * is far harder to diagnose than one that refuses to start.
 */
/**
 * Next.js augments NodeJS.ProcessEnv so NODE_ENV is required, which makes a
 * plain object literal unassignable. Tests pass env through a helper typed as a
 * plain record so each case states exactly which variables it provides.
 */
const env = (values: Record<string, string | undefined>): NodeJS.ProcessEnv =>
  values as unknown as NodeJS.ProcessEnv;

const FULL_ENV = env({
  API_BASE_URL: 'http://api:8080',
  KEYCLOAK_BASE_URL: 'http://keycloak:8080',
  KEYCLOAK_REALM: 'netcommerce',
  KEYCLOAK_CLIENT_ID: 'netcommerce-web',
});

/** Hoisted so tests outside the nested describe blocks can use it too. */
const PROD_ENV = env({ NODE_ENV: 'production' });

describe('readConfig', () => {
  it('reads all four values when the environment is complete', () => {
    const config = readConfig(FULL_ENV);
    expect(config.apiBaseUrl).toBe('http://api:8080');
    expect(config.keycloakBaseUrl).toBe('http://keycloak:8080');
    expect(config.keycloakRealm).toBe('netcommerce');
    expect(config.keycloakClientId).toBe('netcommerce-web');
  });

  it('builds the callback path the redirect URIs depend on', () => {
    // The realm's redirectUris contain http://localhost:3000/callback, so this
    // exact value is part of the auth contract.
    expect(readConfig(FULL_ENV).callbackPath).toBe('/callback');
  });

  it('strips a trailing slash so joined paths never double up', () => {
    const config = readConfig({ ...FULL_ENV, API_BASE_URL: 'http://api:8080/' });
    expect(config.apiBaseUrl).toBe('http://api:8080');
  });

  describe('in development', () => {
    const devEnv = env({ NODE_ENV: 'development' });

    it('falls back to localhost when nothing is configured', () => {
      const config = readConfig(devEnv);
      expect(config.apiBaseUrl).toContain('localhost');
      expect(config.keycloakBaseUrl).toContain('localhost');
    });

    it('does not throw', () => {
      expect(() => readConfig(devEnv)).not.toThrow();
    });
  });

  describe('outside development', () => {
    const prodEnv = PROD_ENV;

    it('throws when API_BASE_URL is missing', () => {
      expect(() => readConfig({ ...prodEnv, ...FULL_ENV, API_BASE_URL: undefined })).toThrow(
        ConfigError,
      );
    });

    it('names the missing variable so the fix is obvious', () => {
      expect(() =>
        readConfig({ ...prodEnv, ...FULL_ENV, KEYCLOAK_BASE_URL: undefined }),
      ).toThrow(/KEYCLOAK_BASE_URL/);
    });

    it('throws when KEYCLOAK_REALM is missing', () => {
      expect(() => readConfig({ ...prodEnv, ...FULL_ENV, KEYCLOAK_REALM: undefined })).toThrow(
        ConfigError,
      );
    });

    it('rejects an empty-string value rather than treating it as set', () => {
      expect(() => readConfig({ ...prodEnv, ...FULL_ENV, API_BASE_URL: '' })).toThrow(ConfigError);
    });

    it('rejects a non-absolute URL', () => {
      expect(() =>
        readConfig({ ...prodEnv, ...FULL_ENV, API_BASE_URL: 'api:8080' }),
      ).toThrow(ConfigError);
    });

    it('accepts a fully specified production environment', () => {
      // "Fully specified" now includes PUBLIC_ORIGIN: outside development the
      // Host header cannot be trusted to produce a stable redirect_uri.
      expect(() =>
        readConfig({ ...prodEnv, ...FULL_ENV, PUBLIC_ORIGIN: 'https://shop.example' }),
      ).not.toThrow();
    });
  });

  it('computes the origin used to build redirect URIs', () => {
    // A configured PUBLIC_ORIGIN must WIN over the Host header, otherwise
    // /login and /callback can disagree behind a proxy and the token exchange
    // is rejected for a redirect_uri mismatch.
    expect(publicOrigin('http://attacker.example/callback', 'https://shop.example')).toBe(
      'https://shop.example',
    );
  });

  it('falls back to the request origin when PUBLIC_ORIGIN is unset', () => {
    expect(publicOrigin('http://localhost:3000/callback', null)).toBe('http://localhost:3000');
    expect(publicOrigin('http://localhost:3000/callback', undefined)).toBe(
      'http://localhost:3000',
    );
  });

  it('exposes PUBLIC_ORIGIN in the config', () => {
    expect(readConfig(env({ ...FULL_ENV, PUBLIC_ORIGIN: 'https://shop.example' })).publicOrigin).toBe(
      'https://shop.example',
    );
  });

  it('reports PUBLIC_ORIGIN as null when unset in development', () => {
    expect(readConfig(env({ NODE_ENV: 'development' })).publicOrigin).toBeNull();
  });

  it('requires PUBLIC_ORIGIN outside development — the Host header is untrusted there', () => {
    expect(() => readConfig(env({ ...PROD_ENV, ...FULL_ENV }))).toThrow(/PUBLIC_ORIGIN/);
  });
});