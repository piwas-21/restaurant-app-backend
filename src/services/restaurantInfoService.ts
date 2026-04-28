import { apiClient } from '@/utils/apiClient';
import type {
  ApiResponse,
  RestaurantInfoDto,
  RestaurantPhoneNumberDto,
  UpdateRestaurantInfoCommand,
  AddPhoneNumberCommand,
  UpdatePhoneNumberCommand,
} from '@/types/restaurantInfo';

const BASE = '/api/restaurant-info';

/** Public read — no auth required. */
export const getRestaurantInfo = async () => {
  return apiClient.get<ApiResponse<RestaurantInfoDto>>(BASE);
};

/** Admin only — full upsert of the singleton's own fields. */
export const updateRestaurantInfo = async (data: UpdateRestaurantInfoCommand) => {
  return apiClient.put<ApiResponse<RestaurantInfoDto>>(BASE, data, { requireAuth: true });
};

/** Admin only — append a new phone. */
export const addPhoneNumber = async (data: AddPhoneNumberCommand) => {
  return apiClient.post<ApiResponse<RestaurantPhoneNumberDto>>(`${BASE}/phones`, data, {
    requireAuth: true,
  });
};

/** Admin only — replace by id. Route id wins over body id server-side. */
export const updatePhoneNumber = async (id: string, data: UpdatePhoneNumberCommand) => {
  return apiClient.put<ApiResponse<RestaurantPhoneNumberDto>>(`${BASE}/phones/${id}`, data, {
    requireAuth: true,
  });
};

/** Admin only — hard delete. */
export const deletePhoneNumber = async (id: string) => {
  return apiClient.delete<ApiResponse<string>>(`${BASE}/phones/${id}`, { requireAuth: true });
};
