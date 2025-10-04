import { apiClient } from './apiClient';
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

  const response = await apiClient.postFormData(`${PRODUCTS_API_URL}/${productId}/images`, formData);
  return response.json();
};

export const getProductImages = async (productId: string) => {
  const response = await apiClient.get(`${PRODUCTS_API_URL}/${productId}/images`);
  return response.json();
};

export const uploadBulkProductImages = async (productId: string, imageFiles: File[]) => {
  const formData = new FormData();
  imageFiles.forEach(file => {
    formData.append('Images', file);
  });

  const response = await apiClient.postFormData(`${PRODUCTS_API_URL}/${productId}/images/bulk`, formData);
  return response.json();
};

export const updateProduct = async (productId: string, productData: any) => {
  try {
    const response = await apiClient.put(`${PRODUCTS_API_URL}/${productId}`, productData);
    return response.json();
  } catch {
    // Fallback to mock API if real API fails
    return mockApiClient.updateProduct(productId, productData);
  }
};

export const updateProductImageDetails = async (productId: string, imageId: string, imageData: any) => {
  const response = await apiClient.put(`${PRODUCTS_API_URL}/${productId}/images/${imageId}`, imageData);
  return response.json();
};

export const deleteProductImage = async (productId: string, imageId: string) => {
  const response = await apiClient.delete(`${PRODUCTS_API_URL}/${productId}/images/${imageId}`);
  return response.json();
};

export const deleteProduct = async (productId: string) => {
  const response = await apiClient.delete(`${PRODUCTS_API_URL}/${productId}`);
  return response.json();
};
