"use client";

import React, { useState, useEffect } from 'react';
import { useTranslation } from 'react-i18next';
import { fidelityPointsService } from '@/services/fidelityPointsService';
import type { FidelityPointBalance } from '@/types/fidelity';
import PointsHistoryModal from './PointsHistoryModal';
import styles from './FidelityPointsSection.module.css';

export default function FidelityPointsSection() {
  const { t } = useTranslation();
  const [balance, setBalance] = useState<FidelityPointBalance | null>(null);
  const [loading, setLoading] = useState(true);
  const [error, setError] = useState<string | null>(null);
  const [showHistoryModal, setShowHistoryModal] = useState(false);

  useEffect(() => {
    const fetchBalance = async () => {
      try {
        setLoading(true);
        setError(null);
        const data = await fidelityPointsService.getBalance();
        setBalance(data);
      } catch (err) {
        // eslint-disable-next-line no-console
        console.error('Error loading fidelity points:', err);
        setError(t('error_loading_points', 'Failed to load fidelity points'));
      } finally {
        setLoading(false);
      }
    };

    fetchBalance();
  }, [t]);

  const loadBalance = async () => {
    try {
      setLoading(true);
      setError(null);
      const data = await fidelityPointsService.getBalance();
      setBalance(data);
    } catch (err) {
      // eslint-disable-next-line no-console
      console.error('Error loading fidelity points:', err);
      setError(t('error_loading_points', 'Failed to load fidelity points'));
    } finally {
      setLoading(false);
    }
  };

  if (loading) {
    return (
      <section className={styles.section}>
        <h2 className={styles.sectionTitle}>{t('fidelity_points_title', 'Fidelity Points')}</h2>
        <p className={styles.loadingText}>{t('loading', 'Loading...')}</p>
      </section>
    );
  }

  if (error) {
    return (
      <section className={styles.section}>
        <h2 className={styles.sectionTitle}>{t('fidelity_points_title', 'Fidelity Points')}</h2>
        <p className={styles.errorText}>{error}</p>
        <button onClick={loadBalance} className={styles.retryButton}>
          {t('retry', 'Retry')}
        </button>
      </section>
    );
  }

  if (!balance) {
    return null;
  }

  const currentPointsValue = balance.currentPointsValue || 0;
  const currentPoints = balance.currentPoints || 0;

  return (
    <section className={styles.section}>
      <h2 className={styles.sectionTitle}>{t('fidelity_points_title', 'Fidelity Points')}</h2>

      {/* Current Points Balance */}
      <div className={styles.pointsBalanceCard}>
        <div className={styles.pointsMainInfo}>
          <div className={styles.pointsNumber}>
            <span className={styles.pointsValue}>{currentPoints.toLocaleString()}</span>
            <span className={styles.pointsLabel}>{t('points', 'Points')}</span>
          </div>
          <div className={styles.pointsValue}>
            <span className={styles.currencyValue}>
              ≈ ${currentPointsValue.toFixed(2)}
            </span>
            <span className={styles.valueLabel}>
              {t('available_discount', 'Available Discount')}
            </span>
          </div>
        </div>

        {/* Points Statistics */}
        <div className={styles.pointsStats}>
          <div className={styles.statItem}>
            <span className={styles.statValue}>
              {balance.totalEarnedPoints.toLocaleString()}
            </span>
            <span className={styles.statLabel}>
              {t('total_earned', 'Total Earned')}
            </span>
          </div>
          <div className={styles.statItem}>
            <span className={styles.statValue}>
              {balance.totalRedeemedPoints.toLocaleString()}
            </span>
            <span className={styles.statLabel}>
              {t('total_redeemed', 'Total Redeemed')}
            </span>
          </div>
        </div>
      </div>

      {/* Action Buttons */}
      <div className={styles.pointsActions}>
        <button
          onClick={() => setShowHistoryModal(true)}
          className={styles.viewHistoryButton}
        >
          {t('view_history', 'View History')}
        </button>
        <button
          onClick={() => {
            // TODO: Scroll to "Learn More" section or open info modal
            alert(t('points_info', '100 points = $1.00 discount. Earn points with every order!'));
          }}
          className={styles.learnMoreButton}
        >
          {t('learn_more', 'Learn More')}
        </button>
      </div>

      {/* Messages */}
      {currentPoints > 0 && (
        <p className={styles.fidelityMessage}>
          {t('points_available_message', 'You can use your points for discounts on your next order!')}
        </p>
      )}
      {currentPoints === 0 && (
        <p className={styles.fidelityMessage}>
          {t('earn_more_points_message', 'Earn points with every order! 100 points = $1 discount.')}
        </p>
      )}

      {/* TODO: Integrate PointsHistoryModal component when created */}
      <PointsHistoryModal
        isOpen={showHistoryModal}
        onClose={() => setShowHistoryModal(false)}
      />
    </section>
  );
}
