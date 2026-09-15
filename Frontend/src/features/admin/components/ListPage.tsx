import { useEffect, useState, type ReactNode } from 'react';
import { Search } from 'lucide-react';
import type { PagedResult } from '../../../shared/api/types';
import { DataTable, type Column } from '../../../shared/components/ui/DataTable';
import { Pagination } from '../../../shared/components/ui/Pagination';
import { EmptyState, ErrorState, Skeleton } from '../../../shared/components/ui/States';
import { useDebounce } from '../../../shared/hooks/useDebounce';

interface ListPageProps<T> {
  title: string;
  columns: Column<T>[];
  rowKey: (row: T) => string;
  query: {
    data?: PagedResult<T>;
    isPending: boolean;
    isError: boolean;
    refetch: () => unknown;
  };
  search?: string;
  onSearchChange?: (value: string) => void;
  searchPlaceholder?: string;
  sort?: string;
  onSortChange?: (sort: string) => void;
  onPageChange: (page: number) => void;
  onRowClick?: (row: T) => void;
  filters?: ReactNode;
  /** Screen-level controls, such as "New product". Sits beside the search box. */
  actions?: ReactNode;
  emptyTitle?: string;
}

/**
 * The shape every admin list screen shares: search, filters, a responsive table and
 * pagination, with explicit loading, empty and error states (spec §11.4).
 *
 * Factored out because six screens differing only in their columns is six chances for one
 * of them to quietly lose its error state.
 */
export const ListPage = <T,>({
  title,
  columns,
  rowKey,
  query,
  search,
  onSearchChange,
  searchPlaceholder = 'Search…',
  sort,
  onSortChange,
  onPageChange,
  onRowClick,
  filters,
  actions,
  emptyTitle = 'Nothing to show',
}: ListPageProps<T>) => {
  // Local state so typing stays responsive; the URL is updated on the debounced value.
  const [searchDraft, setSearchDraft] = useState(search ?? '');
  const debouncedSearch = useDebounce(searchDraft, 300);

  useEffect(() => {
    if (onSearchChange !== undefined && debouncedSearch !== (search ?? '')) {
      onSearchChange(debouncedSearch);
    }
  }, [debouncedSearch, onSearchChange, search]);

  return (
    <div className="flex flex-col gap-4">
      <div className="flex flex-wrap items-center justify-between gap-3">
        <h2 className="text-lg font-semibold text-content">{title}</h2>

        <div className="flex flex-1 flex-wrap items-center justify-end gap-3">
          {onSearchChange !== undefined && (
          <div className="relative w-full sm:w-72">
            <Search
              className="pointer-events-none absolute left-3 top-1/2 size-4 -translate-y-1/2 text-content-muted"
              aria-hidden="true"
            />
            <input
              type="search"
              value={searchDraft}
              onChange={(event) => setSearchDraft(event.target.value)}
              placeholder={searchPlaceholder}
              aria-label={`Search ${title.toLowerCase()}`}
              className="h-9 w-full rounded-lg border border-border-subtle bg-surface pl-9 pr-3 text-sm text-content placeholder:text-content-muted"
            />
          </div>
          )}

          {actions}
        </div>
      </div>

      {filters !== undefined && <div className="flex flex-wrap items-end gap-3">{filters}</div>}

      {query.isPending ? (
        <div aria-busy="true" className="flex flex-col gap-2">
          {Array.from({ length: 6 }, (_, index) => (
            <Skeleton key={index} className="h-12" />
          ))}
        </div>
      ) : query.isError ? (
        <ErrorState onRetry={() => void query.refetch()} />
      ) : query.data === undefined || query.data.items.length === 0 ? (
        <EmptyState title={emptyTitle} description="Try widening your search or clearing a filter." />
      ) : (
        <>
          <DataTable
            caption={title}
            columns={columns}
            rows={query.data.items}
            rowKey={rowKey}
            sort={sort}
            onSortChange={onSortChange}
            onRowClick={onRowClick}
          />

          <Pagination
            page={query.data.page}
            pageSize={query.data.pageSize}
            totalCount={query.data.totalCount}
            totalPages={query.data.totalPages}
            hasNext={query.data.hasNext}
            hasPrevious={query.data.hasPrevious}
            onPageChange={onPageChange}
          />
        </>
      )}
    </div>
  );
};
