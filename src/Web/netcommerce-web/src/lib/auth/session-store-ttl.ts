/**
 * Session lifetime, in its own module.
 *
 * Separate because both the session store (to set the Redis TTL) and the session
 * lifecycle (to reject an expired session) need this number, and importing it
 * from either of those would create a cycle.
 *
 * ## Why an absolute ceiling at all
 *
 * Token refresh can keep a session *usable* indefinitely. Without a ceiling, a
 * stolen cookie would be renewable forever, so the session's lifetime would be
 * bounded only by how long the refresh chain kept renewing. Eight hours is long
 * enough for a working day and short enough that a leak has a limited blast
 * radius.
 *
 * Note the real bound is still upstream: Keycloak's `refresh_expires_in`
 * (~30 minutes by default) is shorter, so an 8h session that stops being
 * refreshed stops working. The ceiling is a cap, not a promise.
 */
export const SESSION_ABSOLUTE_TTL_MS = 8 * 60 * 60 * 1000;