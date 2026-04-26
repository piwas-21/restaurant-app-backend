import React, { useState, useEffect } from 'react';
import { useTranslation } from 'react-i18next';
import {
  UserDto,
  FidelityPointBalanceDto,
  CustomerDiscountRuleDto,
  getUserFidelityBalance,
  getUserDiscountRules,
  calculateDiscountFromPoints,
  calculatePointsToEarn,
} from '@/services/serverService';
import styles from './CustomerInfoPanel.module.css';

interface CustomerInfoPanelProps {
  user: UserDto;
  orderTotal: number;
  pointsToRedeem: number;
  onPointsChange: (points: number) => void;
}

export default function CustomerInfoPanel({
  user,
  orderTotal,
  pointsToRedeem,
  onPointsChange,
}: CustomerInfoPanelProps) {
  const { t } = useTranslation();
  const [balance, setBalance] = useState<FidelityPointBalanceDto | null>(null);
  const [discountRules, setDiscountRules] = useState<CustomerDiscountRuleDto[]>([]);
  const [isLoading, setIsLoading] = useState(true);

  // Fetch user's fidelity balance and discount rules
  useEffect(() => {
    async function loadData() {
      setIsLoading(true);
      const [balanceData, rulesData] = await Promise.all([
        getUserFidelityBalance(user.id),
        getUserDiscountRules(user.id),
      ]);
      setBalance(balanceData);
      setDiscountRules(rulesData);
      setIsLoading(false);
    }
    loadData();
  }, [user.id]);

  const availablePoints = balance?.currentPoints || 0;
  const maxRedeemable = Math.min(availablePoints, Math.floor(orderTotal * 100)); // Can't redeem more than order total
  const discountAmount = calculateDiscountFromPoints(pointsToRedeem);
  const pointsToEarn = calculatePointsToEarn(orderTotal - discountAmount);

  const handleSliderChange = (e: React.ChangeEvent<HTMLInputElement>) => {
    const value = parseInt(e.target.value, 10);
    onPointsChange(Math.min(value, maxRedeemable));
  };

  const handleRedeemAll = () => {
    onPointsChange(maxRedeemable);
  };

  const handleRedeemNone = () => {
    onPointsChange(0);
  };

  // Format discount value based on type
  const formatDiscountValue = (rule: CustomerDiscountRuleDto) => {
    if (rule.discountType === 'Percentage') {
      return `${rule.discountValue}%`;
    }
    return `CHF ${rule.discountValue.toFixed(2)}`;
  };

  if (isLoading) {
    return (
      <div className={styles.panel}>
        <div className={styles.loading}>
          <span className={styles.spinner}>⏳</span>
          <span>{t('server.loading_customer_info', 'Loading customer info...')}</span>
        </div>
      </div>
    );
  }

  // Check if user has any active discounts
  const hasUserDiscount = user.isDiscountActive && user.discountPercentage > 0;
  const hasDiscountRules = discountRules.length > 0;

  return (
    <div className={styles.panel}>
      <div className={styles.header}>
        <span className={styles.customerIcon}>👤</span>
        <span className={styles.customerName}>{user.fullName || `${user.firstName} ${user.lastName}`}</span>
      </div>

      {/* User profile discount */}
      {hasUserDiscount && (
        <div className={styles.discountRow}>
          <span className={styles.discountIcon}>🏷️</span>
          <span className={styles.discountText}>
            {t('server.member_discount', 'Member Discount')}: <strong>{user.discountPercentage}%</strong>
          </span>
          <span className={styles.discountNote}>{t('server.auto_applied', '(auto-applied)')}</span>
        </div>
      )}

      {/* Customer discount rules */}
      {hasDiscountRules && discountRules.map((rule) => (
        <div key={rule.id} className={styles.discountRow}>
          <span className={styles.discountIcon}>🎁</span>
          <span className={styles.discountText}>
            {rule.name}: <strong>{formatDiscountValue(rule)}</strong>
          </span>
          {rule.maxUsageCount && (
            <span className={styles.discountNote}>
              ({rule.usageCount}/{rule.maxUsageCount} {t('server.used', 'used')})
            </span>
          )}
          {!rule.maxUsageCount && (
            <span className={styles.discountNote}>{t('server.auto_applied', '(auto-applied)')}</span>
          )}
        </div>
      ))}

      {/* No discounts message */}
      {!hasUserDiscount && !hasDiscountRules && (
        <div className={styles.noDiscount}>
          <span className={styles.discountIcon}>❌</span>
          <span>{t('server.no_active_discounts', 'No active discounts')}</span>
        </div>
      )}

      {/* Fidelity points section */}
      <div className={styles.pointsSection}>
        <div className={styles.pointsHeader}>
          <span className={styles.pointsIcon}>⭐</span>
          <span className={styles.pointsLabel}>{t('server.fidelity_points', 'Fidelity Points')}</span>
          <span className={styles.pointsBalance}>{availablePoints} pts</span>
        </div>

        {availablePoints > 0 && orderTotal > 0 ? (
          <>
            <div className={styles.redeemControls}>
              <label className={styles.sliderLabel}>
                {t('server.redeem_points', 'Redeem')}:
                <input
                  type="range"
                  min="0"
                  max={maxRedeemable}
                  value={pointsToRedeem}
                  onChange={handleSliderChange}
                  className={styles.slider}
                />
                <span className={styles.pointsValue}>
                  {pointsToRedeem} pts = CHF {discountAmount.toFixed(2)}
                </span>
              </label>
              <div className={styles.quickButtons}>
                <button type="button" className={styles.quickBtn} onClick={handleRedeemNone}>
                  {t('server.none', 'None')}
                </button>
                <button type="button" className={styles.quickBtn} onClick={handleRedeemAll}>
                  {t('server.all', 'All')} ({maxRedeemable})
                </button>
              </div>
            </div>

            {pointsToRedeem > 0 && (
              <div className={styles.discountPreview}>
                <span>{t('server.points_discount', 'Points Discount')}:</span>
                <span className={styles.discountValue}>-CHF {discountAmount.toFixed(2)}</span>
              </div>
            )}
          </>
        ) : availablePoints === 0 ? (
          <p className={styles.noPoints}>{t('server.no_points', 'No points available')}</p>
        ) : (
          <p className={styles.noPoints}>{t('server.add_items', 'Add items to redeem points')}</p>
        )}

        {/* Points to earn */}
        <div className={styles.earnRow}>
          <span className={styles.earnIcon}>✨</span>
          <span>
            {t('server.will_earn', 'Will earn')}: <strong>{pointsToEarn} pts</strong>
          </span>
        </div>
      </div>
    </div>
  );
}
