'use client';

import React from 'react';
import { TrendingUp, TrendingDown, DollarSign, Users, Award, Gift } from 'lucide-react';
import styles from './FidelityAnalyticsCards.module.css';

export interface AnalyticsCardProps {
  title: string;
  value: string | number;
  subtitle?: string;
  icon: 'points-issued' | 'points-redeemed' | 'active-users' | 'discount-given' | 'rules' | 'discounts';
  trend?: {
    value: number;
    isPositive: boolean;
  };
}

interface FidelityAnalyticsCardsProps {
  analytics: {
    totalPointsIssued: number;
    totalPointsRedeemed: number;
    totalActiveUsers: number;
    totalPointsOutstanding: number;
    averagePointsPerUser: number;
    totalDiscountGiven: number;
    activePointRules: number;
    activeCustomerDiscounts: number;
  };
  loading?: boolean;
}

export default function FidelityAnalyticsCards({ analytics, loading }: FidelityAnalyticsCardsProps) {
  const getIcon = (type: string) => {
    switch (type) {
      case 'points-issued':
        return <Award size={24} />;
      case 'points-redeemed':
        return <Gift size={24} />;
      case 'active-users':
        return <Users size={24} />;
      case 'discount-given':
        return <DollarSign size={24} />;
      case 'rules':
        return <TrendingUp size={24} />;
      case 'discounts':
        return <TrendingDown size={24} />;
      default:
        return <Award size={24} />;
    }
  };

  const formatCurrency = (value: number) => {
    return new Intl.NumberFormat('de-CH', {
      style: 'currency',
      currency: 'CHF',
      minimumFractionDigits: 0,
      maximumFractionDigits: 0,
    }).format(value);
  };

  const formatNumber = (value: number) => {
    return new Intl.NumberFormat('en-US').format(value);
  };

  const cards: AnalyticsCardProps[] = [
    {
      title: 'Total Points Issued',
      value: formatNumber(analytics.totalPointsIssued),
      subtitle: `${formatCurrency(analytics.totalPointsIssued / 100)} value`,
      icon: 'points-issued',
    },
    {
      title: 'Total Points Redeemed',
      value: formatNumber(analytics.totalPointsRedeemed),
      subtitle: `${formatCurrency(analytics.totalPointsRedeemed / 100)} in discounts`,
      icon: 'points-redeemed',
    },
    {
      title: 'Active Users with Points',
      value: formatNumber(analytics.totalActiveUsers),
      subtitle: `Avg: ${formatNumber(Math.round(analytics.averagePointsPerUser))} pts/user`,
      icon: 'active-users',
    },
    {
      title: 'Total Discount Given',
      value: formatCurrency(analytics.totalDiscountGiven),
      subtitle: 'Lifetime customer savings',
      icon: 'discount-given',
    },
    {
      title: 'Active Point Rules',
      value: analytics.activePointRules,
      subtitle: 'Earning rules configured',
      icon: 'rules',
    },
    {
      title: 'Active Customer Discounts',
      value: analytics.activeCustomerDiscounts,
      subtitle: 'Special discount rules',
      icon: 'discounts',
    },
  ];

  if (loading) {
    return (
      <div className={styles.grid}>
        {[...Array(6)].map((_, i) => (
          <div key={i} className={`${styles.card} ${styles.loading}`}>
            <div className={styles.skeleton}></div>
          </div>
        ))}
      </div>
    );
  }

  return (
    <div className={styles.grid}>
      {cards.map((card, index) => (
        <div key={index} className={styles.card}>
          <div className={styles.cardHeader}>
            <div className={`${styles.iconWrapper} ${styles[`icon-${card.icon}`]}`}>
              {getIcon(card.icon)}
            </div>
            <h3 className={styles.cardTitle}>{card.title}</h3>
          </div>
          <div className={styles.cardBody}>
            <p className={styles.cardValue}>{card.value}</p>
            {card.subtitle && (
              <p className={styles.cardSubtitle}>{card.subtitle}</p>
            )}
          </div>
        </div>
      ))}
    </div>
  );
}
