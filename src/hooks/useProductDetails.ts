'use client';

import { useState, useEffect, useCallback } from 'react';
import { getProductById } from '@/services/menuService';
import { ProductDetails } from '@/app/admin/menu-management/interfaces';

export const useProductDetails = (productId: string) => {
  const [product, setProduct] = useState<ProductDetails | null>(null);
  const [isLoading, setIsLoading] = useState(true);
  const [error, setError] = useState<string | null>(null);

  const fetchProductData = useCallback(async () => {
    if (!productId) return;
    
    setIsLoading(true);
    setError(null);
    try {
      const productResponse = await getProductById(productId);
      if (productResponse.success) {
        setProduct(productResponse.data);
      } else {
        setError(productResponse.message || 'Failed to fetch product details.');
      }
    } catch (err) {
      setError('An unexpected error occurred.');
    } finally {
      setIsLoading(false);
    }
  }, [productId]);

  useEffect(() => {
    fetchProductData();
  }, [fetchProductData]);

  return { product, isLoading, error, fetchProductData };
};
