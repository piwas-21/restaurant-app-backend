/**
 * Order Service
 *
 * Service layer for interacting with the Order API.
 * Handles order creation, retrieval, status updates, payments, etc.
 */

import { apiClient } from '@/utils/apiClient';
import {
  CreateOrderCommand,
  OrderDto,
  UpdateOrderStatusCommand,
  CancelOrderCommand,
  ToggleFocusOrderCommand,
  AddPaymentToOrderCommand,
  RefundPaymentCommand,
  OrderQueryFilters,
  FocusOrdersQueryFilters,
  OrderDtoApiResponse,
  OrderDtoListApiResponse,
  OrderDtoPagedResultApiResponse,
  OrderPaymentDtoApiResponse,
  PagedResult,
  OrderPaymentDto,
  ApiResponse,
  ZReportDto,
  ZReportApiResponse,
} from '@/types/order';

/**
 * Create a new order
 *
 * @param command - Order creation details
 * @returns Created order
 */
export async function createOrder(command: CreateOrderCommand): Promise<OrderDto> {
  try {
    const response = await apiClient.post<OrderDtoApiResponse>(
      '/api/Orders',
      command,
      { requireAuth: false }
    );
    if (!response.data) {
      throw new Error('Failed to create order');
    }
    return response.data;
  } catch (error) {
    // eslint-disable-next-line no-console
    console.error('Error creating order:', error);
    throw error;
  }
}

/**
 * Get orders for the current authenticated user
 *
 * @param filters - Optional query filters
 * @returns Paged list of orders for current user
 */
export async function getMyOrders(
  filters?: Partial<OrderQueryFilters>
): Promise<PagedResult<OrderDto>> {
  try {
    // Build query string from filters
    const params = new URLSearchParams();
    if (filters) {
      Object.entries(filters).forEach(([key, value]) => {
        if (value !== undefined && value !== null) {
          params.append(key, String(value));
        }
      });
    }

    const queryString = params.toString();
    const endpoint = queryString ? `/api/Orders?${queryString}` : '/api/Orders';

    const response = await apiClient.get<OrderDtoPagedResultApiResponse>(
      endpoint,
      { requireAuth: true }
    );

    if (!response.data) {
      throw new Error('Failed to fetch orders');
    }

    return response.data;
  } catch (error) {
    // eslint-disable-next-line no-console
    console.error('Error fetching my orders:', error);
    throw error;
  }
}

/**
 * Get orders with optional filters
 *
 * @param filters - Query filters (status, payment status, date range, etc.)
 * @returns Paged list of orders
 */
export async function getOrders(
  filters?: OrderQueryFilters
): Promise<PagedResult<OrderDto>> {
  try {
    // Build query string from filters
    const params = new URLSearchParams();
    if (filters) {
      Object.entries(filters).forEach(([key, value]) => {
        if (value !== undefined && value !== null) {
          params.append(key, String(value));
        }
      });
    }

    const queryString = params.toString();
    const endpoint = queryString ? `/api/Orders?${queryString}` : '/api/Orders';

    const response = await apiClient.get<OrderDtoPagedResultApiResponse>(
      endpoint,
      { requireAuth: true }
    );

    if (!response.data) {
      throw new Error('Failed to fetch orders');
    }

    return response.data;
  } catch (error) {
    // eslint-disable-next-line no-console
    console.error('Error fetching orders:', error);
    throw error;
  }
}

/**
 * Get order by ID
 *
 * @param orderId - Order ID
 * @returns Order details
 */
export async function getOrderById(orderId: string): Promise<OrderDto> {
  try {
    const response = await apiClient.get<OrderDtoApiResponse>(`/api/Orders/${orderId}`);
    if (!response.data) {
      throw new Error('Failed to fetch order');
    }
    return response.data;
  } catch (error) {
    // eslint-disable-next-line no-console
    console.error('Error fetching order:', error);
    throw error;
  }
}

/**
 * Update order status
 *
 * @param orderId - Order ID
 * @param command - Status update details
 * @returns Updated order
 */
export async function updateOrderStatus(
  orderId: string,
  command: UpdateOrderStatusCommand
): Promise<OrderDto> {
  try {
    const response = await apiClient.put<OrderDtoApiResponse>(
      `/api/Orders/${orderId}/status`,
      command,
      { requireAuth: true }
    );
    if (!response.data) {
      throw new Error('Failed to update order status');
    }
    return response.data;
  } catch (error) {
    // eslint-disable-next-line no-console
    console.error('Error updating order status:', error);
    throw error;
  }
}

/**
 * Cancel order
 *
 * @param orderId - Order ID
 * @param command - Cancellation details
 * @returns Updated order
 */
export async function cancelOrder(
  orderId: string,
  command: CancelOrderCommand
): Promise<OrderDto> {
  try {
    const response = await apiClient.post<OrderDtoApiResponse>(
      `/api/Orders/${orderId}/cancel`,
      command,
      { requireAuth: true }
    );
    if (!response.data) {
      throw new Error('Failed to cancel order');
    }
    return response.data;
  } catch (error) {
    // eslint-disable-next-line no-console
    console.error('Error cancelling order:', error);
    throw error;
  }
}

/**
 * Delete an order (soft delete - Admin only)
 *
 * @param orderId - Order ID
 */
export async function deleteOrder(orderId: string): Promise<void> {
  try {
    const response = await apiClient.delete<ApiResponse<boolean>>(`/api/Orders/${orderId}`, { requireAuth: true });

    if (!response.success) {
      throw new Error(response.message || 'Failed to delete order');
    }
  } catch (error: any) {
    // eslint-disable-next-line no-console
    console.error('Error deleting order:', error);
    throw error;
  }
}

/**
 * Toggle focus order status
 *
 * @param orderId - Order ID
 * @param command - Focus order details
 * @returns Updated order
 */
export async function toggleFocusOrder(
  orderId: string,
  command: ToggleFocusOrderCommand
): Promise<OrderDto> {
  try {
    const response = await apiClient.put<OrderDtoApiResponse>(
      `/api/Orders/${orderId}/focus`,
      command,
      { requireAuth: true }
    );
    if (!response.data) {
      throw new Error('Failed to toggle focus order');
    }
    return response.data;
  } catch (error) {
    // eslint-disable-next-line no-console
    console.error('Error toggling focus order:', error);
    throw error;
  }
}

/**
 * Get focus orders
 *
 * @param filters - Focus order filters
 * @returns List of focus orders
 */
export async function getFocusOrders(
  filters?: FocusOrdersQueryFilters
): Promise<OrderDto[]> {
  try {
    // Build query string from filters
    const params = new URLSearchParams();
    if (filters) {
      Object.entries(filters).forEach(([key, value]) => {
        if (value !== undefined && value !== null) {
          params.append(key, String(value));
        }
      });
    }

    const queryString = params.toString();
    const endpoint = queryString ? `/api/Orders/focus?${queryString}` : '/api/Orders/focus';

    const response = await apiClient.get<OrderDtoListApiResponse>(
      endpoint,
      { requireAuth: true }
    );

    return response.data || [];
  } catch (error) {
    // eslint-disable-next-line no-console
    console.error('Error fetching focus orders:', error);
    throw error;
  }
}

/**
 * Add payment to order
 *
 * @param orderId - Order ID
 * @param command - Payment details
 * @returns Created payment
 */
export async function addPaymentToOrder(
  orderId: string,
  command: AddPaymentToOrderCommand
): Promise<OrderPaymentDto> {
  try {
    const response = await apiClient.post<OrderPaymentDtoApiResponse>(
      `/api/Orders/${orderId}/payments`,
      command,
      { requireAuth: true }
    );
    if (!response.data) {
      throw new Error('Failed to add payment');
    }
    return response.data;
  } catch (error) {
    // eslint-disable-next-line no-console
    console.error('Error adding payment:', error);
    throw error;
  }
}

/**
 * Refund payment
 *
 * @param orderId - Order ID
 * @param paymentId - Payment ID
 * @param command - Refund details
 * @returns Updated payment
 */
export async function refundPayment(
  orderId: string,
  paymentId: string,
  command: RefundPaymentCommand
): Promise<OrderPaymentDto> {
  try {
    const response = await apiClient.post<OrderPaymentDtoApiResponse>(
      `/api/Orders/${orderId}/payments/${paymentId}/refund`,
      command,
      { requireAuth: true }
    );
    if (!response.data) {
      throw new Error('Failed to refund payment');
    }
    return response.data;
  } catch (error) {
    // eslint-disable-next-line no-console
    console.error('Error refunding payment:', error);
    throw error;
  }
}

/**
 * Get Z-Report (end-of-day financial summary) for a specific date
 *
 * @param date - ISO date string (YYYY-MM-DD), defaults to today on server
 * @returns Z-Report data
 */
export async function getZReport(date?: string): Promise<ZReportDto> {
  try {
    const params = new URLSearchParams();
    if (date) {
      params.append('date', date);
    }
    const queryString = params.toString();
    const endpoint = queryString ? `/api/Orders/z-report?${queryString}` : '/api/Orders/z-report';

    const response = await apiClient.get<ZReportApiResponse>(endpoint, { requireAuth: true });
    if (!response.data) {
      throw new Error('Failed to fetch Z-Report');
    }
    return response.data;
  } catch (error) {
    // eslint-disable-next-line no-console
    console.error('Error fetching Z-Report:', error);
    throw error;
  }
}

/**
 * Order service object with all methods
 */
export const orderService = {
  createOrder,
  getOrders,
  getMyOrders,
  getOrderById,
  updateOrderStatus,
  cancelOrder,
  toggleFocusOrder,
  getFocusOrders,
  addPaymentToOrder,
  refundPayment,
  getZReport,
};
