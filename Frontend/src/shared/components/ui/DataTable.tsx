import { Fragment, type ReactNode } from 'react';
import { ArrowDown, ArrowUp } from 'lucide-react';
import { cn } from '../../lib/cn';

export interface Column<T> {
  /** Stable key, also used as the sort field when `sortable` is set. */
  key: string;
  header: string;
  render: (row: T) => ReactNode;
  sortable?: boolean;
  /** Hidden in the mobile card list - used for columns that are noise on a phone. */
  hideOnCard?: boolean;
  align?: 'left' | 'right';
}

interface DataTableProps<T> {
  columns: Column<T>[];
  rows: T[];
  rowKey: (row: T) => string;
  /** Current sort, in the API's `-field` descending convention. */
  sort?: string;
  onSortChange?: (sort: string) => void;
  onRowClick?: (row: T) => void;
  /**
   * Detail for one row, rendered immediately beneath it - inside the table on desktop and
   * inside the row's card on mobile. Return `null` for every row that is not expanded.
   *
   * Which row is open stays with the caller. The table has no opinion about how many can be
   * open at once and the caller already owns that state to drive its own toggle button.
   */
  renderExpanded?: (row: T) => ReactNode;
  caption: string;
  isLoading?: boolean;
}

/**
 * Tabular data that becomes a card list below `md`.
 *
 * Spec §11.4 is explicit about this: "Data tables → card lists below md. A horizontally-
 * scrolling table on a phone is not responsive." So the same data is rendered twice, and
 * exactly one of the two is in the accessibility tree at a time - `hidden` rather than
 * CSS-only hiding, so a screen reader never encounters both.
 */
export const DataTable = <T,>({
  columns,
  rows,
  rowKey,
  sort,
  onSortChange,
  onRowClick,
  renderExpanded,
  caption,
  isLoading = false,
}: DataTableProps<T>) => {
  const sortField = sort?.replace(/^-/, '');
  const isDescending = sort?.startsWith('-') === true;

  const toggleSort = (key: string) => {
    if (onSortChange === undefined) {
      return;
    }

    // First click sorts ascending; clicking the active column flips direction.
    onSortChange(sortField === key && !isDescending ? `-${key}` : key);
  };

  const ariaSort = (key: string): 'ascending' | 'descending' | 'none' =>
    sortField === key ? (isDescending ? 'descending' : 'ascending') : 'none';

  return (
    <div aria-busy={isLoading}>
      {/* Desktop: a real table, with a caption and proper column headers. */}
      <div className="hidden overflow-x-auto rounded-card border border-border-subtle md:block">
        <table className="w-full text-sm">
          <caption className="sr-only">{caption}</caption>

          <thead className="bg-surface-sunken">
            <tr>
              {columns.map((column) => (
                <th
                  key={column.key}
                  scope="col"
                  aria-sort={column.sortable === true ? ariaSort(column.key) : undefined}
                  className={cn(
                    'px-4 py-2.5 font-medium text-content-muted',
                    column.align === 'right' ? 'text-right' : 'text-left',
                  )}
                >
                  {column.sortable === true && onSortChange !== undefined ? (
                    <button
                      type="button"
                      onClick={() => toggleSort(column.key)}
                      className="inline-flex items-center gap-1 hover:text-content"
                    >
                      {column.header}
                      {sortField === column.key &&
                        (isDescending ? (
                          <ArrowDown className="size-3.5" aria-hidden="true" />
                        ) : (
                          <ArrowUp className="size-3.5" aria-hidden="true" />
                        ))}
                    </button>
                  ) : (
                    column.header
                  )}
                </th>
              ))}
            </tr>
          </thead>

          <tbody className="divide-y divide-border-subtle">
            {rows.map((row) => {
              const detail = renderExpanded?.(row) ?? null;

              return (
                <Fragment key={rowKey(row)}>
                  <tr
                    onClick={onRowClick === undefined ? undefined : () => onRowClick(row)}
                    className={cn('bg-surface', onRowClick !== undefined && 'cursor-pointer hover:bg-surface-sunken')}
                  >
                    {columns.map((column) => (
                      <td
                        key={column.key}
                        className={cn('px-4 py-2.5 text-content', column.align === 'right' && 'text-right')}
                      >
                        {column.render(row)}
                      </td>
                    ))}
                  </tr>

                  {/*
                    A second row rather than a nested table, so the detail lines up with the
                    row it belongs to and spans every column. `colSpan` has to track the
                    column list or the table silently gains a column.
                  */}
                  {detail !== null && (
                    <tr className="bg-surface">
                      <td colSpan={columns.length} className="px-4 pb-3 pt-0">
                        {detail}
                      </td>
                    </tr>
                  )}
                </Fragment>
              );
            })}
          </tbody>
        </table>
      </div>

      {/* Mobile: the same rows as cards, with each value labelled by its column header. */}
      <ul className="flex flex-col gap-3 md:hidden">
        {rows.map((row) => {
          const detail = renderExpanded?.(row) ?? null;

          return (
            <li key={rowKey(row)}>
              <button
                type="button"
                onClick={onRowClick === undefined ? undefined : () => onRowClick(row)}
                disabled={onRowClick === undefined}
                className="w-full rounded-card border border-border-subtle bg-surface p-4 text-left disabled:cursor-default"
              >
                <dl className="flex flex-col gap-1.5">
                  {columns
                    .filter((column) => column.hideOnCard !== true)
                    .map((column) => (
                      <div key={column.key} className="flex items-baseline justify-between gap-3">
                        <dt className="text-xs text-content-muted">{column.header}</dt>
                        <dd className="text-sm text-content">{column.render(row)}</dd>
                      </div>
                    ))}
                </dl>
              </button>

              {detail !== null && <div className="mt-2">{detail}</div>}
            </li>
          );
        })}
      </ul>
    </div>
  );
};

/** Coloured status pill, shared by the order and audit screens. */
export const StatusBadge = ({ status }: { status: string }) => {
  const tone =
    {
      Pending: 'bg-warning/15 text-warning',
      Confirmed: 'bg-brand-100 text-brand-700',
      Processing: 'bg-brand-100 text-brand-700',
      Shipped: 'bg-brand-100 text-brand-700',
      Delivered: 'bg-success/15 text-success',
      Cancelled: 'bg-danger/15 text-danger',
      Refunded: 'bg-danger/15 text-danger',
    }[status] ?? 'bg-surface-sunken text-content-muted';

  return (
    <span className={cn('inline-flex rounded-full px-2 py-0.5 text-xs font-medium', tone)}>{status}</span>
  );
};
