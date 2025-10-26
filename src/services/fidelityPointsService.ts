import { apiClient } from '@/utils/apiClient';
import type {
  FidelityPointBalance,
  FidelityPointsTransaction,
  PointsHistoryParams,
} from '@/types/fidelity';

/**
 * API Response wrapper matching backend
 */
interface ApiResponse<T> {
  data: T;
  success: boolean;
  message?: string;
  errors?: string[];
}

const FIDELITY_ENDPOINTS = {
  BALANCE: '/api/FidelityPoints/balance',
  HISTORY: '/api/FidelityPoints/history',
  CALCULATE_DISCOUNT: '/api/FidelityPoints/calculate-discount',
  CALCULATE_POINTS: '/api/FidelityPoints/calculate-points',
} as const;

export const fidelityPointsService = {
  /**
   * Get user's current fidelity points balance
   */
  async getBalance(): Promise<FidelityPointBalance> {
    const response = await apiClient.get<ApiResponse<FidelityPointBalance>>(
      FIDELITY_ENDPOINTS.BALANCE,
      { requireAuth: true }
    );
    return response.data;
  },

  /**
   * Get user's points transaction history
   */
  async getHistory(
    params: PointsHistoryParams = {}
  ): Promise<FidelityPointsTransaction[]> {
    const { page = 1, pageSize = 50 } = params;
    const queryParams = new URLSearchParams({
      page: page.toString(),
      pageSize: pageSize.toString(),
    });
    const response = await apiClient.get<ApiResponse<FidelityPointsTransaction[]>>(
      `${FIDELITY_ENDPOINTS.HISTORY}?${queryParams.toString()}`,
      { requireAuth: true }
    );
    return response.data;
  },

  /**
   * Calculate discount amount from points
   * @param points - Number of points to convert to discount
   * @returns Discount amount in currency
   */
  async calculateDiscount(points: number): Promise<number> {
    const queryParams = new URLSearchParams({
      points: points.toString(),
    });
    const response = await apiClient.get<ApiResponse<number>>(
      `${FIDELITY_ENDPOINTS.CALCULATE_DISCOUNT}?${queryParams.toString()}`,
      { requireAuth: true }
    );
    return response.data;
  },

  /**
   * Calculate points needed for a specific discount amount
   * @param discountAmount - Desired discount amount in currency
   * @returns Number of points needed
   */
  async calculatePoints(discountAmount: number): Promise<number> {
    const queryParams = new URLSearchParams({
      discountAmount: discountAmount.toString(),
    });
    const response = await apiClient.get<ApiResponse<number>>(
      `${FIDELITY_ENDPOINTS.CALCULATE_POINTS}?${queryParams.toString()}`,
      { requireAuth: true }
    );
    return response.data;
  },

  /**
   * Format points as currency value
   * @param points - Number of points
   * @returns Formatted currency string (e.g., "$5.00")
   */
  formatPointsAsCurrency(points: number): string {
    const value = points / 100; // 100 points = $1
    return new Intl.NumberFormat('en-US', {
      style: 'currency',
      currency: 'USD',
    }).format(value);
  },

  /**
   * Get transaction type display label
   */
  getTransactionTypeLabel(type: string): string {
    const labels: Record<string, string> = {
      Earned: 'Points Earned',
      Redeemed: 'Points Redeemed',
      AdminAdjustment: 'Admin Adjustment',
      Expired: 'Points Expired',
      Refunded: 'Points Refunded',
    };
    return labels[type] || type;
  },

  /**
   * Get transaction type color for UI
   */
  getTransactionTypeColor(type: string): string {
    const colors: Record<string, string> = {
      Earned: 'text-green-600',
      Redeemed: 'text-blue-600',
      AdminAdjustment: 'text-purple-600',
      Expired: 'text-gray-600',
      Refunded: 'text-orange-600',
    };
    return colors[type] || 'text-gray-600';
  },
};
