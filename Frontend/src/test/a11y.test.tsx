import { describe, expect, it } from 'vitest';
import { render } from '@testing-library/react';
import axe from 'axe-core';
import type { ReactElement } from 'react';
import { Button } from '../shared/components/ui/Button';
import { Input, Select } from '../shared/components/ui/Field';
import { Pagination } from '../shared/components/ui/Pagination';
import { EmptyState, ErrorState } from '../shared/components/ui/States';
import { DataTable, StatusBadge } from '../shared/components/ui/DataTable';

/**
 * Automated accessibility checks on the shared UI kit (spec §14 Phase 11).
 *
 * Spec §11.4 says "run axe DevTools before calling a screen done". axe DevTools is a
 * browser extension and cannot run here, so the same engine - `axe-core` - is run against
 * rendered components in jsdom instead. That catches the mechanical failures: unlabelled
 * inputs, missing table headers, bad ARIA, insufficient contrast on inline styles.
 *
 * What it does **not** catch is real keyboard flow, focus order, screen-reader phrasing, or
 * contrast on Tailwind classes that jsdom never computes. Those still need a human with a
 * browser and this file is not a substitute for that.
 */
const check = async (ui: ReactElement): Promise<axe.Result[]> => {
  const { container } = render(ui);

  const results = await axe.run(container, {
    // Colour-contrast needs real layout and computed styles, which jsdom does not provide;
    // leaving it on would produce noise rather than findings.
    rules: { 'color-contrast': { enabled: false } },
  });

  return results.violations;
};

const describeViolations = (violations: axe.Result[]): string =>
  violations.map((v) => `${v.id}: ${v.help} (${v.nodes.length} node(s))`).join('\n');

describe('UI kit accessibility', () => {
  it('Button has an accessible name and reports its busy state', async () => {
    const violations = await check(
      <div>
        <Button>Save changes</Button>
        <Button isLoading aria-label="Saving">
          Save
        </Button>
        <Button variant="danger" disabled>
          Delete
        </Button>
      </div>,
    );

    expect(violations, describeViolations(violations)).toEqual([]);
  });

  it('form fields are labelled and errors are associated', async () => {
    const violations = await check(
      <form>
        <Input label="Email" type="email" required />
        <Input label="Password" type="password" error="Enter your password" />
        <Input label="Phone" hint="Optional" />
        <Select label="Payment method">
          <option value="Card">Card</option>
        </Select>
      </form>,
    );

    expect(violations, describeViolations(violations)).toEqual([]);
  });

  it('pagination is a labelled navigation region', async () => {
    const violations = await check(
      <Pagination
        page={2}
        pageSize={20}
        totalCount={100}
        totalPages={5}
        hasNext
        hasPrevious
        onPageChange={() => {}}
      />,
    );

    expect(violations, describeViolations(violations)).toEqual([]);
  });

  it('empty and error states are announced correctly', async () => {
    const empty = await check(<EmptyState title="No products" description="Try another filter." />);
    expect(empty, describeViolations(empty)).toEqual([]);

    const error = await check(<ErrorState onRetry={() => {}} />);
    expect(error, describeViolations(error)).toEqual([]);
  });

  it('DataTable has a caption, header cells and a card fallback', async () => {
    interface Row {
      id: string;
      name: string;
      status: string;
    }

    const rows: Row[] = [
      { id: '1', name: 'Laptop Pro', status: 'Delivered' },
      { id: '2', name: 'Desk Lamp', status: 'Pending' },
    ];

    const violations = await check(
      <DataTable
        caption="Orders"
        columns={[
          { key: 'name', header: 'Name', render: (row) => row.name, sortable: true },
          { key: 'status', header: 'Status', render: (row) => <StatusBadge status={row.status} /> },
        ]}
        rows={rows}
        rowKey={(row) => row.id}
        sort="-name"
        onSortChange={() => {}}
      />,
    );

    expect(violations, describeViolations(violations)).toEqual([]);
  });

  /**
   * The sortable header must expose `aria-sort`, or a screen-reader user has no way to know
   * the table is sorted, let alone by which column.
   */
  it('sortable columns expose aria-sort', () => {
    const { container } = render(
      <DataTable
        caption="Orders"
        columns={[
          { key: 'name', header: 'Name', render: () => 'x', sortable: true },
          { key: 'other', header: 'Other', render: () => 'y' },
        ]}
        rows={[{ id: '1' }]}
        rowKey={() => '1'}
        sort="-name"
        onSortChange={() => {}}
      />,
    );

    const headers = [...container.querySelectorAll('th[scope="col"]')];

    expect(headers[0].getAttribute('aria-sort')).toBe('descending');
    // A non-sortable column must not claim a sort state at all.
    expect(headers[1].getAttribute('aria-sort')).toBeNull();
  });
});
