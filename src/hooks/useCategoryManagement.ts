'use client';

import { useState, useEffect, useCallback } from 'react';
import { getCategories, deleteCategory } from '@/services/categoryService';
import { Category } from '@/app/admin/menu-management/interfaces';

export const useCategoryManagement = () => {
  const [categories, setCategories] = useState<Category[]>([]);
  const [error, setError] = useState<string | null>(null);
  const [isLoading, setIsLoading] = useState(false);
  const [currentPage, setCurrentPage] = useState(1);
  const [totalPages, setTotalPages] = useState(1);
  const [totalCount, setTotalCount] = useState(0);
  const pageSize = 10;

  const fetchCategories = useCallback(async (page: number = 1) => {
    setIsLoading(true);
    setError(null);
    try {
      const response = await getCategories(page, pageSize) as {
        success: boolean;
        data?: {
          items: any[];
          totalCount: number;
          totalPages: number;
          page: number;
        };
        message?: string
      };
      if (response.success && response.data?.items) {
        setCategories(response.data.items);
        setTotalCount(response.data.totalCount || 0);
        setTotalPages(response.data.totalPages || 1);
        setCurrentPage(page);
      } else {
        setError(response.message || 'Failed to fetch categories');
      }
    } catch {
      setError('An unexpected error occurred.');
    } finally {
      setIsLoading(false);
    }
  }, [pageSize]);

  useEffect(() => {
    fetchCategories(1);
  }, [fetchCategories]);

  const handleDeleteCategory = async (categoryId: string) => {
    try {
      const response = await deleteCategory(categoryId) as { success: boolean; message?: string; errors?: string[] };
      if (response.success) {
        // If after deletion the current page becomes empty and it's not page 1, go to previous page
        if (categories.length === 1 && currentPage > 1) {
          fetchCategories(currentPage - 1);
        } else {
          fetchCategories(currentPage); // Refresh the current page
        }
        return { success: true, message: 'category_deleted_successfully' };
      } else {
        const errorMessage = response.errors ? response.errors.join(', ') : response.message;
        return { success: false, message: errorMessage || 'failed_to_delete_category' };
      }
    } catch {
      return { success: false, message: 'delete_category_error' };
    }
  };

  const handlePageChange = useCallback((page: number) => {
    fetchCategories(page);
  }, [fetchCategories]);

  return {
    categories,
    isLoading,
    error,
    currentPage,
    totalPages,
    totalCount,
    pageSize,
    fetchCategories,
    handleDeleteCategory,
    handlePageChange,
  };
};
