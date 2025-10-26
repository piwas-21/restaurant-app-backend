/**
 * Basket API Type Definitions
 *
 * These types match the backend API DTOs for the Basket endpoints.
 * Backend API: http://localhost:5221/api/Basket
 */

/**
 * Menu item summary included in basket items for menu orders
 */
export interface MenuItemSummaryDto {
  productId?: string;
  productName?: string;
  productDescription?: string;
  productImageUrl?: string;
  categoryName?: string;
  quantity?: number;
  unitPrice?: number;
}

/**
 * Individual item in the basket
 */
export interface BasketItemDto {
  id?: string; // Backend basket item ID (required for updates/deletes)
  productId?: string;
  productName?: string;
  productDescription?: string;
  productImageUrl?: string;
  productVariationId?: string;
  variationName?: string;
  menuId?: string;
  menuName?: string;
  menuDate?: string;
  menuItems?: MenuItemSummaryDto[];
  quantity: number;
  unitPrice: number;
  itemTotal: number;
  specialInstructions?: string;
}

/**
 * Complete basket data
 */
export interface BasketDto {
  id: string;
  userId?: string;
  sessionId?: string;
  subTotal: number;
  tax: number;
  deliveryFee: number;
  discount: number; // Promo code discount
  customerDiscount: number; // Customer-specific discount
  total: number;
  promoCode?: string;
  totalItems: number;
  expiresAt?: string;
  notes?: string;
  items: BasketItemDto[];
}

/**
 * Basket summary (lightweight version for cart counter)
 */
export interface BasketSummaryDto {
  id: string;
  itemCount: number;
  total: number;
}

/**
 * Request to add item to basket
 */
export interface AddToBasketDto {
  productId: string;
  productVariationId?: string;
  menuId?: string;
  quantity: number;
  specialInstructions?: string;
}

/**
 * Request to update basket item
 */
export interface UpdateBasketItemDto {
  quantity: number;
  specialInstructions?: string;
}

/**
 * Request to apply promo code
 */
export interface ApplyPromoCodeRequest {
  promoCode: string;
}

/**
 * Generic API response wrapper
 */
export interface ApiResponse<T> {
  data?: T;
  success: boolean;
  message?: string;
  errors?: string[];
}

/**
 * Basket API response types
 */
export type BasketDtoApiResponse = ApiResponse<BasketDto>;
export type BasketSummaryDtoApiResponse = ApiResponse<BasketSummaryDto>;
