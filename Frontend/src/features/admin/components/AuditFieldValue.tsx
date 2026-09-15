import { cn } from '../../../shared/lib/cn';

interface AuditFieldValueProps {
  /** The value on this side of the change. */
  value: unknown;
  /** The value on the other side, used to mark what a list gained or lost. */
  other: unknown;
  side: 'before' | 'after';
}

/**
 * One side of a field in the audit diff.
 *
 * A scalar renders as text. A list - the permissions or menus a role holds - renders one item
 * per line, with the items that are missing from the other side marked: struck through on the
 * "Before" side because they were removed and highlighted on the "After" side because they
 * were added. A comma-joined string of nineteen permission codes is not something anyone can
 * compare by eye.
 */
export const AuditFieldValue = ({ value, other, side }: AuditFieldValueProps) => {
  if (!Array.isArray(value)) {
    return <>{value === null || value === undefined ? '-' : String(value)}</>;
  }

  if (value.length === 0) {
    return <span className="italic">None</span>;
  }

  const otherItems = new Set(Array.isArray(other) ? other.map(String) : []);
  const label = side === 'before' ? 'Removed: ' : 'Added: ';

  return (
    <ul className="flex flex-col gap-0.5">
      {value.map((item) => {
        const text = String(item);
        const changed = !otherItems.has(text);

        return (
          <li
            key={text}
            className={cn(
              'font-mono',
              changed && side === 'before' && 'text-danger line-through',
              changed && side === 'after' && 'font-semibold text-success',
            )}
          >
            {changed && <span className="sr-only">{label}</span>}
            {text}
          </li>
        );
      })}
    </ul>
  );
};
