import { describeStatus } from '@/lib/ui/status';
import { StatusMark } from '@/components/icons';

/**
 * Status pill: mark + prose label. The label is the accessible name; the SVG is
 * decorative. Rendered as a real <p> rather than a bare <span> so it is announced
 * without needing role=status (which would imply live-region semantics we do
 * not want for a settled state).
 */
export function StatusPill({ status }: { status: string }) {
  const { tone, label, glyph } = describeStatus(status);

  return (
    <p className="status" data-tone={tone} data-glyph={glyph}>
      <StatusMark tone={tone} />
      <span>{label}</span>
    </p>
  );
}