'use client';

import { useState, useEffect, useCallback } from 'react';
import { getProductById } from '@/services/menuService';
import { updateProductImageDetails } from '@/services/productService';
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
      const productResponse = await getProductById(productId) as { success: boolean; data?: any; message?: string };
      if (productResponse.success && productResponse.data) {
        let productData = productResponse.data;
        // If there's only one image, ensure it's primary and has a sort order of 1
        if (productData.images && productData.images.length === 1) {
          const image = productData.images[0];
          if (!image.isPrimary || image.sortOrder !== 1) {
            await updateProductImageDetails(productId, image.id, { ...image, isPrimary: true, sortOrder: 1 });
            // Refetch to get the updated data
            const updatedResponse = await getProductById(productId) as { success: boolean; data?: any };
            if (updatedResponse.success && updatedResponse.data) {
              productData = updatedResponse.data;
            }
          }
        }
        setProduct(productData);
      } else {
        setError(productResponse.message || 'Failed to fetch product details.');
      }
    } catch {
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
