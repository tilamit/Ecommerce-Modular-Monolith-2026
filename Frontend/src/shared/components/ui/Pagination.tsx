import { ChevronLeft, ChevronRight } from 'lucide-react';
import { Button } from './Button';

interface PaginationProps {
  page: number;
  pageSize: number;
  totalCount: number;
  totalPages: number;
  hasNext: boolean;
  hasPrevious: boolean;
  onPageChange: (page: number) => void;
}

export const Pagination = ({
  page,
  pageSize,
  totalCount,
  totalPages,
  hasNext,
  hasPrevious,
  onPageChange,
}: PaginationProps) => {
  if (totalCount === 0) {
    return null;
  }

  const firstRow = (page - 1) * pageSize + 1;
  const lastRow = Math.min(page * pageSize, totalCount);

  return (
    <nav
      aria-label="Pagination"
      className="flex flex-col items-center justify-between gap-3 border-t border-border-subtle pt-4 sm:flex-row"
    >
      {/*
        aria-live so the range is announced after a page change - otherwise a screen-reader
        user gets a silently replaced table with no indication of where they are.
      */}
      <p aria-live="polite" className="text-sm text-content-muted">
        Showing <span className="font-medium text-content">{firstRow}</span>-
        <span className="font-medium text-content">{lastRow}</span> of{' '}
        <span className="font-medium text-content">{totalCount}</span>
      </p>

      <div className="flex items-center gap-2">
        <Button
          variant="secondary"
          size="sm"
          disabled={!hasPrevious}
          onClick={() => onPageChange(page - 1)}
          leadingIcon={<ChevronLeft className="size-4" aria-hidden="true" />}
        >
          Previous
        </Button>

        <span className="px-2 text-sm text-content-muted">
          Page {page} of {totalPages}
        </span>

        <Button variant="secondary" size="sm" disabled={!hasNext} onClick={() => onPageChange(page + 1)}>
          Next
          <ChevronRight className="size-4" aria-hidden="true" />
        </Button>
      </div>
    </nav>
  );
};
