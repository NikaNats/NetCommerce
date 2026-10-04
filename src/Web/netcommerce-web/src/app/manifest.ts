import type { MetadataRoute } from 'next';

/**
 * Web app manifest: installability baseline plus the icon reference that
 * stops `/favicon.ico` 404 noise. Theme colors follow the storefront's dark
 * masthead.
 */
export default function manifest(): MetadataRoute.Manifest {
  return {
    name: 'NetCommerce',
    short_name: 'NetCommerce',
    description: 'Catalog, basket and order tracking for NetCommerce.',
    start_url: '/',
    display: 'standalone',
    background_color: '#ffffff',
    theme_color: '#111111',
    icons: [
      {
        src: '/icon.svg',
        sizes: 'any',
        type: 'image/svg+xml',
      },
    ],
  };
}
