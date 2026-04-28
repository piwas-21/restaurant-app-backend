'use client';

import { useState } from 'react';
import { useTranslation } from 'react-i18next';
import styles from './RestaurantSettingsPage.module.css';
import WorkingHoursManager from '@/components/admin/settings/WorkingHoursManager';
import OrderTypeManager from '@/components/admin/settings/OrderTypeManager';
import TaxConfigurationManager from '@/components/admin/settings/TaxConfigurationManager';
import GeneralSettingsTab from '@/components/admin/restaurant-settings/GeneralSettingsTab';

type TabType = 'hours' | 'order-types' | 'tax' | 'general';

export default function RestaurantSettingsPage() {
  const { t } = useTranslation();
  const [activeTab, setActiveTab] = useState<TabType>('hours');

  const tabs = [
    { id: 'hours' as TabType, label: t('working_hours', 'Working Hours'), icon: '🕐' },
    { id: 'order-types' as TabType, label: t('order_types', 'Order Types'), icon: '📋' },
    { id: 'tax' as TabType, label: t('tax_configuration', 'Tax Configuration'), icon: '💰' },
    { id: 'general' as TabType, label: t('general_settings', 'General Settings'), icon: '⚙️' },
  ];

  return (
    <div className={styles.container}>
      <div className={styles.header}>
        <h1 className={styles.title}>{t('restaurant_settings', 'Restaurant Settings')}</h1>
        <p className={styles.subtitle}>
          {t('restaurant_settings_desc', 'Manage your restaurant configuration and operating hours')}
        </p>
      </div>

      {/* Tabs */}
      <div className={styles.tabs}>
        {tabs.map((tab) => (
          <button
            key={tab.id}
            className={`${styles.tab} ${activeTab === tab.id ? styles.activeTab : ''}`}
            onClick={() => setActiveTab(tab.id)}
          >
            <span className={styles.tabIcon}>{tab.icon}</span>
            <span className={styles.tabLabel}>{tab.label}</span>
          </button>
        ))}
      </div>

      {/* Tab Content */}
      <div className={styles.tabContent}>
        {activeTab === 'hours' && <WorkingHoursManager />}
        {activeTab === 'order-types' && <OrderTypeManager />}
        {activeTab === 'tax' && <TaxConfigurationManager />}
        {activeTab === 'general' && <GeneralSettingsTab />}
      </div>
    </div>
  );
}
