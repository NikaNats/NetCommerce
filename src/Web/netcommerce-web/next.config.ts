import type { NextConfig } from 'next';

const nextConfig: NextConfig = {
  output: 'standalone',

  // NOTE: no `experimental.instrumentationHook`.
  // That flag was required only on Next 13/14. instrumentation.ts is stable
  // since Next 15, and setting the flag on 15+ emits a warning (and is a no-op
  // on 16). The original design guide set it while also specifying Next 16 —
  // the two contradict each other.
  //
  // typedRoutes is on: every href/redirect is checked against the real app
  // routes at build time instead of failing at runtime in production.

  typedRoutes: true,

  images: {
    remotePatterns: [
      { protocol: 'http', hostname: 'localhost' },
      { protocol: 'https', hostname: '*.blob.core.windows.net' },
    ],
  },
};

export default nextConfig;
