import { apiClient } from '@/utils/apiClient';
import { mockApiClient } from './mockApiClient';
import { Product } from '@/app/admin/menu-management/interfaces';

const API_BASE_URL = '/api';
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
  isSpecial?: boolean;
  preparationTimeMinutes?: number;
  type: string;
  allergens?: string[];
  displayOrder?: number;
  categoryIds: string[];
  primaryCategoryId: string;
  variations?: VariationData[];
  content?: ContentData;
  detailedIngredients?: any[];
}

export const getProducts = async (
  pageNumber: number = 1,
  pageSize: number = 10,
  categoryId?: string | null
): Promise<{ success: boolean; message: string; data: PaginatedProducts; errors: any }> => {
  try {
    // Backend expects 'Page' and 'PageSize' (PascalCase), not 'pageNumber' and 'pageSize' (camelCase)
    let url = `${PRODUCTS_API_URL}?Page=${pageNumber}&PageSize=${pageSize}`;
    if (categoryId) {
      url += `&CategoryId=${categoryId}`;
    }
    return await apiClient.get(url) as { success: boolean; message: string; data: PaginatedProducts; errors: any };
  } catch {
    // Fallback to mock API if real API fails
    return mockApiClient.getProducts(pageNumber, pageSize, categoryId);
  }
};

export const createProduct = async (productData: CreateProductData) => {
  try {
    return await apiClient.post(PRODUCTS_API_URL, productData);
  } catch {
    // Fallback to mock API if real API fails
    return mockApiClient.createProduct(productData);
  }
};

export const getProductById = async (productId: string) => {
  try {
    return await apiClient.get(`${PRODUCTS_API_URL}/${productId}`);
  } catch {
    // Fallback to mock API if real API fails
    return mockApiClient.getProductById(productId);
  }
};

export const getFeaturedSpecial = async () => {
  try {
    return await apiClient.get(`${PRODUCTS_API_URL}/featured-special`);
  } catch {
    // Return null if no featured special or API fails
    return { success: true, data: null, message: 'No featured special available' };
  }
};
