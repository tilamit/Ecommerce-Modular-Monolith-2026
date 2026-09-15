import { useCallback, useMemo } from 'react';
import { useSearchParams } from 'react-router';

export interface ListParams {
  page: number;
  pageSize: number;
  sort?: string;
  search?: string;
}

/**
 * List state held in the query string (spec §11.3).
 *
 * "A filtered admin view must be shareable and survive refresh/back." Component state
 * satisfies none of those; the URL satisfies all three for free.
 */
export const useListParams = (defaults: { pageSize?: number; sort?: string } = {}) => {
  const [searchParams, setSearchParams] = useSearchParams();

  const params = useMemo<ListParams>(
    () => ({
      page: Math.max(1, Number(searchParams.get('page') ?? '1')),
      pageSize: Number(searchParams.get('pageSize') ?? String(defaults.pageSize ?? 20)),
      sort: searchParams.get('sort') ?? defaults.sort ?? undefined,
      search: searchParams.get('search') ?? undefined,
    }),
    [searchParams, defaults.pageSize, defaults.sort],
  );

  const update = useCallback(
    (next: Record<string, string | number | boolean | undefined | null>) => {
      setSearchParams(
        (current) => {
          const updated = new URLSearchParams(current);

          // Any change other than paging returns to page 1: staying on page 7 of a
          // narrower result set usually lands the user on an empty page.
          if (!('page' in next)) {
            updated.delete('page');
          }

          for (const [key, value] of Object.entries(next)) {
            if (value === undefined || value === null || value === '' || value === false) {
              updated.delete(key);
            } else {
              updated.set(key, String(value));
            }
          }

          return updated;
        },
        { replace: true },
      );
    },
    [setSearchParams],
  );

  /** Builds the query string sent to the API, omitting empty values. */
  const toQuery = useCallback(
    (extra: Record<string, string | number | boolean | undefined | null> = {}) => {
      const query = new URLSearchParams();

      query.set('page', String(params.page));
      query.set('pageSize', String(params.pageSize));

      if (params.sort !== undefined) query.set('sort', params.sort);
      if (params.search !== undefined) query.set('search', params.search);

      for (const [key, value] of Object.entries(extra)) {
        if (value !== undefined && value !== null && value !== '' && value !== false) {
          query.set(key, String(value));
        }
      }

      return query;
    },
    [params],
  );

  return { params, update, toQuery, searchParams };
};
