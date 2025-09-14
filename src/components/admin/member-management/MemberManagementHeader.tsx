'use client';

import React from 'react';
import { useTranslation } from 'react-i18next';
import styles from '@/app/styles/AdminPage.module.css';
import { useRouter } from 'next/navigation';

interface MemberManagementHeaderProps {
  onRegisterStaff: () => void;
}

const MemberManagementHeader: React.FC<MemberManagementHeaderProps> = ({ onRegisterStaff }) => {
  const { t } = useTranslation();
  const router = useRouter();

  return (
    <div className={styles.adminHeader}>
      <h1>{t('admin_member_management_title')}</h1>
      <div>
        <button className={`${styles.adminButton} ${styles.add}`} onClick={onRegisterStaff}>
          {t('register_staff')}
        </button>
        <button className={styles.adminButton} onClick={() => router.push('/admin/dashboard')}>
          {t('back_to_dashboard')}
        </button>
      </div>
    </div>
  );
};

export default MemberManagementHeader;
