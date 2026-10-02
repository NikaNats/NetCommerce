import { describe, expect, it } from 'vitest';

import {
  isPublished,
  primaryImage,
  productHref,
  type ProductImage,
  type ProductListItem,
} from '@/lib/catalog/products';

const image = (
  overrides: Partial<ProductImage> & { id: string },
): ProductImage => ({
  url: `https://cdn.test/${overrides.id}.jpg`,
  displayOrder: 0,
  isPrimary: false,
  ...overrides,
});

describe('productHref', () => {
  it('prefers the slug route when a slug is present', () => {
    expect(productHref({ slug: 'walnut-desk', id: 'abc' })).toBe('/products/walnut-desk');
  });

  it('falls back to the id route when slug is null', () => {
    expect(productHref({ slug: null, id: 'abc' })).toBe('/products/id/abc');
  });

  it('URL-encodes a slug so a slash cannot forge a path segment', () => {
    expect(productHref({ slug: 'a/b', id: 'abc' })).toBe('/products/a%2Fb');
  });

  it('returns null only when neither identifier exists', () => {
    expect(productHref({ slug: null, id: '' })).toBeNull();
  });

  it('treats an empty-string slug as absent rather than linking to /products/', () => {
    expect(productHref({ slug: '', id: 'abc' })).toBe('/products/id/abc');
  });
});

describe('primaryImage', () => {
  it('returns null for an empty collection', () => {
    expect(primaryImage([])).toBeNull();
  });

  it('prefers the image explicitly flagged primary, whatever its order', () => {
    const images = [
      image({ id: 'a', displayOrder: 0 }),
      image({ id: 'b', displayOrder: 5, isPrimary: true }),
    ];
    expect(primaryImage(images)?.id).toBe('b');
  });

  it('falls back to the lowest displayOrder when none is flagged', () => {
    const images = [
      image({ id: 'a', displayOrder: 7 }),
      image({ id: 'b', displayOrder: 2 }),
      image({ id: 'c', displayOrder: 9 }),
    ];
    expect(primaryImage(images)?.id).toBe('b');
  });

  it('does not mutate the input', () => {
    const images = [image({ id: 'a', displayOrder: 3 })];
    const snapshot = JSON.stringify(images);
    primaryImage(images);
    expect(JSON.stringify(images)).toBe(snapshot);
  });
});

describe('isPublished', () => {
  it('accepts the server spelling', () => {
    expect(isPublished('Published')).toBe(true);
  });

  it('accepts a lowercase variant defensively', () => {
    expect(isPublished('published')).toBe(true);
  });

  it.each(['Draft', 'Archived', '', 'unpublished'])(
    'rejects %j so a non-published row is never shown as purchasable',
    (status) => {
      expect(isPublished(status)).toBe(false);
    },
  );
});

// A card must never link nowhere: productHref returning null has to be visible
// as a deliberate no-link rather than an <a> with an empty href.
describe('list projection field asymmetry', () => {
  const listItem: ProductListItem = {
    id: 'p1',
    name: 'Walnut Desk',
    sku: 'WD-1',
    price: 1299,
    currency: 'USD',
    primaryImageUrl: null,
    status: 'Published',
    slug: 'walnut-desk',
  };

  it('carries primaryImageUrl and slug, which the detail DTO does not', () => {
    expect(listItem.primaryImageUrl).toBeNull();
    expect(productHref(listItem)).toBe('/products/walnut-desk');
  });

  it('yields no href when a server row has neither slug nor id', () => {
    expect(productHref({ slug: null, id: '' })).toBeNull();
  });
});