/**
 * Checkout Context
 *
 * Manages checkout flow state including order type, customer info, and delivery details
 */

"use client";

import React, { createContext, useContext, useState, useEffect, ReactNode } from 'react';
import { OrderType } from '@/types/order';

/**
 * Delivery address structure
 */
export interface DeliveryAddress {
  street: string;
  city: string;
  postalCode: string;
  country: string;
  additionalInfo?: string;
}

/**
 * Customer information structure
 */
export interface CustomerInfo {
  name: string;
  email: string;
  phone: string;
}

/**
 * Checkout state structure
 */
interface CheckoutState {
  orderType: OrderType | null;
  tableNumber: string;
  deliveryAddress: DeliveryAddress | null;
  customerInfo: CustomerInfo | null;
  specialInstructions: string;
  tipAmount: number;
}

/**
 * Checkout context type
 */
interface CheckoutContextType {
  state: CheckoutState;
  setOrderType: (type: OrderType) => void;
  setTableNumber: (tableNumber: string) => void;
  setDeliveryAddress: (address: DeliveryAddress) => void;
  setCustomerInfo: (info: CustomerInfo) => void;
  setSpecialInstructions: (instructions: string) => void;
  setTipAmount: (tipAmount: number) => void;
  clearCheckout: () => void;
}

const CHECKOUT_STORAGE_KEY = 'rumi_checkout_state';

const initialState: CheckoutState = {
  orderType: null,
  tableNumber: '',
  deliveryAddress: null,
  customerInfo: null,
  specialInstructions: '',
  tipAmount: 0,
};

const CheckoutContext = createContext<CheckoutContextType | undefined>(undefined);

/**
 * Load checkout state from localStorage
 */
const loadCheckoutState = (): CheckoutState => {
  if (typeof window === 'undefined') return initialState;

  try {
    const stored = localStorage.getItem(CHECKOUT_STORAGE_KEY);
    if (stored) {
      return JSON.parse(stored);
    }
  } catch (error) {
    // eslint-disable-next-line no-console
    console.error('Failed to load checkout state:', error);
  }
  return initialState;
};

/**
 * Save checkout state to localStorage
 */
const saveCheckoutState = (state: CheckoutState) => {
  if (typeof window === 'undefined') return;

  try {
    localStorage.setItem(CHECKOUT_STORAGE_KEY, JSON.stringify(state));
  } catch (error) {
    // eslint-disable-next-line no-console
    console.error('Failed to save checkout state:', error);
  }
};

/**
 * Checkout Provider Component
 */
export function CheckoutProvider({ children }: { children: ReactNode }) {
  const [state, setState] = useState<CheckoutState>(initialState);

  // Load state from localStorage on mount
  useEffect(() => {
    const loaded = loadCheckoutState();
    setState(loaded);
  }, []);

  // Save state to localStorage whenever it changes
  useEffect(() => {
    saveCheckoutState(state);
  }, [state]);

  const setOrderType = (type: OrderType) => {
    setState((prev) => ({ ...prev, orderType: type }));
  };

  const setTableNumber = (tableNumber: string) => {
    setState((prev) => ({ ...prev, tableNumber }));
  };

  const setDeliveryAddress = (address: DeliveryAddress) => {
    setState((prev) => ({ ...prev, deliveryAddress: address }));
  };

  const setCustomerInfo = (info: CustomerInfo) => {
    setState((prev) => ({ ...prev, customerInfo: info }));
  };

  const setSpecialInstructions = (instructions: string) => {
    setState((prev) => ({ ...prev, specialInstructions: instructions }));
  };

  const setTipAmount = (tipAmount: number) => {
    setState((prev) => ({ ...prev, tipAmount }));
  };

  const clearCheckout = () => {
    setState(initialState);
    if (typeof window !== 'undefined') {
      localStorage.removeItem(CHECKOUT_STORAGE_KEY);
    }
  };

  const value: CheckoutContextType = {
    state,
    setOrderType,
    setTableNumber,
    setDeliveryAddress,
    setCustomerInfo,
    setSpecialInstructions,
    setTipAmount,
    clearCheckout,
  };

  return (
    <CheckoutContext.Provider value={value}>
      {children}
    </CheckoutContext.Provider>
  );
}

/**
 * Hook to use checkout context
 */
export const useCheckout = () => {
  const context = useContext(CheckoutContext);
  if (!context) {
    throw new Error('useCheckout must be used within a CheckoutProvider');
  }
  return context;
};
