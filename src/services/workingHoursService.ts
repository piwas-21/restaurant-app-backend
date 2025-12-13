import { apiClient } from '@/utils/apiClient';
import { WorkingHoursDto, UpdateWorkingHoursDto } from '@/types/workingHours';

interface ApiResponse<T> {
  data: T;
  success: boolean;
  message?: string;
  errors?: string[];
}

const ENDPOINTS = {
  GET_ALL: '/api/WorkingHours',
  GET_BY_DAY: (dayOfWeek: number) => `/api/WorkingHours/${dayOfWeek}`,
  IS_OPEN: '/api/WorkingHours/is-open',
  GET_TODAY: '/api/WorkingHours/today',
  UPDATE: '/api/WorkingHours',
};

export const workingHoursService = {
  /**
   * Get all working hours for all days
   */
  async getAll(): Promise<WorkingHoursDto[]> {
    const response = await apiClient.get<ApiResponse<WorkingHoursDto[]>>(ENDPOINTS.GET_ALL);
    return response.data;
  },

  /**
   * Get working hours for a specific day
   */
  async getByDay(dayOfWeek: number): Promise<WorkingHoursDto> {
    const response = await apiClient.get<ApiResponse<WorkingHoursDto>>(ENDPOINTS.GET_BY_DAY(dayOfWeek));
    return response.data;
  },

  /**
   * Check if restaurant is currently open
   */
  async isOpenNow(): Promise<boolean> {
    const response = await apiClient.get<ApiResponse<boolean>>(ENDPOINTS.IS_OPEN);
    return response.data;
  },

  /**
   * Get today's working hours
   */
  async getToday(): Promise<WorkingHoursDto> {
    const response = await apiClient.get<ApiResponse<WorkingHoursDto>>(ENDPOINTS.GET_TODAY);
    return response.data;
  },

  /**
   * Update working hours (admin only)
   */
  async update(dto: UpdateWorkingHoursDto): Promise<WorkingHoursDto> {
    const response = await apiClient.put<ApiResponse<WorkingHoursDto>>(ENDPOINTS.UPDATE, dto);
    return response.data;
  },
};
