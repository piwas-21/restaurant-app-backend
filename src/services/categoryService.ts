import { apiClient } from './apiClient';
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
  const response = await apiClient.post(CATEGORIES_API_URL, categoryData);
  return response.json();
};

export const updateCategory = async (categoryId: string, categoryData: UpdateCategoryData) => {
  const response = await apiClient.put(`${CATEGORIES_API_URL}/${categoryId}`, categoryData);
  return response.json();
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
  const response = await apiClient.put(`${CATEGORIES_API_URL}/reorder`, payload);
  return response.json();
};

export const deleteCategory = async (categoryId: string) => {
  // The existing apiClient.delete is designed for a body, but passing undefined works for body-less requests.
  // The API spec DELETE /api/Categories/{id} does not require a body.
  const response = await apiClient.delete(`${CATEGORIES_API_URL}/${categoryId}`);
  return response.json();
};

export const uploadCategoryImage = async (categoryId: string, imageFile: File) => {
  const formData = new FormData();
  formData.append('Image', imageFile);

  const response = await apiClient.putFormData(`${CATEGORIES_API_URL}/${categoryId}/image`, formData);
  return response.json();
};

export const getCategories = async () => {
  try {
    const response = await apiClient.get(CATEGORIES_API_URL);
    return response.json();
  } catch {
    // Fallback to mock API if real API fails
    return mockApiClient.getCategories();
  }
};
