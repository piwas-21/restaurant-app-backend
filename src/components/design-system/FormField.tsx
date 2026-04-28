import type { ReactNode } from 'react';
import styles from './FormField.module.css';

export interface FormFieldProps {
  /** Visible label rendered above the input. */
  label: string;
  /** Optional error message; renders below the input when non-empty. */
  error?: string;
  /** Hidden visually but read by screen readers when no label is desired. */
  srOnlyLabel?: boolean;
  /** The input element (or anything that takes a label). */
  children: ReactNode;
  /** Optional id passed through to the label's `htmlFor`. */
  htmlFor?: string;
  /** Extra className on the wrapping element. */
  className?: string;
}

/**
 * Standard label + input + error grouping (CLAUDE.md frontend §5 rule 3).
 *
 * Usage:
 * ```tsx
 * <FormField label={t('email')} error={errors.email?.message}>
 *   <input type="email" {...register('email')} />
 * </FormField>
 * ```
 *
 * The component intentionally wraps `<label>` so that clicking the
 * label focuses the first interactive child without needing an `id`.
 * Pass `htmlFor` only when the input is not a direct child (rare).
 */
export default function FormField({ label, error, srOnlyLabel, children, htmlFor, className }: FormFieldProps) {
  return (
    <label className={`${styles.field}${className ? ` ${className}` : ''}`} htmlFor={htmlFor}>
      <span className={srOnlyLabel ? styles.srOnly : styles.label}>{label}</span>
      {children}
      {error && (
        <span className={styles.error} role="alert">
          {error}
        </span>
      )}
    </label>
  );
}
