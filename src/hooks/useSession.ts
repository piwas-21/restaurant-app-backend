/**
 * useSession Hook
 *
 * React hook for managing session lifecycle in components.
 * Automatically creates session on mount and provides session management functions.
 */

'use client';

import { useEffect, useState } from 'react';
import {
  getSessionId,
  getOrCreateSessionId,
  clearSessionId,
  refreshSessionExpiry,
  hasActiveSession,
  getSessionExpiryDate,
} from '@/services/sessionService';

export interface UseSessionReturn {
  sessionId: string | null;
  isLoading: boolean;
  hasSession: boolean;
  expiryDate: Date | null;
  refreshSession: () => void;
  clearSession: () => void;
  ensureSession: () => string;
}

/**
 * Hook to manage user session
 *
 * @param autoCreate - Automatically create session on mount (default: true)
 * @returns Session management functions and state
 *
 * @example
 * ```tsx
 * function MyComponent() {
 *   const { sessionId, hasSession, ensureSession } = useSession();
 *
 *   const handleAddToCart = () => {
 *     ensureSession(); // Ensure session exists before adding to cart
 *     // ... add to cart logic
 *   };
 * }
 * ```
 */
export function useSession(autoCreate: boolean = true): UseSessionReturn {
  const [sessionId, setSessionId] = useState<string | null>(null);
  const [isLoading, setIsLoading] = useState(true);
  const [expiryDate, setExpiryDate] = useState<Date | null>(null);

  // Initialize session on mount
  useEffect(() => {
    const initializeSession = () => {
      try {
        if (autoCreate) {
          const id = getOrCreateSessionId();
          setSessionId(id);
        } else {
          const id = getSessionId();
          setSessionId(id);
        }

        const expiry = getSessionExpiryDate();
        setExpiryDate(expiry);
      } catch (error) {
        // eslint-disable-next-line no-console
        console.error('Error initializing session:', error);
      } finally {
        setIsLoading(false);
      }
    };

    initializeSession();
  }, [autoCreate]);

  /**
   * Refresh session expiry
   */
  const refreshSession = () => {
    try {
      refreshSessionExpiry();
      const expiry = getSessionExpiryDate();
      setExpiryDate(expiry);
    } catch (error) {
      // eslint-disable-next-line no-console
      console.error('Error refreshing session:', error);
    }
  };

  /**
   * Clear session
   */
  const clearSession = () => {
    try {
      clearSessionId();
      setSessionId(null);
      setExpiryDate(null);
    } catch (error) {
      // eslint-disable-next-line no-console
      console.error('Error clearing session:', error);
    }
  };

  /**
   * Ensure session exists, create if it doesn't
   * Returns the session ID
   */
  const ensureSession = (): string => {
    try {
      const id = getOrCreateSessionId();
      setSessionId(id);

      const expiry = getSessionExpiryDate();
      setExpiryDate(expiry);

      return id;
    } catch (error) {
      // eslint-disable-next-line no-console
      console.error('Error ensuring session:', error);
      throw error;
    }
  };

  return {
    sessionId,
    isLoading,
    hasSession: hasActiveSession(),
    expiryDate,
    refreshSession,
    clearSession,
    ensureSession,
  };
}

/**
 * Hook to get session ID only (no auto-create)
 * Useful for components that just need to read the session ID
 */
export function useSessionId(): string | null {
  const { sessionId } = useSession(false);
  return sessionId;
}
