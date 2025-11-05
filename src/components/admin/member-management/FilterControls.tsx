'use client';

import React from 'react';
import { useTranslation } from 'react-i18next';
import styles from '@/app/styles/AdminPage.module.css';

interface FilterControlsProps {
  activeTab: string;
  setActiveTab: (tab: string) => void;
  searchTerm: string;
  setSearchTerm: (term: string) => void;
  showDeleted: boolean;
  setShowDeleted: (show: boolean) => void;
}

const FilterControls: React.FC<FilterControlsProps> = ({
  activeTab,
  setActiveTab,
  searchTerm,
  setSearchTerm,
  showDeleted,
  setShowDeleted,
}) => {
  const { t } = useTranslation();

  return (
    <>
      <nav className={styles.adminNav}>
        <ul>
          <li>
            <a
              href="#"
              className={activeTab === 'customers' ? styles.activeLink : ''}
              onClick={(e) => {
                e.preventDefault();
                setActiveTab('customers');
              }}
            >
              {t('customers')}
            </a>
          </li>
          <li>
            <a
              href="#"
              className={activeTab === 'staff' ? styles.activeLink : ''}
              onClick={(e) => {
                e.preventDefault();
                setActiveTab('staff');
              }}
            >
              {t('staff')}
            </a>
          </li>
        </ul>
      </nav>
      <div className={styles.filtersContainer}>
        <div className={styles.formGroup}>
          <input
            type="text"
            placeholder={t('search')}
            value={searchTerm}
            onChange={(e) => setSearchTerm(e.target.value)}
          />
        </div>
        {activeTab === 'customers' && (
          <div className={styles.formGroup}>
            <label>
              <input
                type="checkbox"
                checked={showDeleted}
                onChange={(e) => setShowDeleted(e.target.checked)}
              />
              {t('show_deleted')}
            </label>
          </div>
        )}
      </div>
    </>
  );
};

export default FilterControls;
