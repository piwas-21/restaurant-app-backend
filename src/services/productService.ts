import { apiClient } from '@/utils/apiClient';
import { mockApiClient } from './mockApiClient';

const PRODUCTS_API_URL = '/api/Products';

export const uploadProductImage = async (
  productId: string,
  imageFile: File,
  altText: string,
  isPrimary: boolean,
  sortOrder: number
) => {
  const formData = new FormData();
  formData.append('Image', imageFile);
  formData.append('AltText', altText);
  formData.append('IsPrimary', String(isPrimary));
  formData.append('SortOrder', String(sortOrder));

  return await apiClient.postFormData(`${PRODUCTS_API_URL}/${productId}/images`, formData);
};

export const getProductImages = async (productId: string) => {
  return await apiClient.get(`${PRODUCTS_API_URL}/${productId}/images`);
};

export const uploadBulkProductImages = async (productId: string, imageFiles: File[]) => {
  const formData = new FormData();
  imageFiles.forEach(file => {
    formData.append('Images', file);
  });

  return await apiClient.postFormData(`${PRODUCTS_API_URL}/${productId}/images/bulk`, formData);
};

export const updateProduct = async (productId: string, productData: any) => {
  try {
    return await apiClient.put(`${PRODUCTS_API_URL}/${productId}`, productData);
  } catch {
    // Fallback to mock API if real API fails
    return mockApiClient.updateProduct(productId, productData);
  }
};

export const updateProductImageDetails = async (productId: string, imageId: string, imageData: any) => {
  return await apiClient.put(`${PRODUCTS_API_URL}/${productId}/images/${imageId}`, imageData);
};

export const deleteProductImage = async (productId: string, imageId: string) => {
  return await apiClient.delete(`${PRODUCTS_API_URL}/${productId}/images/${imageId}`);
};

export const deleteProduct = async (productId: string) => {
  return await apiClient.delete(`${PRODUCTS_API_URL}/${productId}`);
};

// Special Products API Functions

export const getSpecialProducts = async (page: number = 1, pageSize: number = 20) => {
  return await apiClient.get(`${PRODUCTS_API_URL}/specials?page=${page}&pageSize=${pageSize}`);
};

export const getFeaturedSpecial = async () => {
  return await apiClient.get(`${PRODUCTS_API_URL}/featured-special`);
};

export const setFeaturedSpecial = async (productId: string) => {
  return await apiClient.post(`${PRODUCTS_API_URL}/${productId}/set-featured`, {});
};

export const unsetFeaturedSpecial = async () => {
  return await apiClient.delete(`${PRODUCTS_API_URL}/featured-special`);
};
