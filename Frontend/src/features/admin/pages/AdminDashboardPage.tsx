import { Link } from 'react-router';
import { useQuery } from '@tanstack/react-query';
import { fetchAdminDashboard } from '../api';
import { DailySeriesChart, StatTile, money } from '../../../shared/components/ui/Charts';
import { ErrorState, Skeleton } from '../../../shared/components/ui/States';
import { formatCurrency, formatRelative } from '../../../shared/lib/format';

/** Admin dashboard (spec §10.1). One endpoint, one round trip. */
export const AdminDashboardPage = () => {
  const query = useQuery({
    queryKey: ['dashboard', 'admin'],
    queryFn: ({ signal }) => fetchAdminDashboard(signal),
    // The server already caches this payload for five minutes (spec §10.1); matching it
    // here avoids a refetch that could only ever return the same bytes.
    staleTime: 5 * 60_000,
  });

  if (query.isPending) {
    return (
      <div aria-busy="true" className="flex flex-col gap-4">
        <div className="grid gap-4 sm:grid-cols-2 xl:grid-cols-4">
          {Array.from({ length: 4 }, (_, index) => (
            <Skeleton key={index} className="h-24" />
          ))}
        </div>
        <Skeleton className="h-56" />
      </div>
    );
  }

  if (query.isError) {
    return <ErrorState onRetry={() => void query.refetch()} />;
  }

  const { counts, charts, recentAudit } = query.data;
  const asMoney = money(counts.currencyCode);

  return (
    <div className="flex flex-col gap-6">
      <section aria-label="Key figures" className="grid gap-4 sm:grid-cols-2 xl:grid-cols-4">
        <StatTile label="Active users" value={counts.activeUsers} hint={`${counts.inactiveUsers} inactive`} />
        <StatTile
          label="Active products"
          value={counts.activeProducts}
          hint={`${counts.inactiveProducts} inactive · ${counts.activeCategories} categories`}
        />
        <StatTile label="Orders (30 days)" value={counts.ordersLast30Days} />
        <StatTile label="Revenue (30 days)" value={asMoney(counts.revenueLast30Days)} />
      </section>

      <section aria-label="Trends" className="grid gap-4 lg:grid-cols-3">
        <DailySeriesChart title="Orders per day" data={charts.ordersPerDay} />
        <DailySeriesChart title="Revenue per day" data={charts.revenuePerDay} format={asMoney} />
        <DailySeriesChart title="Registrations per day" data={charts.usersRegisteredPerDay} />
      </section>

      <div className="grid gap-4 lg:grid-cols-2">
        <section className="rounded-card border border-border-subtle p-4">
          <h2 className="text-sm font-semibold text-content">Top products by revenue</h2>

          {charts.topProductsByRevenue.length === 0 ? (
            <p className="mt-4 text-sm text-content-muted">No sales in this period.</p>
          ) : (
            <ol className="mt-3 flex flex-col gap-2">
              {charts.topProductsByRevenue.map((product, index) => (
                <li key={product.productId} className="flex items-baseline justify-between gap-3 text-sm">
                  <span className="min-w-0 flex-1 truncate text-content">
                    <span className="mr-2 text-content-muted">{index + 1}.</span>
                    {product.productName}
                  </span>
                  <span className="shrink-0 text-content-muted">{product.unitsSold} sold</span>
                  <span className="w-24 shrink-0 text-right font-medium text-content">
                    {formatCurrency(product.revenue, counts.currencyCode)}
                  </span>
                </li>
              ))}
            </ol>
          )}
        </section>

        <section className="rounded-card border border-border-subtle p-4">
          <div className="flex items-center justify-between">
            <h2 className="text-sm font-semibold text-content">Recent activity</h2>
            {/* The tile shows page one; the audit screen pages independently (spec §10.1). */}
            <Link to="/admin/audit-trails" className="text-sm text-brand-600 hover:underline">
              View all
            </Link>
          </div>

          {recentAudit.length === 0 ? (
            <p className="mt-4 text-sm text-content-muted">Nothing recorded yet.</p>
          ) : (
            <ul className="mt-3 flex flex-col gap-2">
              {recentAudit.map((entry) => (
                <li key={entry.id} className="flex items-baseline justify-between gap-3 text-sm">
                  <span className="min-w-0 flex-1 truncate text-content">
                    <span className="font-medium">{entry.action}</span> {entry.entityName}
                    {entry.userName !== null && entry.userName !== undefined && (
                      <span className="text-content-muted"> by {entry.userName}</span>
                    )}
                  </span>
                  <time dateTime={entry.occurredUtc} className="shrink-0 text-xs text-content-muted">
                    {formatRelative(entry.occurredUtc)}
                  </time>
                </li>
              ))}
            </ul>
          )}
        </section>
      </div>
    </div>
  );
};
