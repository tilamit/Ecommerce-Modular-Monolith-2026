import { useId, type InputHTMLAttributes, type SelectHTMLAttributes, type ReactNode } from 'react';
import { cn } from '../../lib/cn';

interface FieldShellProps {
  label: string;
  error?: string;
  hint?: string;
  required?: boolean;
  children: (ids: { inputId: string; describedBy: string | undefined }) => ReactNode;
}

/**
 * Label, hint and error wiring for a form control.
 *
 * Every input is labelled and every message is associated via aria-describedby
 * (spec §11.4). A red border alone communicates nothing to a screen reader and nothing
 * at all to someone who cannot distinguish the colour.
 */
const FieldShell = ({ label, error, hint, required, children }: FieldShellProps) => {
  const inputId = useId();
  const errorId = `${inputId}-error`;
  const hintId = `${inputId}-hint`;

  const describedBy =
    [error !== undefined ? errorId : null, hint !== undefined ? hintId : null].filter(Boolean).join(' ') ||
    undefined;

  return (
    <div className="flex flex-col gap-1.5">
      <label htmlFor={inputId} className="text-sm font-medium text-content">
        {label}
        {required === true && (
          <span className="ml-0.5 text-danger" aria-hidden="true">
            *
          </span>
        )}
      </label>

      {children({ inputId, describedBy })}

      {hint !== undefined && error === undefined && (
        <p id={hintId} className="text-xs text-content-muted">
          {hint}
        </p>
      )}

      {error !== undefined && (
        // role="alert" so the message is announced when it appears after a failed submit.
        <p id={errorId} role="alert" className="text-xs text-danger">
          {error}
        </p>
      )}
    </div>
  );
};

const controlClasses = (hasError: boolean) =>
  cn(
    'h-10 w-full rounded-lg border bg-surface px-3 text-sm text-content',
    'placeholder:text-content-muted',
    'disabled:cursor-not-allowed disabled:opacity-60',
    hasError ? 'border-danger' : 'border-border-subtle',
  );

type InputProps = Omit<InputHTMLAttributes<HTMLInputElement>, 'id'> & {
  label: string;
  error?: string;
  hint?: string;
};

export const Input = ({ label, error, hint, required, className, ...props }: InputProps) => (
  <FieldShell label={label} error={error} hint={hint} required={required}>
    {({ inputId, describedBy }) => (
      <input
        id={inputId}
        aria-invalid={error !== undefined}
        aria-describedby={describedBy}
        required={required}
        className={cn(controlClasses(error !== undefined), className)}
        {...props}
      />
    )}
  </FieldShell>
);

type SelectProps = Omit<SelectHTMLAttributes<HTMLSelectElement>, 'id'> & {
  label: string;
  error?: string;
  hint?: string;
};

export const Select = ({ label, error, hint, required, className, children, ...props }: SelectProps) => (
  <FieldShell label={label} error={error} hint={hint} required={required}>
    {({ inputId, describedBy }) => (
      <select
        id={inputId}
        aria-invalid={error !== undefined}
        aria-describedby={describedBy}
        required={required}
        className={cn(controlClasses(error !== undefined), className)}
        {...props}
      >
        {children}
      </select>
    )}
  </FieldShell>
);
