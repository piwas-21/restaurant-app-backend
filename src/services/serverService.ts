/**
 * Server Service
 *
 * Service layer for server-specific API calls.
 * Handles dine-in orders, table status, and server operations.
 */

import { apiClient } from '@/utils/apiClient';
import {
  OrderDto,
  OrderDtoPagedResultApiResponse,
  OrderDtoApiResponse,
  PagedResult,
  CreateOrderItemDto,
  CreateOrderCommand,
  OrderType,
} from '@/types/order';
import { TableDto, ReservationDto, ApiResponse, PagedResult as ReservationPagedResult } from '@/types/reservation';

/**
 * Extended table with order and reservation info for server view
 */
export interface ServerTableDto extends TableDto {
  currentOrders: OrderDto[];
  orderCount: number;
  hasActiveOrders: boolean;
  upcomingReservation?: ReservationDto;
  status: 'available' | 'occupied' | 'reserved' | 'closed';
}

/**
 * Get all dine-in orders with optional filters
 */
export async function getDineInOrders(filters?: {
  status?: string;
  tableNumber?: number;
  page?: number;
  pageSize?: number;
  modifiedSince?: Date;
}): Promise<PagedResult<OrderDto>> {
  const params = new URLSearchParams();
  params.append('type', 'DineIn');

  if (filters) {
    if (filters.status) params.append('status', filters.status);
    if (filters.tableNumber) params.append('tableNumber', filters.tableNumber.toString());
    if (filters.page) params.append('page', filters.page.toString());
    if (filters.pageSize) params.append('pageSize', filters.pageSize.toString());
    if (filters.modifiedSince) params.append('modifiedSince', filters.modifiedSince.toISOString());
  }

  const endpoint = `/api/orders?${params}`;

  const response = await apiClient.get<OrderDtoPagedResultApiResponse>(endpoint, {
    requireAuth: true,
  });

  if (!response.data) {
    throw new Error('Failed to fetch dine-in orders');
  }

  return response.data;
}

/**
 * Get all tables (including closed ones so servers can reopen them)
 */
export async function getTables(): Promise<TableDto[]> {
  const response = await apiClient.get<ApiResponse<TableDto[]>>('/api/tables');
  return response.data || [];
}

/**
 * Get tables with current status (orders and reservations)
 */
export async function getTablesWithStatus(): Promise<ServerTableDto[]> {
  // Fetch tables and active dine-in orders in parallel
  const [tables, ordersResult, reservationsResult] = await Promise.all([
    getTables(),
    getDineInOrders({ status: 'Pending,Confirmed,Preparing,Ready', pageSize: 100 }),
    getUpcomingReservations()
  ]);

  const orders = ordersResult.items || [];
  const reservations = reservationsResult.items || [];

  // Map tables with their current orders and reservations
  return tables.map(table => {
    const tableOrders = orders.filter(
      order => order.tableNumber?.toString() === table.tableNumber
    );

    const upcomingReservation = reservations.find(
      res => res.tableId === table.id
    );

    let status: ServerTableDto['status'] = 'available';

    if (!table.isActive) {
      status = 'closed';
    } else if (tableOrders.length > 0) {
      status = 'occupied';
    } else if (upcomingReservation) {
      // Check if reservation is within the next 30 minutes
      const now = new Date();
      const reservationTime = new Date(`${upcomingReservation.reservationDate}T${upcomingReservation.startTime}`);
      const thirtyMinutesFromNow = new Date(now.getTime() + 30 * 60 * 1000);

      if (reservationTime <= thirtyMinutesFromNow) {
        status = 'reserved';
      }
    }

    return {
      ...table,
      currentOrders: tableOrders,
      orderCount: tableOrders.length,
      hasActiveOrders: tableOrders.length > 0,
      upcomingReservation: upcomingReservation,
      status
    };
  });
}

/**
 * Get upcoming reservations for today
 */
export async function getUpcomingReservations(): Promise<ReservationPagedResult<ReservationDto>> {
  const today = new Date().toISOString().split('T')[0];
  const response = await apiClient.get<ApiResponse<ReservationPagedResult<ReservationDto>>>(
    `/api/reservations?date=${today}&status=1&pageSize=50` // status=1 is Confirmed
  );

  return response.data || { items: [], totalCount: 0, page: 1, pageSize: 50, totalPages: 0 };
}

/**
 * Update order status
 */
export async function updateOrderStatus(
  orderId: string,
  newStatus: string,
  notes?: string
): Promise<OrderDto> {
  const response = await apiClient.put<OrderDtoApiResponse>(
    `/api/orders/${orderId}/status`,
    { orderId, newStatus, notes },
    { requireAuth: true }
  );

  if (!response.success) {
    throw new Error(response.message || 'Failed to update order status');
  }

  if (!response.data) {
    throw new Error('Failed to update order status - no data returned');
  }

  return response.data;
}

/**
 * Mark order as completed (all items served)
 */
export async function markOrderCompleted(orderId: string): Promise<OrderDto> {
  return updateOrderStatus(orderId, 'Completed', 'All items served to table');
}
/**
 * Complete all active orders on a table (frees the table)
 * Uses the backend endpoint that intelligently handles order transitions
 */
export async function completeAllTableOrders(tableNumber: string): Promise<{
  completedCount: number;
  cancelledCount: number;
  totalProcessed: number;
}> {
  const response = await apiClient.post<ApiResponse<{
    completedCount: number;
    cancelledCount: number;
    totalProcessed: number;
    processedOrderNumbers: string[];
  }>>(
    `/api/orders/table/${tableNumber}/complete-all`,
    {},
    { requireAuth: true }
  );

  if (!response.success || !response.data) {
    throw new Error(response.message || 'Failed to complete table orders');
  }

  return {
    completedCount: response.data.completedCount,
    cancelledCount: response.data.cancelledCount,
    totalProcessed: response.data.totalProcessed,
  };
}

/**
 * Get order by ID
 */
export async function getOrderById(orderId: string): Promise<OrderDto> {
  const response = await apiClient.get<OrderDtoApiResponse>(`/api/orders/${orderId}`, {
    requireAuth: true,
  });

  if (!response.data) {
    throw new Error('Failed to fetch order');
  }

  return response.data;
}

/**
 * Get orders for a specific table
 */
export async function getOrdersForTable(tableNumber: string): Promise<OrderDto[]> {
  const result = await getDineInOrders({
    tableNumber: parseInt(tableNumber, 10),
    status: 'Pending,Confirmed,Preparing,Ready',
    pageSize: 50
  });
  return result.items || [];
}

/**
 * Close a table (mark as inactive/unavailable)
 */
export async function closeTable(tableId: string): Promise<TableDto> {
  // First get the current table data
  const tableResponse = await apiClient.get<ApiResponse<TableDto>>(`/api/tables/${tableId}`);
  if (!tableResponse.data) {
    throw new Error('Table not found');
  }

  const table = tableResponse.data;

  // Update the table with isActive = false
  const response = await apiClient.put<ApiResponse<TableDto>>(
    `/api/tables/${tableId}`,
    {
      tableNumber: table.tableNumber,
      maxGuests: table.maxGuests,
      isActive: false,
      isOutdoor: table.isOutdoor,
      positionX: table.positionX,
      positionY: table.positionY,
      width: table.width,
      height: table.height,
      shape: table.shape || 'circle',
      rotation: table.rotation || 0,
      notes: table.notes,
    },
    { requireAuth: true }
  );

  if (!response.success || !response.data) {
    throw new Error(response.message || 'Failed to close table');
  }

  return response.data;
}

/**
 * Open/Reopen a table (mark as active/available)
 */
export async function openTable(tableId: string): Promise<TableDto> {
  // First get the current table data
  const tableResponse = await apiClient.get<ApiResponse<TableDto>>(`/api/tables/${tableId}`);
  if (!tableResponse.data) {
    throw new Error('Table not found');
  }

  const table = tableResponse.data;

  // Update the table with isActive = true
  const response = await apiClient.put<ApiResponse<TableDto>>(
    `/api/tables/${tableId}`,
    {
      tableNumber: table.tableNumber,
      maxGuests: table.maxGuests,
      isActive: true,
      isOutdoor: table.isOutdoor,
      positionX: table.positionX,
      positionY: table.positionY,
      width: table.width,
      height: table.height,
      shape: table.shape || 'circle',
      rotation: table.rotation || 0,
      notes: table.notes,
    },
    { requireAuth: true }
  );

  if (!response.success || !response.data) {
    throw new Error(response.message || 'Failed to open table');
  }

  return response.data;
}

/**
 * Release a table (mark as available by releasing any reservation hold)
 */
export async function releaseTable(tableNumber: string): Promise<boolean> {
  const response = await apiClient.post<ApiResponse<boolean>>(
    `/api/tables/${tableNumber}/release`,
    {},
    { requireAuth: true }
  );

  if (!response.success) {
    throw new Error(response.message || 'Failed to release table');
  }

  return response.data || true;
}

/**
 * Create a dine-in order for a table (server placing an order)
 */
export async function createServerOrder(
  tableNumber: number,
  items: CreateOrderItemDto[],
  customerName?: string,
  notes?: string,
  userId?: string,
  pointsToRedeem?: number
): Promise<OrderDto> {
  const orderCommand: CreateOrderCommand = {
    type: OrderType.DineIn,
    tableNumber,
    items,
    customerName: customerName || `Table ${tableNumber}`,
    notes,
    userId: userId || undefined,
    pointsToRedeem: pointsToRedeem || undefined,
  };

  const response = await apiClient.post<OrderDtoApiResponse>(
    '/api/Orders',
    orderCommand,
    { requireAuth: true }
  );

  if (!response.data) {
    throw new Error('Failed to create order');
  }

  return response.data;
}

/**
 * Get all products for order placement
 */
export async function getMenuProducts(): Promise<Product[]> {
  const response = await apiClient.get<{ success: boolean; data: { items: Product[] } }>(
    '/api/Products?PageSize=100&isActive=true'
  );
  return response.data?.items || [];
}

/**
 * Get all categories
 */
export async function getCategories(): Promise<Category[]> {
  const response = await apiClient.get<ApiResponse<{ items: Category[]; totalCount: number }>>(
    '/api/Categories?PageNumber=1&PageSize=100'
  );
  // Handle both paged result and direct array responses
  if (response.data && 'items' in response.data) {
    return response.data.items || [];
  }
  return (response.data as unknown as Category[]) || [];
}

/**
 * Product type for menu display
 */
export interface Product {
  id: string;
  name: string;
  description?: string;
  basePrice: number;
  isActive: boolean;
  isAvailable: boolean;
  type: string;
  categories?: ProductCategoryLink[];
  primaryCategoryId?: string;
  imageUrl?: string;
  variations?: ProductVariation[];
}

export interface ProductCategoryLink {
  categoryId: string;
  categoryName: string;
  isPrimary: boolean;
}

export interface ProductVariation {
  id: string;
  name: string;
  priceModifier: number;
  isActive: boolean;
}

export interface Category {
  id: string;
  name: string;
  description?: string;
  displayOrder: number;
  isActive: boolean;
}

/**
 * User DTO for customer search
 */
export interface UserDto {
  id: string;
  email: string;
  firstName: string;
  lastName: string;
  fullName: string;
  phoneNumber?: string;
  role: string;
  discountPercentage: number;
  isDiscountActive: boolean;
  orderLimitAmount: number;
}

/**
 * Fidelity point balance DTO
 */
export interface FidelityPointBalanceDto {
  id: string;
  userId: string;
  currentPoints: number;
  totalEarnedPoints: number;
  totalRedeemedPoints: number;
  lastUpdated: string;
}

/**
 * Search users by name or email (for customer lookup)
 */
export async function searchUsers(query: string, pageSize: number = 10): Promise<UserDto[]> {
  if (!query || query.length < 2) return [];

  const params = new URLSearchParams({
    Search: query,
    Role: 'Customer',
    IsDeleted: 'false',
    PageSize: pageSize.toString(),
  });

  const response = await apiClient.get<ApiResponse<{ items: UserDto[] }>>(
    `/api/User/users?${params}`,
    { requireAuth: true }
  );

  return response.data?.items || [];
}

/**
 * Get user's fidelity points balance (Staff only)
 */
export async function getUserFidelityBalance(userId: string): Promise<FidelityPointBalanceDto | null> {
  try {
    const response = await apiClient.get<ApiResponse<FidelityPointBalanceDto>>(
      `/api/FidelityPoints/balance/${userId}`,
      { requireAuth: true }
    );
    return response.data || null;
  } catch (error) {
    console.error('Failed to fetch user fidelity balance:', error);
    return null;
  }
}

/**
 * Calculate discount amount from points (100 points = CHF 1)
 */
export function calculateDiscountFromPoints(points: number): number {
  return points / 100;
}

/**
 * Calculate points earned from order total (1 CHF = 1 point)
 */
export function calculatePointsToEarn(orderTotal: number): number {
  return Math.floor(orderTotal);
}

/**
 * Customer discount rule interface for server use
 */
export interface CustomerDiscountRuleDto {
  id: string;
  userId: string;
  name: string;
  discountType: 'Percentage' | 'FixedAmount';
  discountValue: number;
  minOrderAmount?: number;
  maxOrderAmount?: number;
  maxUsageCount?: number;
  usageCount: number;
  isActive: boolean;
  validFrom?: string;
  validUntil?: string;
}

/**
 * Get user's active discount rules
 */
export async function getUserDiscountRules(userId: string): Promise<CustomerDiscountRuleDto[]> {
  try {
    const response = await apiClient.get<ApiResponse<CustomerDiscountRuleDto[]>>(
      `/api/admin/CustomerDiscounts?userId=${userId}&activeOnly=true`,
      { requireAuth: true }
    );
    return response.data || [];
  } catch (error) {
    console.error('Failed to fetch user discount rules:', error);
    return [];
  }
}

/**
 * Server service object with all methods
 */
export const serverService = {
  getDineInOrders,
  getTables,
  getTablesWithStatus,
  getUpcomingReservations,
  updateOrderStatus,
  markOrderCompleted,
  completeAllTableOrders,
  getOrderById,
  getOrdersForTable,
  closeTable,
  openTable,
  releaseTable,
  createServerOrder,
  getMenuProducts,
  getCategories,
  searchUsers,
  getUserFidelityBalance,
  getUserDiscountRules,
  calculateDiscountFromPoints,
  calculatePointsToEarn,
};
