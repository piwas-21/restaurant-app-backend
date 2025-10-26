import { useState, useEffect } from 'react';
import { OrderStatus } from '@/types/order';

interface OrderFilterPreferences {
  selectedStatus: OrderStatus | 'All';
  selectedPaymentStatus: string;
  selectedOrderType: string;
  showFocusOnly: boolean;
  sortBy: 'date' | 'amount';
  sortOrder: 'asc' | 'desc';
}

const STORAGE_KEY = 'admin-orders-filter-preferences';

const DEFAULT_PREFERENCES: OrderFilterPreferences = {
  selectedStatus: 'All',
  selectedPaymentStatus: 'All',
  selectedOrderType: 'All',
  showFocusOnly: false,
  sortBy: 'date',
  sortOrder: 'desc',
};

/**
 * Custom hook to manage order filter preferences with localStorage persistence
 */
export function useOrderFilterPreferences() {
  const [preferences, setPreferences] = useState<OrderFilterPreferences>(DEFAULT_PREFERENCES);
  const [isLoaded, setIsLoaded] = useState(false);

  // Load preferences from localStorage on mount
  useEffect(() => {
    try {
      const stored = localStorage.getItem(STORAGE_KEY);
      if (stored) {
        const parsed = JSON.parse(stored) as OrderFilterPreferences;
        setPreferences({ ...DEFAULT_PREFERENCES, ...parsed });
      }
    } catch {
      // Failed to load preferences, use defaults
    } finally {
      setIsLoaded(true);
    }
  }, []);

  // Save preferences to localStorage whenever they change
  const savePreferences = (newPreferences: Partial<OrderFilterPreferences>) => {
    const updated = { ...preferences, ...newPreferences };
    setPreferences(updated);

    try {
      localStorage.setItem(STORAGE_KEY, JSON.stringify(updated));
    } catch {
      // Failed to save, continue without persistence
    }
  };

  // Clear all preferences
  const clearPreferences = () => {
    setPreferences(DEFAULT_PREFERENCES);
    try {
      localStorage.removeItem(STORAGE_KEY);
    } catch {
      // Failed to clear, continue
    }
  };

  return {
    preferences,
    isLoaded,
    savePreferences,
    clearPreferences,
  };
}
