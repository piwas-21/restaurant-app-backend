import { apiClient } from './apiClient';

const API_BASE_URL = '/api';
const CATEGORIES_API_URL = `${API_BASE_URL}/Categories`;
const PRODUCTS_API_URL = `${API_BASE_URL}/Products`;

// Interfaces for Product Retrieval
interface Product {
  id: string;
  name: string;
  price: number;
  // Add other product properties as needed based on the full API response
}

interface PaginatedProducts {
  items: Product[];
  totalCount: number;
  page: number;
  pageSize: number;
  totalPages: number;
}

// Interfaces for Product Creation
interface VariationData {
  name: string;
  description?: string;
  priceModifier: number;
  isActive: boolean;
  displayOrder: number;
}

interface ContentData {
  [languageCode: string]: {
    name: string;
    description: string;
  };
}

export interface CreateProductData {
  name: string;
  description?: string;
  basePrice: number;
  isActive: boolean;
  isAvailable: boolean;
  preparationTimeMinutes?: number;
  type: string;
  ingredients?: string[];
  allergens?: string[];
  displayOrder?: number;
  categoryIds: string[];
  primaryCategoryId: string;
  variations?: VariationData[];
  content?: ContentData;
}

export const getProductsByCategoryId = async (
  categoryId: string,
  pageNumber: number = 1,
  pageSize: number = 10
): Promise<{ success: boolean; message: string; data: PaginatedProducts; errors: any }> => {
  const response = await apiClient.get(
    `${CATEGORIES_API_URL}/${categoryId}/products?pageNumber=${pageNumber}&pageSize=${pageSize}`
  );
  return response.json();
};

export const createProduct = async (productData: CreateProductData) => {
  const response = await apiClient.post(PRODUCTS_API_URL, productData);
  return response.json();
};
