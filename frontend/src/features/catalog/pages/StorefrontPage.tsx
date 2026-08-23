import { useSearchParams } from 'react-router';
import { useQuery, keepPreviousData } from '@tanstack/react-query';
import { SlidersHorizontal } from 'lucide-react';
import { fetchCategoryTree, fetchProducts, type ProductQuery } from '../api';
import { ProductCard } from '../components/ProductCard';
import { useCartStore } from '../../cart';
import { useToast } from '../../../shared/components/ui/Toast';
import { Pagination } from '../../../shared/components/ui/Pagination';
import { EmptyState, ErrorState, ProductCardSkeleton } from '../../../shared/components/ui/States';
import { Button } from '../../../shared/components/ui/Button';
import type { ProductListItem } from '../../../shared/api/types';

const PAGE_SIZE = 20;

const SORT_OPTIONS = [
  { value: '-createdUtc', label: 'Newest' },
  { value: 'price', label: 'Price: low to high' },
  { value: '-price', label: 'Price: high to low' },
  { value: 'name', label: 'Name' },
  { value: '-rating', label: 'Top rated' },
] as const;

/**
 * Reads list state from the URL (spec §11.3).
 *
 * The query string is the source of truth for page, sort and every filter, so a filtered
 * view is shareable and survives refresh and the back button. Component state would lose
 * all three.
 */
const useProductQuery = (): [ProductQuery, (next: Partial<ProductQuery>) => void] => {
  const [searchParams, setSearchParams] = useSearchParams();

  const query: ProductQuery = {
    page: Number(searchParams.get('page') ?? '1'),
    pageSize: PAGE_SIZE,
    sort: searchParams.get('sort') ?? '-createdUtc',
    search: searchParams.get('search') ?? undefined,
    categoryIds: searchParams.getAll('categoryIds'),
    minPrice: searchParams.has('minPrice') ? Number(searchParams.get('minPrice')) : undefined,
    maxPrice: searchParams.has('maxPrice') ? Number(searchParams.get('maxPrice')) : undefined,
    inStock: searchParams.get('inStock') === 'true',
  };

  const update = (next: Partial<ProductQuery>) => {
    setSearchParams((current) => {
      const params = new URLSearchParams(current);

      // Any filter change resets to page 1: staying on page 7 of a narrower result set
      // usually lands the user on an empty page.
      if (!('page' in next)) {
        params.delete('page');
      }

      for (const [key, value] of Object.entries(next)) {
        params.delete(key);

        if (Array.isArray(value)) {
          for (const entry of value) {
            params.append(key, String(entry));
          }
        } else if (value !== undefined && value !== '' && value !== false) {
          params.set(key, String(value));
        }
      }

      return params;
    });
  };

  return [query, update];
};

export const StorefrontPage = () => {
  const [query, updateQuery] = useProductQuery();
  const addToCart = useCartStore((state) => state.add);
  const removeFromCart = useCartStore((state) => state.remove);
  const { show } = useToast();

  const productsQuery = useQuery({
    queryKey: ['products', 'list', query],
    queryFn: ({ signal }) => fetchProducts(query, signal),
    // Spec §11.3: the table must not flash empty between pages.
    placeholderData: keepPreviousData,
  });

  const categoriesQuery = useQuery({
    queryKey: ['categories', 'tree'],
    queryFn: ({ signal }) => fetchCategoryTree(signal),
    // The tree changes rarely (spec §11.3).
    staleTime: 5 * 60_000,
  });

  const onAddToCart = (product: ProductListItem) => {
    addToCart(product.id, 1);

    // Spec §11.4: toast on add-to-cart, with an undo.
    show({
      tone: 'success',
      message: `${product.name} added to your cart.`,
      action: { label: 'Undo', onClick: () => removeFromCart(product.id) },
    });
  };

  const selectedCategories = new Set(query.categoryIds ?? []);

  const toggleCategory = (id: string) => {
    const next = new Set(selectedCategories);

    if (next.has(id)) {
      next.delete(id);
    } else {
      next.add(id);
    }

    updateQuery({ categoryIds: [...next] });
  };

  return (
    <div className="flex flex-col gap-6 lg:flex-row">
      <aside className="lg:w-64 lg:shrink-0" aria-label="Filters">
        <details open className="rounded-card border border-border-subtle p-4 lg:open:block">
          <summary className="flex cursor-pointer items-center gap-2 text-sm font-semibold text-content lg:cursor-default">
            <SlidersHorizontal className="size-4" aria-hidden="true" />
            Filters
          </summary>

          <div className="mt-4 flex flex-col gap-5">
            <fieldset>
              <legend className="mb-2 text-xs font-semibold uppercase tracking-wide text-content-muted">
                Category
              </legend>

              <div className="flex max-h-64 flex-col gap-1 overflow-auto">
                {(categoriesQuery.data ?? []).flatMap((root) =>
                  root.children.length > 0 ? root.children : [root],
                ).map((category) => (
                  <label key={category.id} className="flex items-center gap-2 text-sm text-content">
                    <input
                      type="checkbox"
                      checked={selectedCategories.has(category.id)}
                      onChange={() => toggleCategory(category.id)}
                      className="size-4 rounded border-border-subtle"
                    />
                    <span className="flex-1">{category.name}</span>
                    <span className="text-xs text-content-muted">{category.productCount}</span>
                  </label>
                ))}
              </div>
            </fieldset>

            <label className="flex items-center gap-2 text-sm text-content">
              <input
                type="checkbox"
                checked={query.inStock === true}
                onChange={(event) => updateQuery({ inStock: event.target.checked })}
                className="size-4 rounded border-border-subtle"
              />
              In stock only
            </label>

            {(selectedCategories.size > 0 || query.inStock === true || query.search !== undefined) && (
              <Button
                variant="ghost"
                size="sm"
                onClick={() => updateQuery({ categoryIds: [], inStock: false, search: undefined })}
              >
                Clear filters
              </Button>
            )}
          </div>
        </details>
      </aside>

      <section className="flex-1">
        <div className="mb-4 flex flex-wrap items-center justify-between gap-3">
          <h1 className="text-xl font-semibold text-content">
            {query.search !== undefined ? `Results for “${query.search}”` : 'All products'}
          </h1>

          <label className="flex items-center gap-2 text-sm text-content-muted">
            Sort
            <select
              value={query.sort}
              onChange={(event) => updateQuery({ sort: event.target.value })}
              className="h-9 rounded-lg border border-border-subtle bg-surface px-2 text-sm text-content"
            >
              {SORT_OPTIONS.map((option) => (
                <option key={option.value} value={option.value}>
                  {option.label}
                </option>
              ))}
            </select>
          </label>
        </div>

        {productsQuery.isPending ? (
          <div
            aria-busy="true"
            aria-label="Loading products"
            className="grid grid-cols-2 gap-4 md:grid-cols-3 xl:grid-cols-4"
          >
            {Array.from({ length: 8 }, (_, index) => (
              <ProductCardSkeleton key={index} />
            ))}
          </div>
        ) : productsQuery.isError ? (
          <ErrorState onRetry={() => void productsQuery.refetch()} />
        ) : productsQuery.data.items.length === 0 ? (
          <EmptyState
            title="No products match those filters"
            description="Try widening your search or clearing a filter."
            action={
              <Button
                variant="secondary"
                size="sm"
                onClick={() => updateQuery({ categoryIds: [], inStock: false, search: undefined })}
              >
                Clear filters
              </Button>
            }
          />
        ) : (
          <>
            <div className="grid grid-cols-2 gap-4 md:grid-cols-3 xl:grid-cols-4">
              {productsQuery.data.items.map((product) => (
                <ProductCard key={product.id} product={product} onAddToCart={onAddToCart} />
              ))}
            </div>

            <div className="mt-6">
              <Pagination
                page={productsQuery.data.page}
                pageSize={productsQuery.data.pageSize}
                totalCount={productsQuery.data.totalCount}
                totalPages={productsQuery.data.totalPages}
                hasNext={productsQuery.data.hasNext}
                hasPrevious={productsQuery.data.hasPrevious}
                onPageChange={(page) => updateQuery({ page })}
              />
            </div>
          </>
        )}
      </section>
    </div>
  );
};
