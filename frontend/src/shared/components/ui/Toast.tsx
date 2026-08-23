import { createContext, useCallback, useContext, useMemo, useState, type ReactNode } from 'react';
import { X } from 'lucide-react';
import { cn } from '../../lib/cn';

type ToastTone = 'info' | 'success' | 'error';

interface Toast {
  id: string;
  message: string;
  tone: ToastTone;
  /** Optional undo, used by add-to-cart (spec §11.4). */
  action?: { label: string; onClick: () => void };
}

interface ToastContextValue {
  show: (toast: Omit<Toast, 'id'>) => void;
}

const ToastContext = createContext<ToastContextValue | null>(null);

const AUTO_DISMISS_MS = 5_000;

const tones: Record<ToastTone, string> = {
  info: 'border-border-subtle bg-surface-raised',
  success: 'border-success/40 bg-success/10',
  error: 'border-danger/40 bg-danger/10',
};

export const ToastProvider = ({ children }: { children: ReactNode }) => {
  const [toasts, setToasts] = useState<Toast[]>([]);

  const dismiss = useCallback((id: string) => {
    setToasts((current) => current.filter((t) => t.id !== id));
  }, []);

  const show = useCallback(
    (toast: Omit<Toast, 'id'>) => {
      const id = crypto.randomUUID();

      setToasts((current) => [...current, { ...toast, id }]);
      setTimeout(() => dismiss(id), AUTO_DISMISS_MS);
    },
    [dismiss],
  );

  const value = useMemo(() => ({ show }), [show]);

  return (
    <ToastContext.Provider value={value}>
      {children}

      {/*
        aria-live="polite" so toasts are announced without interrupting (spec §11.4).
        Errors use assertive via role="alert" on the individual toast instead.
      */}
      <div
        aria-live="polite"
        aria-atomic="false"
        className="pointer-events-none fixed inset-x-0 bottom-0 z-50 flex flex-col items-center gap-2 p-4 sm:items-end"
      >
        {toasts.map((toast) => (
          <div
            key={toast.id}
            role={toast.tone === 'error' ? 'alert' : 'status'}
            className={cn(
              'pointer-events-auto flex w-full max-w-sm items-center gap-3 rounded-lg border px-4 py-3 shadow-lg',
              tones[toast.tone],
            )}
          >
            <p className="flex-1 text-sm text-content">{toast.message}</p>

            {toast.action !== undefined && (
              <button
                type="button"
                onClick={() => {
                  toast.action?.onClick();
                  dismiss(toast.id);
                }}
                className="text-sm font-medium text-brand-600 hover:underline"
              >
                {toast.action.label}
              </button>
            )}

            <button
              type="button"
              onClick={() => dismiss(toast.id)}
              aria-label="Dismiss notification"
              className="text-content-muted hover:text-content"
            >
              <X className="size-4" aria-hidden="true" />
            </button>
          </div>
        ))}
      </div>
    </ToastContext.Provider>
  );
};

export const useToast = (): ToastContextValue => {
  const context = useContext(ToastContext);

  if (context === null) {
    throw new Error('useToast must be used within a ToastProvider.');
  }

  return context;
};
