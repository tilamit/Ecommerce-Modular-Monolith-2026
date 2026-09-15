import { describe, expect, it, vi, beforeAll } from 'vitest';
import { render, screen } from '@testing-library/react';
import { Modal } from './Modal';

/**
 * jsdom implements `<dialog>` but not the top layer, so `showModal` has to be stubbed for
 * the component to mount at all. That also means none of this asserts real layout - see the
 * centring test for what it does and does not prove.
 */
beforeAll(() => {
  HTMLDialogElement.prototype.showModal = vi.fn(function showModal(this: HTMLDialogElement) {
    this.open = true;
  });

  HTMLDialogElement.prototype.close = vi.fn(function close(this: HTMLDialogElement) {
    this.open = false;
  });
});

const renderModal = (props: Partial<Parameters<typeof Modal>[0]> = {}) =>
  render(
    <Modal open title="Edit product" onClose={vi.fn()} {...props}>
      <p>body</p>
    </Modal>,
  );

describe('Modal', () => {
  it('renders its title and content when open', () => {
    renderModal();

    expect(screen.getByRole('heading', { name: 'Edit product' })).toBeInTheDocument();
    expect(screen.getByText('body')).toBeInTheDocument();
  });

  /**
   * A modal `<dialog>` is centred by the user agent with `inset: 0` and `margin: auto`, and
   * Tailwind's preflight sets `margin: 0` on every element - which pinned the dialog to the
   * top-left corner until `m-auto` put the margin back.
   *
   * jsdom has no layout, so this asserts the class rather than the position. It is a guard
   * against the class being dropped in a refactor, not proof of where the box lands.
   */
  it('keeps the auto margin that centres it against the preflight reset', () => {
    const { container } = renderModal();

    expect(container.querySelector('dialog')).toHaveClass('m-auto');
  });

  it('calls onClose from the close button', async () => {
    const onClose = vi.fn();
    const { default: userEvent } = await import('@testing-library/user-event');

    renderModal({ onClose });
    await userEvent.click(screen.getByRole('button', { name: 'Close' }));

    expect(onClose).toHaveBeenCalledOnce();
  });
});
