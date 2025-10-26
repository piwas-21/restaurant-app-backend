import { apiClient } from '@/utils/apiClient';
import { mockApiClient } from './mockApiClient';

const CATEGORIES_API_URL = '/api/Categories';

interface CategoryData {
  name: string;
  description?: string;
  isActive: boolean;
}

// This interface is for the main update, without displayOrder
interface UpdateCategoryData extends CategoryData {
  id: string;
}

export const createCategory = async (categoryData: CategoryData & { displayOrder: number }) => {
  return await apiClient.post(CATEGORIES_API_URL, categoryData);
};

export const updateCategory = async (categoryId: string, categoryData: UpdateCategoryData) => {
  return await apiClient.put(`${CATEGORIES_API_URL}/${categoryId}`, categoryData);
};

export const reorderCategory = async (categoryId: string, displayOrder: number) => {
  const payload = {
    categoryOrders: [
      {
        categoryId: categoryId,
        displayOrder: displayOrder,
      },
    ],
  };
  return await apiClient.put(`${CATEGORIES_API_URL}/reorder`, payload);
};

export const deleteCategory = async (categoryId: string) => {
  return await apiClient.delete(`${CATEGORIES_API_URL}/${categoryId}`);
};

export const uploadCategoryImage = async (categoryId: string, imageFile: File) => {
  const formData = new FormData();
  formData.append('Image', imageFile);

  return await apiClient.putFormData(`${CATEGORIES_API_URL}/${categoryId}/image`, formData);
};

export const getCategories = async () => {
  try {
    return await apiClient.get(CATEGORIES_API_URL);
  } catch {
    // Fallback to mock API if real API fails
    return mockApiClient.getCategories();
  }
};
