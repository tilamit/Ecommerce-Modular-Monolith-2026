import { useEffect, useRef, type ReactNode } from 'react';
import { X } from 'lucide-react';
import { Button } from './Button';
import { cn } from '../../lib/cn';

interface ModalProps {
  open: boolean;
  title: string;
  description?: string;
  onClose: () => void;
  children: ReactNode;
  footer?: ReactNode;
  /** `lg` for forms with two columns of fields; the default suits a confirmation. */
  size?: 'sm' | 'lg';
}

/**
 * A dialog built on the native `<dialog>` element.
 *
 * Using the platform element rather than a div with `role="dialog"` is what supplies focus
 * trapping, the top layer, the backdrop and Escape to close - all of which are easy to get
 * subtly wrong by hand. The one thing it does not give us is a close on backdrop click, so
 * that is wired up from the click target below.
 */
export const Modal = ({ open, title, description, onClose, children, footer, size = 'sm' }: ModalProps) => {
  const ref = useRef<HTMLDialogElement>(null);

  useEffect(() => {
    const dialog = ref.current;

    if (dialog === null) {
      return;
    }

    if (open && !dialog.open) {
      dialog.showModal();
    } else if (!open && dialog.open) {
      dialog.close();
    }
  }, [open]);

  return (
    <dialog
      ref={ref}
      // `cancel` is Escape. Prevented so closing always goes through the caller's handler,
      // which is what keeps the parent's `open` state from drifting out of step.
      onCancel={(event) => {
        event.preventDefault();
        onClose();
      }}
      onClick={(event) => {
        // The dialog element itself is the backdrop; anything inside stops here first.
        if (event.target === ref.current) {
          onClose();
        }
      }}
      className={cn(
        // `m-auto` is what centres this. A modal <dialog> is centred by the user agent with
        // `inset: 0` plus `margin: auto` and Tailwind's preflight zeroes the margin on every
        // element - so without this the dialog pins to the top-left corner of the viewport.
        'm-auto w-[min(100vw-2rem,var(--modal-width))]',
        'rounded-card border border-border-subtle bg-surface p-0 text-content',
        'backdrop:bg-black/40',
        size === 'lg' ? '[--modal-width:56rem]' : '[--modal-width:32rem]',
      )}
    >
      <form method="dialog" className="contents">
        <div className="flex items-start gap-3 border-b border-border-subtle px-5 py-4">
          <div className="min-w-0">
            <h2 className="text-base font-semibold text-content">{title}</h2>
            {description !== undefined && (
              <p className="mt-0.5 text-sm text-content-muted">{description}</p>
            )}
          </div>

          <button
            type="button"
            onClick={onClose}
            aria-label="Close"
            className="ml-auto rounded-lg p-1.5 text-content-muted transition-colors hover:bg-surface-sunken hover:text-content"
          >
            <X className="size-5" aria-hidden="true" />
          </button>
        </div>

        <div className="max-h-[70dvh] overflow-y-auto px-5 py-4">{children}</div>

        {footer !== undefined && (
          <div className="flex items-center justify-end gap-2 border-t border-border-subtle px-5 py-3">
            {footer}
          </div>
        )}
      </form>
    </dialog>
  );
};

interface ConfirmModalProps {
  open: boolean;
  title: string;
  description: string;
  confirmLabel?: string;
  isPending?: boolean;
  onConfirm: () => void;
  onClose: () => void;
}

/** A delete confirmation. Separate so no screen has to reinvent the wording or the tone. */
export const ConfirmModal = ({
  open,
  title,
  description,
  confirmLabel = 'Delete',
  isPending = false,
  onConfirm,
  onClose,
}: ConfirmModalProps) => (
  <Modal
    open={open}
    title={title}
    onClose={onClose}
    footer={
      <>
        <Button variant="secondary" size="sm" onClick={onClose}>
          Cancel
        </Button>
        <Button variant="danger" size="sm" onClick={onConfirm} disabled={isPending}>
          {isPending ? 'Working…' : confirmLabel}
        </Button>
      </>
    }
  >
    <p className="text-sm text-content-muted">{description}</p>
  </Modal>
);
