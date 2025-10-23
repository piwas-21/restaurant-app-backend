/**
 * OrderService Unit Tests
 *
 * Tests for order service API interactions
 */

import * as orderServiceModule from './orderService';
import { apiClient } from '@/utils/apiClient';
import type { CreateOrderCommand, OrderDto, UpdateOrderStatusCommand } from '@/types/order';

// Mock the apiClient
jest.mock('@/utils/apiClient');

const mockApiClient = apiClient as jest.Mocked<typeof apiClient>;

describe('OrderService', () => {
  const createMockOrder = (overrides?: Partial<OrderDto>): OrderDto => ({
    id: 'order-123',
    orderNumber: 'ORD-20251023-0001',
    userId: 'user-123',
    status: 'Pending',
    paymentStatus: 'Pending',
    orderType: 'DineIn',
    tableNumber: '5',
    customerName: 'John Doe',
    customerEmail: 'john@example.com',
    customerPhone: '+41791234567',
    items: [],
    subTotal: 0,
    tax: 0,
    discount: 0,
    deliveryFee: 0,
    total: 0,
    notes: '',
    createdAt: '2025-10-23T10:00:00Z',
    ...overrides,
  });

  beforeEach(() => {
    jest.clearAllMocks();
  });

  describe('createOrder', () => {
    it('should create order successfully', async () => {
      const command: CreateOrderCommand = {
        orderType: 'DineIn',
        tableNumber: '5',
        customerName: 'John Doe',
        customerEmail: 'john@example.com',
        customerPhone: '+41791234567',
        paymentMethod: 'Cash',
        notes: 'Please serve quickly',
      };

      const mockOrder = createMockOrder({
        items: [
          {
            id: 'item-1',
            productId: 'prod-123',
            productName: 'Pizza Margherita',
            quantity: 2,
            unitPrice: 12.5,
            itemTotal: 25.0,
          },
        ],
        subTotal: 25.0,
        tax: 1.93,
        total: 26.93,
        notes: 'Please serve quickly',
      });

      mockApiClient.post.mockResolvedValue({ data: mockOrder });

      const result = await orderServiceModule.createOrder(command);

      expect(mockApiClient.post).toHaveBeenCalledWith('/api/Orders', command);
      expect(result).toEqual(mockOrder);
      expect(result.orderNumber).toBe('ORD-20251023-0001');
      expect(result.orderType).toBe('DineIn');
    });

    it('should handle create order errors', async () => {
      const command: CreateOrderCommand = {
        orderType: 'DineIn',
        customerName: '',
        customerEmail: '',
        paymentMethod: 'Cash',
      };

      const error = new Error('Validation failed');
      mockApiClient.post.mockRejectedValue(error);

      await expect(orderServiceModule.createOrder(command)).rejects.toThrow('Validation failed');
    });
  });

  describe('getOrders', () => {
    it('should fetch orders with filters', async () => {
      const filters = {
        status: 'Pending',
        paymentStatus: 'Pending',
        pageNumber: 1,
        pageSize: 20,
      };

      const mockResponse = {
        items: [createMockOrder(), createMockOrder({ id: 'order-124' })],
        pageNumber: 1,
        pageSize: 20,
        totalCount: 2,
        totalPages: 1,
      };

      mockApiClient.get.mockResolvedValue({ data: mockResponse });

      const result = await orderServiceModule.getOrders(filters);

      expect(mockApiClient.get).toHaveBeenCalled();
      expect(result.items).toHaveLength(2);
      expect(result.pageNumber).toBe(1);
      expect(result.totalCount).toBe(2);
    });

    it('should fetch orders without filters', async () => {
      const mockResponse = {
        items: [createMockOrder()],
        pageNumber: 1,
        pageSize: 20,
        totalCount: 1,
        totalPages: 1,
      };

      mockApiClient.get.mockResolvedValue({ data: mockResponse });

      const result = await orderServiceModule.getOrders();

      expect(mockApiClient.get).toHaveBeenCalledWith(
        '/api/Orders',
        expect.objectContaining({ requireAuth: true })
      );
      expect(result.items).toHaveLength(1);
    });

    it('should handle errors when fetching orders', async () => {
      const error = new Error('Unauthorized');
      mockApiClient.get.mockRejectedValue(error);

      await expect(orderServiceModule.getOrders()).rejects.toThrow('Unauthorized');
    });
  });

  describe('getOrderById', () => {
    it('should fetch order by ID successfully', async () => {
      const orderId = 'order-123';
      const mockOrder = createMockOrder();

      mockApiClient.get.mockResolvedValue({ data: mockOrder });

      const result = await orderServiceModule.getOrderById(orderId);

      expect(mockApiClient.get).toHaveBeenCalledWith(`/api/Orders/${orderId}`);
      expect(result).toEqual(mockOrder);
      expect(result.id).toBe(orderId);
    });

    it('should handle order not found', async () => {
      const error = new Error('Order not found');
      mockApiClient.get.mockRejectedValue(error);

      await expect(orderServiceModule.getOrderById('non-existent')).rejects.toThrow(
        'Order not found'
      );
    });
  });

  describe('updateOrderStatus', () => {
    it('should update order status successfully', async () => {
      const orderId = 'order-123';
      const command: UpdateOrderStatusCommand = {
        status: 'Confirmed',
      };

      const mockOrder = createMockOrder({ status: 'Confirmed' });

      mockApiClient.put.mockResolvedValue({ data: mockOrder });

      const result = await orderServiceModule.updateOrderStatus(orderId, command);

      expect(mockApiClient.put).toHaveBeenCalledWith(`/api/Orders/${orderId}/status`, command);
      expect(result.status).toBe('Confirmed');
    });

    it('should handle invalid status transition', async () => {
      const error = new Error('Invalid status transition');
      mockApiClient.put.mockRejectedValue(error);

      await expect(
        orderServiceModule.updateOrderStatus('order-123', { status: 'Invalid' as any })
      ).rejects.toThrow('Invalid status transition');
    });
  });

  describe('cancelOrder', () => {
    it('should cancel order successfully', async () => {
      const orderId = 'order-123';
      const command = {
        reason: 'Customer requested cancellation',
      };

      const mockOrder = createMockOrder({ status: 'Cancelled' });

      mockApiClient.put.mockResolvedValue({ data: mockOrder });

      const result = await orderServiceModule.cancelOrder(orderId, command);

      expect(mockApiClient.put).toHaveBeenCalledWith(`/api/Orders/${orderId}/cancel`, command);
      expect(result.status).toBe('Cancelled');
    });

    it('should handle cancel order errors', async () => {
      const error = new Error('Order already completed');
      mockApiClient.put.mockRejectedValue(error);

      await expect(
        orderServiceModule.cancelOrder('order-123', { reason: 'Test' })
      ).rejects.toThrow('Order already completed');
    });
  });

  describe('toggleFocusOrder', () => {
    it('should toggle focus order successfully', async () => {
      const orderId = 'order-123';
      const command = { isFocusOrder: true };

      const mockOrder = createMockOrder({ isFocusOrder: true });

      mockApiClient.put.mockResolvedValue({ data: mockOrder });

      const result = await orderServiceModule.toggleFocusOrder(orderId, command);

      expect(mockApiClient.put).toHaveBeenCalledWith(`/api/Orders/${orderId}/focus`, command);
      expect(result.isFocusOrder).toBe(true);
    });
  });

  describe('getFocusOrders', () => {
    it('should fetch focus orders successfully', async () => {
      const filters = { restaurantId: 'rest-1' };

      const mockResponse = {
        items: [createMockOrder({ isFocusOrder: true })],
        pageNumber: 1,
        pageSize: 20,
        totalCount: 1,
        totalPages: 1,
      };

      mockApiClient.get.mockResolvedValue({ data: mockResponse });

      const result = await orderServiceModule.getFocusOrders(filters);

      expect(mockApiClient.get).toHaveBeenCalled();
      expect(result.items[0]?.isFocusOrder).toBe(true);
    });
  });

  describe('addPaymentToOrder', () => {
    it('should add payment to order successfully', async () => {
      const orderId = 'order-123';
      const command = {
        paymentMethod: 'Cash',
        amount: 26.93,
      };

      const mockPayment = {
        id: 'payment-1',
        orderId,
        paymentMethod: 'Cash',
        amount: 26.93,
        status: 'Completed',
        paidAt: '2025-10-23T10:05:00Z',
      };

      mockApiClient.post.mockResolvedValue({ data: mockPayment });

      const result = await orderServiceModule.addPaymentToOrder(orderId, command);

      expect(mockApiClient.post).toHaveBeenCalledWith(`/api/Orders/${orderId}/payments`, command);
      expect(result.amount).toBe(26.93);
      expect(result.paymentMethod).toBe('Cash');
    });
  });
});
