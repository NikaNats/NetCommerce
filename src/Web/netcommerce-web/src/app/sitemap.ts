import type { MetadataRoute } from 'next';

import { siteOrigin } from '@/lib/config';

/**
 * Sitemap: the stable public routes.
 *
 * Product URLs are intentionally NOT enumerated here. They page through a
 * live catalog (and change on every publish), so a build-time snapshot would
 * be stale on arrival and a request-time crawl of every page would turn each
 * indexer visit into a full catalog scan. The catalog index — which crawlers
 * follow to every product — is listed instead. Revisit when the catalog grows
 * past what the index exposes.
 */
export default function sitemap(): MetadataRoute.Sitemap {
  const origin = siteOrigin();
  const now = new Date();

  return [
    {
      url: `${origin}/`,
      lastModified: now,
      changeFrequency: 'daily',
      priority: 1,
    },
    {
      url: `${origin}/catalog`,
      lastModified: now,
      changeFrequency: 'hourly',
      priority: 0.9,
    },
  ];
}
