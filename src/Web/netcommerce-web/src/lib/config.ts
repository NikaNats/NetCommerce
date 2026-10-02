/**
 * Runtime configuration, read from the environment Aspire injects.
 *
 * The AppHost sets these in src/NetCommerce.AppHost/Program.cs:
 *   API_BASE_URL        -> api.GetEndpoint("http")     (service discovery)
 *   KEYCLOAK_BASE_URL   -> keycloak.GetEndpoint("http")
 *   KEYCLOAK_REALM      -> "netcommerce"
 *   KEYCLOAK_CLIENT_ID  -> "netcommerce-web"
 *
 * ## Why this validates outside development
 *
 * Falling back to localhost everywhere means a production deploy with a missing
 * or misspelled variable boots cleanly, passes every health check, and then
 * fails on every request with a connection error pointing nowhere near the real
 * cause. Outside development a missing variable is a startup error that names
 * the variable. Development keeps the localhost defaults, because that is the
 * entire point of them.
 */

export interface AppConfig {
  apiBaseUrl: string;
  keycloakBaseUrl: string;
  keycloakRealm: string;
  keycloakClientId: string;
  callbackPath: string;
  /**
   * The operator-declared public origin, when set.
   *
   * The redirect_uri MUST be byte-identical between the /authorize request and
   * the token exchange, or Keycloak rejects it. Deriving that URI from the Host
   * header works in local dev and breaks behind any proxy or ingress: /login and
   * /callback can see different origins, and the failure surfaces only as a
   * token-exchange error. Pinning PUBLIC_ORIGIN removes the ambiguity.
   */
  publicOrigin: string | null;
}

/** Part of the auth contract: the realm's redirectUris allow /callback on :3000. */
export const CALLBACK_PATH = '/callback';

const DEV_API_FALLBACK = 'http://localhost:5000';
const DEV_KEYCLOAK_FALLBACK = 'http://localhost:8080';

export class ConfigError extends Error {
  constructor(
    readonly variable: string,
    detail: string,
  ) {
    super(`Invalid configuration: ${variable} ${detail}`);
    this.name = 'ConfigError';
  }
}

function isAbsoluteHttpUrl(value: string): boolean {
  try {
    const url = new URL(value);
    return url.protocol === 'http:' || url.protocol === 'https:';
  } catch {
    return false;
  }
}

function required(
  env: NodeJS.ProcessEnv,
  name: string,
  fallback: string,
  isDev: boolean,
  { requireUrl = false } = {},
): string {
  const raw = env[name];

  if (raw === undefined || raw.trim() === '') {
    if (isDev) return fallback;
    throw new ConfigError(name, 'is required but was not set');
  }

  const value = raw.trim();

  if (requireUrl && !isAbsoluteHttpUrl(value)) {
    throw new ConfigError(name, `must be an absolute http(s) URL, got "${value}"`);
  }

  return value.replace(/\/$/, '');
}

export function readConfig(env: NodeJS.ProcessEnv = process.env): AppConfig {
  const isDev = env.NODE_ENV !== 'production';

  return {
    apiBaseUrl: required(env, 'API_BASE_URL', DEV_API_FALLBACK, isDev, { requireUrl: true }),
    keycloakBaseUrl: required(env, 'KEYCLOAK_BASE_URL', DEV_KEYCLOAK_FALLBACK, isDev, {
      requireUrl: true,
    }),
    keycloakRealm: required(env, 'KEYCLOAK_REALM', 'netcommerce', isDev),
    keycloakClientId: required(env, 'KEYCLOAK_CLIENT_ID', 'netcommerce-web', isDev),
    callbackPath: CALLBACK_PATH,
    // Optional everywhere: development runs fine off the Host header. Outside
    // development it is required, because that is exactly where a proxy makes
    // the Host header untrustworthy.
    publicOrigin: required(env, 'PUBLIC_ORIGIN', '', isDev, { requireUrl: true }) || null,
  };
}

/**
 * The origin the browser used, used to build redirect URIs.
 *
 * Prefers the operator-declared PUBLIC_ORIGIN over the Host header. The Host
 * header is attacker-influenced and proxy-dependent, and a redirect_uri derived
 * from it can differ between the /authorize request and the token exchange —
 * which Keycloak rejects with an opaque error. See AppConfig.publicOrigin.
 */
export function publicOrigin(requestUrl: string, configured?: string | null): string {
  return configured || new URL(requestUrl).origin;
}
