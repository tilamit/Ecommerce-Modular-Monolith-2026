import { describe, expect, it } from 'vitest';
import { render, screen, within } from '@testing-library/react';
import { AuditFieldValue } from './AuditFieldValue';

describe('AuditFieldValue', () => {
  it('renders a scalar as text and a missing value as a dash', () => {
    const { rerender } = render(<AuditFieldValue value={19.99} other={24.99} side="before" />);
    expect(screen.getByText('19.99')).toBeInTheDocument();

    rerender(<AuditFieldValue value={undefined} other="x" side="before" />);
    expect(screen.getByText('-')).toBeInTheDocument();
  });

  it('lists the permissions a role lost on the before side', () => {
    render(
      <AuditFieldValue
        value={['audit.read', 'cart.own', 'dashboard.admin']}
        other={['cart.own', 'dashboard.admin', 'orders.manage']}
        side="before"
      />,
    );

    const items = within(screen.getByRole('list')).getAllByRole('listitem');

    expect(items.map((item) => item.textContent)).toEqual(['Removed: audit.read', 'cart.own', 'dashboard.admin']);
    expect(items[0]).toHaveClass('line-through');
    expect(items[1]).not.toHaveClass('line-through');
  });

  it('lists the permissions a role gained on the after side', () => {
    render(
      <AuditFieldValue
        value={['cart.own', 'dashboard.admin', 'orders.manage']}
        other={['audit.read', 'cart.own', 'dashboard.admin']}
        side="after"
      />,
    );

    const items = within(screen.getByRole('list')).getAllByRole('listitem');

    expect(items.map((item) => item.textContent)).toEqual(['cart.own', 'dashboard.admin', 'Added: orders.manage']);
  });

  it('says None for an empty list, such as a new role with no menus yet', () => {
    render(<AuditFieldValue value={[]} other={['Dashboard']} side="before" />);

    expect(screen.getByText('None')).toBeInTheDocument();
  });
});
