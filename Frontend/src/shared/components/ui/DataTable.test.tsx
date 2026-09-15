import { describe, expect, it } from 'vitest';
import { render, screen, within } from '@testing-library/react';
import { DataTable, type Column } from './DataTable';

interface Row {
  id: string;
  name: string;
  role: string;
}

const ROWS: Row[] = [
  { id: '1', name: 'Ada', role: 'Admin' },
  { id: '2', name: 'Grace', role: 'Customer' },
  { id: '3', name: 'Alan', role: 'Customer' },
];

const COLUMNS: Column<Row>[] = [
  { key: 'name', header: 'Name', render: (r) => r.name },
  { key: 'role', header: 'Role', render: (r) => r.role },
];

const renderTable = (renderExpanded?: (row: Row) => React.ReactNode) =>
  render(
    <DataTable
      caption="People"
      columns={COLUMNS}
      rows={ROWS}
      rowKey={(row) => row.id}
      renderExpanded={renderExpanded}
    />,
  );

describe('DataTable expanded rows', () => {
  it('renders nothing extra when no detail is supplied', () => {
    renderTable();

    // Header plus one row each and no detail cell.
    expect(screen.getAllByRole('row')).toHaveLength(ROWS.length + 1);
  });

  /**
   * The point of the feature. The audit screen used to render the open entry's detail after
   * the whole table, so opening a row near the top of a twenty-five row page put its detail
   * below the fold, a scroll away from the button that opened it.
   */
  it('puts the detail in the row directly beneath the one it belongs to', () => {
    renderTable((row) => (row.id === '2' ? <p>detail for {row.name}</p> : null));

    const rows = screen.getAllByRole('row');

    // header, Ada, Grace, Grace's detail, Alan
    expect(rows).toHaveLength(ROWS.length + 2);
    expect(within(rows[2]).getByText('Grace')).toBeInTheDocument();
    expect(within(rows[3]).getByText(/detail for Grace/)).toBeInTheDocument();
  });

  /**
   * A detail cell that does not span every column silently adds one to the table, which
   * throws the header alignment out for every row below it.
   */
  it('spans the full width of the table', () => {
    renderTable((row) => (row.id === '1' ? <p>detail</p> : null));

    const cell = within(screen.getByRole('table')).getByText('detail').closest('td');

    expect(cell).toHaveAttribute('colspan', String(COLUMNS.length));
  });

  it('renders a detail for every row the caller opens', () => {
    renderTable(() => <p>always</p>);

    expect(within(screen.getByRole('table')).getAllByText('always')).toHaveLength(ROWS.length);
  });

  /**
   * Below `md` the same rows are a card list rather than a table and the detail has to
   * follow its card there too - otherwise the fix only holds on a desktop.
   */
  it('renders the detail inside its own card on the mobile list', () => {
    renderTable((row) => (row.id === '2' ? <p>detail for {row.name}</p> : null));

    const cards = screen.getAllByRole('listitem');

    expect(within(cards[1]).getByText(/detail for Grace/)).toBeInTheDocument();
    expect(within(cards[0]).queryByText(/detail for/)).not.toBeInTheDocument();
  });
});
