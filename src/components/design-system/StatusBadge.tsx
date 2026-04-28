import type { ReactNode } from 'react';
import styles from './StatusBadge.module.css';

export type StatusBadgeTone = 'success' | 'warning' | 'danger' | 'neutral' | 'info';

export interface StatusBadgeProps {
  tone?: StatusBadgeTone;
  children: ReactNode;
  className?: string;
}

/**
 * Standard status pill (CLAUDE.md frontend §5 rule 4). Tone maps to a
 * design-token background + foreground pair.
 *
 * Usage: `<StatusBadge tone="success">WhatsApp</StatusBadge>`.
 */
export default function StatusBadge({ tone = 'neutral', children, className }: StatusBadgeProps) {
  return <span className={`${styles.badge} ${styles[tone]}${className ? ` ${className}` : ''}`}>{children}</span>;
}
