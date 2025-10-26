import { apiClient } from '@/utils/apiClient';
import type {
  PointEarningRule,
  CustomerDiscountRule,
} from '@/types/fidelity';

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
 * Create Point Earning Rule DTO
 */
export interface CreatePointRuleDto {
  name: string;
  minOrderAmount: number;
  maxOrderAmount?: number;
  pointsAwarded: number;
  isActive: boolean;
  priority: number;
}

/**
 * Update Point Earning Rule DTO
 */
export interface UpdatePointRuleDto extends CreatePointRuleDto {
  id: string;
}

/**
 * Create Customer Discount Rule DTO
 */
export interface CreateCustomerDiscountDto {
  userId: string;
  name: string;
  discountType: 'Percentage' | 'FixedAmount';
  discountValue: number;
  minOrderAmount?: number;
  maxOrderAmount?: number;
  maxUsageCount?: number;
  isActive: boolean;
  validFrom?: string;
  validUntil?: string;
}

/**
 * Update Customer Discount Rule DTO
 */
export interface UpdateCustomerDiscountDto extends CreateCustomerDiscountDto {
  id: string;
}

/**
 * Validate Rule Request
 */
export interface ValidateRuleRequest {
  minOrderAmount: number;
  maxOrderAmount?: number;
  excludeRuleId?: string;
}

const ADMIN_ENDPOINTS = {
  POINT_RULES: '/api/admin/PointRules',
  CUSTOMER_DISCOUNTS: '/api/admin/CustomerDiscounts',
} as const;

export const adminFidelityService = {
  // ==================== Point Earning Rules ====================

  /**
   * Get all point earning rules
   */
  async getPointRules(activeOnly: boolean = false): Promise<PointEarningRule[]> {
    const params = new URLSearchParams();
    if (activeOnly) {
      params.append('activeOnly', 'true');
    }
    const url = `${ADMIN_ENDPOINTS.POINT_RULES}${params.toString() ? `?${params.toString()}` : ''}`;
    const response = await apiClient.get<ApiResponse<PointEarningRule[]>>(url, {
      requireAuth: true,
    });
    return response.data;
  },

  /**
   * Get point earning rule by ID
   */
  async getPointRuleById(id: string): Promise<PointEarningRule> {
    const response = await apiClient.get<ApiResponse<PointEarningRule>>(
      `${ADMIN_ENDPOINTS.POINT_RULES}/${id}`,
      { requireAuth: true }
    );
    return response.data;
  },

  /**
   * Create new point earning rule
   */
  async createPointRule(rule: CreatePointRuleDto): Promise<PointEarningRule> {
    const response = await apiClient.post<ApiResponse<PointEarningRule>>(
      ADMIN_ENDPOINTS.POINT_RULES,
      rule,
      { requireAuth: true }
    );
    return response.data;
  },

  /**
   * Update existing point earning rule
   */
  async updatePointRule(id: string, rule: UpdatePointRuleDto): Promise<PointEarningRule> {
    const response = await apiClient.put<ApiResponse<PointEarningRule>>(
      `${ADMIN_ENDPOINTS.POINT_RULES}/${id}`,
      rule,
      { requireAuth: true }
    );
    return response.data;
  },

  /**
   * Delete point earning rule
   */
  async deletePointRule(id: string): Promise<void> {
    await apiClient.delete<ApiResponse<void>>(
      `${ADMIN_ENDPOINTS.POINT_RULES}/${id}`,
      { requireAuth: true }
    );
  },

  /**
   * Validate point earning rule (check for overlaps)
   */
  async validatePointRule(request: ValidateRuleRequest): Promise<boolean> {
    const response = await apiClient.post<ApiResponse<boolean>>(
      `${ADMIN_ENDPOINTS.POINT_RULES}/validate`,
      request,
      { requireAuth: true }
    );
    return response.data;
  },

  // ==================== Customer Discount Rules ====================

  /**
   * Get all customer discount rules with optional filters
   */
  async getCustomerDiscounts(
    userId?: string,
    activeOnly: boolean = false
  ): Promise<CustomerDiscountRule[]> {
    const params = new URLSearchParams();
    if (userId) {
      params.append('userId', userId);
    }
    if (activeOnly) {
      params.append('activeOnly', 'true');
    }
    const url = `${ADMIN_ENDPOINTS.CUSTOMER_DISCOUNTS}${params.toString() ? `?${params.toString()}` : ''}`;
    const response = await apiClient.get<ApiResponse<CustomerDiscountRule[]>>(url, {
      requireAuth: true,
    });
    return response.data;
  },

  /**
   * Get customer discount rule by ID
   */
  async getCustomerDiscountById(id: string): Promise<CustomerDiscountRule> {
    const response = await apiClient.get<ApiResponse<CustomerDiscountRule>>(
      `${ADMIN_ENDPOINTS.CUSTOMER_DISCOUNTS}/${id}`,
      { requireAuth: true }
    );
    return response.data;
  },

  /**
   * Create new customer discount rule
   */
  async createCustomerDiscount(
    discount: CreateCustomerDiscountDto
  ): Promise<CustomerDiscountRule> {
    const response = await apiClient.post<ApiResponse<CustomerDiscountRule>>(
      ADMIN_ENDPOINTS.CUSTOMER_DISCOUNTS,
      discount,
      { requireAuth: true }
    );
    return response.data;
  },

  /**
   * Update existing customer discount rule
   */
  async updateCustomerDiscount(
    id: string,
    discount: UpdateCustomerDiscountDto
  ): Promise<CustomerDiscountRule> {
    const response = await apiClient.put<ApiResponse<CustomerDiscountRule>>(
      `${ADMIN_ENDPOINTS.CUSTOMER_DISCOUNTS}/${id}`,
      discount,
      { requireAuth: true }
    );
    return response.data;
  },

  /**
   * Delete customer discount rule
   */
  async deleteCustomerDiscount(id: string): Promise<void> {
    await apiClient.delete<ApiResponse<void>>(
      `${ADMIN_ENDPOINTS.CUSTOMER_DISCOUNTS}/${id}`,
      { requireAuth: true }
    );
  },

  // ==================== Helper Methods ====================

  /**
   * Format currency value
   */
  formatCurrency(value: number): string {
    return new Intl.NumberFormat('en-US', {
      style: 'currency',
      currency: 'USD',
    }).format(value);
  },

  /**
   * Format date
   */
  formatDate(dateString: string): string {
    const date = new Date(dateString);
    return new Intl.DateTimeFormat('en-US', {
      year: 'numeric',
      month: 'short',
      day: 'numeric',
    }).format(date);
  },

  /**
   * Get discount type label
   */
  getDiscountTypeLabel(type: string): string {
    const labels: Record<string, string> = {
      Percentage: 'Percentage',
      FixedAmount: 'Fixed Amount',
    };
    return labels[type] || type;
  },
};
