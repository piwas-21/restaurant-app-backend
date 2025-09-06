'use client';

import React, { useState, useEffect } from 'react';
import { useTranslation } from 'react-i18next';
import { useRouter } from 'next/navigation';
import styles from '@/app/styles/AdminPage.module.css';
import CreateCategoryModal from '@/components/admin/CreateCategoryModal';
import EditCategoryModal from '@/components/admin/EditCategoryModal';
import ConfirmationModal from '@/components/common/ConfirmationModal';
import ResultModal from '@/components/common/ResultModal';
import { getCategories, deleteCategory } from '@/services/categoryService';

interface Category {
  id: string;
  name: string;
  description?: string | null;
  isActive: boolean;
  displayOrder: number;
}

const CategoryManagementPage = () => {
  const { t } = useTranslation();
  const router = useRouter();
  const [isCreateModalOpen, setIsCreateModalOpen] = useState(false);
  const [isEditModalOpen, setIsEditModalOpen] = useState(false);
  const [selectedCategory, setSelectedCategory] = useState<Category | null>(null);
  const [categories, setCategories] = useState<Category[]>([]);
  const [error, setError] = useState<string | null>(null);
  const [isConfirmationModalOpen, setIsConfirmationModalOpen] = useState(false);
  const [categoryToDelete, setCategoryToDelete] = useState<Category | null>(null);
  const [isResultModalOpen, setIsResultModalOpen] = useState(false);
  const [resultModalMessage, setResultModalMessage] = useState('');
  const [isResultModalSuccess, setIsResultModalSuccess] = useState(false);

  const fetchCategories = async () => {
    try {
      setError(null);
      const response = await getCategories();
      if (response.success) {
        setCategories(response.data.items);
      } else {
        setError(response.message || 'Failed to fetch categories');
      }
    } catch (err) {
      setError('An unexpected error occurred.');
    }
  };

  useEffect(() => {
    fetchCategories();
  }, []);

  const handleCategoryCreated = () => {
    fetchCategories();
  };

  const handleCategoryUpdated = () => {
    fetchCategories();
  };

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
      setIsConfirmationModalOpen(false);
      try {
        const response = await deleteCategory(categoryToDelete.id);
        if (response.success) {
          setResultModalMessage(t('category_deleted_successfully'));
          setIsResultModalSuccess(true);
          fetchCategories();
        } else {
          setResultModalMessage(response.message || t('failed_to_delete_category'));
          setIsResultModalSuccess(false);
        }
      } catch (err) {
        setResultModalMessage(t('delete_category_error'));
        setIsResultModalSuccess(false);
      }
      setIsResultModalOpen(true);
      setCategoryToDelete(null);
    }
  };

  const handleViewProducts = (category: Category) => {
    router.push(`/admin/menu-management?categoryId=${category.id}&categoryName=${encodeURIComponent(category.name)}`);
  };

  return (
    <div className={styles.adminContainer}>
      <div className={styles.adminHeader}>
        <h1>{t('admin_category_management_title')}</h1>
        <button className={`${styles.adminButton} ${styles.add}`} onClick={() => setIsCreateModalOpen(true)}>
          {t('create_category')}
        </button>
      </div>
      <div className={styles.adminContent}>
        {error && <p className={styles.error}>{error}</p>}
        <div className={styles.adminTableContainer}>
          <table className={styles.adminTable}>
            <thead>
              <tr>
                <th>{t('category_name')}</th>
                <th>{t('is_active')}</th>
                <th>{t('display_order')}</th>
                <th>{t('actions_header')}</th>
              </tr>
            </thead>
            <tbody>
              {categories.length > 0 ? (
                categories.map((category) => (
                  <tr key={category.id}>
                    <td>{category.name}</td>
                    <td>{category.isActive ? t('yes') : t('no')}</td>
                    <td>{category.displayOrder}</td>
                    <td>
                      <button
                        className={`${styles.adminButton} ${styles.edit}`}
                        onClick={() => handleEditClick(category)}
                      >
                        {t('edit')}
                      </button>
                      <button
                        className={`${styles.adminButton} ${styles.delete}`}
                        onClick={() => handleDeleteClick(category)}
                      >
                        {t('delete')}
                      </button>
                      <button
                        className={`${styles.adminButton} ${styles.view}`}
                        onClick={() => handleViewProducts(category)}
                      >
                        {t('view_products')}
                      </button>
                    </td>
                  </tr>
                ))
              ) : (
                <tr>
                  <td colSpan={4}>{t('no_categories_found')}</td>
                </tr>
              )}
            </tbody>
          </table>
        </div>
      </div>
      <CreateCategoryModal
        isOpen={isCreateModalOpen}
        onClose={() => setIsCreateModalOpen(false)}
        onCategoryCreated={handleCategoryCreated}
      />
      <EditCategoryModal
        isOpen={isEditModalOpen}
        onClose={() => setIsEditModalOpen(false)}
        onCategoryUpdated={handleCategoryUpdated}
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
    </div>
  );
};

export default CategoryManagementPage;
