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
 * Fidelity Analytics Data
 */
export interface FidelityAnalytics {
  totalPointsIssued: number;
  totalPointsRedeemed: number;
  totalActiveUsers: number;
  totalPointsOutstanding: number;
  averagePointsPerUser: number;
  totalDiscountGiven: number;
  activePointRules: number;
  activeCustomerDiscounts: number;
  recentTransactionsCount: number;
}

/**
 * Admin Fidelity Analytics Service
 */
export const adminFidelityAnalyticsService = {
  /**
   * Get fidelity system analytics
   */
  async getAnalytics(): Promise<FidelityAnalytics> {
    const response = await apiClient.get<ApiResponse<FidelityAnalytics>>(
      '/api/Admin/FidelityAnalytics'
    );
    return response.data;
  },
};
