"use client";

import React from 'react';
import { useTranslation } from 'react-i18next';
import styles from "@/app/styles/AdminPage.module.css";
import PageHeader from '@/components/admin/PageHeader';
import { AdminAuthGuard } from '@/components/admin/AdminAuthGuard';

export default function AdminDashboardPage() {
  const { t } = useTranslation();

  return (
    <AdminAuthGuard>
      <main className={styles.adminContainer}>
        <PageHeader title={t('admin_dashboard_title')} />
        <section className={styles.adminContent}>
          <h2>{t('welcome_admin')}</h2>
          <p>{t('admin_dashboard_welcome_message')}</p>
        </section>
      </main>
    </AdminAuthGuard>
  );
}
