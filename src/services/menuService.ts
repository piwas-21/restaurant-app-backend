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

export interface MenuSectionItemData {
  productId: string;
  additionalPrice: number;
  displayOrder: number;
  isDefault: boolean;
}

export interface MenuSectionData {
  name: string;
  description?: string;
  displayOrder: number;
  isRequired: boolean;
  minSelection: number;
  maxSelection: number;
  items: MenuSectionItemData[];
}

export interface MenuDefinitionData {
  isAlwaysAvailable: boolean;
  startTime?: string | null;
  endTime?: string | null;
  availableMonday: boolean;
  availableTuesday: boolean;
  availableWednesday: boolean;
  availableThursday: boolean;
  availableFriday: boolean;
  availableSaturday: boolean;
  availableSunday: boolean;
  sections: MenuSectionData[];
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
  primaryCategoryId: string | null;
  variations?: VariationData[];
  content?: ContentData;
  detailedIngredients?: any[];
  menuDefinition?: MenuDefinitionData;
}

export const getProducts = async (
  pageNumber: number = 1,
  pageSize: number = 10,
  categoryId?: string | null
): Promise<{ success: boolean; message: string; data: PaginatedProducts; errors: any }> => {
  try {
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

export const MENUS_API_URL = `${API_BASE_URL}/Menus`;

export const createMenuBundle = async (menuData: any) => {
  try {
    return await apiClient.post(MENUS_API_URL, menuData);
  } catch (error) {
    console.error("Create Menu Bundle Failed:", error);
    throw error;
  }
};

export const updateMenuBundle = async (id: string, menuData: any) => {
  try {
    return await apiClient.put(`${MENUS_API_URL}/${id}`, menuData);
  } catch (error) {
    console.error("Update Menu Bundle Failed:", error);
    throw error;
  }
};

export const getMenuBundles = async (page: number = 1, pageSize: number = 10) => {
  try {
    const url = `${MENUS_API_URL}?page=${page}&pageSize=${pageSize}`;
    return await apiClient.get(url);
  } catch (error) {
    console.error("Get Menu Bundles Failed:", error);
    throw error;
  }
};

export const getMenuBundleById = async (id: string) => {
  try {
    return await apiClient.get(`${MENUS_API_URL}/${id}`);
  } catch (error) {
    console.error("Get Menu Bundle Failed:", error);
    throw error;
  }
};

export const deleteMenuBundle = async (id: string) => {
  try {
    return await apiClient.delete(`${MENUS_API_URL}/${id}`);
  } catch (error) {
    console.error("Delete Menu Bundle Failed:", error);
    throw error;
  }
};

export const createProduct = async (productData: CreateProductData) => {
  try {
    return await apiClient.post(PRODUCTS_API_URL, productData);
  } catch (error) {
    console.error("Create Product Failed:", error);
    throw error;
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
