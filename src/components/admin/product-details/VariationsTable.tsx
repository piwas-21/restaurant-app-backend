'use client';

import React from 'react';
import { Variation } from '@/app/admin/menu-management/interfaces';
import detailsStyles from '@/app/styles/DetailsPage.module.css';
import { useTranslation } from 'react-i18next';

interface VariationsTableProps {
  variations: Variation[];
}

const VariationsTable: React.FC<VariationsTableProps> = ({ variations }) => {
  const { t } = useTranslation();

  if (!variations || variations.length === 0) {
    return null;
  }

  return (
    <div className={detailsStyles.infoSection}>
      <h3>{t('variations')}</h3>
      <table className={detailsStyles.variationsTable}>
        <thead>
          <tr>
            <th>{t('variation_name')}</th>
            <th>{t('price_modifier')}</th>
            <th>{t('final_price')}</th>
            <th>{t('status')}</th>
          </tr>
        </thead>
        <tbody>
          {variations.map(v => (
            <tr key={v.name}>
              <td>{v.name}</td>
              <td>${v.priceModifier.toFixed(2)}</td>
              <td>${v.finalPrice.toFixed(2)}</td>
              <td>{v.isActive ? t('active') : t('inactive')}</td>
            </tr>
          ))}
        </tbody>
      </table>
    </div>
  );
};

export default VariationsTable;
