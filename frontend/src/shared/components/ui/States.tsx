import type { ReactNode } from 'react';
import { AlertCircle, PackageOpen } from 'lucide-react';
import { Button } from './Button';
import { cn } from '../../lib/cn';

/**
 * Skeleton placeholder.
 *
 * Sized to match the final layout so nothing shifts when content arrives (spec §11.4).
 * A spinner in the middle of an empty page tells the user nothing about what is coming.
 */
export const Skeleton = ({ className }: { className?: string }) => (
  <div
    className={cn('animate-pulse rounded-md bg-surface-sunken', className)}
    // Decorative: the surrounding region announces loading via aria-busy.
    aria-hidden="true"
  />
);

export const ProductCardSkeleton = () => (
  <div className="flex flex-col gap-3 rounded-card border border-border-subtle p-3">
    <Skeleton className="aspect-square w-full" />
    <Skeleton className="h-4 w-3/4" />
    <Skeleton className="h-4 w-1/2" />
    <Skeleton className="h-8 w-full" />
  </div>
);

interface EmptyStateProps {
  title: string;
  description?: string;
  icon?: ReactNode;
  action?: ReactNode;
}

/** The "nothing here" state - one of the three every list must have (spec §11.4). */
export const EmptyState = ({ title, description, icon, action }: EmptyStateProps) => (
  <div className="flex flex-col items-center justify-center gap-3 rounded-card border border-dashed border-border-subtle px-6 py-16 text-center">
    <div className="text-content-muted" aria-hidden="true">
      {icon ?? <PackageOpen className="size-10" />}
    </div>
    <h2 className="text-base font-semibold text-content">{title}</h2>
    {description !== undefined && <p className="max-w-sm text-sm text-content-muted">{description}</p>}
    {action}
  </div>
);

interface ErrorStateProps {
  title?: string;
  description?: string;
  onRetry?: () => void;
}

/**
 * The error state, with a retry. A dead end with no way forward is the difference between
 * a demo and something that feels finished (spec §11.4).
 */
export const ErrorState = ({
  title = 'Something went wrong',
  description = 'We could not load this. Please try again.',
  onRetry,
}: ErrorStateProps) => (
  <div
    role="alert"
    className="flex flex-col items-center justify-center gap-3 rounded-card border border-danger/40 bg-danger/5 px-6 py-12 text-center"
  >
    <AlertCircle className="size-8 text-danger" aria-hidden="true" />
    <h2 className="text-base font-semibold text-content">{title}</h2>
    <p className="max-w-sm text-sm text-content-muted">{description}</p>
    {onRetry !== undefined && (
      <Button variant="secondary" size="sm" onClick={onRetry}>
        Try again
      </Button>
    )}
  </div>
);
