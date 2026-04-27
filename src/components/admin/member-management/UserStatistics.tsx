'use client';

import React, { useEffect, useState } from 'react';
import { Users, UserCheck, Shield, Trash2, TrendingUp } from 'lucide-react';
import { useTranslation } from 'react-i18next';
import { getUserStatistics } from '@/services/userService';
import type { UserStatistics as UserStatsType } from '@/types/user';
import styles from './UserStatistics.module.css';

const UserStatistics: React.FC = () => {
  const { t } = useTranslation();
  const [stats, setStats] = useState<UserStatsType | null>(null);
  const [loading, setLoading] = useState(true);
  const [error, setError] = useState<string | null>(null);

  const fetchStats = async () => {
    try {
      setLoading(true);
      setError(null);
      const response = await getUserStatistics();
      if (response.success && response.data) {
        setStats(response.data);
      } else {
        console.error('Failed response:', response);
        setError(t('failed_to_load_stats', 'Failed to load statistics'));
      }
    } catch (err) {
      console.error('Error fetching user statistics:', err);
      setError(t('error_loading_stats', 'Error loading statistics'));
    } finally {
      setLoading(false);
    }
  };

  useEffect(() => {
    fetchStats();
    // eslint-disable-next-line react-hooks/exhaustive-deps
  }, []);

  if (error) {
    return (
      <div className={styles.statsGrid}>
        <div className={styles.error}>{error}</div>
      </div>
    );
  }

  return (
    <div className={styles.statsGrid}>
      <StatCard
        icon={<Users size={28} />}
        label={t('total_customers', 'Total Customers')}
        value={stats?.totalCustomers ?? 0}
        loading={loading}
        variant="primary"
      />
      <StatCard
        icon={<UserCheck size={28} />}
        label={t('total_staff', 'Staff Members')}
        value={stats?.totalStaff ?? 0}
        loading={loading}
        variant="success"
      />
      <StatCard
        icon={<Shield size={28} />}
        label={t('total_admins', 'Administrators')}
        value={stats?.totalAdmins ?? 0}
        loading={loading}
        variant="info"
      />
      <StatCard
        icon={<TrendingUp size={28} />}
        label={t('recent_registrations', 'New (Last 7 Days)')}
        value={stats?.recentRegistrations ?? 0}
        loading={loading}
        variant="warning"
      />
      <StatCard
        icon={<Trash2 size={28} />}
        label={t('deleted_users', 'Deleted Users')}
        value={stats?.deletedUsers ?? 0}
        loading={loading}
        variant="danger"
      />
    </div>
  );
};

interface StatCardProps {
  icon: React.ReactNode;
  label: string;
  value: number;
  loading: boolean;
  variant: 'primary' | 'success' | 'info' | 'warning' | 'danger';
}

const StatCard: React.FC<StatCardProps> = ({ icon, label, value, loading, variant }) => {
  return (
    <div className={styles.statCard}>
      <div className={`${styles.iconWrapper} ${styles[variant]}`}>{icon}</div>
      <div className={styles.statContent}>
        {loading ? (
          <div className={styles.loadingSkeleton} />
        ) : (
          <h3 className={styles.statValue}>{value.toLocaleString()}</h3>
        )}
        <p className={styles.statLabel}>{label}</p>
      </div>
    </div>
  );
};

export default UserStatistics;
