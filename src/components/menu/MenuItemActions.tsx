'use client';

import React from 'react';
import styles from './MenuItemActions.module.css';

type Props = {
  onAdd: () => void;
  onFeedback: () => void;
  addLabel: string;
  addAria: string;
  feedbackLabel: string;
  feedbackAria: string;
  onDetails?: () => void;
  detailsLabel?: string;
};

export default function MenuItemActions({
  onAdd,
  onFeedback: _onFeedback,
  addLabel,
  addAria,
  feedbackLabel: _feedbackLabel,
  feedbackAria: _feedbackAria,
  onDetails,
  detailsLabel,
}: Props) {
  return (
    <div className={styles.itemActions}>
      <button className={styles.addToOrderButton} onClick={onAdd} aria-label={addAria}>
        {addLabel}
      </button>
      {onDetails && (
        <button className={styles.viewDetailsButton} onClick={onDetails} aria-label={detailsLabel || 'Details'}>
          {detailsLabel || 'Details'}
        </button>
      )}
      {/* feedback feature will be implemented in the next release */}
      {/* <button className={styles.feedbackButton} onClick={onFeedback} aria-label={feedbackAria}>
        {feedbackLabel}
      </button> */}
    </div>
  );
}
