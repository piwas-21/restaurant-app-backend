'use client';

import { useState, useEffect, useCallback, useRef } from 'react';
import { useSearchParams, useRouter } from 'next/navigation';
import { getProducts, getMenuBundles } from '@/services/menuService';
import { getCategories } from '@/services/categoryService';
import { Product, Category } from '@/app/admin/menu-management/interfaces';

export const useMenuManagement = (activeTab: 'products' | 'menus' = 'products') => {
  const _router = useRouter();
  const searchParams = useSearchParams();
  const initialCategoryId = searchParams.get('categoryId');
  const activeTabRef = useRef(activeTab);

  const [products, setProducts] = useState<Product[]>([]);
  const [categories, setCategories] = useState<Category[]>([]);
  const [selectedCategoryId, setSelectedCategoryId] = useState<string | null>(initialCategoryId);
  const [error, setError] = useState<string | null>(null);
  const [isLoading, setIsLoading] = useState(false);
  const [currentPage, setCurrentPage] = useState(1);
  const [totalPages, setTotalPages] = useState(1);
  const [totalCount, setTotalCount] = useState(0);
  const pageSize = 20;

  const fetchProducts = useCallback(
    async (page: number = 1) => {
      const requestTab = activeTab; // Capture which tab this request is for
      setIsLoading(true);
      setError(null);
      try {
        let response: any;
        if (activeTab === 'menus') {
          // Use dedicated endpoint for menu bundles (category-free)
          response = await getMenuBundles(page, pageSize);
        } else {
          // Use generic products endpoint (backend now excludes menus by default)
          response = await getProducts(page, pageSize, selectedCategoryId);
        }

        // Only update state if we're still on the same tab (check against ref)
        if (requestTab === activeTabRef.current) {
          if (response.success) {
            setProducts(response.data.items);
            setTotalPages(response.data.totalPages || 1);
            setTotalCount(response.data.totalCount || 0);
            setCurrentPage(page);
          } else {
            setError(response.message || 'Failed to fetch items');
          }
        } else {
        }
      } catch {
        if (requestTab === activeTabRef.current) {
          setError('An unexpected error occurred.');
        }
      } finally {
        if (requestTab === activeTabRef.current) {
          setIsLoading(false);
        }
      }
    },
    [activeTab, selectedCategoryId, pageSize],
  ); // selectedCategoryId only affects products tab

  useEffect(() => {
    const fetchCategories = async () => {
      try {
        // Fetch all categories for dropdown
        const response = (await getCategories(1, 100)) as { success: boolean; data?: { items: any[] } };
        if (response.success && response.data?.items && Array.isArray(response.data.items)) {
          setCategories(response.data.items);
        }
      } catch {
        // Silently fail - categories will remain empty
      }
    };
    fetchCategories();
  }, []);

  // Fetch when tab or category changes
  useEffect(() => {
    activeTabRef.current = activeTab; // Update ref to current tab
    // Only reset products on tab change to avoid flash, but consistent behavior is key
    // for category change we might want to keep showing old until new loads?
    // accepted pattern: setProducts([]) to show loading state cleanly or keep it.
    // Existing code did setProducts([]) on tab change.

    // We want to reset page to 1 when tab OR category changes
    setCurrentPage(1);
    fetchProducts(1);
    // fetchProducts closes over component state and is recreated each render;
    // adding it to deps would cause an infinite re-fetch loop. The intent is
    // to fetch when the tab or category filter changes — which the listed
    // deps already cover.
    // eslint-disable-next-line react-hooks/exhaustive-deps
  }, [activeTab, selectedCategoryId]);

  const handleCategoryChange = (event: React.ChangeEvent<HTMLSelectElement>) => {
    const categoryId = event.target.value;
    setSelectedCategoryId(categoryId === 'all' ? null : categoryId);
    // Removed router.push which was clearing params/state unnecessarily
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
    fetchProducts: (page?: number) => fetchProducts(page || currentPage),
  };
};
