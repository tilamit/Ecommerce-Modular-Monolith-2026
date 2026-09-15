import { useState } from 'react';
import { useQuery, useQueryClient, useMutation, keepPreviousData } from '@tanstack/react-query';
import { Pencil, Plus, Trash2 } from 'lucide-react';
import {
  deleteCategory,
  deleteOffer,
  deleteProduct,
  fetchCategories,
  fetchOffers,
  setCategoryStatus,
  setProductStatus,
  type CategoryListItem,
  type OfferListItem,
} from '../api';
import { fetchProducts } from '../../catalog';
import { ListPage } from '../components/ListPage';
import { ProductFormModal } from '../components/ProductFormModal';
import { CategoryFormModal } from '../components/CategoryFormModal';
import { OfferFormModal } from '../components/OfferFormModal';
import { useListParams } from '../../../shared/hooks/useListParams';
import { Button } from '../../../shared/components/ui/Button';
import { ConfirmModal } from '../../../shared/components/ui/Modal';
import { useToast } from '../../../shared/components/ui/Toast';
import { hasPermission } from '../../../shared/api/authStore';
import { formatCurrency, formatDate } from '../../../shared/lib/format';
import type { Column } from '../../../shared/components/ui/DataTable';
import type { ProductListItem } from '../../../shared/api/types';

/**
 * Every catalog write is gated on the permission the endpoint itself requires, so an admin
 * who has the read permission but not the write one sees the grid without the controls.
 *
 * This is presentation only. Hiding a button is not the security boundary - the endpoints
 * behind these calls check the same permission server-side, which is what actually stops
 * the request. Granting `catalog.products.write` to another role through access management
 * is all it takes for these controls to appear for that role.
 */
const CATALOG_WRITE = {
  products: 'catalog.products.write',
  categories: 'catalog.categories.write',
  offers: 'catalog.offers.write',
} as const;

/** Admin products (spec §9.2). Unlike the storefront, this shows inactive rows too. */
export const AdminProductsPage = () => {
  const { params, update, searchParams } = useListParams({ sort: '-createdUtc' });
  const queryClient = useQueryClient();
  const { show } = useToast();

  const canWrite = hasPermission(CATALOG_WRITE.products);

  const [editing, setEditing] = useState<ProductListItem | null>(null);
  const [isFormOpen, setIsFormOpen] = useState(false);
  const [deleting, setDeleting] = useState<ProductListItem | null>(null);

  // The form needs somewhere to file a product and the picker has to list categories the
  // admin has not paged to. One unpaged read, only while the form can be opened.
  const categoriesQuery = useQuery({
    queryKey: ['admin', 'categories', 'all'],
    queryFn: ({ signal }) => fetchCategories(new URLSearchParams({ pageSize: '200' }), signal),
    enabled: canWrite,
  });

  const refreshLists = async () => {
    await queryClient.invalidateQueries({ queryKey: ['admin', 'products'] });
    await queryClient.invalidateQueries({ queryKey: ['products'] });
  };

  const deleteMutation = useMutation({
    mutationFn: (id: string) => deleteProduct(id),
    onSuccess: async () => {
      await refreshLists();
      show({ tone: 'success', message: 'Product deleted.' });
      setDeleting(null);
    },
    onError: () => show({ tone: 'error', message: 'Could not delete the product.' }),
  });

  const isActive = searchParams.get('isActive') ?? '';

  const query = useQuery({
    queryKey: ['admin', 'products', params, isActive],
    queryFn: ({ signal }) =>
      fetchProducts(
        {
          page: params.page,
          pageSize: params.pageSize,
          sort: params.sort,
          search: params.search,
          // An admin explicitly filtering by status is why this list is never served from
          // the storefront's anonymous cache.
          ...(isActive === '' ? {} : { isActive: isActive === 'true' }),
        },
        signal,
      ),
    placeholderData: keepPreviousData,
  });

  const statusMutation = useMutation({
    mutationFn: ({ id, active }: { id: string; active: boolean }) => setProductStatus(id, active),
    onSuccess: async () => {
      // Both the admin list and the storefront list change, so both are invalidated.
      await queryClient.invalidateQueries({ queryKey: ['admin', 'products'] });
      await queryClient.invalidateQueries({ queryKey: ['products'] });
      show({ tone: 'success', message: 'Product status updated.' });
    },
    onError: () => show({ tone: 'error', message: 'Could not change the product status.' }),
  });

  const columns: Column<ProductListItem>[] = [
    { key: 'sku', header: 'SKU', sortable: false, render: (p) => <span className="font-mono text-xs">{p.sku}</span> },
    { key: 'name', header: 'Name', sortable: true, render: (p) => p.name },
    { key: 'categoryName', header: 'Category', hideOnCard: true, render: (p) => p.categoryName },
    {
      key: 'price',
      header: 'Price',
      sortable: true,
      align: 'right',
      render: (p) => formatCurrency(p.price, p.currencyCode),
    },
    {
      key: 'stockQuantity',
      header: 'Stock',
      sortable: true,
      align: 'right',
      render: (p) => (
        <span className={p.stockQuantity === 0 ? 'text-danger' : 'text-content'}>{p.stockQuantity}</span>
      ),
    },
    {
      key: 'isActive',
      header: 'Status',
      render: (p) => (
        <span className={p.isActive ? 'text-success' : 'text-content-muted'}>
          {p.isActive ? 'Active' : 'Inactive'}
        </span>
      ),
    },
    {
      key: 'actions',
      header: 'Actions',
      align: 'right',
      render: (p) => (
        <div className="flex items-center justify-end gap-1">
          <Button
            variant="secondary"
            size="sm"
            isLoading={statusMutation.isPending && statusMutation.variables?.id === p.id}
            onClick={() => statusMutation.mutate({ id: p.id, active: !p.isActive })}
            aria-label={`${p.isActive ? 'Deactivate' : 'Activate'} ${p.name}`}
          >
            {p.isActive ? 'Deactivate' : 'Activate'}
          </Button>

          {canWrite && (
            <>
              <Button
                variant="ghost"
                size="sm"
                aria-label={`Edit ${p.name}`}
                onClick={() => {
                  setEditing(p);
                  setIsFormOpen(true);
                }}
              >
                <Pencil className="size-4" aria-hidden="true" />
              </Button>

              <Button
                variant="ghost"
                size="sm"
                aria-label={`Delete ${p.name}`}
                onClick={() => setDeleting(p)}
              >
                <Trash2 className="size-4" aria-hidden="true" />
              </Button>
            </>
          )}
        </div>
      ),
    },
  ];

  return (
    <>
    <ListPage
      title="Products"
      columns={columns}
      rowKey={(p) => p.id}
      query={query}
      search={params.search}
      onSearchChange={(search) => update({ search })}
      searchPlaceholder="Search by name or SKU…"
      sort={params.sort}
      onSortChange={(sort) => update({ sort })}
      onPageChange={(page) => update({ page })}
      emptyTitle="No products match those filters"
      actions={
        canWrite && (
          <Button
            size="sm"
            onClick={() => {
              setEditing(null);
              setIsFormOpen(true);
            }}
          >
            <Plus className="size-4" aria-hidden="true" />
            New product
          </Button>
        )
      }
      filters={
        <label className="flex items-center gap-2 text-sm text-content-muted">
          Status
          <select
            value={isActive}
            onChange={(event) => update({ isActive: event.target.value })}
            className="h-9 rounded-lg border border-border-subtle bg-surface px-2 text-sm text-content"
          >
            <option value="">All</option>
            <option value="true">Active</option>
            <option value="false">Inactive</option>
          </select>
        </label>
      }
    />

    {canWrite && (
      <ProductFormModal
        open={isFormOpen}
        product={editing}
        categories={categoriesQuery.data?.items ?? []}
        onClose={() => setIsFormOpen(false)}
        onSaved={() => void refreshLists()}
      />
    )}

    <ConfirmModal
      open={deleting !== null}
      title="Delete product"
      description={`"${deleting?.name ?? ''}" will be deleted. It leaves the storefront and every list, but its record is kept for the audit trail and orders that already contain it are unaffected.`}
      isPending={deleteMutation.isPending}
      onConfirm={() => deleting !== null && deleteMutation.mutate(deleting.id)}
      onClose={() => setDeleting(null)}
    />
    </>
  );
};

export const AdminCategoriesPage = () => {
  const { params, update, toQuery } = useListParams();
  const queryClient = useQueryClient();
  const { show } = useToast();

  const canWrite = hasPermission(CATALOG_WRITE.categories);

  const [editing, setEditing] = useState<CategoryListItem | null>(null);
  const [isFormOpen, setIsFormOpen] = useState(false);
  const [deleting, setDeleting] = useState<CategoryListItem | null>(null);

  const query = useQuery({
    queryKey: ['admin', 'categories', params],
    queryFn: ({ signal }) => fetchCategories(toQuery(), signal),
    placeholderData: keepPreviousData,
  });

  // The category tree feeds the storefront filter panel and the product form's picker, so
  // every write here invalidates more than this grid.
  const refreshLists = async () => {
    await queryClient.invalidateQueries({ queryKey: ['admin', 'categories'] });
    await queryClient.invalidateQueries({ queryKey: ['categories'] });
  };

  const statusMutation = useMutation({
    mutationFn: ({ id, active }: { id: string; active: boolean }) => setCategoryStatus(id, active),
    onSuccess: async () => {
      await refreshLists();
      show({ tone: 'success', message: 'Category status updated.' });
    },
    onError: () => show({ tone: 'error', message: 'Could not change the category status.' }),
  });

  const deleteMutation = useMutation({
    mutationFn: (id: string) => deleteCategory(id),
    onSuccess: async () => {
      await refreshLists();
      show({ tone: 'success', message: 'Category deleted.' });
      setDeleting(null);
    },
    // A category holding products, or one with children, is refused by the server. That is
    // the useful half of the message, so it is passed through rather than replaced.
    onError: (error: unknown) =>
      show({
        tone: 'error',
        message: error instanceof Error ? error.message : 'Could not delete the category.',
      }),
  });

  const columns: Column<CategoryListItem>[] = [
    { key: 'name', header: 'Name', render: (c) => c.name },
    { key: 'slug', header: 'Slug', hideOnCard: true, render: (c) => <span className="font-mono text-xs">{c.slug}</span> },
    {
      key: 'parentId',
      header: 'Type',
      render: (c) => (
        <span className="text-content-muted">
          {c.parentId === null || c.parentId === undefined ? 'Top level' : 'Subcategory'}
        </span>
      ),
    },
    { key: 'productCount', header: 'Products', align: 'right', render: (c) => c.productCount },
    {
      key: 'isActive',
      header: 'Status',
      render: (c) => (
        <span className={c.isActive ? 'text-success' : 'text-content-muted'}>
          {c.isActive ? 'Active' : 'Inactive'}
        </span>
      ),
    },
    {
      key: 'actions',
      header: 'Actions',
      align: 'right',
      render: (c) =>
        canWrite ? (
          <div className="flex items-center justify-end gap-1">
            <Button
              variant="secondary"
              size="sm"
              isLoading={statusMutation.isPending && statusMutation.variables?.id === c.id}
              onClick={() => statusMutation.mutate({ id: c.id, active: !c.isActive })}
              aria-label={`${c.isActive ? 'Deactivate' : 'Activate'} ${c.name}`}
            >
              {c.isActive ? 'Deactivate' : 'Activate'}
            </Button>

            <Button
              variant="ghost"
              size="sm"
              aria-label={`Edit ${c.name}`}
              onClick={() => {
                setEditing(c);
                setIsFormOpen(true);
              }}
            >
              <Pencil className="size-4" aria-hidden="true" />
            </Button>

            <Button variant="ghost" size="sm" aria-label={`Delete ${c.name}`} onClick={() => setDeleting(c)}>
              <Trash2 className="size-4" aria-hidden="true" />
            </Button>
          </div>
        ) : null,
    },
  ];

  return (
    <>
      <ListPage
        title="Categories"
        columns={columns}
        rowKey={(c) => c.id}
        query={query}
        search={params.search}
        onSearchChange={(search) => update({ search })}
        onPageChange={(page) => update({ page })}
        emptyTitle="No categories yet"
        actions={
          canWrite && (
            <Button
              size="sm"
              onClick={() => {
                setEditing(null);
                setIsFormOpen(true);
              }}
            >
              <Plus className="size-4" aria-hidden="true" />
              New category
            </Button>
          )
        }
      />

      {canWrite && (
        <CategoryFormModal
          open={isFormOpen}
          category={editing}
          categories={query.data?.items ?? []}
          onClose={() => setIsFormOpen(false)}
          onSaved={() => void refreshLists()}
        />
      )}

      <ConfirmModal
        open={deleting !== null}
        title="Delete category"
        description={`"${deleting?.name ?? ''}" will be deleted. Its record is kept for the audit trail. A category that still holds products cannot be deleted.`}
        isPending={deleteMutation.isPending}
        onConfirm={() => deleting !== null && deleteMutation.mutate(deleting.id)}
        onClose={() => setDeleting(null)}
      />
    </>
  );
};

export const AdminOffersPage = () => {
  const { params, update, toQuery } = useListParams();
  const queryClient = useQueryClient();
  const { show } = useToast();

  const canWrite = hasPermission(CATALOG_WRITE.offers);

  const [editing, setEditing] = useState<OfferListItem | null>(null);
  const [isFormOpen, setIsFormOpen] = useState(false);
  const [deleting, setDeleting] = useState<OfferListItem | null>(null);

  const query = useQuery({
    queryKey: ['admin', 'offers', params],
    queryFn: ({ signal }) => fetchOffers(toQuery(), signal),
    placeholderData: keepPreviousData,
  });

  const refreshList = () => queryClient.invalidateQueries({ queryKey: ['admin', 'offers'] });

  const deleteMutation = useMutation({
    mutationFn: (id: string) => deleteOffer(id),
    onSuccess: async () => {
      await refreshList();
      show({ tone: 'success', message: 'Offer deleted.' });
      setDeleting(null);
    },
    onError: (error: unknown) =>
      show({ tone: 'error', message: error instanceof Error ? error.message : 'Could not delete the offer.' }),
  });

  const columns: Column<OfferListItem>[] = [
    { key: 'code', header: 'Code', render: (o) => <span className="font-mono text-xs">{o.code}</span> },
    { key: 'name', header: 'Name', render: (o) => o.name },
    {
      key: 'discount',
      header: 'Discount',
      align: 'right',
      render: (o) =>
        o.discountType === 'Percentage' ? `${o.discountValue}%` : formatCurrency(o.discountValue),
    },
    {
      key: 'window',
      header: 'Window',
      hideOnCard: true,
      render: (o) => (
        <span className="text-content-muted">
          {formatDate(o.startUtc)} - {formatDate(o.endUtc)}
        </span>
      ),
    },
    {
      key: 'redemptions',
      header: 'Used',
      align: 'right',
      render: (o) => (o.maxRedemptions === null || o.maxRedemptions === undefined ? o.redemptionCount : `${o.redemptionCount} / ${o.maxRedemptions}`),
    },
    {
      key: 'isRedeemable',
      header: 'State',
      render: (o) => (
        // isRedeemable folds together active, in-window and under-cap. Showing the derived
        // answer avoids the admin having to compute it from three columns.
        <span className={o.isRedeemable ? 'text-success' : 'text-content-muted'}>
          {o.isRedeemable ? 'Redeemable' : o.isActive ? 'Out of window' : 'Inactive'}
        </span>
      ),
    },
    {
      key: 'actions',
      header: 'Actions',
      align: 'right',
      render: (o) =>
        canWrite ? (
          <div className="flex items-center justify-end gap-1">
            <Button
              variant="ghost"
              size="sm"
              aria-label={`Edit ${o.code}`}
              onClick={() => {
                setEditing(o);
                setIsFormOpen(true);
              }}
            >
              <Pencil className="size-4" aria-hidden="true" />
            </Button>

            <Button variant="ghost" size="sm" aria-label={`Delete ${o.code}`} onClick={() => setDeleting(o)}>
              <Trash2 className="size-4" aria-hidden="true" />
            </Button>
          </div>
        ) : null,
    },
  ];

  return (
    <>
      <ListPage
        title="Offers"
        columns={columns}
        rowKey={(o) => o.id}
        query={query}
        search={params.search}
        onSearchChange={(search) => update({ search })}
        searchPlaceholder="Search by code or name…"
        onPageChange={(page) => update({ page })}
        emptyTitle="No offers yet"
        actions={
          canWrite && (
            <Button
              size="sm"
              onClick={() => {
                setEditing(null);
                setIsFormOpen(true);
              }}
            >
              <Plus className="size-4" aria-hidden="true" />
              New offer
            </Button>
          )
        }
      />

      {canWrite && (
        <OfferFormModal
          open={isFormOpen}
          offer={editing}
          onClose={() => setIsFormOpen(false)}
          onSaved={() => void refreshList()}
        />
      )}

      <ConfirmModal
        open={deleting !== null}
        title="Delete offer"
        description={`"${deleting?.code ?? ''}" will be deleted. It leaves every list and can no longer be used, but its record is kept for the audit trail and the code becomes available for a new offer.`}
        isPending={deleteMutation.isPending}
        onConfirm={() => deleting !== null && deleteMutation.mutate(deleting.id)}
        onClose={() => setDeleting(null)}
      />
    </>
  );
};
