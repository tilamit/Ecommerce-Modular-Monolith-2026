import { useNavigate, useParams, Link } from 'react-router';
import { useQuery, useQueryClient, useMutation, keepPreviousData } from '@tanstack/react-query';
import { ArrowLeft } from 'lucide-react';
import { fetchAllOrders, fetchOrder, changeOrderStatus, type OrderRow } from '../../orders';
import { ListPage } from '../components/ListPage';
import { useListParams } from '../../../shared/hooks/useListParams';
import { StatusBadge, type Column } from '../../../shared/components/ui/DataTable';
import { useToast } from '../../../shared/components/ui/Toast';
import { ErrorState, Skeleton } from '../../../shared/components/ui/States';
import { formatCurrency, formatDate, formatDateTime } from '../../../shared/lib/format';

const STATUSES = ['Pending', 'Confirmed', 'Processing', 'Shipped', 'Delivered', 'Cancelled', 'Refunded'];

export const AdminOrdersPage = () => {
  const navigate = useNavigate();
  const { params, update, toQuery, searchParams } = useListParams({ sort: '-placedUtc' });

  const status = searchParams.get('status') ?? '';
  const customerType = searchParams.get('customerType') ?? '';
  const dateFrom = searchParams.get('dateFrom') ?? '';
  const dateTo = searchParams.get('dateTo') ?? '';

  const query = useQuery({
    queryKey: ['admin', 'orders', params, status, customerType, dateFrom, dateTo],
    queryFn: ({ signal }) =>
      fetchAllOrders(
        toQuery({
          status: status || undefined,
          customerType: customerType || undefined,
          dateFrom: dateFrom || undefined,
          dateTo: dateTo || undefined,
        }),
        signal,
      ),
    placeholderData: keepPreviousData,
  });

  const columns: Column<OrderRow>[] = [
    {
      key: 'orderNumber',
      header: 'Order',
      sortable: true,
      render: (o) => <span className="font-mono text-xs">{o.orderNumber}</span>,
    },
    {
      key: 'customer',
      header: 'Customer',
      render: (o) => (
        <span>
          {o.customerName}
          {/* Requirement C5: registered and guest must be distinguishable at a glance. */}
          <span className="ml-2 rounded-full bg-surface-sunken px-1.5 py-0.5 text-[10px] text-content-muted">
            {o.customerType}
          </span>
        </span>
      ),
    },
    { key: 'customerEmail', header: 'Email', hideOnCard: true, render: (o) => o.customerEmail },
    { key: 'status', header: 'Status', render: (o) => <StatusBadge status={o.status} /> },
    { key: 'itemCount', header: 'Items', align: 'right', hideOnCard: true, render: (o) => o.itemCount },
    {
      key: 'grandTotal',
      header: 'Total',
      sortable: true,
      align: 'right',
      render: (o) => formatCurrency(o.grandTotal, o.currencyCode),
    },
    {
      key: 'placedUtc',
      header: 'Placed',
      sortable: true,
      render: (o) => <time dateTime={o.placedUtc}>{formatDate(o.placedUtc)}</time>,
    },
  ];

  return (
    <ListPage
      title="Orders"
      columns={columns}
      rowKey={(o) => o.id}
      query={query}
      search={params.search}
      onSearchChange={(search) => update({ search })}
      searchPlaceholder="Order number or guest email…"
      sort={params.sort}
      onSortChange={(sort) => update({ sort })}
      onPageChange={(page) => update({ page })}
      onRowClick={(order) => navigate(`/admin/orders/${order.id}`)}
      emptyTitle="No orders match those filters"
      filters={
        <>
          <label className="flex items-center gap-2 text-sm text-content-muted">
            Status
            <select
              value={status}
              onChange={(event) => update({ status: event.target.value })}
              className="h-9 rounded-lg border border-border-subtle bg-surface px-2 text-sm text-content"
            >
              <option value="">All</option>
              {STATUSES.map((value) => (
                <option key={value} value={value}>
                  {value}
                </option>
              ))}
            </select>
          </label>

          <label className="flex items-center gap-2 text-sm text-content-muted">
            Customer
            <select
              value={customerType}
              onChange={(event) => update({ customerType: event.target.value })}
              className="h-9 rounded-lg border border-border-subtle bg-surface px-2 text-sm text-content"
            >
              <option value="">All</option>
              <option value="Registered">Registered</option>
              <option value="Guest">Guest</option>
            </select>
          </label>

          <label className="flex items-center gap-2 text-sm text-content-muted">
            From
            <input
              type="date"
              value={dateFrom}
              onChange={(event) => update({ dateFrom: event.target.value })}
              className="h-9 rounded-lg border border-border-subtle bg-surface px-2 text-sm text-content"
            />
          </label>

          <label className="flex items-center gap-2 text-sm text-content-muted">
            To
            <input
              type="date"
              value={dateTo}
              onChange={(event) => update({ dateTo: event.target.value })}
              className="h-9 rounded-lg border border-border-subtle bg-surface px-2 text-sm text-content"
            />
          </label>
        </>
      }
    />
  );
};

export const AdminOrderDetailPage = () => {
  const { id = '' } = useParams();
  const queryClient = useQueryClient();
  const { show } = useToast();

  const query = useQuery({
    queryKey: ['admin', 'orders', 'detail', id],
    queryFn: ({ signal }) => fetchOrder(id, signal),
    enabled: id.length > 0,
    staleTime: 0,
  });

  const statusMutation = useMutation({
    mutationFn: (status: string) => changeOrderStatus(id, status),
    onSuccess: async () => {
      await queryClient.invalidateQueries({ queryKey: ['admin', 'orders'] });
      show({ tone: 'success', message: 'Order status updated.' });
    },
    onError: (error: Error) =>
      // The domain refuses illegal transitions (Delivered cannot go back to Pending), so
      // the server's message is the useful one here.
      show({ tone: 'error', message: error.message }),
  });

  if (query.isPending) {
    return <Skeleton className="h-96 w-full" />;
  }

  if (query.isError) {
    return <ErrorState title="Order not found" onRetry={() => void query.refetch()} />;
  }

  const order = query.data;

  return (
    <div className="flex flex-col gap-6">
      <Link to="/admin/orders" className="inline-flex items-center gap-1 text-sm text-content-muted hover:text-content">
        <ArrowLeft className="size-4" aria-hidden="true" />
        Back to orders
      </Link>

      <div className="flex flex-wrap items-center justify-between gap-3">
        <div>
          <h2 className="font-mono text-lg font-semibold text-content">{order.orderNumber}</h2>
          <p className="text-sm text-content-muted">
            {order.customerType} · {order.customerName} · {order.customerEmail}
          </p>
        </div>

        <div className="flex items-center gap-2">
          <StatusBadge status={order.status} />

          <label className="flex items-center gap-2 text-sm text-content-muted">
            Change to
            <select
              defaultValue=""
              disabled={statusMutation.isPending}
              onChange={(event) => {
                if (event.target.value !== '') {
                  statusMutation.mutate(event.target.value);
                }
              }}
              className="h-9 rounded-lg border border-border-subtle bg-surface px-2 text-sm text-content"
            >
              <option value="">Select…</option>
              {STATUSES.filter((s) => s !== order.status).map((s) => (
                <option key={s} value={s}>
                  {s}
                </option>
              ))}
            </select>
          </label>
        </div>
      </div>

      <div className="grid gap-4 lg:grid-cols-[1fr_20rem]">
        <section className="rounded-card border border-border-subtle p-4">
          <h3 className="text-sm font-semibold text-content">Items</h3>

          <ul className="mt-3 flex flex-col gap-2 text-sm">
            {order.items.map((item) => (
              <li key={item.productId} className="flex items-baseline justify-between gap-3">
                <span className="min-w-0 flex-1">
                  {/* Snapshotted at order time: renaming the product later must not
                      change what this order says was bought (spec §4.2). */}
                  <span className="text-content">{item.productName}</span>
                  <span className="ml-2 font-mono text-xs text-content-muted">{item.sku}</span>
                </span>
                <span className="text-content-muted">
                  {item.quantity} × {formatCurrency(item.unitPrice, order.currencyCode)}
                </span>
                <span className="w-24 text-right font-medium text-content">
                  {formatCurrency(item.lineTotal, order.currencyCode)}
                </span>
              </li>
            ))}
          </ul>

          <dl className="mt-4 flex flex-col gap-1 border-t border-border-subtle pt-3 text-sm">
            <div className="flex justify-between">
              <dt className="text-content-muted">Subtotal</dt>
              <dd>{formatCurrency(order.subTotal, order.currencyCode)}</dd>
            </div>
            {order.discountTotal > 0 && (
              <div className="flex justify-between">
                <dt className="text-content-muted">Discount</dt>
                <dd>−{formatCurrency(order.discountTotal, order.currencyCode)}</dd>
              </div>
            )}
            <div className="flex justify-between text-base font-semibold text-content">
              <dt>Total</dt>
              <dd>{formatCurrency(order.grandTotal, order.currencyCode)}</dd>
            </div>
          </dl>
        </section>

        <div className="flex flex-col gap-4">
          <section className="rounded-card border border-border-subtle p-4">
            <h3 className="text-sm font-semibold text-content">Shipping address</h3>
            <address className="mt-2 text-sm not-italic text-content-muted">
              {order.shippingAddress.fullName}
              <br />
              {order.shippingAddress.line1}
              <br />
              {order.shippingAddress.line2 !== null && order.shippingAddress.line2 !== undefined && (
                <>
                  {order.shippingAddress.line2}
                  <br />
                </>
              )}
              {order.shippingAddress.city}, {order.shippingAddress.postalCode}
              <br />
              {order.shippingAddress.country}
            </address>
            <p className="mt-2 text-xs text-content-muted">
              Payment: {order.paymentMethod} ({order.paymentStatus})
            </p>
          </section>

          <section className="rounded-card border border-border-subtle p-4">
            <h3 className="text-sm font-semibold text-content">History</h3>
            <ol className="mt-2 flex flex-col gap-2 text-sm">
              {order.statusHistory.map((entry, index) => (
                <li key={index} className="flex flex-col">
                  <span className="text-content">
                    {entry.fromStatus === null || entry.fromStatus === undefined
                      ? entry.toStatus
                      : `${entry.fromStatus} → ${entry.toStatus}`}
                  </span>
                  <time dateTime={entry.changedUtc} className="text-xs text-content-muted">
                    {formatDateTime(entry.changedUtc)}
                  </time>
                  {entry.reason !== null && entry.reason !== undefined && (
                    <span className="text-xs text-content-muted">{entry.reason}</span>
                  )}
                </li>
              ))}
            </ol>
          </section>
        </div>
      </div>
    </div>
  );
};
