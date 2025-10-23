"use client";

import React, { createContext, useContext, useReducer, useEffect, ReactNode } from 'react';
import { BasketDto, BasketItemDto } from '@/types/basket';
import { basketService } from '@/services/basketService';
import { useSessionContext } from '@/contexts/SessionContext';
import { getErrorMessage } from '@/utils/apiClient';

/**
 * Extended cart item with backend basket item ID
 */
interface CartItem extends BasketItemDto {
  basketItemId?: string; // Backend basket item ID for updates/deletes
}

/**
 * Cart state structure
 */
interface CartState {
  items: CartItem[];
  basket: BasketDto | null;
  isLoading: boolean;
  isSyncing: boolean;
  error: string | null;
  lastSyncedAt: number | null;
}

/**
 * Action payload types
 */
interface SyncBasketPayload {
  basket: BasketDto;
}

interface AddItemPayload {
  productId: string;
  productVariationId?: string;
  menuId?: string;
  quantity: number;
  specialInstructions?: string;
}

interface UpdateItemPayload {
  basketItemId: string;
  quantity: number;
  specialInstructions?: string;
}

interface RemoveItemPayload {
  basketItemId: string;
}

interface SetLoadingPayload {
  isLoading: boolean;
}

interface SetSyncingPayload {
  isSyncing: boolean;
}

interface SetErrorPayload {
  error: string | null;
}

/**
 * Cart actions
 */
type CartAction =
  | { type: 'SYNC_BASKET'; payload: SyncBasketPayload }
  | { type: 'SET_LOADING'; payload: SetLoadingPayload }
  | { type: 'SET_SYNCING'; payload: SetSyncingPayload }
  | { type: 'SET_ERROR'; payload: SetErrorPayload }
  | { type: 'OPTIMISTIC_ADD'; payload: CartItem }
  | { type: 'OPTIMISTIC_UPDATE'; payload: UpdateItemPayload }
  | { type: 'OPTIMISTIC_REMOVE'; payload: RemoveItemPayload }
  | { type: 'ROLLBACK'; payload: { previousState: CartState } };

/**
 * Initial state
 */
const initialState: CartState = {
  items: [],
  basket: null,
  isLoading: false,
  isSyncing: false,
  error: null,
  lastSyncedAt: null,
};

/**
 * Convert BasketDto to CartItem array
 */
function basketToCartItems(basket: BasketDto | null): CartItem[] {
  if (!basket || !basket.items) return [];

  return basket.items.map(item => ({
    ...item,
    basketItemId: item.id, // Store backend ID for updates/deletes
  }));
}

/**
 * Cart reducer
 */
function cartReducer(state: CartState, action: CartAction): CartState {
  switch (action.type) {
    case 'SYNC_BASKET':
      return {
        ...state,
        basket: action.payload.basket,
        items: basketToCartItems(action.payload.basket),
        lastSyncedAt: Date.now(),
        error: null,
      };

    case 'SET_LOADING':
      return {
        ...state,
        isLoading: action.payload.isLoading,
      };

    case 'SET_SYNCING':
      return {
        ...state,
        isSyncing: action.payload.isSyncing,
      };

    case 'SET_ERROR':
      return {
        ...state,
        error: action.payload.error,
        isSyncing: false,
      };

    case 'OPTIMISTIC_ADD':
      // Optimistically add item to cart (will be replaced by server response)
      return {
        ...state,
        items: [...state.items, action.payload],
        isSyncing: true,
      };

    case 'OPTIMISTIC_UPDATE':
      // Optimistically update item quantity
      return {
        ...state,
        items: state.items.map(item =>
          item.basketItemId === action.payload.basketItemId
            ? { ...item, quantity: action.payload.quantity, specialInstructions: action.payload.specialInstructions }
            : item
        ),
        isSyncing: true,
      };

    case 'OPTIMISTIC_REMOVE':
      // Optimistically remove item from cart
      return {
        ...state,
        items: state.items.filter(item => item.basketItemId !== action.payload.basketItemId),
        isSyncing: true,
      };

    case 'ROLLBACK':
      // Rollback to previous state on error
      return {
        ...action.payload.previousState,
        error: state.error, // Keep the error message
        isSyncing: false,
      };

    default:
      return state;
  }
}

/**
 * Cart context type
 */
interface CartContextType {
  state: CartState;
  syncBasket: () => Promise<void>;
  addItem: (payload: AddItemPayload) => Promise<void>;
  updateItem: (basketItemId: string, quantity: number, specialInstructions?: string) => Promise<void>;
  removeItem: (basketItemId: string) => Promise<void>;
  clearCart: () => Promise<void>;
  applyPromoCode: (promoCode: string) => Promise<void>;
  removePromoCode: () => Promise<void>;
  getItemCount: () => number;
  getTotal: () => number;
}

const CartContext = createContext<CartContextType | undefined>(undefined);

/**
 * Cart Provider Component
 */
export const CartProvider = ({ children }: { children: ReactNode }) => {
  const [state, dispatch] = useReducer(cartReducer, initialState);
  const { sessionId, ensureSession } = useSessionContext();

  /**
   * Sync basket from backend
   */
  const syncBasket = async () => {
    try {
      dispatch({ type: 'SET_LOADING', payload: { isLoading: true } });

      const basket = await basketService.getBasket();

      if (basket) {
        dispatch({ type: 'SYNC_BASKET', payload: { basket } });
      } else {
        // Empty basket
        dispatch({ type: 'SYNC_BASKET', payload: { basket: { ...initialState.basket!, items: [] } } });
      }
    } catch (error) {
      const errorMessage = getErrorMessage(error);
      dispatch({ type: 'SET_ERROR', payload: { error: errorMessage } });
      // eslint-disable-next-line no-console
      console.error('Error syncing basket:', error);
    } finally {
      dispatch({ type: 'SET_LOADING', payload: { isLoading: false } });
    }
  };

  /**
   * Add item to basket
   */
  const addItem = async (payload: AddItemPayload) => {
    // Ensure session exists
    ensureSession();

    // Save previous state for rollback
    const previousState = { ...state };

    try {
      // Optimistic update (show immediately in UI)
      dispatch({
        type: 'OPTIMISTIC_ADD',
        payload: {
          productId: payload.productId,
          quantity: payload.quantity,
          unitPrice: 0, // Will be updated from server
          itemTotal: 0,
          specialInstructions: payload.specialInstructions,
        },
      });

      // Call backend
      const updatedBasket = await basketService.addItemToBasket(payload);

      // Sync with server response
      dispatch({ type: 'SYNC_BASKET', payload: { basket: updatedBasket } });
    } catch (error) {
      const errorMessage = getErrorMessage(error);
      dispatch({ type: 'SET_ERROR', payload: { error: errorMessage } });
      dispatch({ type: 'ROLLBACK', payload: { previousState } });
      // eslint-disable-next-line no-console
      console.error('Error adding item to basket:', error);
      throw error; // Re-throw for component-level error handling
    } finally {
      dispatch({ type: 'SET_SYNCING', payload: { isSyncing: false } });
    }
  };

  /**
   * Update item in basket
   */
  const updateItem = async (basketItemId: string, quantity: number, specialInstructions?: string) => {
    const previousState = { ...state };

    try {
      // Optimistic update
      dispatch({
        type: 'OPTIMISTIC_UPDATE',
        payload: { basketItemId, quantity, specialInstructions },
      });

      // Call backend
      const updatedBasket = await basketService.updateBasketItem(basketItemId, {
        quantity,
        specialInstructions,
      });

      // Sync with server response
      dispatch({ type: 'SYNC_BASKET', payload: { basket: updatedBasket } });
    } catch (error) {
      const errorMessage = getErrorMessage(error);
      dispatch({ type: 'SET_ERROR', payload: { error: errorMessage } });
      dispatch({ type: 'ROLLBACK', payload: { previousState } });
      // eslint-disable-next-line no-console
      console.error('Error updating basket item:', error);
      throw error;
    } finally {
      dispatch({ type: 'SET_SYNCING', payload: { isSyncing: false } });
    }
  };

  /**
   * Remove item from basket
   */
  const removeItem = async (basketItemId: string) => {
    const previousState = { ...state };

    try {
      // Optimistic update
      dispatch({
        type: 'OPTIMISTIC_REMOVE',
        payload: { basketItemId },
      });

      // Call backend
      const updatedBasket = await basketService.removeItemFromBasket(basketItemId);

      // Sync with server response
      dispatch({ type: 'SYNC_BASKET', payload: { basket: updatedBasket } });
    } catch (error) {
      const errorMessage = getErrorMessage(error);
      dispatch({ type: 'SET_ERROR', payload: { error: errorMessage } });
      dispatch({ type: 'ROLLBACK', payload: { previousState } });
      // eslint-disable-next-line no-console
      console.error('Error removing basket item:', error);
      throw error;
    } finally {
      dispatch({ type: 'SET_SYNCING', payload: { isSyncing: false } });
    }
  };

  /**
   * Clear entire basket
   */
  const clearCart = async () => {
    const previousState = { ...state };

    try {
      dispatch({ type: 'SET_SYNCING', payload: { isSyncing: true } });

      // Call backend
      const updatedBasket = await basketService.clearBasket();

      // Sync with server response
      dispatch({ type: 'SYNC_BASKET', payload: { basket: updatedBasket } });
    } catch (error) {
      const errorMessage = getErrorMessage(error);
      dispatch({ type: 'SET_ERROR', payload: { error: errorMessage } });
      dispatch({ type: 'ROLLBACK', payload: { previousState } });
      // eslint-disable-next-line no-console
      console.error('Error clearing basket:', error);
      throw error;
    } finally {
      dispatch({ type: 'SET_SYNCING', payload: { isSyncing: false } });
    }
  };

  /**
   * Apply promo code to basket
   */
  const applyPromoCode = async (promoCode: string) => {
    const previousState = { ...state };

    try {
      dispatch({ type: 'SET_SYNCING', payload: { isSyncing: true } });

      // Call backend
      const updatedBasket = await basketService.applyPromoCode(promoCode);

      // Sync with server response
      dispatch({ type: 'SYNC_BASKET', payload: { basket: updatedBasket } });
    } catch (error) {
      const errorMessage = getErrorMessage(error);
      dispatch({ type: 'SET_ERROR', payload: { error: errorMessage } });
      dispatch({ type: 'ROLLBACK', payload: { previousState } });
      // eslint-disable-next-line no-console
      console.error('Error applying promo code:', error);
      throw error;
    } finally {
      dispatch({ type: 'SET_SYNCING', payload: { isSyncing: false } });
    }
  };

  /**
   * Remove promo code from basket
   */
  const removePromoCode = async () => {
    const previousState = { ...state };

    try {
      dispatch({ type: 'SET_SYNCING', payload: { isSyncing: true } });

      // Call backend
      const updatedBasket = await basketService.removePromoCode();

      // Sync with server response
      dispatch({ type: 'SYNC_BASKET', payload: { basket: updatedBasket } });
    } catch (error) {
      const errorMessage = getErrorMessage(error);
      dispatch({ type: 'SET_ERROR', payload: { error: errorMessage } });
      dispatch({ type: 'ROLLBACK', payload: { previousState } });
      // eslint-disable-next-line no-console
      console.error('Error removing promo code:', error);
      throw error;
    } finally {
      dispatch({ type: 'SET_SYNCING', payload: { isSyncing: false } });
    }
  };

  /**
   * Get total item count
   */
  const getItemCount = (): number => {
    return state.items.reduce((total, item) => total + item.quantity, 0);
  };

  /**
   * Get cart total (from backend basket)
   */
  const getTotal = (): number => {
    return state.basket?.total || 0;
  };

  // Sync basket from backend on mount (when session is ready)
  useEffect(() => {
    if (sessionId) {
      syncBasket();
    }
  }, [sessionId]);

  const value: CartContextType = {
    state,
    syncBasket,
    addItem,
    updateItem,
    removeItem,
    clearCart,
    applyPromoCode,
    removePromoCode,
    getItemCount,
    getTotal,
  };

  return (
    <CartContext.Provider value={value}>
      {children}
    </CartContext.Provider>
  );
};

/**
 * Hook to use cart context
 */
export const useCart = () => {
  const context = useContext(CartContext);
  if (context === undefined) {
    throw new Error('useCart must be used within a CartProvider');
  }
  return context;
};
