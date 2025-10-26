"use client";

import React, { useState, useEffect } from 'react';
import { useSnackbar } from 'notistack';
import { useRouter } from 'next/navigation';
import { Award, Gift } from 'lucide-react';
import styles from "@/app/styles/AdminPage.module.css";
import PageHeader from '@/components/admin/PageHeader';
import FidelityAnalyticsCards from '@/components/admin/FidelityAnalyticsCards';
import { adminFidelityAnalyticsService } from '@/services/adminFidelityAnalyticsService';
import type { FidelityAnalytics } from '@/services/adminFidelityAnalyticsService';

export default function FidelityAnalyticsPage() {
  const { enqueueSnackbar } = useSnackbar();
  const router = useRouter();
  const [analytics, setAnalytics] = useState<FidelityAnalytics | null>(null);
  const [loading, setLoading] = useState(true);

  useEffect(() => {
    fetchAnalytics();
    // eslint-disable-next-line react-hooks/exhaustive-deps
  }, []);

  const fetchAnalytics = async () => {
    try {
      setLoading(true);
      const data = await adminFidelityAnalyticsService.getAnalytics();
      setAnalytics(data);
    } catch {
      enqueueSnackbar('Failed to load fidelity analytics', { variant: 'error' });
    } finally {
      setLoading(false);
    }
  };

  return (
    <main className={styles.adminContainer}>
      <PageHeader title="Fidelity Analytics" />
      
      <section className={styles.adminContent}>
        {/* Page Description */}
        <div style={{ marginBottom: '2rem' }}>
          <h2>Fidelity Points System Overview</h2>
          <p>
            Monitor and analyze customer loyalty points, redemptions, and program engagement
          </p>
        </div>

        {/* Analytics Cards */}
        {analytics && !loading ? (
          <FidelityAnalyticsCards analytics={analytics} loading={false} />
        ) : (
          <FidelityAnalyticsCards
            analytics={{
              totalPointsIssued: 0,
              totalPointsRedeemed: 0,
              totalActiveUsers: 0,
              totalPointsOutstanding: 0,
              averagePointsPerUser: 0,
              totalDiscountGiven: 0,
              activePointRules: 0,
              activeCustomerDiscounts: 0,
            }}
            loading={true}
          />
        )}
      </section>

      {/* Quick Actions Section */}
      <section className={styles.adminContent} style={{ marginTop: '2rem' }}>
        <h2>Fidelity System Management</h2>
        <div style={{ display: 'grid', gridTemplateColumns: 'repeat(auto-fit, minmax(280px, 1fr))', gap: '1.5rem', marginTop: '1.5rem' }}>
          {/* Point Rules Card */}
          <div
            onClick={() => router.push('/admin/point-rules')}
            className={styles.quickActionCard}
          >
            <div style={{ display: 'flex', alignItems: 'center', gap: '1rem', marginBottom: '1rem' }}>
              <Award size={32} />
              <h4 style={{ fontSize: '1.25rem', fontWeight: '700', margin: 0 }}>Point Earning Rules</h4>
            </div>
            <p style={{ margin: 0, opacity: 0.9 }}>
              Configure how customers earn points based on order amounts
            </p>
            <div style={{ marginTop: '1rem', fontSize: '0.9rem', opacity: 0.8 }}>
              {analytics ? `${analytics.activePointRules} active rules` : 'Loading...'}
            </div>
          </div>

          {/* Customer Discounts Card */}
          <div
            onClick={() => router.push('/admin/customer-discounts')}
            className={styles.quickActionCardGreen}
          >
            <div style={{ display: 'flex', alignItems: 'center', gap: '1rem', marginBottom: '1rem' }}>
              <Gift size={32} />
              <h4 style={{ fontSize: '1.25rem', fontWeight: '700', margin: 0 }}>Customer Discounts</h4>
            </div>
            <p style={{ margin: 0, opacity: 0.9 }}>
              Create exclusive discount rules for specific customers
            </p>
            <div style={{ marginTop: '1rem', fontSize: '0.9rem', opacity: 0.8 }}>
              {analytics ? `${analytics.activeCustomerDiscounts} active discounts` : 'Loading...'}
            </div>
          </div>
        </div>
      </section>
    </main>
  );
}
