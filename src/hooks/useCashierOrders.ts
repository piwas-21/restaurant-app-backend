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

// PRIMARY: Polling interval (5 seconds) - guaranteed order delivery
const POLLING_INTERVAL_MS = 5000;
// SSE health check interval (20 seconds for monitoring)
const HEALTH_CHECK_INTERVAL_MS = 20000;
// Maximum time without any SSE event before considering connection dead
const MAX_SILENCE_MS = 35000;
// Minimum time between SSE reconnection attempts
const MIN_RECONNECT_INTERVAL_MS = 2000;

export function useCashierOrders(): UseCashierOrdersReturn {
  const [orders, setOrders] = useState<OrderDto[]>([]);
  const [isConnected, setIsConnected] = useState(false);
  const [isLoading, setIsLoading] = useState(true);
  const [error, setError] = useState<string | null>(null);
  const [lastEventTime, setLastEventTime] = useState<Date | null>(null);
  const [connectionState, setConnectionState] = useState<'connecting' | 'connected' | 'disconnected' | 'error'>('disconnected');

  // Refs for stable references across renders
  const eventSourceRef = useRef<EventSource | null>(null);
  const pollingTimeoutRef = useRef<NodeJS.Timeout | null>(null);
  const healthCheckIntervalRef = useRef<NodeJS.Timeout | null>(null);
  const reconnectTimeoutRef = useRef<NodeJS.Timeout | null>(null);
  const reconnectAttemptRef = useRef(0);
  const lastEventTimeRef = useRef<Date | null>(null);
  const lastReconnectTimeRef = useRef<number>(0);
  const connectionIdRef = useRef<string>(''); // Track connection ID to prevent stale closures
  const isMountedRef = useRef(true);
  const lastPolledAtRef = useRef<Date | null>(null);  // Track last poll time for modifiedSince
  const primaryPollingIntervalRef = useRef<NodeJS.Timeout | null>(null); // Primary polling mechanism
  const maxReconnectAttempts = 15;

  /**
   * Fetch orders from API (supports modifiedSince for efficient polling)
   */
  const refreshOrders = useCallback(async (modifiedSince?: Date) => {
    if (!isMountedRef.current) return;

    try {
      setError(null);
      // Use modifiedSince parameter for efficient polling
      const result = await getCashierOrders(modifiedSince ? { modifiedSince } : undefined);
      if (isMountedRef.current) {
        if (modifiedSince && result.items && result.items.length > 0) {
          // Incremental update: merge new/updated orders
          console.log(`📦 Polling: Found ${result.items.length} new/updated orders`);
          setOrders((prev) => {
            const newOrders = [...prev];
            for (const order of result.items) {
              const existingIndex = newOrders.findIndex(o => o.id === order.id);
              if (existingIndex >= 0) {
                newOrders[existingIndex] = order; // Update existing
              } else {
                newOrders.unshift(order); // Add new at start
              }
            }
            return newOrders;
          });
        } else if (!modifiedSince) {
          // Full refresh
          setOrders(result.items || []);
        }
        lastPolledAtRef.current = new Date();
        setIsLoading(false);
      }
    } catch (err) {
      if (isMountedRef.current) {
        const errorMessage = err instanceof Error ? err.message : 'Failed to load orders';
        setError(errorMessage);
        setIsLoading(false);
        console.error('Error fetching orders:', err);
      }
    }
  }, []);

  /**
   * Clean up SSE connection
   */
  const cleanupSSE = useCallback(() => {
    if (eventSourceRef.current) {
      console.log('🔌 SSE: Closing connection');
      eventSourceRef.current.close();
      eventSourceRef.current = null;
    }
    if (healthCheckIntervalRef.current) {
      clearInterval(healthCheckIntervalRef.current);
      healthCheckIntervalRef.current = null;
    }
    if (reconnectTimeoutRef.current) {
      clearTimeout(reconnectTimeoutRef.current);
      reconnectTimeoutRef.current = null;
    }
  }, []);

  /**
   * Connect to SSE stream for real-time updates
   */
  const connectToSSE = useCallback(() => {
    if (!isMountedRef.current) {
      console.log('⚠️ SSE: Component unmounted, skipping connection');
      return;
    }

    // Prevent rapid reconnections
    const now = Date.now();
    if (now - lastReconnectTimeRef.current < MIN_RECONNECT_INTERVAL_MS) {
      console.log('⚠️ SSE: Too soon for reconnection, scheduling...');
      if (reconnectTimeoutRef.current) clearTimeout(reconnectTimeoutRef.current);
      reconnectTimeoutRef.current = setTimeout(() => {
        if (isMountedRef.current) connectToSSE();
      }, MIN_RECONNECT_INTERVAL_MS);
      return;
    }
    lastReconnectTimeRef.current = now;

    // Already connected or connecting
    if (eventSourceRef.current && eventSourceRef.current.readyState !== EventSource.CLOSED) {
      console.log('⚠️ SSE: Already connected or connecting, skipping');
      return;
    }

    // Clean up any existing connection
    cleanupSSE();

    // Generate unique connection ID for this attempt
    const connectionId = `conn_${Date.now()}_${Math.random().toString(36).substr(2, 9)}`;
    connectionIdRef.current = connectionId;

    try {
      setConnectionState('connecting');

      const authToken = localStorage.getItem('auth_token');
      const apiUrl = process.env.NEXT_PUBLIC_API_URL || 'http://localhost:5221';
      const endpoint = '/api/events/service';

      let url = `${apiUrl}${endpoint}`;
      if (authToken) {
        url += `?token=${encodeURIComponent(authToken)}`;
      }

      console.log(`🔌 SSE: Connecting [${connectionId}] attempt ${reconnectAttemptRef.current + 1}/${maxReconnectAttempts}`);

      const eventSource = new EventSource(url);
      eventSourceRef.current = eventSource;

      // Handle successful connection
      eventSource.addEventListener('connected', (event) => {
        // Check if this is still the active connection
        if (connectionIdRef.current !== connectionId || !isMountedRef.current) {
          console.log(`⚠️ SSE: Stale connection event [${connectionId}], ignoring`);
          return;
        }

        try {
          const data = JSON.parse(event.data);
          console.log(`✅ SSE: Connected [${connectionId}] clientId:`, data.clientId);
          setIsConnected(true);
          setConnectionState('connected');
          setError(null);
          const eventTime = new Date();
          setLastEventTime(eventTime);
          lastEventTimeRef.current = eventTime;
          reconnectAttemptRef.current = 0;
        } catch (err) {
          console.error('❌ SSE: Error parsing connected event:', err);
        }
      });

      // Handle heartbeat events
      eventSource.addEventListener('heartbeat', (event) => {
        if (connectionIdRef.current !== connectionId || !isMountedRef.current) return;

        const eventTime = new Date();
        setLastEventTime(eventTime);
        lastEventTimeRef.current = eventTime;
        console.log('💓 SSE: Heartbeat received at', eventTime.toISOString());
      });

      // Handle order events
      eventSource.addEventListener('order-created', (event) => {
        if (connectionIdRef.current !== connectionId || !isMountedRef.current) return;

        try {
          const data = JSON.parse(event.data);
          console.log('📦 SSE: order-created:', data.order?.orderNumber || data.orderNumber);
          setOrders((prev) => {
            const newOrder = data.order || data;
            if (prev.some(o => o.id === newOrder.id)) {
              console.log('📦 SSE: Order already exists, skipping duplicate');
              return prev;
            }
            return [newOrder, ...prev];
          });
          const eventTime = new Date();
          setLastEventTime(eventTime);
          lastEventTimeRef.current = eventTime;
          reconnectAttemptRef.current = 0;
        } catch (err) {
          console.error('Error parsing order-created event:', err);
        }
      });

      eventSource.addEventListener('order-status-changed', (event) => {
        if (connectionIdRef.current !== connectionId || !isMountedRef.current) return;

        try {
          const data = JSON.parse(event.data);
          const orderId = data.orderId || data.order?.id;
          console.log('📝 SSE: order-status-changed:', orderId, '→', data.order?.status);
          setOrders((prev) =>
            prev.map((order) =>
              order.id === orderId ? (data.order || { ...order, ...data }) : order
            )
          );
          const eventTime = new Date();
          setLastEventTime(eventTime);
          lastEventTimeRef.current = eventTime;
          reconnectAttemptRef.current = 0;
        } catch (err) {
          console.error('Error parsing order-status-changed event:', err);
        }
      });

      eventSource.addEventListener('order-ready', (event) => {
        if (connectionIdRef.current !== connectionId || !isMountedRef.current) return;

        try {
          const data = JSON.parse(event.data);
          const orderId = data.orderId || data.order?.id;
          setOrders((prev) =>
            prev.map((order) =>
              order.id === orderId
                ? { ...order, status: 'Ready', ...data.order }
                : order
            )
          );
          const eventTime = new Date();
          setLastEventTime(eventTime);
          lastEventTimeRef.current = eventTime;
        } catch (err) {
          console.error('Error parsing order-ready event:', err);
        }
      });

      eventSource.addEventListener('order-completed', (event) => {
        if (connectionIdRef.current !== connectionId || !isMountedRef.current) return;

        try {
          const data = JSON.parse(event.data);
          const orderId = data.orderId || data.order?.id;
          setOrders((prev) =>
            prev.map((order) =>
              order.id === orderId
                ? { ...order, status: 'Completed', ...data.order }
                : order
            )
          );
          const eventTime = new Date();
          setLastEventTime(eventTime);
          lastEventTimeRef.current = eventTime;
        } catch (err) {
          console.error('Error parsing order-completed event:', err);
        }
      });

      eventSource.addEventListener('focus-order-update', (event) => {
        if (connectionIdRef.current !== connectionId || !isMountedRef.current) return;

        try {
          const data = JSON.parse(event.data);
          const orderId = data.orderId || data.order?.id;
          setOrders((prev) =>
            prev.map((order) =>
              order.id === orderId
                ? { ...order, isFocusOrder: data.isFocus, ...data.order }
                : order
            )
          );
          const eventTime = new Date();
          setLastEventTime(eventTime);
          lastEventTimeRef.current = eventTime;
        } catch (err) {
          console.error('Error parsing focus-order-update event:', err);
        }
      });

      // Handle connection errors
      eventSource.onerror = () => {
        if (connectionIdRef.current !== connectionId) {
          console.log(`⚠️ SSE: Stale error event [${connectionId}], ignoring`);
          return;
        }

        console.error(`❌ SSE: Connection error [${connectionId}]`, {
          readyState: eventSource.readyState,
          readyStateText: ['CONNECTING', 'OPEN', 'CLOSED'][eventSource.readyState],
        });

        if (!isMountedRef.current) return;

        setIsConnected(false);
        setConnectionState('error');
        cleanupSSE();

        // Reconnect with exponential backoff
        if (reconnectAttemptRef.current < maxReconnectAttempts) {
          reconnectAttemptRef.current += 1;
          const backoffMs = Math.min(1000 * Math.pow(1.5, reconnectAttemptRef.current), 30000);
          console.warn(`🔄 SSE: Reconnecting in ${Math.round(backoffMs/1000)}s (attempt ${reconnectAttemptRef.current}/${maxReconnectAttempts})`);

          reconnectTimeoutRef.current = setTimeout(() => {
            if (isMountedRef.current) {
              connectToSSE();
            }
          }, backoffMs);
        } else {
          console.warn('⚠️ SSE: Max reconnect attempts reached, continuing with polling only');
          setError('Real-time SSE unavailable - using polling (every 5s)');
          // No need to setupPolling - primary polling is already running
        }
      };

      // Setup health check interval
      healthCheckIntervalRef.current = setInterval(() => {
        if (!isMountedRef.current || connectionIdRef.current !== connectionId) {
          return;
        }

        const lastEvent = lastEventTimeRef.current;
        const currentEventSource = eventSourceRef.current;

        // 1. Check if connection is closed or null
        if (!currentEventSource || currentEventSource.readyState === EventSource.CLOSED) {
          console.warn('⚠️ SSE: Connection closed, reconnecting...');
          reconnectAttemptRef.current = 0;
          connectToSSE();
          return;
        }

        // 2. Check if stuck in CONNECTING state (0) for too long
        // If we are connecting but haven't received 'connected' event or any data
        if (currentEventSource.readyState === EventSource.CONNECTING) {
             const connectingDuration = Date.now() - lastReconnectTimeRef.current;
             // If connecting for more than 15 seconds, assume stuck
             if (connectingDuration > 15000) {
                 console.warn(`⚠️ SSE: Stuck in CONNECTING state for ${Math.round(connectingDuration/1000)}s, forcing reconnect...`);
                 eventSourceRef.current?.close(); // Force close
                 connectToSSE();
                 return;
             }
        }

        // 3. Check for silence timeout (only if OPEN/connected)
        if (currentEventSource.readyState === EventSource.OPEN && lastEvent) {
          const silenceMs = Date.now() - lastEvent.getTime();
          if (silenceMs > MAX_SILENCE_MS) {
            console.warn(`⚠️ SSE: No events for ${Math.round(silenceMs / 1000)}s, reconnecting...`);
            reconnectAttemptRef.current = 0;
            connectToSSE();
          }
        }
      }, HEALTH_CHECK_INTERVAL_MS);

    } catch (err) {
      console.error('Error connecting to SSE:', err);
      if (isMountedRef.current) {
        setIsConnected(false);
        setConnectionState('error');
        setError('SSE unavailable - using polling (every 5s)');
        // No need to setupPolling - primary polling is already running
      }
    }
  }, [cleanupSSE]); // Minimal dependencies

  /**
   * Start primary polling mechanism (runs ALWAYS, not just as fallback)
   */
  const startPrimaryPolling = useCallback(() => {
    if (primaryPollingIntervalRef.current) return; // Already running

    console.log('🔄 Starting PRIMARY polling (every 5s)');

    primaryPollingIntervalRef.current = setInterval(() => {
      if (!isMountedRef.current) return;

      // Use modifiedSince for efficient incremental updates
      const since = lastPolledAtRef.current;
      refreshOrders(since || undefined);
    }, POLLING_INTERVAL_MS);
  }, [refreshOrders]);

  /**
   * Stop primary polling
   */
  const stopPrimaryPolling = useCallback(() => {
    if (primaryPollingIntervalRef.current) {
      clearInterval(primaryPollingIntervalRef.current);
      primaryPollingIntervalRef.current = null;
    }
  }, []);

  /**
   * Handle visibility change - reconnect when tab becomes visible
   */
  useEffect(() => {
    const handleVisibilityChange = () => {
      if (document.visibilityState === 'visible' && isMountedRef.current) {
        console.log('👁️ Tab became visible, checking connection...');

        // Refresh orders immediately
        refreshOrders();

        // Check if SSE needs reconnection
        const eventSource = eventSourceRef.current;
        if (!eventSource || eventSource.readyState === EventSource.CLOSED) {
          console.log('🔄 SSE: Connection lost while hidden, reconnecting...');
          reconnectAttemptRef.current = 0;
          connectToSSE();
        } else {
          // Check for silence while tab was hidden
          const lastEvent = lastEventTimeRef.current;
          if (lastEvent) {
            const silenceMs = Date.now() - lastEvent.getTime();
            if (silenceMs > MAX_SILENCE_MS) {
              console.log(`🔄 SSE: Silent for ${Math.round(silenceMs / 1000)}s while hidden, reconnecting...`);
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
      if (!isMountedRef.current) return;
      console.log('🌐 Network: Back online, reconnecting SSE...');
      reconnectAttemptRef.current = 0;
      connectToSSE();
      refreshOrders();
    };

    const handleOffline = () => {
      if (!isMountedRef.current) return;
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
   * Initialize polling (primary) and SSE (enhancement) connections
   */
  useEffect(() => {
    console.log('🔌 Initializing cashier orders hook...');
    isMountedRef.current = true;

    // Initial full fetch
    refreshOrders();

    // Start PRIMARY polling mechanism (guaranteed order delivery every 5s)
    const pollingStartTimeout = setTimeout(() => {
      if (isMountedRef.current) {
        startPrimaryPolling();
      }
    }, 100);

    // Start SSE as optional ENHANCEMENT (provides instant updates when working)
    // Delay slightly to ensure component is fully mounted
    const sseTimeout = setTimeout(() => {
      if (isMountedRef.current) {
        console.log('🔌 Attempting SSE connection (enhancement)...');
        connectToSSE();
      }
    }, 500);

    return () => {
      console.log('🔌 Cleaning up cashier orders hook...');
      isMountedRef.current = false;
      clearTimeout(pollingStartTimeout);
      clearTimeout(sseTimeout);
      stopPrimaryPolling();
      cleanupSSE();
      if (pollingTimeoutRef.current) {
        clearTimeout(pollingTimeoutRef.current);
        pollingTimeoutRef.current = null;
      }
    };
  }, []); // Empty dependency array - only run on mount/unmount

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
