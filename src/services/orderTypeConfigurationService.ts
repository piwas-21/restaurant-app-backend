import { OrderType } from '@/types/order';
import { apiClient } from '@/utils/apiClient';

/**
 * API Response wrapper
 */
interface ApiResponse<T> {
  data: T;
  success: boolean;
  message?: string;
  errors?: string[];
}

export interface OrderTypeConfigurationDto {
  orderType: OrderType;
  isEnabled: boolean;
  displayOrder: number;
}

export interface UpdateOrderTypeConfigurationDto {
  orderType: OrderType;
  isEnabled: boolean;
}

const ENDPOINTS = {
  GET_ALL: '/api/OrderTypeConfiguration',
  GET_ENABLED: '/api/OrderTypeConfiguration/enabled',
  UPDATE: '/api/OrderTypeConfiguration',
};

export const orderTypeConfigurationService = {
  /**
   * Get all order type configurations (admin only)
   */
  async getAll(): Promise<OrderTypeConfigurationDto[]> {
    const response = await apiClient.get<ApiResponse<OrderTypeConfigurationDto[]>>(ENDPOINTS.GET_ALL);
    return response.data;
  },

  /**
   * Get enabled order types (public endpoint)
   */
  async getEnabled(): Promise<OrderType[]> {
    const response = await apiClient.get<ApiResponse<OrderType[]>>(ENDPOINTS.GET_ENABLED);
    return response.data;
  },

  /**
   * Update order type configuration (admin only)
   */
  async update(dto: UpdateOrderTypeConfigurationDto): Promise<OrderTypeConfigurationDto> {
    const response = await apiClient.put<ApiResponse<OrderTypeConfigurationDto>>(ENDPOINTS.UPDATE, dto);
    return response.data;
  },
};
