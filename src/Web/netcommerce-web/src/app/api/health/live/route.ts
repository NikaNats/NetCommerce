/**
 * Liveness probe: is this process serving traffic?
 *
 * Intentionally dependency-free — no config, no Redis, no API. A liveness
 * failure restarts the container, so it must only fail when the process
 * itself is broken. Dependency health belongs to the readiness probe
 * (../ready/route.ts), mirroring the backend's /health/live vs /health/ready
 * split in ServiceDefaults/Extensions.cs.
 */

export const dynamic = 'force-dynamic';
export const revalidate = 0;

export async function GET(): Promise<Response> {
  return Response.json({ status: 'live' });
}
