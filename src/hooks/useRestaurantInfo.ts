'use client';

import { useCallback, useEffect, useState } from 'react';
import { getRestaurantInfo } from '@/services/restaurantInfoService';
import type { RestaurantInfoDto } from '@/types/restaurantInfo';

/**
 * Module-scope cache shared across all consumers of the hook so the
 * footer / contact section / map don't each issue their own request.
 *
 * Cache is invalidated by either the TTL (30 s — matches the B2
 * acceptance criterion "home page reflects edits within 30 s") or by an
 * explicit call to {@link invalidateRestaurantInfoCache} from the admin
 * save flow (MR 4).
 */
const CACHE_TTL_MS = 30_000;

interface CacheState {
  data: RestaurantInfoDto | null;
  fetchedAt: number;
  /** Single in-flight promise so concurrent first reads don't all hit the API. */
  inflight: Promise<RestaurantInfoDto | null> | null;
}

const cache: CacheState = {
  data: null,
  fetchedAt: 0,
  inflight: null,
};

/**
 * Subscribers — invoked when the cache is updated (success, failure, or
 * invalidation). Each consumer registers `fetchIfStale` so an external
 * invalidation triggers an actual fetch in mounted components, not just a
 * re-render against the stale cache.
 */
const subscribers = new Set<() => void | Promise<void>>();
const notifySubscribers = () => subscribers.forEach((fn) => void fn());

/** Force the next read to bypass the cache. Call after admin saves. */
export const invalidateRestaurantInfoCache = () => {
  cache.fetchedAt = 0;
  notifySubscribers();
};

const isFresh = () => cache.data !== null && Date.now() - cache.fetchedAt < CACHE_TTL_MS;

const loadFromApi = async (): Promise<RestaurantInfoDto | null> => {
  if (cache.inflight) return cache.inflight;
  cache.inflight = (async () => {
    try {
      const response = await getRestaurantInfo();
      const data = response.data ?? null;
      cache.data = data;
      cache.fetchedAt = Date.now();
      notifySubscribers();
      return data;
    } finally {
      cache.inflight = null;
    }
  })();
  return cache.inflight;
};

export interface UseRestaurantInfoReturn {
  info: RestaurantInfoDto | null;
  isLoading: boolean;
  error: Error | null;
  /** Force an immediate refetch and update subscribers. */
  refetch: () => Promise<void>;
}

export function useRestaurantInfo(): UseRestaurantInfoReturn {
  const [info, setInfo] = useState<RestaurantInfoDto | null>(cache.data);
  const [isLoading, setIsLoading] = useState<boolean>(!isFresh());
  const [error, setError] = useState<Error | null>(null);

  const fetchIfStale = useCallback(async () => {
    if (isFresh()) {
      setInfo(cache.data);
      setIsLoading(false);
      return;
    }
    setIsLoading(true);
    setError(null);
    try {
      const data = await loadFromApi();
      setInfo(data);
    } catch (err) {
      setError(err instanceof Error ? err : new Error('Failed to load restaurant info'));
    } finally {
      setIsLoading(false);
    }
  }, []);

  const refetch = useCallback(async () => {
    invalidateRestaurantInfoCache();
    await fetchIfStale();
  }, [fetchIfStale]);

  useEffect(() => {
    // Subscribe with fetchIfStale (not just a setInfo sync) so that
    // invalidateRestaurantInfoCache() — fired by another mounted
    // component on admin save — actually triggers a re-fetch in this
    // consumer rather than re-rendering with the still-stale cache.data.
    subscribers.add(fetchIfStale);
    fetchIfStale();
    return () => {
      subscribers.delete(fetchIfStale);
    };
  }, [fetchIfStale]);

  return { info, isLoading, error, refetch };
}
