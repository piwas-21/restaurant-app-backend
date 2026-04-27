'use client';

import React, { useState, useEffect } from 'react';
import { useTranslation } from 'react-i18next';
import { fidelityPointsService } from '@/services/fidelityPointsService';
import type { FidelityPointsTransaction } from '@/types/fidelity';
import styles from './PointsHistoryModal.module.css';

interface PointsHistoryModalProps {
  isOpen: boolean;
  onClose: () => void;
}

export default function PointsHistoryModal({ isOpen, onClose }: PointsHistoryModalProps) {
  const { t } = useTranslation();
  const [transactions, setTransactions] = useState<FidelityPointsTransaction[]>([]);
  const [loading, setLoading] = useState(true);
  const [error, setError] = useState<string | null>(null);
  const [page, setPage] = useState(1);
  const [hasMore, setHasMore] = useState(true);
  const pageSize = 20;

  const loadTransactions = async () => {
    try {
      setLoading(true);
      setError(null);
      const data = await fidelityPointsService.getHistory({ page, pageSize });

      if (page === 1) {
        setTransactions(data);
      } else {
        setTransactions((prev) => [...prev, ...data]);
      }

      // Check if there are more pages
      setHasMore(data.length === pageSize);
    } catch (err) {
      console.error('Error loading points history:', err);
      setError(t('error_loading_history', 'Failed to load points history'));
    } finally {
      setLoading(false);
    }
  };

  useEffect(() => {
    if (isOpen) {
      loadTransactions();
    }
    // eslint-disable-next-line react-hooks/exhaustive-deps
  }, [isOpen, page]);

  const loadMore = () => {
    setPage((prev) => prev + 1);
  };

  const formatDate = (dateString: string) => {
    const date = new Date(dateString);
    return new Intl.DateTimeFormat('en-US', {
      year: 'numeric',
      month: 'short',
      day: 'numeric',
      hour: '2-digit',
      minute: '2-digit',
    }).format(date);
  };

  const getTransactionIcon = (type: string) => {
    switch (type) {
      case 'Earned':
        return '↑';
      case 'Redeemed':
        return '↓';
      case 'AdminAdjustment':
        return '⚙';
      case 'Expired':
        return '⏱';
      case 'Refunded':
        return '↩';
      default:
        return '•';
    }
  };

  if (!isOpen) {
    return null;
  }

  return (
    <div className={styles.modalOverlay} onClick={onClose}>
      <div className={styles.modal} onClick={(e) => e.stopPropagation()}>
        <div className={styles.modalHeader}>
          <h2 className={styles.modalTitle}>{t('points_history', 'Points History')}</h2>
          <button className={styles.closeButton} onClick={onClose} aria-label={t('close', 'Close')}>
            ×
          </button>
        </div>

        <div className={styles.modalBody}>
          {loading && page === 1 ? (
            <div className={styles.loading}>
              <p>{t('loading', 'Loading...')}</p>
            </div>
          ) : error ? (
            <div className={styles.error}>
              <p>{error}</p>
              <button onClick={loadTransactions} className={styles.retryButton}>
                {t('retry', 'Retry')}
              </button>
            </div>
          ) : transactions.length === 0 ? (
            <div className={styles.emptyState}>
              <p>{t('no_transactions', 'No transactions yet')}</p>
              <p className={styles.emptyStateSubtext}>
                {t('earn_points_message', 'Start earning points by placing orders!')}
              </p>
            </div>
          ) : (
            <>
              <div className={styles.transactionsList}>
                {transactions.map((transaction) => (
                  <div
                    key={transaction.id}
                    className={`${styles.transactionItem} ${
                      transaction.points > 0 ? styles.positive : styles.negative
                    }`}
                  >
                    <div className={styles.transactionIcon}>{getTransactionIcon(transaction.transactionType)}</div>
                    <div className={styles.transactionDetails}>
                      <div className={styles.transactionHeader}>
                        <span className={styles.transactionType}>
                          {fidelityPointsService.getTransactionTypeLabel(transaction.transactionType)}
                        </span>
                        <span
                          className={`${styles.transactionPoints} ${
                            transaction.points > 0 ? styles.pointsPositive : styles.pointsNegative
                          }`}
                        >
                          {transaction.points > 0 ? '+' : ''}
                          {transaction.points.toLocaleString()}
                        </span>
                      </div>
                      <p className={styles.transactionDescription}>{transaction.description}</p>
                      {transaction.orderTotal && (
                        <p className={styles.transactionOrderTotal}>
                          {t('order_total', 'Order Total')}: ${transaction.orderTotal.toFixed(2)}
                        </p>
                      )}
                      <p className={styles.transactionDate}>{formatDate(transaction.createdAt)}</p>
                    </div>
                  </div>
                ))}
              </div>

              {hasMore && (
                <div className={styles.loadMoreContainer}>
                  <button onClick={loadMore} disabled={loading} className={styles.loadMoreButton}>
                    {loading ? t('loading', 'Loading...') : t('load_more', 'Load More')}
                  </button>
                </div>
              )}
            </>
          )}
        </div>
      </div>
    </div>
  );
}
