/**
 * Session Provider
 *
 * Context provider for managing session state across the application.
 * Automatically creates and maintains session for anonymous users.
 */

'use client';

import React, { createContext, useContext, useEffect, useState, ReactNode } from 'react';
import {
  getOrCreateSessionId,
  clearSessionId,
  refreshSessionExpiry,
  hasActiveSession,
  getSessionExpiryDate,
  getSessionId,
} from '@/services/sessionService';

interface SessionContextType {
  sessionId: string | null;
  isLoading: boolean;
  hasSession: boolean;
  expiryDate: Date | null;
  refreshSession: () => void;
  clearSession: () => void;
  ensureSession: () => string;
}

const SessionContext = createContext<SessionContextType | undefined>(undefined);

interface SessionProviderProps {
  children: ReactNode;
}

/**
 * Session Provider Component
 * Wrap your app with this provider to enable session management
 *
 * @example
 * ```tsx
 * <SessionProvider>
 *   <App />
 * </SessionProvider>
 * ```
 */
export function SessionProvider({ children }: SessionProviderProps) {
  const [sessionId, setSessionId] = useState<string | null>(null);
  const [isLoading, setIsLoading] = useState(true);
  const [expiryDate, setExpiryDate] = useState<Date | null>(null);

  // Initialize session on mount
  useEffect(() => {
    const initializeSession = () => {
      try {
        // Get or create session for anonymous users
        const id = getOrCreateSessionId();
        setSessionId(id);

        const expiry = getSessionExpiryDate();
        setExpiryDate(expiry);
      } catch (error) {
        console.error('Error initializing session:', error);
      } finally {
        setIsLoading(false);
      }
    };

    initializeSession();
  }, []);

  // Refresh session periodically (every 30 minutes)
  useEffect(() => {
    const refreshInterval = setInterval(
      () => {
        if (hasActiveSession()) {
          refreshSessionExpiry();
          const expiry = getSessionExpiryDate();
          setExpiryDate(expiry);
        }
      },
      30 * 60 * 1000,
    ); // 30 minutes

    return () => clearInterval(refreshInterval);
  }, []);

  /**
   * Refresh session expiry
   */
  const refreshSession = () => {
    try {
      refreshSessionExpiry();
      const expiry = getSessionExpiryDate();
      setExpiryDate(expiry);
    } catch (error) {
      console.error('Error refreshing session:', error);
    }
  };

  /**
   * Clear session (used on logout or login)
   */
  const clearSession = () => {
    try {
      clearSessionId();
      setSessionId(null);
      setExpiryDate(null);
    } catch (error) {
      console.error('Error clearing session:', error);
    }
  };

  /**
   * Ensure session exists, create if it doesn't
   */
  const ensureSession = (): string => {
    try {
      const id = getOrCreateSessionId();
      setSessionId(id);

      const expiry = getSessionExpiryDate();
      setExpiryDate(expiry);

      return id;
    } catch (error) {
      console.error('Error ensuring session:', error);
      throw error;
    }
  };

  const value: SessionContextType = {
    sessionId,
    isLoading,
    hasSession: hasActiveSession(),
    expiryDate,
    refreshSession,
    clearSession,
    ensureSession,
  };

  return <SessionContext.Provider value={value}>{children}</SessionContext.Provider>;
}

/**
 * Hook to use session context
 * Must be used within SessionProvider
 */
export function useSessionContext(): SessionContextType {
  const context = useContext(SessionContext);

  if (context === undefined) {
    throw new Error('useSessionContext must be used within SessionProvider');
  }

  return context;
}

/**
 * Hook to get current session ID
 * Returns null if session doesn't exist
 */
export function useCurrentSessionId(): string | null {
  const context = useContext(SessionContext);

  // Allow usage outside of provider (returns null)
  if (context === undefined) {
    return getSessionId();
  }

  return context.sessionId;
}
