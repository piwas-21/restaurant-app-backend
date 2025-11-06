'use client';

import { useState, useEffect, useCallback } from 'react';
import { useSearchParams, useRouter } from 'next/navigation';
import { getProducts } from '@/services/menuService';
import { getCategories } from '@/services/categoryService';
import { Product, Category } from '@/app/admin/menu-management/interfaces';

export const useMenuManagement = () => {
  const router = useRouter();
  const searchParams = useSearchParams();
  const initialCategoryId = searchParams.get('categoryId');

  const [products, setProducts] = useState<Product[]>([]);
  const [categories, setCategories] = useState<Category[]>([]);
  const [selectedCategoryId, setSelectedCategoryId] = useState<string | null>(initialCategoryId);
  const [error, setError] = useState<string | null>(null);
  const [isLoading, setIsLoading] = useState(false);
  const [currentPage, setCurrentPage] = useState(1);
  const [totalPages, setTotalPages] = useState(1);
  const [totalCount, setTotalCount] = useState(0);
  const pageSize = 20;

  const fetchProducts = useCallback(async (page: number = 1) => {
    setIsLoading(true);
    setError(null);
    try {
      const response = await getProducts(page, pageSize, selectedCategoryId);

      if (response.success) {
        setProducts(response.data.items);
        setTotalPages(response.data.totalPages || 1);
        setTotalCount(response.data.totalCount || 0);
        setCurrentPage(page);
      } else {
        setError(response.message || 'Failed to fetch products');
      }
    } catch {
      setError('An unexpected error occurred.');
    } finally {
      setIsLoading(false);
    }
  }, [selectedCategoryId, pageSize]);

  useEffect(() => {
    const fetchCategories = async () => {
      try {
        // Fetch all categories for dropdown
        const response = await getCategories(1, 100) as { success: boolean; data?: { items: any[] } };
        if (response.success && response.data?.items && Array.isArray(response.data.items)) {
          setCategories(response.data.items);
        }
      } catch {
        // Silently fail - categories will remain empty
      }
    };
    fetchCategories();
  }, []);

  useEffect(() => {
    setCurrentPage(1);
    fetchProducts(1);
  }, [fetchProducts]);

  const handleCategoryChange = (event: React.ChangeEvent<HTMLSelectElement>) => {
    const categoryId = event.target.value;
    setSelectedCategoryId(categoryId === 'all' ? null : categoryId);
    setCurrentPage(1);
    router.push('/admin/menu-management');
  };

  const handlePageChange = (page: number) => {
    fetchProducts(page);
    window.scrollTo({ top: 0, behavior: 'smooth' });
  };

  return {
    products,
    categories,
    selectedCategoryId,
    isLoading,
    error,
    currentPage,
    totalPages,
    totalCount,
    pageSize,
    handleCategoryChange,
    handlePageChange,
    fetchProducts: () => fetchProducts(currentPage),
  };
};
