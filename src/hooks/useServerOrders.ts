'use client';

import { useState, useEffect, useCallback, useRef } from 'react';
import {
  getDineInOrders,
  getTablesWithStatus,
  updateOrderStatus as updateOrderStatusService,
  ServerTableDto,
} from '@/services/serverService';
import { OrderDto } from '@/types/order';

interface UseServerOrdersReturn {
  orders: OrderDto[];
  tables: ServerTableDto[];
  isConnected: boolean;
  isLoading: boolean;
  error: string | null;
  lastEventTime: Date | null;
  connectionState: 'connecting' | 'connected' | 'disconnected' | 'error';
  refreshOrders: () => Promise<void>;
  refreshTables: () => Promise<void>;
  updateOrderStatus: (orderId: string, status: string) => Promise<OrderDto>;
  getOrdersForTable: (tableNumber: string) => OrderDto[];
}

// Polling interval (5 seconds) - guaranteed order delivery
const POLLING_INTERVAL_MS = 5000;
// SSE health check interval (20 seconds for monitoring)
const HEALTH_CHECK_INTERVAL_MS = 20000;
// Maximum time without any SSE event before considering connection dead
const MAX_SILENCE_MS = 35000;
// Minimum time between SSE reconnection attempts
const MIN_RECONNECT_INTERVAL_MS = 2000;

export function useServerOrders(): UseServerOrdersReturn {
  const [orders, setOrders] = useState<OrderDto[]>([]);
  const [tables, setTables] = useState<ServerTableDto[]>([]);
  const [isConnected, setIsConnected] = useState(false);
  const [isLoading, setIsLoading] = useState(true);
  const [error, setError] = useState<string | null>(null);
  const [lastEventTime, setLastEventTime] = useState<Date | null>(null);
  const [connectionState, setConnectionState] = useState<'connecting' | 'connected' | 'disconnected' | 'error'>(
    'disconnected',
  );

  // Refs for stable references across renders
  const eventSourceRef = useRef<EventSource | null>(null);
  const healthCheckIntervalRef = useRef<NodeJS.Timeout | null>(null);
  const reconnectTimeoutRef = useRef<NodeJS.Timeout | null>(null);
  const reconnectAttemptRef = useRef(0);
  const lastEventTimeRef = useRef<Date | null>(null);
  const lastReconnectTimeRef = useRef<number>(0);
  const connectionIdRef = useRef<string>('');
  const isMountedRef = useRef(true);
  const lastPolledAtRef = useRef<Date | null>(null);
  const primaryPollingIntervalRef = useRef<NodeJS.Timeout | null>(null);
  const maxReconnectAttempts = 15;

  /**
   * Fetch orders from API
   */
  const refreshOrders = useCallback(async (modifiedSince?: Date) => {
    if (!isMountedRef.current) return;

    try {
      setError(null);
      const result = await getDineInOrders(modifiedSince ? { modifiedSince } : undefined);
      if (isMountedRef.current) {
        if (modifiedSince && result.items && result.items.length > 0) {
          // Incremental update: merge new/updated orders
          setOrders((prev) => {
            const newOrders = [...prev];
            for (const order of result.items) {
              const existingIndex = newOrders.findIndex((o) => o.id === order.id);
              if (existingIndex >= 0) {
                newOrders[existingIndex] = order;
              } else {
                newOrders.unshift(order);
              }
            }
            return newOrders;
          });
        } else if (!modifiedSince) {
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
   * Fetch tables with status
   */
  const refreshTables = useCallback(async () => {
    if (!isMountedRef.current) return;

    try {
      const tablesWithStatus = await getTablesWithStatus();
      if (isMountedRef.current) {
        setTables(tablesWithStatus);
      }
    } catch (err) {
      console.error('Error fetching tables:', err);
    }
  }, []);

  /**
   * Clean up SSE connection
   */
  const cleanupSSE = useCallback(() => {
    if (eventSourceRef.current) {
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
      return;
    }

    // Prevent rapid reconnections
    const now = Date.now();
    if (now - lastReconnectTimeRef.current < MIN_RECONNECT_INTERVAL_MS) {
      if (reconnectTimeoutRef.current) clearTimeout(reconnectTimeoutRef.current);
      reconnectTimeoutRef.current = setTimeout(() => {
        if (isMountedRef.current) connectToSSE();
      }, MIN_RECONNECT_INTERVAL_MS);
      return;
    }
    lastReconnectTimeRef.current = now;

    if (eventSourceRef.current && eventSourceRef.current.readyState !== EventSource.CLOSED) {
      return;
    }

    cleanupSSE();

    const connectionId = `srv_${Date.now()}_${Math.random().toString(36).substr(2, 9)}`;
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

      const eventSource = new EventSource(url);
      eventSourceRef.current = eventSource;

      // Handle successful connection
      eventSource.addEventListener('connected', (event) => {
        if (connectionIdRef.current !== connectionId || !isMountedRef.current) return;

        try {
          const data = JSON.parse(event.data);
          setIsConnected(true);
          setConnectionState('connected');
          setError(null);
          const eventTime = new Date();
          setLastEventTime(eventTime);
          lastEventTimeRef.current = eventTime;
          reconnectAttemptRef.current = 0;
        } catch (err) {
          console.error('❌ Server SSE: Error parsing connected event:', err);
        }
      });

      // Handle heartbeat events
      eventSource.addEventListener('heartbeat', () => {
        if (connectionIdRef.current !== connectionId || !isMountedRef.current) return;

        const eventTime = new Date();
        setLastEventTime(eventTime);
        lastEventTimeRef.current = eventTime;
      });

      // Handle order events
      eventSource.addEventListener('order-created', (event) => {
        if (connectionIdRef.current !== connectionId || !isMountedRef.current) return;

        try {
          const data = JSON.parse(event.data);
          const newOrder = data.order || data;

          // Only add dine-in orders
          if (newOrder.type === 'DineIn') {
            setOrders((prev) => {
              if (prev.some((o) => o.id === newOrder.id)) {
                return prev;
              }
              return [newOrder, ...prev];
            });
            // Refresh tables to update status
            refreshTables();
          }

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
          setOrders((prev) =>
            prev.map((order) => (order.id === orderId ? data.order || { ...order, ...data } : order)),
          );
          // Refresh tables to update status
          refreshTables();

          const eventTime = new Date();
          setLastEventTime(eventTime);
          lastEventTimeRef.current = eventTime;
          reconnectAttemptRef.current = 0;
        } catch (err) {
          console.error('Error parsing order-status-changed event:', err);
        }
      });

      eventSource.addEventListener('order-completed', (event) => {
        if (connectionIdRef.current !== connectionId || !isMountedRef.current) return;

        try {
          const data = JSON.parse(event.data);
          const orderId = data.orderId || data.order?.id;
          setOrders((prev) =>
            prev.map((order) => (order.id === orderId ? { ...order, status: 'Completed', ...data.order } : order)),
          );
          refreshTables();

          const eventTime = new Date();
          setLastEventTime(eventTime);
          lastEventTimeRef.current = eventTime;
        } catch (err) {
          console.error('Error parsing order-completed event:', err);
        }
      });

      // Handle connection errors
      eventSource.onerror = () => {
        if (connectionIdRef.current !== connectionId) return;

        console.error(`❌ Server SSE: Connection error [${connectionId}]`);

        if (!isMountedRef.current) return;

        setIsConnected(false);
        setConnectionState('error');
        cleanupSSE();

        if (reconnectAttemptRef.current < maxReconnectAttempts) {
          reconnectAttemptRef.current += 1;
          const backoffMs = Math.min(1000 * Math.pow(1.5, reconnectAttemptRef.current), 30000);
          console.warn(`🔄 Server SSE: Reconnecting in ${Math.round(backoffMs / 1000)}s`);

          reconnectTimeoutRef.current = setTimeout(() => {
            if (isMountedRef.current) {
              connectToSSE();
            }
          }, backoffMs);
        } else {
          console.warn('⚠️ Server SSE: Max reconnect attempts reached, using polling only');
          setError('Real-time updates unavailable - using polling');
        }
      };

      // Setup health check interval
      healthCheckIntervalRef.current = setInterval(() => {
        if (!isMountedRef.current || connectionIdRef.current !== connectionId) return;

        const lastEvent = lastEventTimeRef.current;
        const currentEventSource = eventSourceRef.current;

        if (!currentEventSource || currentEventSource.readyState === EventSource.CLOSED) {
          console.warn('⚠️ Server SSE: Connection closed, reconnecting...');
          reconnectAttemptRef.current = 0;
          connectToSSE();
          return;
        }

        if (currentEventSource.readyState === EventSource.OPEN && lastEvent) {
          const silenceMs = Date.now() - lastEvent.getTime();
          if (silenceMs > MAX_SILENCE_MS) {
            console.warn(`⚠️ Server SSE: No events for ${Math.round(silenceMs / 1000)}s, reconnecting...`);
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
        setError('SSE unavailable - using polling');
      }
    }
  }, [cleanupSSE, refreshTables]);

  /**
   * Start primary polling mechanism
   */
  const startPrimaryPolling = useCallback(() => {
    if (primaryPollingIntervalRef.current) return;

    primaryPollingIntervalRef.current = setInterval(() => {
      if (!isMountedRef.current) return;

      const since = lastPolledAtRef.current;
      refreshOrders(since || undefined);
      refreshTables();
    }, POLLING_INTERVAL_MS);
  }, [refreshOrders, refreshTables]);

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
   * Handle visibility change
   */
  useEffect(() => {
    const handleVisibilityChange = () => {
      if (document.visibilityState === 'visible' && isMountedRef.current) {
        refreshOrders();
        refreshTables();

        const eventSource = eventSourceRef.current;
        if (!eventSource || eventSource.readyState === EventSource.CLOSED) {
          reconnectAttemptRef.current = 0;
          connectToSSE();
        }
      }
    };

    document.addEventListener('visibilitychange', handleVisibilityChange);
    return () => document.removeEventListener('visibilitychange', handleVisibilityChange);
  }, [connectToSSE, refreshOrders, refreshTables]);

  /**
   * Initialize connections
   */
  useEffect(() => {
    isMountedRef.current = true;

    // Initial fetch
    refreshOrders();
    refreshTables();

    // Start polling
    const pollingStartTimeout = setTimeout(() => {
      if (isMountedRef.current) {
        startPrimaryPolling();
      }
    }, 100);

    // Start SSE
    const sseTimeout = setTimeout(() => {
      if (isMountedRef.current) {
        connectToSSE();
      }
    }, 500);

    return () => {
      isMountedRef.current = false;
      clearTimeout(pollingStartTimeout);
      clearTimeout(sseTimeout);
      stopPrimaryPolling();
      cleanupSSE();
    };
  }, []);

  /**
   * Update order status
   */
  const handleUpdateOrderStatus = useCallback(
    async (orderId: string, status: string) => {
      try {
        const updatedOrder = await updateOrderStatusService(orderId, status);
        let mergedOrder: OrderDto | undefined;
        setOrders((prev) =>
          prev.map((order) => {
            if (order.id === orderId) {
              mergedOrder = { ...order, ...updatedOrder };
              return mergedOrder;
            }
            return order;
          }),
        );
        // Refresh tables to update status
        await refreshTables();
        return mergedOrder || updatedOrder;
      } catch (err) {
        const errorMessage = err instanceof Error ? err.message : 'Failed to update status';
        setError(errorMessage);
        throw err;
      }
    },
    [refreshTables],
  );

  /**
   * Get orders for a specific table
   */
  const getOrdersForTable = useCallback(
    (tableNumber: string) => {
      return orders.filter((order) => order.tableNumber?.toString() === tableNumber);
    },
    [orders],
  );

  return {
    orders,
    tables,
    isConnected,
    isLoading,
    error,
    lastEventTime,
    connectionState,
    refreshOrders,
    refreshTables,
    updateOrderStatus: handleUpdateOrderStatus,
    getOrdersForTable,
  };
}
