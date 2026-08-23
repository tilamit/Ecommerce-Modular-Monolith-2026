import { api } from '../../shared/api/httpClient';
import type {
  CategoryNode,
  PagedResult,
  ProductDetail,
  ProductListItem,
  ProductSuggestion,
} from '../../shared/api/types';

export interface ProductQuery {
  page?: number;
  pageSize?: number;
  sort?: string;
  search?: string;
  categoryIds?: string[];
  minPrice?: number;
  maxPrice?: number;
  inStock?: boolean;
}

/**
 * Builds the query string.
 *
 * Empty values are omitted rather than sent blank, so the URL stays readable and shareable
 * (spec §11.3: the URL is the source of truth for list state).
 */
export const toSearchParams = (query: ProductQuery): URLSearchParams => {
  const params = new URLSearchParams();

  if (query.page !== undefined && query.page > 1) params.set('page', String(query.page));
  if (query.pageSize !== undefined) params.set('pageSize', String(query.pageSize));
  if (query.sort) params.set('sort', query.sort);
  if (query.search) params.set('search', query.search);
  if (query.minPrice !== undefined) params.set('minPrice', String(query.minPrice));
  if (query.maxPrice !== undefined) params.set('maxPrice', String(query.maxPrice));
  if (query.inStock === true) params.set('inStock', 'true');

  for (const id of query.categoryIds ?? []) {
    params.append('categoryIds', id);
  }

  return params;
};

export const fetchProducts = (query: ProductQuery, signal?: AbortSignal) =>
  api.get<PagedResult<ProductListItem>>(`/api/v1/catalog/products?${toSearchParams(query)}`, { signal });

export const fetchProduct = (idOrSlug: string, signal?: AbortSignal) =>
  api.get<ProductDetail>(`/api/v1/catalog/products/${encodeURIComponent(idOrSlug)}`, { signal });

export const fetchNewArrivals = (pageSize = 8, signal?: AbortSignal) =>
  api.get<PagedResult<ProductListItem>>(`/api/v1/catalog/products/new?pageSize=${pageSize}`, { signal });

/** Typeahead. The caller passes an AbortSignal so in-flight requests are cancelled (spec §11.4). */
export const suggestProducts = (term: string, signal?: AbortSignal) =>
  api.get<ProductSuggestion[]>(`/api/v1/catalog/products/suggest?q=${encodeURIComponent(term)}`, { signal });

export const fetchCategoryTree = (signal?: AbortSignal) =>
  api.get<CategoryNode[]>('/api/v1/catalog/categories/tree', { signal });
