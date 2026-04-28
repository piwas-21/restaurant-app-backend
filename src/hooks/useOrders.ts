'use client';

import { useCallback, useEffect, useRef, useState } from 'react';
import { useTranslation } from 'react-i18next';
import { useSnackbar } from 'notistack';
import { getOrders } from '@/services/orderService';
import { OrderDto } from '@/types/order';
import { ACTIVE_STATUSES, PAST_STATUSES } from '@/constants/orderStatus';

export type OrderTab = 'active' | 'past';

const POLL_INTERVAL_MS = 30_000;
const PAST_PAGE_SIZE = 20;

export function useOrders() {
  const { t } = useTranslation();
  const { enqueueSnackbar } = useSnackbar();

  const [activeOrders, setActiveOrders] = useState<OrderDto[]>([]);
  const [pastOrders, setPastOrders] = useState<OrderDto[]>([]);
  const [isLoading, setIsLoading] = useState(true);
  const [error, setError] = useState('');
  const [activeTab, setActiveTab] = useState<OrderTab>('active');
  const [expandedOrderId, setExpandedOrderId] = useState<string | null>(null);
  const [reorderingOrderId, setReorderingOrderId] = useState<string | null>(null);
  const [lastUpdated, setLastUpdated] = useState<Date | null>(null);

  // Pagination for past tab
  const [pastPage, setPastPage] = useState(1);
  const [pastHasMore, setPastHasMore] = useState(false);
  const [isLoadingMore, setIsLoadingMore] = useState(false);

  const pollTimerRef = useRef<ReturnType<typeof setTimeout> | null>(null);

  const fetchActive = useCallback(async () => {
    try {
      const result = await getOrders({ descending: true });
      const sorted = result.items
        .filter((o) => (ACTIVE_STATUSES as string[]).includes(o.status))
        .sort((a, b) => new Date(b.orderDate).getTime() - new Date(a.orderDate).getTime());
      setActiveOrders(sorted);
      setLastUpdated(new Date());
    } catch {
      // Silently keep stale data on poll failure; only show error on first load
    }
  }, []);

  const fetchPast = useCallback(async (page: number, append: boolean) => {
    try {
      if (append) setIsLoadingMore(true);
      const result = await getOrders({ descending: true, page, pageSize: PAST_PAGE_SIZE });
      const pageItems = result.items
        .filter((o) => (PAST_STATUSES as string[]).includes(o.status))
        .sort((a, b) => new Date(b.orderDate).getTime() - new Date(a.orderDate).getTime());
      setPastOrders((prev) => (append ? [...prev, ...pageItems] : pageItems));
      setPastHasMore(result.hasNextPage);
    } catch {
      // silently fail on pagination
    } finally {
      if (append) setIsLoadingMore(false);
    }
  }, []);

  const fetchAll = useCallback(async () => {
    try {
      setIsLoading(true);
      setError('');
      await Promise.all([fetchActive(), fetchPast(1, false)]);
      setPastPage(1);
    } catch {
      const msg = t('failed_to_load_orders', 'Failed to load orders');
      setError(msg);
      enqueueSnackbar(msg, { variant: 'error', anchorOrigin: { vertical: 'bottom', horizontal: 'right' } });
    } finally {
      setIsLoading(false);
    }
  }, [fetchActive, fetchPast, t, enqueueSnackbar]);

  // Schedule next active-tab poll when tab is visible
  const schedulePoll = useCallback(() => {
    if (pollTimerRef.current) clearTimeout(pollTimerRef.current);
    pollTimerRef.current = setTimeout(async () => {
      if (document.visibilityState === 'visible') {
        await fetchActive();
      }
      schedulePoll();
    }, POLL_INTERVAL_MS);
  }, [fetchActive]);

  useEffect(() => {
    schedulePoll();
    return () => {
      if (pollTimerRef.current) clearTimeout(pollTimerRef.current);
    };
  }, [schedulePoll]);

  const loadMorePast = useCallback(async () => {
    const next = pastPage + 1;
    await fetchPast(next, true);
    setPastPage(next);
  }, [pastPage, fetchPast]);

  const toggleExpand = useCallback((id: string) => {
    setExpandedOrderId((prev) => (prev === id ? null : id));
  }, []);

  return {
    activeOrders,
    pastOrders,
    isLoading,
    error,
    activeTab,
    setActiveTab,
    expandedOrderId,
    toggleExpand,
    reorderingOrderId,
    setReorderingOrderId,
    lastUpdated,
    pastHasMore,
    isLoadingMore,
    loadMorePast,
    refresh: fetchAll,
    fetchAll,
  };
}
