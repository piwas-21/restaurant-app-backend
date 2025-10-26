import React from 'react';
import { X, Keyboard } from 'lucide-react';
import { KeyboardShortcut, formatShortcut } from '@/hooks/useKeyboardShortcuts';
import styles from './KeyboardShortcutsModal.module.css';

interface KeyboardShortcutsModalProps {
  shortcuts: KeyboardShortcut[];
  onClose: () => void;
}

export default function KeyboardShortcutsModal({ shortcuts, onClose }: KeyboardShortcutsModalProps) {
  return (
    <div className={styles.modal} onClick={onClose}>
      <div className={styles.modalContent} onClick={(e) => e.stopPropagation()}>
        <div className={styles.modalHeader}>
          <div className={styles.titleRow}>
            <Keyboard size={24} />
            <h2>Keyboard Shortcuts</h2>
          </div>
          <button
            className={styles.closeButton}
            onClick={onClose}
            aria-label="Close"
          >
            <X size={24} />
          </button>
        </div>

        <div className={styles.modalBody}>
          <p className={styles.description}>
            Use these keyboard shortcuts to navigate quickly:
          </p>

          <div className={styles.shortcutsList}>
            {shortcuts.map((shortcut, index) => (
              <div key={index} className={styles.shortcutRow}>
                <kbd className={styles.shortcutKey}>{formatShortcut(shortcut)}</kbd>
                <span className={styles.shortcutDescription}>{shortcut.description}</span>
              </div>
            ))}
          </div>
        </div>

        <div className={styles.modalFooter}>
          <button onClick={onClose} className={styles.closeButtonPrimary}>
            Got it!
          </button>
        </div>
      </div>
    </div>
  );
}
