import { useState } from 'react';
import { Link, useNavigate, useParams } from 'react-router';
import { useMutation, useQuery, useQueryClient, keepPreviousData } from '@tanstack/react-query';
import { useForm } from 'react-hook-form';
import { zodResolver } from '@hookform/resolvers/zod';
import { z } from 'zod';
import { ArrowLeft, PackageOpen } from 'lucide-react';
import {
  cancelMyOrder,
  fetchCustomerDashboard,
  fetchMyOrder,
  fetchMyOrders,
  type OrderRow,
} from '../../orders';
import { api } from '../../../shared/api/httpClient';
import { useAuthStore } from '../../../shared/api/authStore';
import { DailySeriesChart, StatTile, money } from '../../../shared/components/ui/Charts';
import { DataTable, StatusBadge, type Column } from '../../../shared/components/ui/DataTable';
import { Pagination } from '../../../shared/components/ui/Pagination';
import { Button } from '../../../shared/components/ui/Button';
import { Input } from '../../../shared/components/ui/Field';
import { EmptyState, ErrorState, Skeleton } from '../../../shared/components/ui/States';
import { useToast } from '../../../shared/components/ui/Toast';
import { useListParams } from '../../../shared/hooks/useListParams';
import { formatCurrency, formatDate, formatDateTime } from '../../../shared/lib/format';
import type { UserProfile } from '../../../shared/api/types';

/** Customer dashboard (spec §10.2). Scoped server-side; no user id is ever sent. */
export const AccountDashboardPage = () => {
  const query = useQuery({
    queryKey: ['dashboard', 'customer'],
    queryFn: ({ signal }) => fetchCustomerDashboard(signal),
    staleTime: 0,
  });

  if (query.isPending) {
    return <Skeleton className="h-96 w-full" />;
  }

  if (query.isError) {
    return <ErrorState onRetry={() => void query.refetch()} />;
  }

  const { counts, charts, recentOrders } = query.data;
  const asMoney = money(counts.currencyCode);

  return (
    <div className="flex flex-col gap-6">
      <section aria-label="Your figures" className="grid gap-4 sm:grid-cols-2 xl:grid-cols-4">
        <StatTile label="Total orders" value={counts.totalOrders} />
        <StatTile label="Total spent" value={asMoney(counts.totalSpent)} />
        <StatTile
          label="Last 30 days"
          value={counts.ordersLast30Days}
          hint={`${asMoney(counts.spentLast30Days)} spent`}
        />
        <StatTile label="Awaiting confirmation" value={counts.pendingOrders} />
      </section>

      <section aria-label="Your activity" className="grid gap-4 lg:grid-cols-2">
        <DailySeriesChart title="Orders per day" data={charts.ordersPerDay} />
        <DailySeriesChart title="Spend per day" data={charts.spendPerDay} format={asMoney} />
      </section>

      <section className="rounded-card border border-border-subtle p-4">
        <div className="flex items-center justify-between">
          <h2 className="text-sm font-semibold text-content">Recent orders</h2>
          <Link to="/account/orders" className="text-sm text-brand-600 hover:underline">
            View all
          </Link>
        </div>

        {recentOrders.length === 0 ? (
          <p className="mt-4 text-sm text-content-muted">You have not placed an order yet.</p>
        ) : (
          <ul className="mt-3 flex flex-col gap-2">
            {recentOrders.map((order) => (
              <li key={order.id} className="flex flex-wrap items-baseline justify-between gap-2 text-sm">
                <Link to={`/account/orders/${order.id}`} className="font-mono text-xs text-brand-600 hover:underline">
                  {order.orderNumber}
                </Link>
                <time dateTime={order.placedUtc} className="text-content-muted">
                  {formatDate(order.placedUtc)}
                </time>
                <StatusBadge status={order.status} />
                <span className="font-medium text-content">
                  {formatCurrency(order.grandTotal, order.currencyCode)}
                </span>
              </li>
            ))}
          </ul>
        )}
      </section>
    </div>
  );
};

/** Purchase history with a date range and status filter (spec §14 Phase 10). */
export const AccountOrdersPage = () => {
  const navigate = useNavigate();
  const { params, update, toQuery, searchParams } = useListParams({ sort: '-placedUtc' });

  const dateFrom = searchParams.get('dateFrom') ?? '';
  const dateTo = searchParams.get('dateTo') ?? '';

  const query = useQuery({
    queryKey: ['orders', 'mine', params, dateFrom, dateTo],
    queryFn: ({ signal }) =>
      fetchMyOrders(toQuery({ dateFrom: dateFrom || undefined, dateTo: dateTo || undefined }), signal),
    placeholderData: keepPreviousData,
    staleTime: 0,
  });

  const columns: Column<OrderRow>[] = [
    {
      key: 'orderNumber',
      header: 'Order',
      sortable: true,
      render: (o) => <span className="font-mono text-xs">{o.orderNumber}</span>,
    },
    {
      key: 'placedUtc',
      header: 'Placed',
      sortable: true,
      render: (o) => <time dateTime={o.placedUtc}>{formatDate(o.placedUtc)}</time>,
    },
    { key: 'status', header: 'Status', render: (o) => <StatusBadge status={o.status} /> },
    { key: 'itemCount', header: 'Items', align: 'right', hideOnCard: true, render: (o) => o.itemCount },
    {
      key: 'grandTotal',
      header: 'Total',
      sortable: true,
      align: 'right',
      render: (o) => formatCurrency(o.grandTotal, o.currencyCode),
    },
  ];

  return (
    <div className="flex flex-col gap-4">
      <div className="flex flex-wrap items-center justify-between gap-3">
        <h2 className="text-lg font-semibold text-content">Purchase history</h2>

        <div className="flex flex-wrap items-center gap-3">
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
        </div>
      </div>

      {query.isPending ? (
        <div aria-busy="true" className="flex flex-col gap-2">
          {Array.from({ length: 5 }, (_, index) => (
            <Skeleton key={index} className="h-12" />
          ))}
        </div>
      ) : query.isError ? (
        <ErrorState onRetry={() => void query.refetch()} />
      ) : query.data.items.length === 0 ? (
        <EmptyState
          title="No orders yet"
          icon={<PackageOpen className="size-10" />}
          description="When you place an order it will appear here."
          action={
            <Button size="sm" onClick={() => navigate('/')}>
              Start shopping
            </Button>
          }
        />
      ) : (
        <>
          <DataTable
            caption="Your orders"
            columns={columns}
            rows={query.data.items}
            rowKey={(o) => o.id}
            sort={params.sort}
            onSortChange={(sort) => update({ sort })}
            onRowClick={(order) => navigate(`/account/orders/${order.id}`)}
          />

          <Pagination
            page={query.data.page}
            pageSize={query.data.pageSize}
            totalCount={query.data.totalCount}
            totalPages={query.data.totalPages}
            hasNext={query.data.hasNext}
            hasPrevious={query.data.hasPrevious}
            onPageChange={(page) => update({ page })}
          />
        </>
      )}
    </div>
  );
};

/** Statuses a customer may cancel from (ADR-003). Beyond these, support has to do it. */
const CUSTOMER_CANCELLABLE = new Set(['Pending', 'Confirmed']);

export const AccountOrderDetailPage = () => {
  const { id = '' } = useParams();
  const queryClient = useQueryClient();
  const { show } = useToast();
  const [confirmCancel, setConfirmCancel] = useState(false);

  const query = useQuery({
    queryKey: ['orders', 'mine', 'detail', id],
    queryFn: ({ signal }) => fetchMyOrder(id, signal),
    enabled: id.length > 0,
    staleTime: 0,
  });

  const cancelMutation = useMutation({
    mutationFn: () => cancelMyOrder(id, 'Cancelled from the account area.'),
    onSuccess: async () => {
      await queryClient.invalidateQueries({ queryKey: ['orders', 'mine'] });
      await queryClient.invalidateQueries({ queryKey: ['dashboard', 'customer'] });
      setConfirmCancel(false);
      show({ tone: 'success', message: 'Your order has been cancelled.' });
    },
    onError: (error: Error) => show({ tone: 'error', message: error.message }),
  });

  if (query.isPending) {
    return <Skeleton className="h-96 w-full" />;
  }

  if (query.isError) {
    // A 404 here is also what a customer gets for someone else's order id, which is
    // deliberate: they should not learn that it exists (spec §7.5).
    return <ErrorState title="Order not found" description="We could not find that order on your account." />;
  }

  const order = query.data;
  const canCancel = CUSTOMER_CANCELLABLE.has(order.status);

  return (
    <div className="flex flex-col gap-6">
      <Link
        to="/account/orders"
        className="inline-flex items-center gap-1 text-sm text-content-muted hover:text-content"
      >
        <ArrowLeft className="size-4" aria-hidden="true" />
        Back to orders
      </Link>

      <div className="flex flex-wrap items-center justify-between gap-3">
        <div>
          <h2 className="font-mono text-lg font-semibold text-content">{order.orderNumber}</h2>
          <p className="text-sm text-content-muted">Placed {formatDateTime(order.placedUtc)}</p>
        </div>

        <div className="flex items-center gap-3">
          <StatusBadge status={order.status} />

          {canCancel &&
            (confirmCancel ? (
              <div className="flex items-center gap-2">
                <span className="text-sm text-content-muted">Cancel this order?</span>
                <Button
                  variant="danger"
                  size="sm"
                  isLoading={cancelMutation.isPending}
                  onClick={() => cancelMutation.mutate()}
                >
                  Yes, cancel
                </Button>
                <Button variant="ghost" size="sm" onClick={() => setConfirmCancel(false)}>
                  Keep it
                </Button>
              </div>
            ) : (
              <Button variant="secondary" size="sm" onClick={() => setConfirmCancel(true)}>
                Cancel order
              </Button>
            ))}
        </div>
      </div>

      {!canCancel && order.status !== 'Cancelled' && (
        <p className="rounded-card border border-border-subtle bg-surface-raised p-3 text-sm text-content-muted">
          This order is already {order.status.toLowerCase()} and can no longer be cancelled online.
          Contact support if you need help.
        </p>
      )}

      <div className="grid gap-4 lg:grid-cols-[1fr_20rem]">
        <section className="rounded-card border border-border-subtle p-4">
          <h3 className="text-sm font-semibold text-content">Items</h3>

          <ul className="mt-3 flex flex-col gap-2 text-sm">
            {order.items.map((item) => (
              <li key={item.productId} className="flex items-baseline justify-between gap-3">
                <span className="min-w-0 flex-1 truncate text-content">{item.productName}</span>
                <span className="text-content-muted">
                  {item.quantity} × {formatCurrency(item.unitPrice, order.currencyCode)}
                </span>
                <span className="w-24 text-right font-medium text-content">
                  {formatCurrency(item.lineTotal, order.currencyCode)}
                </span>
              </li>
            ))}
          </ul>

          <div className="mt-4 flex justify-between border-t border-border-subtle pt-3 text-base font-semibold text-content">
            <span>Total</span>
            <span>{formatCurrency(order.grandTotal, order.currencyCode)}</span>
          </div>
        </section>

        <section className="rounded-card border border-border-subtle p-4">
          <h3 className="text-sm font-semibold text-content">Delivery</h3>
          <address className="mt-2 text-sm not-italic text-content-muted">
            {order.shippingAddress.fullName}
            <br />
            {order.shippingAddress.line1}
            <br />
            {order.shippingAddress.city}, {order.shippingAddress.postalCode}
            <br />
            {order.shippingAddress.country}
          </address>
          <p className="mt-2 text-xs text-content-muted">Payment: {order.paymentMethod}</p>
        </section>
      </div>
    </div>
  );
};

const profileSchema = z.object({
  firstName: z.string().min(1, 'Required').max(100),
  lastName: z.string().min(1, 'Required').max(100),
  phoneNumber: z.string().max(32).optional(),
});

type ProfileForm = z.infer<typeof profileSchema>;

/** Profile (spec §9.1 `/users/me/profile`). Email and roles are deliberately not editable here. */
export const AccountProfilePage = () => {
  const user = useAuthStore((state) => state.user);
  const setUser = useAuthStore((state) => state.setUser);
  const { show } = useToast();

  const form = useForm<ProfileForm>({
    resolver: zodResolver(profileSchema),
    defaultValues: {
      firstName: user?.firstName ?? '',
      lastName: user?.lastName ?? '',
      phoneNumber: user?.phoneNumber ?? '',
    },
  });

  const mutation = useMutation({
    mutationFn: (values: ProfileForm) => api.put<UserProfile>('/api/v1/users/me/profile', values),
    onSuccess: (updated) => {
      setUser({ ...updated, permissions: user?.permissions ?? [], roles: user?.roles ?? [] });
      show({ tone: 'success', message: 'Profile updated.' });
    },
    onError: () => show({ tone: 'error', message: 'Could not save your profile.' }),
  });

  return (
    <div className="max-w-md">
      <h2 className="text-lg font-semibold text-content">Your profile</h2>

      <form
        onSubmit={(event) => void form.handleSubmit((values) => mutation.mutate(values))(event)}
        className="mt-4 flex flex-col gap-4"
      >
        <Input
          label="Email"
          value={user?.email ?? ''}
          readOnly
          disabled
          hint="Contact support to change the email on your account."
        />

        <Input
          label="First name"
          required
          autoComplete="given-name"
          error={form.formState.errors.firstName?.message}
          {...form.register('firstName')}
        />

        <Input
          label="Last name"
          required
          autoComplete="family-name"
          error={form.formState.errors.lastName?.message}
          {...form.register('lastName')}
        />

        <Input label="Phone number" type="tel" autoComplete="tel" {...form.register('phoneNumber')} />

        <Button type="submit" isLoading={mutation.isPending} className="self-start">
          Save changes
        </Button>
      </form>
    </div>
  );
};
