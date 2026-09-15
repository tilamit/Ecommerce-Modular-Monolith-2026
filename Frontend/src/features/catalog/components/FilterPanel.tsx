import { useEffect, useId, useMemo, useState } from 'react';
import { ChevronDown, Search, SlidersHorizontal, X } from 'lucide-react';
import type { CategoryNode } from '../../../shared/api/types';
import { Button } from '../../../shared/components/ui/Button';
import { Skeleton } from '../../../shared/components/ui/States';
import { cn } from '../../../shared/lib/cn';
import type { ProductQuery } from '../api';

/** Leaf categories rendered before the list collapses behind "Show all". */
const COLLAPSED_LEAF_LIMIT = 12;

/** A search box inside the category list only earns its space past this many options. */
const SEARCHABLE_FROM = 10;

interface Leaf {
  id: string;
  name: string;
  productCount: number;
}

interface Group {
  key: string;
  /** Undefined for a top-level category with no children: it is rendered without a heading. */
  name?: string;
  leaves: Leaf[];
}

const toLeaf = (category: CategoryNode): Leaf => ({
  id: category.id,
  name: category.name,
  productCount: category.productCount,
});

/**
 * Groups the category tree by its parent.
 *
 * The API returns roots with children and the storefront filters on the leaves. Showing
 * those leaves under their parent's name keeps the structure visible: a flat run of ten
 * checkboxes reads as one undifferentiated list.
 */
const toGroups = (roots: CategoryNode[]): Group[] =>
  roots.map((root) =>
    root.children.length > 0
      ? { key: root.id, name: root.name, leaves: root.children.map(toLeaf) }
      : { key: root.id, leaves: [toLeaf(root)] },
  );

const matches = (leaf: Leaf, term: string): boolean =>
  term === '' || leaf.name.toLowerCase().includes(term.toLowerCase());

/** Drops leaves that do not match, then any group left with nothing in it. */
const filterGroups = (groups: Group[], term: string): Group[] =>
  groups
    .map((group) => ({ ...group, leaves: group.leaves.filter((leaf) => matches(leaf, term)) }))
    .filter((group) => group.leaves.length > 0);

/** Caps the rendered list by *leaf* count, so a group heading never costs a slot. */
const capLeaves = (groups: Group[], limit: number): Group[] => {
  let remaining = limit;

  return groups
    .map((group) => {
      const leaves = group.leaves.slice(0, Math.max(remaining, 0));
      remaining -= leaves.length;

      return { ...group, leaves };
    })
    .filter((group) => group.leaves.length > 0);
};

interface ActiveChip {
  key: string;
  label: string;
  clear: Partial<ProductQuery>;
}

const priceLabel = (min?: number, max?: number): string => {
  if (min !== undefined && max !== undefined) {
    return `${min} - ${max}`;
  }

  return min !== undefined ? `From ${min}` : `Up to ${max ?? 0}`;
};

export interface FilterPanelProps {
  categories: CategoryNode[];
  isLoading: boolean;
  /**
   * True when the category request failed.
   *
   * Without this the panel could not tell an empty catalogue from an unanswered request,
   * and said "No categories yet." to both - which is wrong on a first run, before the API
   * is listening.
   */
  isError?: boolean;
  /** Re-runs the category request, for the retry this panel offers on that failure. */
  onRetryCategories?: () => void;
  query: ProductQuery;
  /** Total matching products, so the panel can say what the filters actually did. */
  resultCount?: number;
  onChange: (next: Partial<ProductQuery>) => void;
}

/**
 * The storefront's left-hand filter rail (spec §11.3, §11.4).
 *
 * Four decisions drive the layout. It stays open from `lg` up and collapses behind a
 * labelled toggle below that, where a permanently expanded rail pushes every product off
 * the first screen. Everything currently applied is repeated as a removable chip at the
 * top, so "why am I seeing so few products" is answerable without reading the whole panel.
 * The category list grows by revealing rows rather than by growing an inner scrollbar,
 * since a scroll region nested inside a scrolling page is easy to miss.
 *
 * And the rail itself scrolls, but only from `lg` up and only once it is taller than the
 * screen. That is not the same thing as the list having its own scrollbar: it is what keeps
 * the bottom of a *sticky* panel reachable, since a sticky element pinned at the top of the
 * viewport cannot be scrolled past. Below `lg` the page's own scroll still does the work.
 *
 * List state still lives in the URL (spec §11.3): this component reads `query` and reports
 * changes and owns nothing but its own presentation state.
 */
export const FilterPanel = ({
  categories,
  isLoading,
  isError = false,
  query,
  resultCount,
  onChange,
  onRetryCategories,
}: FilterPanelProps) => {
  const bodyId = useId();
  const categoryBodyId = useId();
  const categorySearchId = useId();
  const minPriceId = useId();
  const maxPriceId = useId();

  const [isOpenOnMobile, setIsOpenOnMobile] = useState(false);

  // Folded to begin with, so the rail opens as a short list of what can be filtered on
  // rather than as a wall of checkboxes - on a seeded catalogue the category list alone is
  // longer than the other two sections put together. The count badge on the header carries
  // the state that matters while it is closed.
  const [isCategoryOpen, setIsCategoryOpen] = useState(false);

  const [categoryTerm, setCategoryTerm] = useState('');
  const [showAllCategories, setShowAllCategories] = useState(false);

  // The price boxes are a draft until applied. Binding them straight to the URL would fire
  // a request per keystroke, each one resetting the shopper to page 1 halfway through
  // typing "1200".
  const [minDraft, setMinDraft] = useState('');
  const [maxDraft, setMaxDraft] = useState('');

  useEffect(() => {
    setMinDraft(query.minPrice === undefined ? '' : String(query.minPrice));
    setMaxDraft(query.maxPrice === undefined ? '' : String(query.maxPrice));
  }, [query.minPrice, query.maxPrice]);

  // Unfold on failure. The section is folded by default, but a tree that cannot be loaded
  // has to report that and offer the retry, which a fold would hide. Left as an effect
  // rather than folded into the value so the shopper can still close it afterwards.
  useEffect(() => {
    if (isError) {
      setIsCategoryOpen(true);
    }
  }, [isError]);

  const groups = useMemo(() => toGroups(categories), [categories]);
  const leafCount = useMemo(() => groups.reduce((total, group) => total + group.leaves.length, 0), [groups]);

  const matching = useMemo(() => filterGroups(groups, categoryTerm), [groups, categoryTerm]);
  const matchingLeafCount = matching.reduce((total, group) => total + group.leaves.length, 0);
  const isCapped = !showAllCategories && matchingLeafCount > COLLAPSED_LEAF_LIMIT;
  const visible = isCapped ? capLeaves(matching, COLLAPSED_LEAF_LIMIT) : matching;

  const selected = useMemo(() => new Set(query.categoryIds ?? []), [query.categoryIds]);

  const nameById = useMemo(() => {
    const map = new Map<string, string>();

    for (const group of groups) {
      for (const leaf of group.leaves) {
        map.set(leaf.id, leaf.name);
      }
    }

    return map;
  }, [groups]);

  const toggleCategory = (id: string) => {
    const next = new Set(selected);

    if (next.has(id)) {
      next.delete(id);
    } else {
      next.add(id);
    }

    onChange({ categoryIds: [...next] });
  };

  const chips: ActiveChip[] = [
    ...(query.search !== undefined && query.search !== ''
      ? [{ key: 'search', label: `Search: ${query.search}`, clear: { search: undefined } }]
      : []),
    ...[...selected].map((id) => ({
      key: `category-${id}`,
      label: nameById.get(id) ?? 'Category',
      clear: { categoryIds: [...selected].filter((other) => other !== id) },
    })),
    ...(query.minPrice !== undefined || query.maxPrice !== undefined
      ? [
          {
            key: 'price',
            label: priceLabel(query.minPrice, query.maxPrice),
            clear: { minPrice: undefined, maxPrice: undefined },
          },
        ]
      : []),
    ...(query.inStock === true ? [{ key: 'stock', label: 'In stock only', clear: { inStock: false } }] : []),
  ];

  const clearAll: Partial<ProductQuery> = {
    categoryIds: [],
    inStock: false,
    search: undefined,
    minPrice: undefined,
    maxPrice: undefined,
  };

  const applyPrice = () => {
    const min = minDraft.trim() === '' ? undefined : Number(minDraft);
    const max = maxDraft.trim() === '' ? undefined : Number(maxDraft);

    // A reversed range matches nothing and reads as a broken catalogue, so swap it rather
    // than send it.
    const reversed = min !== undefined && max !== undefined && min > max;

    onChange({ minPrice: reversed ? max : min, maxPrice: reversed ? min : max });
  };

  return (
    <aside className="lg:w-64 lg:shrink-0" aria-label="Product filters">
      {/*
        From `lg` up the rail is sticky and a sticky element taller than the viewport pins
        at `top-20` with everything past the fold unreachable, since the page scrolls around
        it rather than through it. With the category list expanded the rail passes that
        height, so it is bounded to the visible space and scrolls.

        `flex-col` keeps the header outside the scrolling region, so "Filters" and its active
        count stay put. `min-h-0` because a flex item will not shrink below its content
        without it, which would leave the overflow with nothing to do.

        Below `lg` none of this applies: the rail is in normal flow behind its own toggle, so
        the page's own scroll is the right one.
      */}
      <div className="rounded-card border border-border-subtle bg-surface-raised lg:sticky lg:top-20 lg:flex lg:max-h-[calc(100dvh-6rem)] lg:flex-col">
        <div className="flex items-center gap-2 px-4 py-3 lg:shrink-0">
          <SlidersHorizontal className="size-4 text-content-muted" aria-hidden="true" />
          <h2 className="text-sm font-semibold text-content">Filters</h2>

          {chips.length > 0 && (
            <span className="rounded-full bg-brand-600 px-2 py-0.5 text-xs font-semibold text-white">
              {chips.length}
            </span>
          )}

          {/* Mobile only: from `lg` up the rail costs no vertical space, so it stays open. */}
          <button
            type="button"
            onClick={() => setIsOpenOnMobile((open) => !open)}
            aria-expanded={isOpenOnMobile}
            aria-controls={bodyId}
            className="ml-auto flex items-center gap-1 rounded-lg px-2 py-1 text-sm text-content-muted transition-colors hover:bg-surface-sunken hover:text-content lg:hidden"
          >
            {isOpenOnMobile ? 'Hide' : 'Show'}
            <ChevronDown
              className={cn('size-4 transition-transform', isOpenOnMobile && 'rotate-180')}
              aria-hidden="true"
            />
          </button>
        </div>

        <div
          id={bodyId}
          className={cn(
            'border-t border-border-subtle lg:min-h-0 lg:overflow-y-auto',
            !isOpenOnMobile && 'hidden lg:block',
          )}
        >
          {chips.length > 0 && (
            <div className="flex flex-wrap gap-1.5 border-b border-border-subtle p-4">
              {chips.map((chip) => (
                <button
                  key={chip.key}
                  type="button"
                  onClick={() => onChange(chip.clear)}
                  aria-label={`Remove filter: ${chip.label}`}
                  className="inline-flex max-w-full items-center gap-1 rounded-full border border-border-subtle bg-surface px-2.5 py-1 text-xs text-content transition-colors hover:border-danger hover:text-danger"
                >
                  <span className="truncate">{chip.label}</span>
                  <X className="size-3 shrink-0" aria-hidden="true" />
                </button>
              ))}

              <button
                type="button"
                onClick={() => onChange(clearAll)}
                className="rounded-full px-2.5 py-1 text-xs font-medium text-brand-600 transition-colors hover:bg-surface-sunken"
              >
                Clear all
              </button>
            </div>
          )}

          <fieldset className="border-b border-border-subtle p-4">
            {/*
              The button lives inside the legend rather than replacing it, so the fieldset
              still names the checkbox group for a screen reader while also being the thing
              that folds it. `w-full` because a legend is not a normal flow box - without it
              the button shrink-wraps its text and the chevron lands mid-heading.
            */}
            <legend className="w-full">
              <button
                type="button"
                onClick={() => setIsCategoryOpen((open) => !open)}
                aria-expanded={isCategoryOpen}
                aria-controls={categoryBodyId}
                className="flex w-full items-center gap-2 rounded-lg py-1 text-xs font-semibold uppercase tracking-wide text-content-muted transition-colors hover:text-content"
              >
                Category

                {/* The chips at the top of the rail do not say which section they came
                    from, so folded this is the only indication the filter is active. */}
                {selected.size > 0 && (
                  <span className="rounded-full bg-brand-600 px-1.5 text-xs font-semibold normal-case tracking-normal text-white">
                    {selected.size}
                  </span>
                )}

                <ChevronDown
                  className={cn('ml-auto size-4 transition-transform', !isCategoryOpen && '-rotate-90')}
                  aria-hidden="true"
                />
              </button>
            </legend>

            {/*
              The `hidden` attribute rather than a `hidden` class: this fold is not
              responsive and the attribute removes the collapsed rows from the accessibility
              tree without depending on a stylesheet. The rows stay in the DOM, so the search
              term and the "show all" state survive a fold.
            */}
            <div id={categoryBodyId} className="mt-2" hidden={!isCategoryOpen}>
              {leafCount >= SEARCHABLE_FROM && (
                <div className="relative mb-2">
                  <Search
                    className="pointer-events-none absolute left-2.5 top-1/2 size-4 -translate-y-1/2 text-content-muted"
                    aria-hidden="true"
                  />
                  <label htmlFor={categorySearchId} className="sr-only">
                    Find a category
                  </label>
                  <input
                    id={categorySearchId}
                    type="search"
                    value={categoryTerm}
                    onChange={(event) => setCategoryTerm(event.target.value)}
                    placeholder="Find a category"
                    className="h-9 w-full rounded-lg border border-border-subtle bg-surface pl-8 pr-2 text-sm text-content placeholder:text-content-muted"
                  />
                </div>
              )}

              {isLoading ? (
                <div className="flex flex-col gap-2" aria-busy="true" aria-label="Loading categories">
                  {Array.from({ length: 6 }, (_, index) => (
                    <Skeleton key={index} className="h-7 w-full" />
                  ))}
                </div>
              ) : isError ? (
                <div role="alert" className="flex flex-col items-start gap-1 py-2">
                  <p className="text-sm text-content-muted">Categories could not be loaded.</p>
                  {onRetryCategories !== undefined && (
                    <button
                      type="button"
                      onClick={onRetryCategories}
                      className="rounded-lg px-2 py-1 text-xs font-medium text-brand-600 transition-colors hover:bg-surface-sunken"
                    >
                      Try again
                    </button>
                  )}
                </div>
              ) : matchingLeafCount === 0 ? (
                <p className="py-2 text-sm text-content-muted">
                  {leafCount === 0 ? 'No categories yet.' : `No category matches "${categoryTerm}".`}
                </p>
              ) : (
                <div className="flex flex-col gap-3">
                  {visible.map((group) => (
                    <div key={group.key} className="flex flex-col gap-0.5">
                      {group.name !== undefined && (
                        <p className="px-1 text-xs font-medium text-content-muted">{group.name}</p>
                      )}

                      {group.leaves.map((leaf) => (
                        <label
                          key={leaf.id}
                          className={cn(
                            'flex cursor-pointer items-center gap-2 rounded-lg px-2 py-1.5 text-sm text-content',
                            'transition-colors hover:bg-surface-sunken',
                            selected.has(leaf.id) && 'bg-surface-sunken font-medium',
                          )}
                        >
                          <input
                            type="checkbox"
                            checked={selected.has(leaf.id)}
                            onChange={() => toggleCategory(leaf.id)}
                            className="size-4 shrink-0 rounded border-border-subtle accent-brand-600"
                          />
                          <span className="flex-1 truncate">{leaf.name}</span>
                          {/* The leading comma is what keeps this from being read as "Laptops12 products". */}
                          <span className="sr-only">, {leaf.productCount} products</span>
                          <span
                            aria-hidden="true"
                            className="shrink-0 rounded-full bg-surface-sunken px-1.5 text-xs tabular-nums text-content-muted"
                          >
                            {leaf.productCount}
                          </span>
                        </label>
                      ))}
                    </div>
                  ))}

                  {matchingLeafCount > COLLAPSED_LEAF_LIMIT && (
                    <button
                      type="button"
                      onClick={() => setShowAllCategories((shown) => !shown)}
                      className="self-start rounded-lg px-2 py-1 text-xs font-medium text-brand-600 transition-colors hover:bg-surface-sunken"
                    >
                      {isCapped ? `Show all ${matchingLeafCount} categories` : 'Show fewer'}
                    </button>
                  )}
                </div>
              )}
            </div>
          </fieldset>

          <fieldset className="border-b border-border-subtle p-4">
            <legend className="mb-2 text-xs font-semibold uppercase tracking-wide text-content-muted">
              Price
            </legend>

            <div className="flex items-end gap-2">
              <div className="flex-1">
                <label htmlFor={minPriceId} className="mb-1 block text-xs text-content-muted">
                  Min
                </label>
                <input
                  id={minPriceId}
                  type="number"
                  inputMode="decimal"
                  min={0}
                  value={minDraft}
                  onChange={(event) => setMinDraft(event.target.value)}
                  onKeyDown={(event) => {
                    if (event.key === 'Enter') {
                      applyPrice();
                    }
                  }}
                  placeholder="0"
                  className="h-9 w-full rounded-lg border border-border-subtle bg-surface px-2 text-sm text-content placeholder:text-content-muted"
                />
              </div>

              <span aria-hidden="true" className="pb-2 text-content-muted">
                -
              </span>

              <div className="flex-1">
                <label htmlFor={maxPriceId} className="mb-1 block text-xs text-content-muted">
                  Max
                </label>
                <input
                  id={maxPriceId}
                  type="number"
                  inputMode="decimal"
                  min={0}
                  value={maxDraft}
                  onChange={(event) => setMaxDraft(event.target.value)}
                  onKeyDown={(event) => {
                    if (event.key === 'Enter') {
                      applyPrice();
                    }
                  }}
                  placeholder="Any"
                  className="h-9 w-full rounded-lg border border-border-subtle bg-surface px-2 text-sm text-content placeholder:text-content-muted"
                />
              </div>
            </div>

            <Button variant="secondary" size="sm" className="mt-2 w-full" onClick={applyPrice}>
              Apply price
            </Button>
          </fieldset>

          <fieldset className="p-4">
            <legend className="mb-2 text-xs font-semibold uppercase tracking-wide text-content-muted">
              Availability
            </legend>

            <label className="flex cursor-pointer items-center gap-2 rounded-lg px-2 py-1.5 text-sm text-content transition-colors hover:bg-surface-sunken">
              <input
                type="checkbox"
                checked={query.inStock === true}
                onChange={(event) => onChange({ inStock: event.target.checked })}
                className="size-4 shrink-0 rounded border-border-subtle accent-brand-600"
              />
              In stock only
            </label>
          </fieldset>

          {resultCount !== undefined && (
            <p role="status" className="border-t border-border-subtle px-4 py-3 text-xs text-content-muted">
              {resultCount === 1 ? '1 product matches' : `${resultCount} products match`}
            </p>
          )}
        </div>
      </div>
    </aside>
  );
};
