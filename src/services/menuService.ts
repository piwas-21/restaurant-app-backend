import { apiClient } from './apiClient';
import { Product } from '@/app/admin/menu-management/interfaces';

const API_BASE_URL = '/api';
const CATEGORIES_API_URL = `${API_BASE_URL}/Categories`;
const PRODUCTS_API_URL = `${API_BASE_URL}/Products`;

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

export const getProducts = async (
  pageNumber: number = 1,
  pageSize: number = 10,
  categoryId?: string | null
): Promise<{ success: boolean; message: string; data: PaginatedProducts; errors: any }> => {
  let url = `${PRODUCTS_API_URL}?pageNumber=${pageNumber}&pageSize=${pageSize}`;
  if (categoryId) {
    url += `&CategoryId=${categoryId}`;
  }
  const response = await apiClient.get(url);
  return response.json();
};

export const createProduct = async (productData: CreateProductData) => {
  const response = await apiClient.post(PRODUCTS_API_URL, productData);
  return response.json();
};

export const getProductById = async (productId: string) => {
  const response = await apiClient.get(`${PRODUCTS_API_URL}/${productId}`);
  return response.json();
};
