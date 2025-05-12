"use client";

import React, { createContext, useContext, useReducer, ReactNode } from 'react';

interface CartItem {
  id: string;
  name: string;
  price: number;
  quantity: number;
  image_url?: string;
}

interface CartState {
  items: CartItem[];
}

// Define specific payload types for each action
interface AddItemPayload {
  id: string;
  name: string;
  price: number;
  quantity?: number;
  // Include any other fields needed for adding an item
}

interface RemoveItemPayload {
  id: string;
}

interface UpdateQuantityPayload {
  id: string;
  quantity: number;
}

interface DecrementItemPayload {
  id: string;
}

// Use discriminated union type for CartAction
type CartAction = 
  | { type: 'ADD_ITEM'; payload: AddItemPayload }
  | { type: 'REMOVE_ITEM'; payload: RemoveItemPayload }
  | { type: 'UPDATE_QUANTITY'; payload: UpdateQuantityPayload }
  | { type: 'DECREMENT_ITEM'; payload: DecrementItemPayload };

const initialState: CartState = {
  items: [],
};

const CartContext = createContext<{
  state: CartState;
  dispatch: React.Dispatch<CartAction>;
} | undefined>(undefined);

function cartReducer(state: CartState, action: CartAction): CartState {
  switch (action.type) {
    case 'ADD_ITEM':
      // Logic to add item, check if exists, update quantity or add new
      const existingItemIndex = state.items.findIndex(item => item.id === action.payload.id);
      if (existingItemIndex > -1) {
        const updatedItems = [...state.items];
        updatedItems[existingItemIndex].quantity += action.payload.quantity || 1;
        return { ...state, items: updatedItems };
      } else {
        return { ...state, items: [...state.items, { ...action.payload, quantity: action.payload.quantity || 1 }] };
      }
    case 'REMOVE_ITEM':
      return { ...state, items: state.items.filter(item => item.id !== action.payload.id) };
    case 'UPDATE_QUANTITY':
      return {
        ...state,
        items: state.items.map(item =>
          item.id === action.payload.id ? { ...item, quantity: Math.max(0, action.payload.quantity) } : item
        ).filter(item => item.quantity > 0) // Optionally remove if quantity is 0
      };
    case 'DECREMENT_ITEM':
      return {
        ...state,
        items: state.items.map(item =>
          item.id === action.payload.id ? { ...item, quantity: Math.max(0, item.quantity - 1) } : item
        ).filter(item => item.quantity > 0) // Remove if quantity becomes 0
      };
    default:
      return state;
  }
}

export const CartProvider = ({ children }: { children: ReactNode }) => {
  const [state, dispatch] = useReducer(cartReducer, initialState);
  return (
    <CartContext.Provider value={{ state, dispatch }}>
      {children}
    </CartContext.Provider>
  );
};

export const useCart = () => {
  const context = useContext(CartContext);
  if (context === undefined) {
    throw new Error('useCart must be used within a CartProvider');
  }
  return context;
};
