import type { ButtonHTMLAttributes, ReactNode } from 'react';
import { Loader2 } from 'lucide-react';
import { cn } from '../../lib/cn';

type Variant = 'primary' | 'secondary' | 'ghost' | 'danger';
type Size = 'sm' | 'md' | 'lg';

const variants: Record<Variant, string> = {
  primary: 'bg-brand-600 text-white hover:bg-brand-700 disabled:bg-brand-300',
  secondary:
    'bg-surface-raised text-content border border-border-subtle hover:bg-surface-sunken disabled:opacity-50',
  ghost: 'text-content hover:bg-surface-sunken disabled:opacity-50',
  danger: 'bg-danger text-white hover:opacity-90 disabled:opacity-50',
};

const sizes: Record<Size, string> = {
  sm: 'h-8 px-3 text-sm',
  md: 'h-10 px-4 text-sm',
  lg: 'h-12 px-6 text-base',
};

interface ButtonProps extends ButtonHTMLAttributes<HTMLButtonElement> {
  variant?: Variant;
  size?: Size;
  isLoading?: boolean;
  leadingIcon?: ReactNode;
}

export const Button = ({
  variant = 'primary',
  size = 'md',
  isLoading = false,
  leadingIcon,
  className,
  children,
  disabled,
  ...props
}: ButtonProps) => (
  <button
    type="button"
    // Disabled while loading, so a double-click cannot submit twice.
    disabled={disabled === true || isLoading}
    // aria-busy tells a screen reader the control is working; the spinner alone is silent.
    aria-busy={isLoading}
    className={cn(
      'inline-flex items-center justify-center gap-2 rounded-lg font-medium transition-colors',
      'disabled:cursor-not-allowed',
      variants[variant],
      sizes[size],
      className,
    )}
    {...props}
  >
    {isLoading ? <Loader2 className="size-4 animate-spin" aria-hidden="true" /> : leadingIcon}
    {children}
  </button>
);
