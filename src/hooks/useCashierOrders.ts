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

export function useCashierOrders(): UseCashierOrdersReturn {
  const [orders, setOrders] = useState<OrderDto[]>([]);
  const [isConnected, setIsConnected] = useState(false);
  const [isLoading, setIsLoading] = useState(true);
  const [error, setError] = useState<string | null>(null);

  const eventSourceRef = useRef<EventSource | null>(null);
  const pollingTimeoutRef = useRef<NodeJS.Timeout | null>(null);
  const reconnectAttemptRef = useRef(0);
  const maxReconnectAttemptsRef = useRef(5);

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
      // eslint-disable-next-line no-console
      console.error('Error fetching orders:', err);
    }
  }, []);

  /**
   * Connect to SSE stream for real-time updates
   */
  const connectToSSE = useCallback(() => {
    if (eventSourceRef.current) {
      console.log('⚠️ SSE: Already connected, skipping duplicate connection');
      return; // Already connected
    }

    try {
      // Get auth token and pass as query parameter (EventSource doesn't support headers)
      const authToken = localStorage.getItem('auth_token');
      const apiUrl = process.env.NEXT_PUBLIC_API_URL || 'http://localhost:5221';
      const endpoint = '/api/events/service';

      // Always use service endpoint, pass token as query param if available
      let url = `${apiUrl}${endpoint}`;
      if (authToken) {
        url += `?token=${encodeURIComponent(authToken)}`;
      }

      console.log('🔌 SSE: Initiating connection to:', endpoint, '| API URL:', apiUrl);
      console.log('🔌 SSE: Auth token present:', !!authToken);

      const eventSource = new EventSource(url);
      console.log('🔌 SSE: EventSource created, readyState:', eventSource.readyState);

      // Listen for successful connection before marking as connected
      eventSource.addEventListener('connected', (event) => {
        try {
          const data = JSON.parse(event.data);
          console.log('✅ SSE: Successfully connected with clientId:', data.clientId);
          setIsConnected(true);
          setError(null);
          reconnectAttemptRef.current = 0;
        } catch (err) {
          console.error('❌ SSE: Error parsing connected event:', err);
        }
      });

      eventSource.addEventListener('order-created', (event) => {
        try {
          const data = JSON.parse(event.data);
          // eslint-disable-next-line no-console
          console.log('Received order-created event:', data);
          setOrders((prev) => [data.order || data, ...prev]);
          reconnectAttemptRef.current = 0; // Reset retry count on success
        } catch (err) {
          // eslint-disable-next-line no-console
          console.error('Error parsing order-created event:', err);
        }
      });

      eventSource.addEventListener('order-status-changed', (event) => {
        try {
          const data = JSON.parse(event.data);
          setOrders((prev) =>
            prev.map((order) =>
              order.id === (data.orderId || data.order?.id) ? (data.order || data) : order
            )
          );
          reconnectAttemptRef.current = 0;
        } catch (err) {
          // eslint-disable-next-line no-console
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
          reconnectAttemptRef.current = 0;
        } catch (err) {
          // eslint-disable-next-line no-console
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
          reconnectAttemptRef.current = 0;
        } catch (err) {
          // eslint-disable-next-line no-console
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
          reconnectAttemptRef.current = 0;
        } catch (err) {
          // eslint-disable-next-line no-console
          console.error('Error parsing focus-order-update event:', err);
        }
      });

      eventSource.onerror = (event) => {
        console.error('❌ SSE: Connection error occurred:', {
          readyState: eventSource.readyState,
          readyStateText: eventSource.readyState === 0 ? 'CONNECTING' : eventSource.readyState === 1 ? 'OPEN' : 'CLOSED',
          url: url,
          hasAuthToken: !!authToken,
          timestamp: new Date().toISOString()
        });
        setIsConnected(false);
        eventSourceRef.current?.close();
        eventSourceRef.current = null;

        // Attempt to reconnect with exponential backoff
        if (reconnectAttemptRef.current < maxReconnectAttemptsRef.current) {
          reconnectAttemptRef.current += 1;
          const backoffMs = Math.min(1000 * Math.pow(2, reconnectAttemptRef.current), 10000);
          console.warn(`🔄 SSE: Reconnect attempt ${reconnectAttemptRef.current}/${maxReconnectAttemptsRef.current} scheduled in ${backoffMs}ms`);

          if (pollingTimeoutRef.current) clearTimeout(pollingTimeoutRef.current);
          pollingTimeoutRef.current = setTimeout(() => {
            console.log('🔄 SSE: Executing reconnection attempt...');
            connectToSSE();
          }, backoffMs);
        } else {
          // Fall back to polling if SSE fails too many times
          console.warn('⚠️ SSE: Connection failed after max attempts, falling back to polling');
          setError('Real-time updates unavailable - using 10s polling');
          setupPolling();
        }
      };

      eventSourceRef.current = eventSource;
      // Don't set as connected yet - wait for 'connected' event
    } catch (err) {
      // eslint-disable-next-line no-console
      console.error('Error connecting to SSE:', err);
      setIsConnected(false);
      setError('Failed to establish SSE connection');
      // Fall back to polling
      setupPolling();
    }
  }, []);

  /**
   * Setup polling fallback (every 10 seconds)
   */
  const setupPolling = useCallback(() => {
    setIsConnected(false);

    const pollOrders = async () => {
      try {
        await refreshOrders();
      } catch (err) {
        // eslint-disable-next-line no-console
        console.error('Polling error:', err);
      }

      // Schedule next poll in 10 seconds
      pollingTimeoutRef.current = setTimeout(pollOrders, 10000);
    };

    // Start polling
    pollOrders();
  }, [refreshOrders]);

  /**
   * Initialize SSE connection and handle cleanup
   */
  useEffect(() => {
    console.log('🔌 Initializing cashier orders hook...');
    
    // Initial fetch
    refreshOrders();

    // Small delay to ensure component is fully mounted before SSE connection
    const connectionTimeout = setTimeout(() => {
      console.log('🔌 Attempting SSE connection...');
      connectToSSE();
    }, 100);

    return () => {
      // Cleanup on unmount
      console.log('🔌 Cleaning up cashier orders hook...');
      clearTimeout(connectionTimeout);
      if (eventSourceRef.current) {
        eventSourceRef.current.close();
        eventSourceRef.current = null;
      }
      if (pollingTimeoutRef.current) {
        clearTimeout(pollingTimeoutRef.current);
        pollingTimeoutRef.current = null;
      }
    };
    // eslint-disable-next-line react-hooks/exhaustive-deps
  }, []); // Empty dependency array is intentional - only run on mount

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
    refreshOrders,
    updateOrderStatus: handleUpdateOrderStatus,
    addPayment: handleAddPayment,
    refundPayment: handleRefundPayment,
    cancelOrder: handleCancelOrder,
    toggleFocusOrder: handleToggleFocusOrder,
  };
}
