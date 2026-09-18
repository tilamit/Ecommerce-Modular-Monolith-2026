import type { ReactElement } from 'react';
import { describe, expect, it } from 'vitest';
import { render, screen } from '@testing-library/react';
import { createMemoryRouter, RouterProvider } from 'react-router';
import { NewTabLink } from './NewTabLink';

const renderLink = (ui: ReactElement) => {
  const router = createMemoryRouter([{ path: '/cart', element: ui }], { initialEntries: ['/cart'] });

  return render(<RouterProvider router={router} />);
};

describe('NewTabLink', () => {
  it('points at the target and opens a new tab', () => {
    renderLink(<NewTabLink to="/products/8e2f" label="Blue Mug" />);

    const link = screen.getByRole('link', { name: /Blue Mug/ });

    expect(link).toHaveAttribute('href', '/products/8e2f');
    expect(link).toHaveAttribute('target', '_blank');
  });

  /**
   * Without `noopener` the opened page holds a `window.opener` handle and can navigate this
   * tab somewhere else while the reader is looking at the other one.
   */
  it('denies the opened page a handle back to this one', () => {
    renderLink(<NewTabLink to="/products/8e2f" label="Blue Mug" />);

    const rel = screen.getByRole('link', { name: /Blue Mug/ }).getAttribute('rel') ?? '';

    expect(rel).toContain('noopener');
    expect(rel).toContain('noreferrer');
  });

  it('announces the new tab, after the visible text rather than instead of it', () => {
    renderLink(<NewTabLink to="/products/8e2f" label="Blue Mug" />);

    // Voice control matches on the visible words, so the name has to start with them.
    expect(screen.getByRole('link', { name: 'Blue Mug (opens in a new tab)' })).toBeInTheDocument();
    expect(screen.getByText('Blue Mug')).toBeInTheDocument();
  });

  it('keeps the icon out of the accessible name', () => {
    const { container } = renderLink(<NewTabLink to="/products/8e2f" label="Blue Mug" />);

    const icon = container.querySelector('svg');

    expect(icon).not.toBeNull();
    expect(icon).toHaveAttribute('aria-hidden', 'true');
  });

  it('can drop the icon where the layout already signals it', () => {
    const { container } = renderLink(
      <NewTabLink to="/products/8e2f" label="Blue Mug" showIcon={false} />,
    );

    expect(container.querySelector('svg')).toBeNull();
    expect(screen.getByRole('link', { name: 'Blue Mug (opens in a new tab)' })).toBeInTheDocument();
  });

  it('renders custom content in place of the label', () => {
    renderLink(
      <NewTabLink to="/products/8e2f" label="Blue Mug">
        <span>Blue Mug, 350ml</span>
      </NewTabLink>,
    );

    expect(screen.getByText('Blue Mug, 350ml')).toBeInTheDocument();
    expect(screen.getByRole('link', { name: 'Blue Mug (opens in a new tab)' })).toBeInTheDocument();
  });
});
