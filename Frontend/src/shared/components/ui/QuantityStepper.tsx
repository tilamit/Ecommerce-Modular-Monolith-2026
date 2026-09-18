import { Minus, Plus } from 'lucide-react';
import { Button } from './Button';
import { cn } from '../../lib/cn';

interface QuantityStepperProps {
  value: number;
  onChange: (quantity: number) => void;
  /** Usually the available stock. A value above it can never be emitted. */
  max: number;
  min?: number;
  /** Accessible name for the group, for example `Quantity for Blue Mug`. */
  label: string;
  /**
   * Accessible names for the two buttons. In a list of products "Decrease quantity" is
   * ambiguous to a screen-reader user moving between rows, so the caller passes the item
   * name; on a single product page the default is enough.
   */
  decreaseLabel?: string;
  increaseLabel?: string;
  size?: 'sm' | 'md';
  disabled?: boolean;
  className?: string;
}

/**
 * Increment and decrement control for a quantity (spec §8.4).
 *
 * The number is displayed rather than typed. A free-text box invites `0`, `-2` and `1e3`,
 * each of which then has to be validated, explained and clamped somewhere; two buttons that
 * cannot produce an invalid value need none of that. Stock is the real limit, so the upper
 * bound is enforced here and not left to the caller.
 */
export const QuantityStepper = ({
  value,
  onChange,
  max,
  min = 1,
  label,
  decreaseLabel = 'Decrease quantity',
  increaseLabel = 'Increase quantity',
  size = 'sm',
  disabled = false,
  className,
}: QuantityStepperProps) => {
  // `max` below `min` means there is nothing to pick from - out of stock, or a single unit
  // left. Ordering the two this way keeps the range valid rather than inverted.
  const upper = Math.max(max, min);
  const clamp = (next: number): number => Math.min(Math.max(next, min), upper);

  const current = clamp(value);

  return (
    <div className={cn('flex items-center gap-1', className)} role="group" aria-label={label}>
      <Button
        variant="secondary"
        size={size}
        disabled={disabled || current <= min}
        aria-label={decreaseLabel}
        onClick={() => onChange(clamp(current - 1))}
      >
        <Minus className="size-4" aria-hidden="true" />
      </Button>

      {/* Polite, so a screen reader announces the new number without interrupting. */}
      <span aria-live="polite" className="w-10 text-center text-sm font-medium text-content">
        {current}
      </span>

      <Button
        variant="secondary"
        size={size}
        disabled={disabled || current >= upper}
        aria-label={increaseLabel}
        onClick={() => onChange(clamp(current + 1))}
      >
        <Plus className="size-4" aria-hidden="true" />
      </Button>
    </div>
  );
};
