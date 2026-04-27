'use client';

import { useState, useCallback } from 'react';
import { useTranslation } from 'react-i18next';
import { OrderDto } from '@/types/order';
import { MessageSquare, Plus } from 'lucide-react';

interface OrderNotesProps {
  order: OrderDto | null;
  onAddNote?: (note: string) => Promise<void>;
  isLoading?: boolean;
}

/**
 * Component for displaying and managing order modification notes
 * Used as a workaround for backend limitations (order type changes, item modifications)
 */
export default function OrderNotes({ order, onAddNote, isLoading = false }: OrderNotesProps) {
  const { t } = useTranslation();
  const [noteText, setNoteText] = useState<string>('');
  const [isExpanded, setIsExpanded] = useState(false);
  const [error, setError] = useState<string | null>(null);

  const handleAddNote = useCallback(async () => {
    if (!noteText.trim()) {
      setError(t('cashier.note_required') || 'Please enter a note');
      return;
    }

    if (!onAddNote) {
      // Fallback: just display the note locally
      setNoteText('');
      setIsExpanded(false);
      return;
    }

    try {
      await onAddNote(noteText);
      setNoteText('');
      setError(null);
      setIsExpanded(false);
    } catch (err) {
      setError((err as Error).message || t('cashier.failed_to_add_note') || 'Failed to add note');
    }
  }, [noteText, onAddNote, t]);

  if (!order) return null;

  return (
    <div className="notes-section">
      <div className="notes-header">
        <button
          className="notes-toggle"
          onClick={() => setIsExpanded(!isExpanded)}
          title={t('cashier.toggle_notes') || 'Toggle notes'}
        >
          <MessageSquare size={18} />
          <span>{t('cashier.order_notes') || 'Order Notes'}</span>
          <span className="notes-count">{(order.notes ? 1 : 0) + (noteText ? 1 : 0)}</span>
        </button>
      </div>

      {isExpanded && (
        <div className="notes-content">
          {/* Existing Notes */}
          {order.notes && (
            <div className="notes-list">
              <h4>{t('cashier.existing_notes') || 'Existing Notes'}</h4>
              <div className="note-item">
                <div className="note-text">{order.notes}</div>
              </div>
            </div>
          )}

          {/* Add Note Form */}
          <div className="note-form">
            <h4>{t('cashier.add_modification_note') || 'Add Modification Note'}</h4>
            <p className="note-hint">
              {t('cashier.note_hint') ||
                'Use this to document requested changes like order type changes, item modifications, or special requests that cannot be applied directly.'}
            </p>

            <textarea
              className="note-input"
              placeholder={
                t('cashier.note_placeholder') ||
                'e.g., Customer requested dine-in instead of takeaway. Tax will need to be recalculated manually.'
              }
              value={noteText}
              onChange={(e) => {
                setNoteText(e.target.value);
                setError(null);
              }}
              disabled={isLoading}
              rows={3}
              maxLength={500}
            />

            <div className="note-footer">
              <span className="char-count">
                {noteText.length}/500 {t('cashier.characters') || 'characters'}
              </span>
              <button
                className="btn btn-primary btn-sm"
                onClick={handleAddNote}
                disabled={!noteText.trim() || isLoading}
                title={t('cashier.add_note') || 'Add note'}
              >
                <Plus size={16} />
                {isLoading ? t('common.loading') || 'Loading...' : t('cashier.add_note') || 'Add Note'}
              </button>
            </div>

            {error && <div className="alert alert-error">{error}</div>}
          </div>

          {/* Info Box */}
          <div className="alert alert-info">
            <strong>{t('cashier.notes_info_title') || 'About Notes'}:</strong>
            <p>
              {t('cashier.notes_info') ||
                'Use notes to document requested changes that cannot be applied directly through this interface. This is especially useful for order type changes, item modifications, or special requests.'}
            </p>
          </div>
        </div>
      )}
    </div>
  );
}
