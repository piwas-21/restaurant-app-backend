/**
 * Basket Service
 *
 * Service layer for interacting with the Basket API.
 * Handles all basket operations: add, update, remove items, promo codes, etc.
 */

import { apiClient } from '@/utils/apiClient';
import {
  BasketDto,
  BasketSummaryDto,
  AddToBasketDto,
  UpdateBasketItemDto,
  ApplyPromoCodeRequest,
  BasketDtoApiResponse,
  BasketSummaryDtoApiResponse,
} from '@/types/basket';

/**
 * Get current user's basket
 * Uses session ID for anonymous users or auth token for logged-in users
 */
export async function getBasket(): Promise<BasketDto | null> {
  try {
    const response = await apiClient.get<BasketDtoApiResponse>('/api/Basket');
    return response.data || null;
  } catch (error) {
    console.error('Error fetching basket:', error);
    throw error;
  }
}

/**
 * Get basket summary (lightweight version for cart counter)
 * Returns item count and total only
 */
export async function getBasketSummary(): Promise<BasketSummaryDto | null> {
  try {
    const response = await apiClient.get<BasketSummaryDtoApiResponse>('/api/Basket/summary');
    return response.data || null;
  } catch (error) {
    console.error('Error fetching basket summary:', error);
    throw error;
  }
}

/**
 * Add item to basket
 *
 * @param item - Item details to add
 * @returns Updated basket
 */
export async function addItemToBasket(item: AddToBasketDto): Promise<BasketDto> {
  try {
    const response = await apiClient.post<BasketDtoApiResponse>('/api/Basket/items', item);
    if (!response.data) {
      throw new Error('Failed to add item to basket');
    }
    return response.data;
  } catch (error) {
    console.error('Error adding item to basket:', error);
    throw error;
  }
}

/**
 * Update basket item quantity or special instructions
 *
 * @param basketItemId - ID of the basket item to update
 * @param updates - Updated quantity and/or special instructions
 * @returns Updated basket
 */
export async function updateBasketItem(basketItemId: string, updates: UpdateBasketItemDto): Promise<BasketDto> {
  try {
    const response = await apiClient.put<BasketDtoApiResponse>(`/api/Basket/items/${basketItemId}`, updates);
    if (!response.data) {
      throw new Error('Failed to update basket item');
    }
    return response.data;
  } catch (error) {
    console.error('Error updating basket item:', error);
    throw error;
  }
}

/**
 * Remove item from basket
 *
 * @param basketItemId - ID of the basket item to remove
 * @returns Updated basket
 */
export async function removeItemFromBasket(basketItemId: string): Promise<BasketDto> {
  try {
    const response = await apiClient.delete<BasketDtoApiResponse>(`/api/Basket/items/${basketItemId}`);
    if (!response.data) {
      throw new Error('Failed to remove item from basket');
    }
    return response.data;
  } catch (error) {
    console.error('Error removing item from basket:', error);
    throw error;
  }
}

/**
 * Clear entire basket
 * Removes all items from the basket
 *
 * @returns Updated (empty) basket
 */
export async function clearBasket(): Promise<BasketDto> {
  try {
    const response = await apiClient.delete<BasketDtoApiResponse>('/api/Basket');
    if (!response.data) {
      throw new Error('Failed to clear basket');
    }
    return response.data;
  } catch (error) {
    console.error('Error clearing basket:', error);
    throw error;
  }
}

/**
 * Apply promo code to basket
 *
 * @param promoCode - Promo code to apply
 * @returns Updated basket with discount applied
 */
export async function applyPromoCode(promoCode: string): Promise<BasketDto> {
  try {
    const request: ApplyPromoCodeRequest = { promoCode };
    const response = await apiClient.post<BasketDtoApiResponse>('/api/Basket/promo-code', request);
    if (!response.data) {
      throw new Error('Failed to apply promo code');
    }
    return response.data;
  } catch (error) {
    console.error('Error applying promo code:', error);
    throw error;
  }
}

/**
 * Remove promo code from basket
 *
 * @returns Updated basket without discount
 */
export async function removePromoCode(): Promise<BasketDto> {
  try {
    const response = await apiClient.delete<BasketDtoApiResponse>('/api/Basket/promo-code');
    if (!response.data) {
      throw new Error('Failed to remove promo code');
    }
    return response.data;
  } catch (error) {
    console.error('Error removing promo code:', error);
    throw error;
  }
}

/**
 * Basket service object with all methods
 */
export const basketService = {
  getBasket,
  getBasketSummary,
  addItemToBasket,
  updateBasketItem,
  removeItemFromBasket,
  clearBasket,
  applyPromoCode,
  removePromoCode,
};
