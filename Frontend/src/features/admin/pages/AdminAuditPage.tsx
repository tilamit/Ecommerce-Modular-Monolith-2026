import { useState } from 'react';
import { useQuery } from '@tanstack/react-query';
import { fetchAuditEntry, fetchAuditTrail, type AuditListItem } from '../api';
import { Button } from '../../../shared/components/ui/Button';
import { EmptyState, ErrorState, Skeleton } from '../../../shared/components/ui/States';
import { DataTable, type Column } from '../../../shared/components/ui/DataTable';
import { formatDateTime } from '../../../shared/lib/format';
import { useListParams } from '../../../shared/hooks/useListParams';

const ACTIONS = ['Insert', 'Update', 'Delete', 'Login', 'LoginFailed', 'Logout', 'Export', 'PermissionChange'];

const detailId = (id: number) => `audit-detail-${id}`;

const parseJson = (value?: string | null): Record<string, unknown> | null => {
  if (value === null || value === undefined) {
    return null;
  }

  try {
    return JSON.parse(value) as Record<string, unknown>;
  } catch {
    return null;
  }
};

/**
 * Renders the old/new diff for one entry (spec §9.4: "full old/new value diff").
 *
 * `id` is the row's audit id and also what the toggle button points `aria-controls` at, so
 * the button and the panel it opens are linked for a screen reader rather than only being
 * next to each other visually.
 */
const ValueDiff = ({ id }: { id: number }) => {
  const query = useQuery({
    queryKey: ['admin', 'audit', 'detail', id],
    queryFn: ({ signal }) => fetchAuditEntry(id, signal),
  });

  if (query.isPending) {
    return <Skeleton className="h-24 w-full" />;
  }

  if (query.isError) {
    return <ErrorState onRetry={() => void query.refetch()} />;
  }

  const entry = query.data;
  const oldValues = parseJson(entry.oldValues);
  const newValues = parseJson(entry.newValues);
  const changed = (parseJson(entry.changedColumns) as unknown as string[] | null) ?? [];

  const keys = [...new Set([...Object.keys(oldValues ?? {}), ...Object.keys(newValues ?? {})])].toSorted();

  return (
    <div id={detailId(id)} className="flex flex-col gap-3 rounded-lg bg-surface-sunken p-3 text-sm">
      {/*
        `max-content` for the labels rather than an even split. A half-and-half grid puts the
        value at the midpoint of a full-width table row, which leaves each label stranded a
        long way from what it describes.
      */}
      <dl className="grid grid-cols-[max-content_1fr] gap-x-4 gap-y-1 text-xs">
        <dt className="text-content-muted">Correlation</dt>
        <dd className="font-mono">{entry.correlationId ?? '-'}</dd>
        <dt className="text-content-muted">Request</dt>
        <dd className="font-mono">
          {entry.httpMethod ?? '-'} {entry.path ?? ''}
        </dd>
        <dt className="text-content-muted">Screen</dt>
        <dd className="font-mono">{entry.screenName ?? '-'}</dd>
        <dt className="text-content-muted">IP</dt>
        <dd className="font-mono">{entry.ipAddress ?? '-'}</dd>
      </dl>

      {keys.length === 0 ? (
        <p className="text-content-muted">No field-level changes were recorded for this entry.</p>
      ) : (
        <table className="w-full text-xs">
          <caption className="sr-only">Field changes</caption>
          <thead>
            <tr className="text-left text-content-muted">
              <th scope="col" className="py-1 pr-3 font-medium">
                Field
              </th>
              <th scope="col" className="py-1 pr-3 font-medium">
                Before
              </th>
              <th scope="col" className="py-1 font-medium">
                After
              </th>
            </tr>
          </thead>
          <tbody className="divide-y divide-border-subtle">
            {keys.map((key) => (
              <tr key={key}>
                <th scope="row" className="py-1 pr-3 text-left font-medium text-content">
                  {key}
                  {changed.includes(key) && <span className="ml-1 text-brand-600">•</span>}
                </th>
                <td className="py-1 pr-3 text-content-muted">{String(oldValues?.[key] ?? '-')}</td>
                <td className="py-1 text-content">{String(newValues?.[key] ?? '-')}</td>
              </tr>
            ))}
          </tbody>
        </table>
      )}
    </div>
  );
};

/**
 * The audit trail (spec §9.4).
 *
 * Keyset-paginated, so this screen has a "Load more" rather than page numbers: the cursor
 * is opaque and there is no total count to page against. That is deliberate - this table
 * grows fastest and `COUNT(*)` over it would get slower every day.
 *
 * Read-only. There is no edit or delete affordance anywhere on this page, because the
 * trail is append-only with no admin override.
 */
export const AdminAuditPage = () => {
  const { params, update, searchParams } = useListParams();
  const [cursors, setCursors] = useState<(string | null)[]>([null]);
  const [expanded, setExpanded] = useState<number | null>(null);

  const action = searchParams.get('action') ?? '';
  const module = searchParams.get('module') ?? '';

  const cursor = cursors[cursors.length - 1];

  const query = useQuery({
    queryKey: ['admin', 'audit', cursor, action, module, params.search],
    queryFn: ({ signal }) => {
      const search = new URLSearchParams({ pageSize: '25' });

      if (cursor !== null) search.set('cursor', cursor);
      if (action !== '') search.set('action', action);
      if (module !== '') search.set('module', module);
      if (params.search !== undefined) search.set('search', params.search);

      return fetchAuditTrail(search, signal);
    },
  });

  const resetPaging = () => setCursors([null]);

  const columns: Column<AuditListItem>[] = [
    {
      key: 'occurredUtc',
      header: 'When',
      render: (e) => <time dateTime={e.occurredUtc}>{formatDateTime(e.occurredUtc)}</time>,
    },
    { key: 'action', header: 'Action', render: (e) => e.action },
    { key: 'module', header: 'Module', render: (e) => e.module },
    { key: 'entityName', header: 'Entity', render: (e) => e.entityName },
    { key: 'userName', header: 'By', render: (e) => e.userName ?? '-' },
    {
      key: 'screenName',
      header: 'Screen',
      hideOnCard: true,
      render: (e) => <span className="font-mono text-xs text-content-muted">{e.screenName ?? '-'}</span>,
    },
    {
      key: 'actions',
      header: 'Detail',
      align: 'right',
      render: (e) => (
        <Button
          variant="ghost"
          size="sm"
          aria-expanded={expanded === e.id}
          aria-controls={expanded === e.id ? detailId(e.id) : undefined}
          onClick={() => setExpanded(expanded === e.id ? null : e.id)}
        >
          {expanded === e.id ? 'Hide' : 'View'}
        </Button>
      ),
    },
  ];

  return (
    <div className="flex flex-col gap-4">
      <div className="flex flex-wrap items-center justify-between gap-3">
        <h2 className="text-lg font-semibold text-content">Audit trail</h2>

        <div className="flex flex-wrap items-center gap-3">
          <label className="flex items-center gap-2 text-sm text-content-muted">
            Action
            <select
              value={action}
              onChange={(event) => {
                resetPaging();
                update({ action: event.target.value });
              }}
              className="h-9 rounded-lg border border-border-subtle bg-surface px-2 text-sm text-content"
            >
              <option value="">All</option>
              {ACTIONS.map((value) => (
                <option key={value} value={value}>
                  {value}
                </option>
              ))}
            </select>
          </label>

          <label className="flex items-center gap-2 text-sm text-content-muted">
            Module
            <select
              value={module}
              onChange={(event) => {
                resetPaging();
                update({ module: event.target.value });
              }}
              className="h-9 rounded-lg border border-border-subtle bg-surface px-2 text-sm text-content"
            >
              <option value="">All</option>
              {['identity', 'catalog', 'ordering', 'audit'].map((value) => (
                <option key={value} value={value}>
                  {value}
                </option>
              ))}
            </select>
          </label>
        </div>
      </div>

      {query.isPending ? (
        <div aria-busy="true" className="flex flex-col gap-2">
          {Array.from({ length: 8 }, (_, index) => (
            <Skeleton key={index} className="h-12" />
          ))}
        </div>
      ) : query.isError ? (
        <ErrorState onRetry={() => void query.refetch()} />
      ) : query.data.items.length === 0 ? (
        <EmptyState title="No audit entries match those filters" />
      ) : (
        <>
          <DataTable
            caption="Audit trail"
            columns={columns}
            rows={query.data.items}
            rowKey={(entry) => String(entry.id)}
            // Beneath the row it belongs to. Rendered after the table, the detail for a row
            // near the top of a twenty-five row page appeared below the fold, which is a
            // scroll away from the "View" that opened it and from the row it describes.
            renderExpanded={(entry) => (entry.id === expanded ? <ValueDiff id={entry.id} /> : null)}
          />

          <div className="flex items-center justify-between gap-3 border-t border-border-subtle pt-4">
            <Button
              variant="secondary"
              size="sm"
              disabled={cursors.length === 1}
              onClick={() => setCursors((current) => current.slice(0, -1))}
            >
              Previous
            </Button>

            <p className="text-sm text-content-muted">
              Showing {query.data.items.length} entries
              {cursors.length > 1 && ` · page ${cursors.length}`}
            </p>

            <Button
              variant="secondary"
              size="sm"
              disabled={query.data.nextCursor === null}
              onClick={() => setCursors((current) => [...current, query.data.nextCursor])}
            >
              Next
            </Button>
          </div>
        </>
      )}
    </div>
  );
};
