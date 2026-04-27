'use client';

import React, { useState, useEffect } from 'react';
import { useTranslation } from 'react-i18next';
import { fidelityPointsService } from '@/services/fidelityPointsService';
import type { FidelityPointBalance } from '@/types/fidelity';
import { Gift, Coins, Percent } from 'lucide-react';
import styles from './FidelityPointsCheckout.module.css';

interface FidelityPointsCheckoutProps {
  orderSubtotal: number;
  onPointsRedemption?: (points: number, discountAmount: number) => void;
}

export default function FidelityPointsCheckout({ orderSubtotal, onPointsRedemption }: FidelityPointsCheckoutProps) {
  const { t } = useTranslation();
  const [balance, setBalance] = useState<FidelityPointBalance | null>(null);
  const [loading, setLoading] = useState(true);
  const [pointsToEarn, setPointsToEarn] = useState(0);
  const [pointsToRedeem, setPointsToRedeem] = useState(0);
  const [maxRedeemablePoints, setMaxRedeemablePoints] = useState(0);
  const [showRedemption, setShowRedemption] = useState(false);

  const loadBalanceAndCalculate = async () => {
    try {
      setLoading(true);

      // Load user's current balance
      const balanceData = await fidelityPointsService.getBalance();
      setBalance(balanceData);

      // Calculate points that will be earned (approximate - backend will calculate exact)
      // Simple calculation: For every $20-50, earn 15 points (Silver tier example)
      // This should match backend logic, but we'll show estimated points
      const estimatedPoints = Math.floor(orderSubtotal / 10) * 5; // Rough estimate
      setPointsToEarn(estimatedPoints);

      // Calculate max redeemable points (can't exceed current balance or order total)
      const maxPointsBasedOnOrder = orderSubtotal * 100; // 100 points = $1
      const maxPoints = Math.min(balanceData.currentPoints, maxPointsBasedOnOrder);
      setMaxRedeemablePoints(maxPoints);
    } catch {
      // Silently handle errors (e.g., non-authenticated users)
      // Don't log auth errors to avoid console noise during checkout for non-logged-in users
    } finally {
      setLoading(false);
    }
  };

  useEffect(() => {
    loadBalanceAndCalculate();
    // eslint-disable-next-line react-hooks/exhaustive-deps
  }, [orderSubtotal]);

  const handleRedeemPoints = async () => {
    if (pointsToRedeem > 0 && pointsToRedeem <= maxRedeemablePoints) {
      try {
        const discountAmount = await fidelityPointsService.calculateDiscount(pointsToRedeem);
        if (onPointsRedemption) {
          onPointsRedemption(pointsToRedeem, discountAmount);
        }
      } catch (err) {
        // eslint-disable-next-line no-console
        console.error('Error calculating discount:', err);
      }
    }
  };

  const handleSliderChange = (e: React.ChangeEvent<HTMLInputElement>) => {
    const value = parseInt(e.target.value, 10);
    setPointsToRedeem(value);
  };

  const handleMaxPoints = () => {
    setPointsToRedeem(maxRedeemablePoints);
  };

  const discountAmount = pointsToRedeem / 100; // 100 points = $1

  if (loading || !balance) {
    return null; // Don't show anything while loading
  }

  return (
    <div className={styles.container}>
      {/* Points to be Earned */}
      <div className={styles.earnSection}>
        <div className={styles.iconWrapper}>
          <Coins size={20} />
        </div>
        <div className={styles.content}>
          <h3 className={styles.title}>{t('earn_points_with_order', 'Earn Points with this Order')}</h3>
          <p className={styles.earnPoints}>
            +{pointsToEarn} {t('points', 'points')}
            <span className={styles.earnValue}>
              (≈ CHF{(pointsToEarn / 100).toFixed(2)} {t('future_discount', 'future discount')})
            </span>
          </p>
        </div>
      </div>

      {/* Current Balance Display */}
      <div className={styles.balanceSection}>
        <div className={styles.balanceInfo}>
          <span className={styles.balanceLabel}>{t('your_current_balance', 'Your Current Balance')}:</span>
          <span className={styles.balanceValue}>
            {balance.currentPoints.toLocaleString()} {t('points', 'points')}
            <span className={styles.balanceValueCurrency}>(${balance.currentPointsValue.toFixed(2)})</span>
          </span>
        </div>
      </div>

      {/* Points Redemption Section */}
      {balance.currentPoints > 0 && (
        <div className={styles.redemptionSection}>
          <button onClick={() => setShowRedemption(!showRedemption)} className={styles.toggleButton}>
            <Gift size={18} />
            {showRedemption
              ? t('hide_redemption', 'Hide Point Redemption')
              : t('use_points', 'Use Points for Discount')}
          </button>

          {showRedemption && (
            <div className={styles.redemptionPanel}>
              <p className={styles.redemptionInfo}>
                {t('redemption_info', 'Redeem your points for an instant discount. 100 points = $1.00')}
              </p>

              {/* Slider for points selection */}
              <div className={styles.sliderContainer}>
                <div className={styles.sliderHeader}>
                  <label htmlFor="pointsSlider" className={styles.sliderLabel}>
                    {t('points_to_redeem', 'Points to Redeem')}:
                  </label>
                  <button onClick={handleMaxPoints} className={styles.maxButton}>
                    {t('use_max', 'Use Max')}
                  </button>
                </div>

                <input
                  id="pointsSlider"
                  type="range"
                  min="0"
                  max={maxRedeemablePoints}
                  step="100"
                  value={pointsToRedeem}
                  onChange={handleSliderChange}
                  className={styles.slider}
                />

                <div className={styles.sliderValues}>
                  <span className={styles.pointsSelected}>
                    {pointsToRedeem.toLocaleString()} {t('points', 'points')}
                  </span>
                  <span className={styles.discountAmount}>
                    <Percent size={16} />${discountAmount.toFixed(2)} {t('discount', 'discount')}
                  </span>
                </div>

                <div className={styles.sliderLimits}>
                  <span>0</span>
                  <span>{maxRedeemablePoints.toLocaleString()}</span>
                </div>
              </div>

              {/* Apply Button */}
              <button onClick={handleRedeemPoints} disabled={pointsToRedeem === 0} className={styles.applyButton}>
                {t('apply_discount', 'Apply Discount')}
              </button>

              <p className={styles.balanceAfterRedemption}>
                {t('balance_after_redemption', 'Balance after redemption')}:{' '}
                {(balance.currentPoints - pointsToRedeem).toLocaleString()} {t('points', 'points')}
              </p>
            </div>
          )}
        </div>
      )}
    </div>
  );
}
