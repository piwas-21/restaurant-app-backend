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
  const [showLearnMoreModal, setShowLearnMoreModal] = useState(false);

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
          onClick={() => setShowLearnMoreModal(true)}
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

      {showLearnMoreModal && (
        <div className={styles.modalOverlay} onClick={() => setShowLearnMoreModal(false)}>
          <div className={styles.modalContent} onClick={(e) => e.stopPropagation()}>
            <div className={styles.modalHeader}>
              <h2 className={styles.modalTitle}>{t('learn_more', 'Learn More')}</h2>
              <button
                className={styles.closeButton}
                onClick={() => setShowLearnMoreModal(false)}
                aria-label={t('close', 'Close')}
              >
                ×
              </button>
            </div>
            <div className={styles.modalBody}>
              <p><strong>{t('how_fidelity_works', 'How Fidelity Points Work:')}</strong></p>
              <ul>
                <li>{t('fidelity_rule_1', 'Earn 1 point for every CHF 1.00 you spend')}</li>
                <li>{t('fidelity_rule_2', '100 points = CHF 1.00 discount on future orders')}</li>
                <li>{t('fidelity_rule_3', 'Points never expire as long as you use your account')}</li>
                <li>{t('fidelity_rule_4', 'Automatically applied to your orders at checkout')}</li>
              </ul>
              <p><strong>{t('example_title', 'Example:')}</strong></p>
              <p>{t('example_text', 'Spend CHF 50.00 → Earn 50 points → Get CHF 0.50 discount on next order')}</p>
            </div>
            <div className={styles.formActions}>
              <button
                onClick={() => setShowLearnMoreModal(false)}
                className={styles.closeModalButton}
              >
                {t('close', 'Close')}
              </button>
            </div>
          </div>
        </div>
      )}
    </section>
  );
}
