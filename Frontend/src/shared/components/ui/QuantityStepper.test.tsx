import { describe, expect, it, vi } from 'vitest';
import { render, screen } from '@testing-library/react';
import userEvent from '@testing-library/user-event';
import { QuantityStepper } from './QuantityStepper';

/**
 * The control exists to make an invalid quantity unreachable, so these tests are mostly
 * about the bounds rather than the arithmetic.
 */
describe('QuantityStepper', () => {
  it('increments and decrements by one', async () => {
    const user = userEvent.setup();
    const onChange = vi.fn();

    render(<QuantityStepper value={2} onChange={onChange} max={10} label="Quantity" />);

    await user.click(screen.getByRole('button', { name: 'Increase quantity' }));
    expect(onChange).toHaveBeenCalledWith(3);

    await user.click(screen.getByRole('button', { name: 'Decrease quantity' }));
    expect(onChange).toHaveBeenCalledWith(1);
  });

  it('stops at the stock limit', async () => {
    const user = userEvent.setup();
    const onChange = vi.fn();

    render(<QuantityStepper value={4} onChange={onChange} max={4} label="Quantity" />);

    const increase = screen.getByRole('button', { name: 'Increase quantity' });

    expect(increase).toBeDisabled();
    await user.click(increase);
    expect(onChange).not.toHaveBeenCalled();
  });

  it('stops at the minimum', async () => {
    const user = userEvent.setup();
    const onChange = vi.fn();

    render(<QuantityStepper value={1} onChange={onChange} max={9} label="Quantity" />);

    const decrease = screen.getByRole('button', { name: 'Decrease quantity' });

    expect(decrease).toBeDisabled();
    await user.click(decrease);
    expect(onChange).not.toHaveBeenCalled();
  });

  it('allows zero when the caller asks for it, which is how a cart line is removed', async () => {
    const user = userEvent.setup();
    const onChange = vi.fn();

    render(<QuantityStepper value={1} onChange={onChange} min={0} max={9} label="Quantity" />);

    await user.click(screen.getByRole('button', { name: 'Decrease quantity' }));
    expect(onChange).toHaveBeenCalledWith(0);
  });

  it('displays a value above the maximum as the maximum, and cannot emit it', async () => {
    const user = userEvent.setup();
    const onChange = vi.fn();

    // A product page holding quantity 5 while the reader moves to a product with 2 in stock.
    render(<QuantityStepper value={5} onChange={onChange} max={2} label="Quantity" />);

    expect(screen.getByText('2')).toBeInTheDocument();

    await user.click(screen.getByRole('button', { name: 'Decrease quantity' }));
    expect(onChange).toHaveBeenCalledWith(1);
  });

  it('names both buttons for the item when asked, for a list of products', () => {
    render(
      <QuantityStepper
        value={1}
        onChange={vi.fn()}
        max={5}
        label="Quantity for Blue Mug"
        decreaseLabel="Decrease quantity of Blue Mug"
        increaseLabel="Increase quantity of Blue Mug"
      />,
    );

    expect(screen.getByRole('group', { name: 'Quantity for Blue Mug' })).toBeInTheDocument();
    expect(screen.getByRole('button', { name: 'Increase quantity of Blue Mug' })).toBeInTheDocument();
  });
});
