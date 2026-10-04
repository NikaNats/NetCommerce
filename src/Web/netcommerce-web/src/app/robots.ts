import type { MetadataRoute } from 'next';

import { siteOrigin } from '@/lib/config';

/**
 * Crawler policy.
 *
 * The catalog and product pages are the public shop window; everything behind
 * a session (basket, checkout, orders) or an API boundary is disallowed so
 * crawlers never index a signed-in view or hammer authenticated routes.
 */
export default function robots(): MetadataRoute.Robots {
  return {
    rules: [
      {
        userAgent: '*',
        allow: ['/', '/catalog', '/products/'],
        disallow: ['/api/', '/basket', '/checkout', '/orders/', '/login', '/callback'],
      },
    ],
    sitemap: `${siteOrigin()}/sitemap.xml`,
  };
}
