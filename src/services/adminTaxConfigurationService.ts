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

/**
 * Tax Configuration interface
 */
export interface TaxConfiguration {
  id: string;
  name: string;
  rate: number;
  isEnabled: boolean;
  description: string;
}

/**
 * Create Tax Configuration DTO
 */
export interface CreateTaxConfigurationDto {
  name: string;
  rate: number;
  isEnabled: boolean;
  description: string;
}

/**
 * Update Tax Configuration DTO
 */
export interface UpdateTaxConfigurationDto extends CreateTaxConfigurationDto {
  id: string;
}

/**
 * Tax Configuration API Endpoints
 */
const ENDPOINTS = {
  TAX_CONFIGURATION: '/api/TaxConfiguration',
  TAX_CONFIGURATION_ACTIVE: '/api/TaxConfiguration/active',
  TAX_CONFIGURATION_BY_ID: (id: string) => `/api/TaxConfiguration/${id}`,
} as const;

/**
 * Admin Tax Configuration Service
 * Handles all tax configuration management operations
 */
export const adminTaxConfigurationService = {
  /**
   * Get all tax configurations
   */
  async getAllTaxConfigurations(): Promise<TaxConfiguration[]> {
    const response = await apiClient.get<ApiResponse<TaxConfiguration[]>>(
      ENDPOINTS.TAX_CONFIGURATION
    );
    return response.data;
  },

  /**
   * Get active tax configuration
   */
  async getActiveTaxConfiguration(): Promise<TaxConfiguration | null> {
    const response = await apiClient.get<ApiResponse<TaxConfiguration | null>>(
      ENDPOINTS.TAX_CONFIGURATION_ACTIVE
    );
    return response.data;
  },

  /**
   * Get tax configuration by ID
   */
  async getTaxConfigurationById(id: string): Promise<TaxConfiguration> {
    const response = await apiClient.get<ApiResponse<TaxConfiguration>>(
      ENDPOINTS.TAX_CONFIGURATION_BY_ID(id)
    );
    return response.data;
  },

  /**
   * Create a new tax configuration
   */
  async createTaxConfiguration(
    dto: CreateTaxConfigurationDto
  ): Promise<TaxConfiguration> {
    const response = await apiClient.post<ApiResponse<TaxConfiguration>>(
      ENDPOINTS.TAX_CONFIGURATION,
      dto
    );
    return response.data;
  },

  /**
   * Update an existing tax configuration
   */
  async updateTaxConfiguration(
    dto: UpdateTaxConfigurationDto
  ): Promise<TaxConfiguration> {
    const response = await apiClient.put<ApiResponse<TaxConfiguration>>(
      ENDPOINTS.TAX_CONFIGURATION,
      dto
    );
    return response.data;
  },

  /**
   * Delete a tax configuration
   */
  async deleteTaxConfiguration(id: string): Promise<void> {
    await apiClient.delete<ApiResponse<boolean>>(
      ENDPOINTS.TAX_CONFIGURATION_BY_ID(id)
    );
  },
};
