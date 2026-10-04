/**
 * Instant navigation feedback.
 *
 * Server pages (catalog, product, basket) fetch before they paint; without a
 * loading boundary the previous page sits frozen until the new one is ready,
 * which reads as broken on slow networks. This skeleton paints immediately on
 * every navigation and is replaced the moment the real page resolves.
 */
export default function Loading() {
  return (
    <main id="main" className="stack rail" aria-busy="true" aria-label="Loading">
      <div className="stack" style={{ gap: '0.5rem' }} aria-hidden="true">
        <div className="skeleton skeleton--title" />
        <div className="skeleton skeleton--line" />
        <div className="skeleton skeleton--line skeleton--short" />
      </div>
    </main>
  );
}
