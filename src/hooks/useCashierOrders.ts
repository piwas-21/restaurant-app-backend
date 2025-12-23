'use client';

import { useState, useEffect, useCallback, useRef } from 'react';
import {
  getCashierOrders,
  getOrderById,
  updateOrderStatus,
  addPaymentToOrder,
  refundPayment,
  cancelOrder,
  toggleFocusOrder,
} from '@/services/cashierService';
import { OrderDto } from '@/types/order';

interface UseCashierOrdersReturn {
  orders: OrderDto[];
  isConnected: boolean;
  isLoading: boolean;
  error: string | null;
  lastEventTime: Date | null;
  connectionState: 'connecting' | 'connected' | 'disconnected' | 'error';
  refreshOrders: () => Promise<void>;
  updateOrderStatus: (orderId: string, status: string) => Promise<OrderDto>;
  addPayment: (orderId: string, paymentData: any) => Promise<OrderDto>;
  refundPayment: (orderId: string, paymentId: string, amount?: number) => Promise<OrderDto>;
  cancelOrder: (orderId: string, reason?: string) => Promise<OrderDto>;
  toggleFocusOrder: (
    orderId: string,
    isFocus: boolean,
    priority?: number,
    reason?: string
  ) => Promise<OrderDto>;
}

// Connection health check interval (30 seconds)
const HEALTH_CHECK_INTERVAL_MS = 30000;
// Maximum time without any event before considering connection dead (45 seconds)
const MAX_SILENCE_MS = 45000;

export function useCashierOrders(): UseCashierOrdersReturn {
  const [orders, setOrders] = useState<OrderDto[]>([]);
  const [isConnected, setIsConnected] = useState(false);
  const [isLoading, setIsLoading] = useState(true);
  const [error, setError] = useState<string | null>(null);
  const [lastEventTime, setLastEventTime] = useState<Date | null>(null);
  const [connectionState, setConnectionState] = useState<'connecting' | 'connected' | 'disconnected' | 'error'>('disconnected');

  const eventSourceRef = useRef<EventSource | null>(null);
  const pollingTimeoutRef = useRef<NodeJS.Timeout | null>(null);
  const healthCheckIntervalRef = useRef<NodeJS.Timeout | null>(null);
  const reconnectAttemptRef = useRef(0);
  const maxReconnectAttemptsRef = useRef(10); // Increased from 5
  const lastEventTimeRef = useRef<Date | null>(null); // Ref for health check
  const isReconnectingRef = useRef(false); // Prevent duplicate reconnections

  /**
   * Fetch orders from API
   */
  const refreshOrders = useCallback(async () => {
    try {
      setError(null);
      const result = await getCashierOrders();
      setOrders(result.items || []);
      setIsLoading(false);
    } catch (err) {
      const errorMessage = err instanceof Error ? err.message : 'Failed to load orders';
      setError(errorMessage);
      setIsLoading(false);
      console.error('Error fetching orders:', err);
    }
  }, []);

  /**
   * Clean up SSE connection
   */
  const cleanupSSE = useCallback(() => {
    if (eventSourceRef.current) {
      console.log('🔌 SSE: Closing existing connection');
      eventSourceRef.current.close();
      eventSourceRef.current = null;
    }
    if (healthCheckIntervalRef.current) {
      clearInterval(healthCheckIntervalRef.current);
      healthCheckIntervalRef.current = null;
    }
  }, []);

  /**
   * Connect to SSE stream for real-time updates
   */
  const connectToSSE = useCallback(() => {
    // Prevent duplicate connections
    if (eventSourceRef.current && eventSourceRef.current.readyState !== EventSource.CLOSED) {
      console.log('⚠️ SSE: Already connected or connecting, skipping');
      return;
    }

    if (isReconnectingRef.current) {
      console.log('⚠️ SSE: Reconnection already in progress, skipping');
      return;
    }

    // Clean up any existing connection first
    cleanupSSE();

    try {
      setConnectionState('connecting');
      
      const authToken = localStorage.getItem('auth_token');
      const apiUrl = process.env.NEXT_PUBLIC_API_URL || 'http://localhost:5221';
      const endpoint = '/api/events/service';

      let url = `${apiUrl}${endpoint}`;
      if (authToken) {
        url += `?token=${encodeURIComponent(authToken)}`;
      }

      console.log('🔌 SSE: Initiating connection to:', endpoint);
      console.log('🔌 SSE: Connection attempt:', reconnectAttemptRef.current + 1);

      const eventSource = new EventSource(url);
      eventSourceRef.current = eventSource;

      // Handle successful connection
      eventSource.addEventListener('connected', (event) => {
        try {
          const data = JSON.parse(event.data);
          console.log('✅ SSE: Connected with clientId:', data.clientId);
          setIsConnected(true);
          setConnectionState('connected');
          setError(null);
          const now = new Date();
          setLastEventTime(now);
          lastEventTimeRef.current = now;
          reconnectAttemptRef.current = 0;
          isReconnectingRef.current = false;
        } catch (err) {
          console.error('❌ SSE: Error parsing connected event:', err);
        }
      });

      // Handle heartbeat events (keep-alive)
      eventSource.addEventListener('heartbeat', (event) => {
        const now = new Date();
        setLastEventTime(now);
        lastEventTimeRef.current = now;
        console.log('💓 SSE: Heartbeat received at', now.toISOString());
      });

      // Handle order events
      eventSource.addEventListener('order-created', (event) => {
        try {
          const data = JSON.parse(event.data);
          console.log('📦 SSE: Received order-created:', data.order?.orderNumber || data.orderNumber);
          setOrders((prev) => {
            // Prevent duplicates by checking if order already exists
            const exists = prev.some(o => o.id === (data.order?.id || data.id));
            if (exists) {
              console.log('📦 SSE: Order already exists, skipping duplicate');
              return prev;
            }
            return [data.order || data, ...prev];
          });
          const now = new Date();
          setLastEventTime(now);
          lastEventTimeRef.current = now;
          reconnectAttemptRef.current = 0;
        } catch (err) {
          console.error('Error parsing order-created event:', err);
        }
      });

      eventSource.addEventListener('order-status-changed', (event) => {
        try {
          const data = JSON.parse(event.data);
          console.log('📝 SSE: Received order-status-changed:', data.orderId || data.order?.id);
          setOrders((prev) =>
            prev.map((order) =>
              order.id === (data.orderId || data.order?.id) ? (data.order || data) : order
            )
          );
          const now = new Date();
          setLastEventTime(now);
          lastEventTimeRef.current = now;
          reconnectAttemptRef.current = 0;
        } catch (err) {
          console.error('Error parsing order-status-changed event:', err);
        }
      });

      eventSource.addEventListener('order-ready', (event) => {
        try {
          const data = JSON.parse(event.data);
          setOrders((prev) =>
            prev.map((order) =>
              order.id === (data.orderId || data.order?.id)
                ? { ...order, status: 'Ready', ...data.order }
                : order
            )
          );
          const now = new Date();
          setLastEventTime(now);
          lastEventTimeRef.current = now;
          reconnectAttemptRef.current = 0;
        } catch (err) {
          console.error('Error parsing order-ready event:', err);
        }
      });

      eventSource.addEventListener('order-completed', (event) => {
        try {
          const data = JSON.parse(event.data);
          setOrders((prev) =>
            prev.map((order) =>
              order.id === (data.orderId || data.order?.id)
                ? { ...order, status: 'Completed', ...data.order }
                : order
            )
          );
          const now = new Date();
          setLastEventTime(now);
          lastEventTimeRef.current = now;
          reconnectAttemptRef.current = 0;
        } catch (err) {
          console.error('Error parsing order-completed event:', err);
        }
      });

      eventSource.addEventListener('focus-order-update', (event) => {
        try {
          const data = JSON.parse(event.data);
          setOrders((prev) =>
            prev.map((order) =>
              order.id === (data.orderId || data.order?.id)
                ? { ...order, isFocusOrder: data.isFocus, ...data.order }
                : order
            )
          );
          const now = new Date();
          setLastEventTime(now);
          lastEventTimeRef.current = now;
          reconnectAttemptRef.current = 0;
        } catch (err) {
          console.error('Error parsing focus-order-update event:', err);
        }
      });

      eventSource.onerror = (event) => {
        console.error('❌ SSE: Connection error:', {
          readyState: eventSource.readyState,
          readyStateText: ['CONNECTING', 'OPEN', 'CLOSED'][eventSource.readyState],
          timestamp: new Date().toISOString(),
        });
        
        setIsConnected(false);
        setConnectionState('error');
        cleanupSSE();

        // Reconnect with exponential backoff
        if (reconnectAttemptRef.current < maxReconnectAttemptsRef.current && !isReconnectingRef.current) {
          isReconnectingRef.current = true;
          reconnectAttemptRef.current += 1;
          const backoffMs = Math.min(1000 * Math.pow(2, reconnectAttemptRef.current), 15000);
          console.warn(`🔄 SSE: Reconnecting in ${backoffMs}ms (attempt ${reconnectAttemptRef.current}/${maxReconnectAttemptsRef.current})`);

          if (pollingTimeoutRef.current) clearTimeout(pollingTimeoutRef.current);
          pollingTimeoutRef.current = setTimeout(() => {
            isReconnectingRef.current = false;
            connectToSSE();
          }, backoffMs);
        } else if (reconnectAttemptRef.current >= maxReconnectAttemptsRef.current) {
          console.warn('⚠️ SSE: Max reconnect attempts reached, falling back to polling');
          setError('Real-time updates unavailable - using 10s polling');
          isReconnectingRef.current = false;
          setupPolling();
        }
      };

      // Setup health check interval
      healthCheckIntervalRef.current = setInterval(() => {
        const lastEvent = lastEventTimeRef.current;
        if (lastEvent) {
          const silenceMs = Date.now() - lastEvent.getTime();
          if (silenceMs > MAX_SILENCE_MS) {
            console.warn(`⚠️ SSE: No events for ${Math.round(silenceMs / 1000)}s, reconnecting...`);
            cleanupSSE();
            connectToSSE();
          }
        }
      }, HEALTH_CHECK_INTERVAL_MS);

    } catch (err) {
      console.error('Error connecting to SSE:', err);
      setIsConnected(false);
      setConnectionState('error');
      setError('Failed to establish SSE connection');
      setupPolling();
    }
    // eslint-disable-next-line react-hooks/exhaustive-deps
  }, [cleanupSSE]); // setupPolling is intentionally omitted to avoid circular dependency

  /**
   * Setup polling fallback (every 10 seconds)
   */
  const setupPolling = useCallback(() => {
    setIsConnected(false);
    setConnectionState('disconnected');

    const pollOrders = async () => {
      try {
        await refreshOrders();
      } catch (err) {
        console.error('Polling error:', err);
      }
      pollingTimeoutRef.current = setTimeout(pollOrders, 10000);
    };

    pollOrders();
  }, [refreshOrders]);

  /**
   * Handle visibility change - reconnect when tab becomes visible
   */
  useEffect(() => {
    const handleVisibilityChange = () => {
      if (document.visibilityState === 'visible') {
        console.log('👁️ Tab became visible, checking connection...');
        
        // Refresh orders immediately when tab becomes visible
        refreshOrders();
        
        // Check if SSE needs reconnection
        const eventSource = eventSourceRef.current;
        if (!eventSource || eventSource.readyState === EventSource.CLOSED) {
          console.log('🔄 SSE: Connection lost while tab was hidden, reconnecting...');
          reconnectAttemptRef.current = 0; // Reset retry count
          connectToSSE();
        } else {
          // Check if we've been silent too long
          const lastEvent = lastEventTimeRef.current;
          if (lastEvent) {
            const silenceMs = Date.now() - lastEvent.getTime();
            if (silenceMs > MAX_SILENCE_MS) {
              console.log(`🔄 SSE: Silent for ${Math.round(silenceMs / 1000)}s, reconnecting...`);
              reconnectAttemptRef.current = 0;
              connectToSSE();
            }
          }
        }
      }
    };

    document.addEventListener('visibilitychange', handleVisibilityChange);
    return () => document.removeEventListener('visibilitychange', handleVisibilityChange);
  }, [connectToSSE, refreshOrders]);

  /**
   * Handle online/offline events
   */
  useEffect(() => {
    const handleOnline = () => {
      console.log('🌐 Network: Back online, reconnecting SSE...');
      reconnectAttemptRef.current = 0;
      connectToSSE();
      refreshOrders();
    };

    const handleOffline = () => {
      console.log('🌐 Network: Went offline');
      setIsConnected(false);
      setConnectionState('disconnected');
      setError('Network connection lost');
    };

    window.addEventListener('online', handleOnline);
    window.addEventListener('offline', handleOffline);
    return () => {
      window.removeEventListener('online', handleOnline);
      window.removeEventListener('offline', handleOffline);
    };
  }, [connectToSSE, refreshOrders]);

  /**
   * Initialize SSE connection and handle cleanup
   */
  useEffect(() => {
    console.log('🔌 Initializing cashier orders hook...');
    
    // Initial fetch
    refreshOrders();

    // Small delay to ensure component is fully mounted before SSE connection
    const connectionTimeout = setTimeout(() => {
      console.log('🔌 Attempting initial SSE connection...');
      connectToSSE();
    }, 100);

    return () => {
      console.log('🔌 Cleaning up cashier orders hook...');
      clearTimeout(connectionTimeout);
      cleanupSSE();
      if (pollingTimeoutRef.current) {
        clearTimeout(pollingTimeoutRef.current);
        pollingTimeoutRef.current = null;
      }
    };
    // eslint-disable-next-line react-hooks/exhaustive-deps
  }, []);

  /**
   * Update order status
   */
  const handleUpdateOrderStatus = useCallback(
    async (orderId: string, status: string) => {
      try {
        const updatedOrder = await updateOrderStatus(orderId, status);
        let mergedOrder: OrderDto | undefined;
        setOrders((prev) =>
          prev.map((order) => {
            if (order.id === orderId) {
              mergedOrder = { ...order, ...updatedOrder };
              return mergedOrder;
            }
            return order;
          })
        );
        return mergedOrder || updatedOrder;
      } catch (err) {
        const errorMessage = err instanceof Error ? err.message : 'Failed to update status';
        setError(errorMessage);
        throw err;
      }
    },
    []
  );

  /**
   * Add payment to order
   */
  const handleAddPayment = useCallback(
    async (orderId: string, paymentData: any) => {
      try {
        const updatedOrder = await addPaymentToOrder(orderId, paymentData);
        let mergedOrder: OrderDto | undefined;
        setOrders((prev) =>
          prev.map((order) => {
            if (order.id === orderId) {
              mergedOrder = { ...order, ...updatedOrder };
              return mergedOrder;
            }
            return order;
          })
        );
        return mergedOrder || updatedOrder;
      } catch (err) {
        const errorMessage = err instanceof Error ? err.message : 'Failed to add payment';
        setError(errorMessage);
        throw err;
      }
    },
    []
  );

  /**
   * Refund payment
   */
  const handleRefundPayment = useCallback(
    async (orderId: string, paymentId: string, amount?: number) => {
      try {
        const updatedOrder = await refundPayment(orderId, paymentId, amount);
        let mergedOrder: OrderDto | undefined;
        setOrders((prev) =>
          prev.map((order) => {
            if (order.id === orderId) {
              mergedOrder = { ...order, ...updatedOrder };
              return mergedOrder;
            }
            return order;
          })
        );
        return mergedOrder || updatedOrder;
      } catch (err) {
        const errorMessage = err instanceof Error ? err.message : 'Failed to refund';
        setError(errorMessage);
        throw err;
      }
    },
    []
  );

  /**
   * Cancel order
   */
  const handleCancelOrder = useCallback(
    async (orderId: string, reason?: string) => {
      try {
        const updatedOrder = await cancelOrder(orderId, reason);
        let mergedOrder: OrderDto | undefined;
        setOrders((prev) =>
          prev.map((order) => {
            if (order.id === orderId) {
              mergedOrder = { ...order, ...updatedOrder };
              return mergedOrder;
            }
            return order;
          })
        );
        return mergedOrder || updatedOrder;
      } catch (err) {
        const errorMessage = err instanceof Error ? err.message : 'Failed to cancel order';
        setError(errorMessage);
        throw err;
      }
    },
    []
  );

  /**
   * Toggle focus order
   */
  const handleToggleFocusOrder = useCallback(
    async (orderId: string, isFocus: boolean, priority?: number, reason?: string) => {
      try {
        const updatedOrder = await toggleFocusOrder(orderId, isFocus, priority, reason);
        let mergedOrder: OrderDto | undefined;
        setOrders((prev) =>
          prev.map((order) => {
            if (order.id === orderId) {
              mergedOrder = { ...order, ...updatedOrder };
              return mergedOrder;
            }
            return order;
          })
        );
        return mergedOrder || updatedOrder;
      } catch (err) {
        const errorMessage = err instanceof Error ? err.message : 'Failed to toggle focus';
        setError(errorMessage);
        throw err;
      }
    },
    []
  );

  return {
    orders,
    isConnected,
    isLoading,
    error,
    lastEventTime,
    connectionState,
    refreshOrders,
    updateOrderStatus: handleUpdateOrderStatus,
    addPayment: handleAddPayment,
    refundPayment: handleRefundPayment,
    cancelOrder: handleCancelOrder,
    toggleFocusOrder: handleToggleFocusOrder,
  };
}
