import React from 'react';
import { useTranslation } from 'react-i18next';
import { UtensilsCrossed } from 'lucide-react';
import styles from '@/app/styles/MenuPage.module.css';

export default function MenuPageHeader() {
  const { t } = useTranslation();

  return (
    <h1 id="menu-page-heading" className={styles.pageTitle}>
      <UtensilsCrossed size={48} strokeWidth={2} aria-label={t("menu_title")} />
    </h1>
  );
}
