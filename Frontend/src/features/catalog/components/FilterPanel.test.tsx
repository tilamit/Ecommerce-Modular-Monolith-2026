import { describe, expect, it, vi } from 'vitest';
import { render, screen, within } from '@testing-library/react';
import userEvent from '@testing-library/user-event';
import axe from 'axe-core';
import { FilterPanel } from './FilterPanel';
import type { ProductQuery } from '../api';
import type { CategoryNode } from '../../../shared/api/types';

const leaf = (id: string, name: string, productCount: number): CategoryNode => ({
  id,
  name,
  slug: name.toLowerCase(),
  displayOrder: 0,
  isActive: true,
  productCount,
  children: [],
});

const root = (id: string, name: string, children: CategoryNode[]): CategoryNode => ({
  ...leaf(id, name, 0),
  children,
});

const CATEGORIES: CategoryNode[] = [
  root('electronics', 'Electronics', [
    leaf('laptops', 'Laptops', 12),
    leaf('phones', 'Phones', 11),
    leaf('audio', 'Audio', 11),
  ]),
  root('home', 'Home', [leaf('cookware', 'Cookware', 12), leaf('appliances', 'Appliances', 13)]),
];

const BASE: ProductQuery = { page: 1, pageSize: 20, sort: '-createdUtc', categoryIds: [], inStock: false };

const renderPanel = (query: Partial<ProductQuery> = {}) => {
  const onChange = vi.fn();

  const view = render(
    <FilterPanel
      categories={CATEGORIES}
      isLoading={false}
      query={{ ...BASE, ...query }}
      resultCount={107}
      onChange={onChange}
    />,
  );

  return { ...view, onChange };
};

/** The category section ships folded, so anything reaching the list opens it first. */
const openCategories = () => userEvent.click(screen.getByRole('button', { name: /^Category/ }));

describe('FilterPanel', () => {
  it('groups leaf categories under their parent and shows each count', async () => {
    renderPanel();
    await openCategories();

    expect(screen.getByText('Electronics')).toBeInTheDocument();
    expect(screen.getByRole('checkbox', { name: /Laptops, 12 products/ })).toBeInTheDocument();
    expect(screen.getByRole('checkbox', { name: /Appliances, 13 products/ })).toBeInTheDocument();
  });

  it('reports the result count so the panel says what the filters did', () => {
    renderPanel();

    expect(screen.getByRole('status')).toHaveTextContent('107 products match');
  });

  it('toggles a category without discarding the others', async () => {
    const { onChange } = renderPanel({ categoryIds: ['phones'] });
    await openCategories();

    await userEvent.click(screen.getByRole('checkbox', { name: /Laptops/ }));

    expect(onChange).toHaveBeenCalledWith({ categoryIds: ['phones', 'laptops'] });
  });

  it('lists every applied filter as a chip that removes only itself', async () => {
    const { onChange } = renderPanel({ categoryIds: ['laptops', 'phones'], inStock: true });

    // 2 categories + in-stock and the panel header repeats the total.
    expect(screen.getByText('3')).toBeInTheDocument();

    await userEvent.click(screen.getByRole('button', { name: 'Remove filter: Laptops' }));

    expect(onChange).toHaveBeenCalledWith({ categoryIds: ['phones'] });
  });

  it('clears every filter at once', async () => {
    const { onChange } = renderPanel({ inStock: true });

    await userEvent.click(screen.getByRole('button', { name: 'Clear all' }));

    expect(onChange).toHaveBeenCalledWith({
      categoryIds: [],
      inStock: false,
      search: undefined,
      minPrice: undefined,
      maxPrice: undefined,
    });
  });

  it('holds the price range until it is applied and straightens a reversed one', async () => {
    const { onChange } = renderPanel();

    await userEvent.type(screen.getByLabelText('Min'), '900');
    await userEvent.type(screen.getByLabelText('Max'), '100');

    // Typing must not have queried anything yet: a request per keystroke would reset the
    // shopper to page 1 halfway through the number.
    expect(onChange).not.toHaveBeenCalled();

    await userEvent.click(screen.getByRole('button', { name: 'Apply price' }));

    expect(onChange).toHaveBeenCalledWith({ minPrice: 100, maxPrice: 900 });
  });

  it('is collapsed behind a labelled toggle on small screens', async () => {
    renderPanel();

    const toggle = screen.getByRole('button', { name: /Show/ });
    expect(toggle).toHaveAttribute('aria-expanded', 'false');

    await userEvent.click(toggle);

    expect(screen.getByRole('button', { name: /Hide/ })).toHaveAttribute('aria-expanded', 'true');
  });

  it('ships folded and unfolds and refolds on the header', async () => {
    renderPanel();

    const toggle = screen.getByRole('button', { name: /^Category/ });
    expect(toggle).toHaveAttribute('aria-expanded', 'false');

    // Still in the DOM, so the search term and "show all" state survive a fold - but out of
    // the accessibility tree, which is what `toBeVisible` checks.
    expect(screen.getByRole('checkbox', { name: /Laptops/, hidden: true })).not.toBeVisible();

    await userEvent.click(toggle);

    expect(toggle).toHaveAttribute('aria-expanded', 'true');
    expect(screen.getByRole('checkbox', { name: /Laptops/ })).toBeVisible();

    await userEvent.click(toggle);

    expect(toggle).toHaveAttribute('aria-expanded', 'false');
    expect(screen.getByRole('checkbox', { name: /Laptops/, hidden: true })).not.toBeVisible();
  });

  /**
   * Folded, the section header is the only thing left in the rail that attributes an active
   * filter to *this* section - the chips at the top do not say where they came from.
   */
  it('counts the selected categories on the header', async () => {
    renderPanel({ categoryIds: ['laptops', 'phones'] });

    const toggle = screen.getByRole('button', { name: /^Category/ });

    expect(toggle).toHaveTextContent('2');

    await userEvent.click(toggle);

    expect(toggle).toHaveTextContent('2');
  });

  it('shows a skeleton rather than an empty list while the tree loads', () => {
    render(
      <FilterPanel categories={[]} isLoading query={BASE} onChange={() => {}} />,
    );

    expect(screen.getByLabelText('Loading categories')).toBeInTheDocument();
  });

  /**
   * A failed request and an empty catalogue look identical from the outside, so the panel
   * has to distinguish them rather than describing both as "No categories yet."
   */
  it('says the categories failed to load rather than claiming there are none', async () => {
    const onRetryCategories = vi.fn();

    render(
      <FilterPanel
        categories={[]}
        isLoading={false}
        isError
        query={BASE}
        onChange={() => {}}
        onRetryCategories={onRetryCategories}
      />,
    );

    // Unfolded without being asked: the section is folded by default and a failure the
    // shopper cannot see is the same silence the message exists to break.
    expect(screen.getByRole('button', { name: /^Category/ })).toHaveAttribute('aria-expanded', 'true');

    expect(screen.queryByText('No categories yet.')).not.toBeInTheDocument();
    expect(screen.getByRole('alert')).toHaveTextContent('Categories could not be loaded.');

    await userEvent.click(screen.getByRole('button', { name: 'Try again' }));

    expect(onRetryCategories).toHaveBeenCalledOnce();
  });

  it('has no axe violations', async () => {
    const { container } = renderPanel({ categoryIds: ['laptops'] });

    const results = await axe.run(container, { rules: { 'color-contrast': { enabled: false } } });

    expect(
      results.violations,
      results.violations.map((v) => `${v.id}: ${v.help}`).join('\n'),
    ).toEqual([]);
  });

  it('filters the category list once there are enough options to warrant a search box', async () => {
    const many: CategoryNode[] = [
      root(
        'all',
        'All',
        Array.from({ length: 12 }, (_, index) => leaf(`c${index}`, `Category ${index}`, index)),
      ),
    ];

    render(<FilterPanel categories={many} isLoading={false} query={BASE} onChange={() => {}} />);
    await openCategories();

    await userEvent.type(screen.getByLabelText('Find a category'), 'Category 1');

    const group = screen.getByText('All').parentElement as HTMLElement;

    // "Category 1", "Category 10" and "Category 11" - not the other nine.
    expect(within(group).getAllByRole('checkbox')).toHaveLength(3);
  });
});
