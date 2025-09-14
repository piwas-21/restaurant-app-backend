'use client';

import React from 'react';
import Link from 'next/link';
import { SideItem } from '@/app/admin/menu-management/interfaces';
import styles from '@/app/styles/AdminPage.module.css';
import detailsStyles from '@/app/styles/DetailsPage.module.css';
import { useTranslation } from 'react-i18next';

interface SuggestedSideItemsTableProps {
  suggestedSideItems: SideItem[];
}

const SuggestedSideItemsTable: React.FC<SuggestedSideItemsTableProps> = ({ suggestedSideItems }) => {
  const { t } = useTranslation();

  if (!suggestedSideItems || suggestedSideItems.length === 0) {
    return null;
  }

  return (
    <div className={detailsStyles.infoSection}>
      <h3>{t('suggested_side_items')}</h3>
      <table className={detailsStyles.variationsTable}>
        <thead>
          <tr>
            <th>{t('item_name')}</th>
            <th>{t('price')}</th>
            <th>{t('required')}</th>
            <th>{t('actions_header')}</th>
          </tr>
        </thead>
        <tbody>
          {suggestedSideItems.map(item => (
            <tr key={item.id}>
              <td>{item.name}</td>
              <td>${item.price.toFixed(2)}</td>
              <td>{item.isRequired ? t('yes') : t('no')}</td>
              <td>
                <Link href={`/admin/menu-management/${item.id}`} className={`${styles.adminButton} ${styles.details}`}>
                  {t('details')}
                </Link>
              </td>
            </tr>
          ))}
        </tbody>
      </table>
    </div>
  );
};

export default SuggestedSideItemsTable;
