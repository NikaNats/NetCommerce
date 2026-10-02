import type { StatusTone } from '@/lib/ui/status';

/**
 * Inline SVG glyphs. No icon font, no emoji — emoji as UI icon is a rendering
 * lottery across platforms and reads as unpolished.
 *
 * `StatusMark` is the accessibility-critical one: the mark is decorative and the
 * adjacent label carries the meaning, so the SVG is aria-hidden. If a mark were
 * the only signal, colour-blind and greyscale users would lose the state.
 */

export function StatusMark({ tone }: { tone: StatusTone }) {
  return (
    <svg
      className="status__mark"
      viewBox="0 0 10 10"
      aria-hidden="true"
      focusable="false"
      data-tone={tone}
    >
      {tone === 'alert' ? (
        <path d="M5 0 10 10 H0 Z" fill="currentColor" />
      ) : tone === 'live' ? (
        <circle cx="5" cy="5" r="3.5" fill="none" stroke="currentColor" strokeWidth="1.5" />
      ) : (
        <circle cx="5" cy="5" r="4" fill="currentColor" />
      )}
    </svg>
  );
}

export function ArrowIcon() {
  return (
    <svg width="14" height="14" viewBox="0 0 14 14" aria-hidden="true" focusable="false">
      <path
        d="M1 7h11M8 3l4 4-4 4"
        fill="none"
        stroke="currentColor"
        strokeWidth="1.5"
        strokeLinecap="square"
      />
    </svg>
  );
}

export function LockIcon() {
  return (
    <svg width="13" height="13" viewBox="0 0 14 14" aria-hidden="true" focusable="false">
      <rect x="2" y="6" width="10" height="7" fill="none" stroke="currentColor" strokeWidth="1.4" />
      <path d="M4.5 6V4a2.5 2.5 0 0 1 5 0v2" fill="none" stroke="currentColor" strokeWidth="1.4" />
    </svg>
  );
}