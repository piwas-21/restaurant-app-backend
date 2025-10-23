/**
 * BasketService Unit Tests
 *
 * Tests for basket service API interactions
 */

import * as basketServiceModule from './basketService';
import { apiClient } from '@/utils/apiClient';
import type { BasketDto, AddToBasketDto, UpdateBasketItemDto } from '@/types/basket';

// Mock the apiClient
jest.mock('@/utils/apiClient');

const mockApiClient = apiClient as jest.Mocked<typeof apiClient>;

describe('BasketService', () => {
  const createMockBasket = (overrides?: Partial<BasketDto>): BasketDto => ({
    id: 'basket-123',
    sessionId: 'session-123',
    items: [],
    subTotal: 0,
    discount: 0,
    tax: 0,
    deliveryFee: 0,
    total: 0,
    totalItems: 0,
    ...overrides,
  });

  beforeEach(() => {
    jest.clearAllMocks();
  });

  describe('getBasket', () => {
    it('should fetch basket successfully', async () => {
      const mockBasket = createMockBasket();
      mockApiClient.get.mockResolvedValue({ data: mockBasket });

      const result = await basketServiceModule.getBasket();

      expect(mockApiClient.get).toHaveBeenCalledWith('/api/Basket');
      expect(result).toEqual(mockBasket);
    });

    it('should handle errors when fetching basket', async () => {
      const error = new Error('Network error');
      mockApiClient.get.mockRejectedValue(error);

      await expect(basketServiceModule.getBasket()).rejects.toThrow('Network error');
    });
  });

  describe('getBasketSummary', () => {
    it('should fetch basket summary successfully', async () => {
      const mockSummary = { id: 'basket-123', itemCount: 5, total: 50.0 };
      mockApiClient.get.mockResolvedValue({ data: mockSummary });

      const result = await basketServiceModule.getBasketSummary();

      expect(mockApiClient.get).toHaveBeenCalledWith('/api/Basket/summary');
      expect(result).toEqual(mockSummary);
    });
  });

  describe('addItemToBasket', () => {
    it('should add item to basket successfully', async () => {
      const request: AddToBasketDto = {
        productId: 'prod-123',
        quantity: 2,
        specialInstructions: 'No onions',
      };

      const mockBasket = createMockBasket({
        items: [
          {
            id: 'item-1',
            productId: 'prod-123',
            productName: 'Pizza Margherita',
            quantity: 2,
            unitPrice: 12.5,
            itemTotal: 25.0,
            specialInstructions: 'No onions',
          },
        ],
        subTotal: 25.0,
        tax: 1.93,
        total: 26.93,
        totalItems: 2,
      });

      mockApiClient.post.mockResolvedValue({ data: mockBasket });

      const result = await basketServiceModule.addItemToBasket(request);

      expect(mockApiClient.post).toHaveBeenCalledWith('/api/Basket/items', request);
      expect(result).toEqual(mockBasket);
      expect(result.items).toHaveLength(1);
      expect(result.items[0]?.quantity).toBe(2);
    });

    it('should handle add item errors', async () => {
      const request: AddToBasketDto = { productId: 'prod-123', quantity: 1 };
      const error = new Error('Product not found');
      mockApiClient.post.mockRejectedValue(error);

      await expect(basketServiceModule.addItemToBasket(request)).rejects.toThrow(
        'Product not found'
      );
    });
  });

  describe('updateBasketItem', () => {
    it('should update basket item successfully', async () => {
      const basketItemId = 'item-1';
      const updates: UpdateBasketItemDto = {
        quantity: 3,
        specialInstructions: 'Extra cheese',
      };

      const mockBasket = createMockBasket({
        items: [
          {
            id: 'item-1',
            productId: 'prod-123',
            productName: 'Pizza Margherita',
            quantity: 3,
            unitPrice: 12.5,
            itemTotal: 37.5,
            specialInstructions: 'Extra cheese',
          },
        ],
        subTotal: 37.5,
        tax: 2.89,
        total: 40.39,
        totalItems: 3,
      });

      mockApiClient.put.mockResolvedValue({ data: mockBasket });

      const result = await basketServiceModule.updateBasketItem(basketItemId, updates);

      expect(mockApiClient.put).toHaveBeenCalledWith(`/api/Basket/items/${basketItemId}`, updates);
      expect(result.items[0]?.quantity).toBe(3);
      expect(result.items[0]?.specialInstructions).toBe('Extra cheese');
    });

    it('should handle update errors', async () => {
      const error = new Error('Item not found');
      mockApiClient.put.mockRejectedValue(error);

      await expect(
        basketServiceModule.updateBasketItem('item-1', { quantity: 2 })
      ).rejects.toThrow('Item not found');
    });
  });

  describe('removeItemFromBasket', () => {
    it('should remove item from basket', async () => {
      const basketItemId = 'item-1';
      const mockBasket = createMockBasket();

      mockApiClient.delete.mockResolvedValue({ data: mockBasket });

      const result = await basketServiceModule.removeItemFromBasket(basketItemId);

      expect(mockApiClient.delete).toHaveBeenCalledWith(`/api/Basket/items/${basketItemId}`);
      expect(result.items).toHaveLength(0);
      expect(result.total).toBe(0);
    });

    it('should handle remove errors', async () => {
      const error = new Error('Item not found');
      mockApiClient.delete.mockRejectedValue(error);

      await expect(basketServiceModule.removeItemFromBasket('item-1')).rejects.toThrow(
        'Item not found'
      );
    });
  });

  describe('clearBasket', () => {
    it('should clear entire basket', async () => {
      const mockBasket = createMockBasket();
      mockApiClient.delete.mockResolvedValue({ data: mockBasket });

      const result = await basketServiceModule.clearBasket();

      expect(mockApiClient.delete).toHaveBeenCalledWith('/api/Basket');
      expect(result.items).toHaveLength(0);
    });

    it('should handle clear errors', async () => {
      const error = new Error('Server error');
      mockApiClient.delete.mockRejectedValue(error);

      await expect(basketServiceModule.clearBasket()).rejects.toThrow('Server error');
    });
  });

  describe('applyPromoCode', () => {
    it('should apply promo code successfully', async () => {
      const promoCode = 'SUMMER20';
      const mockBasket = createMockBasket({
        items: [
          {
            id: 'item-1',
            productId: 'prod-123',
            productName: 'Pizza',
            quantity: 2,
            unitPrice: 12.5,
            itemTotal: 25.0,
          },
        ],
        subTotal: 25.0,
        discount: 5.0,
        tax: 1.54,
        total: 21.54,
        totalItems: 2,
        promoCode: 'SUMMER20',
      });

      mockApiClient.post.mockResolvedValue({ data: mockBasket });

      const result = await basketServiceModule.applyPromoCode(promoCode);

      expect(mockApiClient.post).toHaveBeenCalledWith('/api/Basket/promo-code', { promoCode });
      expect(result.promoCode).toBe('SUMMER20');
      expect(result.discount).toBe(5.0);
    });

    it('should handle invalid promo code', async () => {
      const error = new Error('Invalid promo code');
      mockApiClient.post.mockRejectedValue(error);

      await expect(basketServiceModule.applyPromoCode('INVALID')).rejects.toThrow(
        'Invalid promo code'
      );
    });
  });

  describe('removePromoCode', () => {
    it('should remove promo code', async () => {
      const mockBasket = createMockBasket({
        subTotal: 25.0,
        tax: 1.93,
        total: 26.93,
      });

      mockApiClient.delete.mockResolvedValue({ data: mockBasket });

      const result = await basketServiceModule.removePromoCode();

      expect(mockApiClient.delete).toHaveBeenCalledWith('/api/Basket/promo-code');
      expect(result.promoCode).toBeUndefined();
      expect(result.discount).toBe(0);
    });

    it('should handle remove promo code errors', async () => {
      const error = new Error('Server error');
      mockApiClient.delete.mockRejectedValue(error);

      await expect(basketServiceModule.removePromoCode()).rejects.toThrow('Server error');
    });
  });
});
