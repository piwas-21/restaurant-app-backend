"use client";

import React, { useState } from 'react';
import { useTranslation } from 'react-i18next';
import styles from "@/app/styles/AdminPage.module.css";
import PageHeader from '@/components/admin/PageHeader';
import SpecialsTable from '@/components/admin/specials-management/SpecialsTable';
import FeaturedSpecialCard from '@/components/admin/specials-management/FeaturedSpecialCard';
import ConfirmationModal from '@/components/common/ConfirmationModal';
import ResultModal from '@/components/common/ResultModal';
import { useSpecialsManagement } from '@/hooks/useSpecialsManagement';
import { AdminAuthGuard } from '@/components/admin/AdminAuthGuard';

export default function SpecialsManagementPage() {
  const { t } = useTranslation();
  const {
    specialProducts,
    featuredSpecial,
    isLoading,
    error,
    handleSetFeaturedSpecial,
    handleUnsetFeaturedSpecial,
  } = useSpecialsManagement();

  const [isConfirmationModalOpen, setIsConfirmationModalOpen] = useState(false);
  const [confirmationAction, setConfirmationAction] = useState<'set' | 'unset'>('set');
  const [selectedProductId, setSelectedProductId] = useState<string | null>(null);
  const [selectedProductName, setSelectedProductName] = useState<string>('');

  const [isResultModalOpen, setIsResultModalOpen] = useState(false);
  const [resultModalMessage, setResultModalMessage] = useState('');
  const [isResultModalSuccess, setIsResultModalSuccess] = useState(false);

  const handleSetFeaturedClick = (productId: string) => {
    const product = specialProducts.find(p => p.id === productId);
    if (product) {
      setSelectedProductId(productId);
      setSelectedProductName(product.name);
      setConfirmationAction('set');
      setIsConfirmationModalOpen(true);
    }
  };

  const handleRemoveFeaturedClick = () => {
    setConfirmationAction('unset');
    setIsConfirmationModalOpen(true);
  };

  const handleConfirmAction = async () => {
    setIsConfirmationModalOpen(false);

    let result;
    if (confirmationAction === 'set' && selectedProductId) {
      result = await handleSetFeaturedSpecial(selectedProductId);
    } else if (confirmationAction === 'unset') {
      result = await handleUnsetFeaturedSpecial();
    } else {
      return;
    }

    setResultModalMessage(result.message);
    setIsResultModalSuccess(result.success);
    setIsResultModalOpen(true);
    setSelectedProductId(null);
    setSelectedProductName('');
  };

  const getConfirmationMessage = () => {
    if (confirmationAction === 'set') {
      return t('confirm_set_featured',
        `Are you sure you want to set "${selectedProductName}" as the featured special? This will replace any current featured special.`);
    } else {
      return t('confirm_remove_featured',
        'Are you sure you want to remove the featured special?');
    }
  };

  return (
    <AdminAuthGuard>
      <main className={styles.adminContainer}>
        <PageHeader title={t('admin_specials_management_title', 'Specials Management')} />

        <section className={styles.adminContent}>
          <FeaturedSpecialCard
            featuredSpecial={featuredSpecial}
            onRemoveFeatured={handleRemoveFeaturedClick}
          />

          <div className={styles.sectionDivider} />

          <h2>{t('specials_table_header', 'Special Menu Items')}</h2>
          <SpecialsTable
            specialProducts={specialProducts}
            isLoading={isLoading}
            error={error}
            onSetFeatured={handleSetFeaturedClick}
          />
        </section>
      </main>

      <ConfirmationModal
        isOpen={isConfirmationModalOpen}
        onClose={() => setIsConfirmationModalOpen(false)}
        onConfirm={handleConfirmAction}
        message={getConfirmationMessage()}
      />

      <ResultModal
        isOpen={isResultModalOpen}
        onClose={() => setIsResultModalOpen(false)}
        message={resultModalMessage}
        isSuccess={isResultModalSuccess}
      />
    </AdminAuthGuard>
  );
}
