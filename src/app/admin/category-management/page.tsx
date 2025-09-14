'use client';

import React, { useState } from 'react';
import { useTranslation } from 'react-i18next';
import { useCategoryManagement } from '@/hooks/useCategoryManagement';
import styles from '@/app/styles/AdminPage.module.css';
import CreateCategoryModal from '@/components/admin/CreateCategoryModal';
import EditCategoryModal from '@/components/admin/EditCategoryModal';
import ConfirmationModal from '@/components/common/ConfirmationModal';
import ResultModal from '@/components/common/ResultModal';
import CategoryManagementHeader from '@/components/admin/category-management/CategoryManagementHeader';
import CategoriesTable from '@/components/admin/category-management/CategoriesTable';
import { Category } from '@/app/admin/menu-management/interfaces';

const CategoryManagementPage = () => {
  const { t } = useTranslation();
  const {
    categories,
    isLoading,
    error,
    fetchCategories,
    handleDeleteCategory,
  } = useCategoryManagement();

  const [isCreateModalOpen, setIsCreateModalOpen] = useState(false);
  const [isEditModalOpen, setIsEditModalOpen] = useState(false);
  const [selectedCategory, setSelectedCategory] = useState<Category | null>(null);
  const [isConfirmationModalOpen, setIsConfirmationModalOpen] = useState(false);
  const [categoryToDelete, setCategoryToDelete] = useState<Category | null>(null);
  const [isResultModalOpen, setIsResultModalOpen] = useState(false);
  const [resultModalMessage, setResultModalMessage] = useState('');
  const [isResultModalSuccess, setIsResultModalSuccess] = useState(false);

  const handleEditClick = (category: Category) => {
    setSelectedCategory(category);
    setIsEditModalOpen(true);
  };

  const handleDeleteClick = (category: Category) => {
    setCategoryToDelete(category);
    setIsConfirmationModalOpen(true);
  };

  const handleConfirmDelete = async () => {
    if (categoryToDelete) {
      const result = await handleDeleteCategory(categoryToDelete.id);
      setIsConfirmationModalOpen(false);
      setResultModalMessage(t(result.message));
      setIsResultModalSuccess(result.success);
      setIsResultModalOpen(true);
      setCategoryToDelete(null);
    }
  };

  return (
    <>
      <div className={styles.adminContainer}>
        <CategoryManagementHeader onOpenCreateModal={() => setIsCreateModalOpen(true)} />
        <div className={styles.adminContent}>
          <CategoriesTable
            categories={categories}
            isLoading={isLoading}
            error={error}
            onEdit={handleEditClick}
            onDelete={handleDeleteClick}
          />
        </div>
      </div>
      <CreateCategoryModal
        isOpen={isCreateModalOpen}
        onClose={() => setIsCreateModalOpen(false)}
        onCategoryCreated={fetchCategories}
      />
      <EditCategoryModal
        isOpen={isEditModalOpen}
        onClose={() => setIsEditModalOpen(false)}
        onCategoryUpdated={fetchCategories}
        category={selectedCategory}
      />
      <ConfirmationModal
        isOpen={isConfirmationModalOpen}
        onClose={() => setIsConfirmationModalOpen(false)}
        onConfirm={handleConfirmDelete}
        message={t('delete_category_confirmation_message', { name: categoryToDelete?.name })}
      />
      <ResultModal
        isOpen={isResultModalOpen}
        onClose={() => setIsResultModalOpen(false)}
        message={resultModalMessage}
        isSuccess={isResultModalSuccess}
      />
    </>
  );
};

export default CategoryManagementPage;
