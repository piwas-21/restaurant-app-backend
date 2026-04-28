'use client';

import React, { useEffect } from 'react';
import { useRouter } from 'next/navigation';
import { useTranslation } from 'react-i18next';
import { useAuth } from '@/components/AuthContext';
import { useCart } from '@/components/cart/CartContext';
import { useOrders } from '@/hooks/useOrders';
import { useSnackbar } from 'notistack';
import { OrderDto } from '@/types/order';
import { Package, RefreshCw, Loader2, AlertCircle } from 'lucide-react';
import OrderCard from '@/components/orders/OrderCard';
import styles from '../styles/OrdersPage.module.css';

export default function OrdersPage() {
  const { t } = useTranslation();
  const router = useRouter();
  const { user, isLoading: authLoading } = useAuth();
  const { enqueueSnackbar } = useSnackbar();
  const { addItem } = useCart();

  const {
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
    refresh,
    fetchAll,
  } = useOrders();

  useEffect(() => {
    if (authLoading) return;
    if (!user) { router.push('/login'); return; }
    fetchAll();
  }, [authLoading, user, router, fetchAll]);

  const handleReorder = async (order: OrderDto) => {
    try {
      setReorderingOrderId(order.id);
      for (const item of order.items) {
        if (item.productId) {
          await addItem({
            productId: item.productId,
            productVariationId: item.productVariationId,
            menuId: item.menuId,
            quantity: item.quantity,
            specialInstructions: item.specialInstructions,
          });
        }
      }
      enqueueSnackbar(t('items_added_to_cart', 'Items added to cart'), {
        variant: 'success',
        anchorOrigin: { vertical: 'bottom', horizontal: 'right' },
      });
      router.push('/cart');
    } catch {
      enqueueSnackbar(t('failed_to_reorder', 'Failed to add items to cart'), {
        variant: 'error',
        anchorOrigin: { vertical: 'bottom', horizontal: 'right' },
      });
    } finally {
      setReorderingOrderId(null);
    }
  };

  const displayOrders = activeTab === 'active' ? activeOrders : pastOrders;

  if (authLoading || isLoading) {
    return (
      <main className={styles.container}>
        <div className={styles.loadingState}>
          <Loader2 size={64} className={styles.spinner} />
          <p>{authLoading ? t('authenticating', 'Authenticating...') : t('loading_orders', 'Loading your orders...')}</p>
        </div>
      </main>
    );
  }

  return (
    <main className={styles.container}>
      <div className={styles.content}>
        <div className={styles.header}>
          <div className={styles.titleSection}>
            <h1 className={styles.title}>
              <Package size={32} />
              {t('my_orders', 'My Orders')}
            </h1>
            <p className={styles.subtitle}>{t('my_orders_desc', 'View and manage your order history')}</p>
          </div>
          <button onClick={refresh} className={styles.refreshButton} title={t('refresh', 'Refresh')}>
            <RefreshCw size={20} />
            {t('refresh', 'Refresh')}
          </button>
        </div>

        <div className={styles.tabs} role="tablist">
          <button
            role="tab"
            aria-selected={activeTab === 'active'}
            className={`${styles.tab} ${activeTab === 'active' ? styles.tabActive : ''}`}
            onClick={() => setActiveTab('active')}
          >
            {t('tab_active_orders', 'Active')}
            {activeOrders.length > 0 && <span className={styles.tabBadge}>{activeOrders.length}</span>}
          </button>
          <button
            role="tab"
            aria-selected={activeTab === 'past'}
            className={`${styles.tab} ${activeTab === 'past' ? styles.tabActive : ''}`}
            onClick={() => setActiveTab('past')}
          >
            {t('tab_past_orders', 'Past')}
            {pastOrders.length > 0 && <span className={styles.tabBadge}>{pastOrders.length}</span>}
          </button>
          {activeTab === 'active' && lastUpdated && (
            <span className={styles.lastUpdated}>
              {t('last_updated', 'Updated')} {lastUpdated.toLocaleTimeString('de-CH', { hour: '2-digit', minute: '2-digit' })}
            </span>
          )}
        </div>

        {error && (
          <div className={styles.errorAlert}>
            <AlertCircle size={20} />
            <p>{error}</p>
          </div>
        )}

        {displayOrders.length === 0 ? (
          <div className={styles.emptyState}>
            <Package size={64} className={styles.emptyIcon} />
            <h2>
              {activeTab === 'active'
                ? t('no_active_orders_title', 'No Active Orders')
                : t('no_past_orders_title', 'No Past Orders')}
            </h2>
            <p>
              {activeTab === 'active'
                ? t('no_active_orders_cta', 'Place an order and it will appear here')
                : t('no_past_orders_message', 'Your completed orders will appear here')}
            </p>
            {activeTab === 'active' && (
              <button onClick={() => router.push('/menu')} className={styles.browseButton}>
                {t('cart_browse_menu_button', 'Browse Menu')}
              </button>
            )}
          </div>
        ) : (
          <div className={styles.ordersList} role="tabpanel">
            {displayOrders.map((order) => (
              <OrderCard
                key={order.id}
                order={order}
                isExpanded={expandedOrderId === order.id}
                onToggleExpand={toggleExpand}
                isReordering={reorderingOrderId === order.id}
                onReorder={handleReorder}
              />
            ))}
            {activeTab === 'past' && pastHasMore && (
              <div className={styles.loadMoreWrapper}>
                <button onClick={loadMorePast} disabled={isLoadingMore} className={styles.loadMoreButton}>
                  {isLoadingMore
                    ? <><Loader2 size={18} className={styles.spinner} />{t('loading', 'Loading...')}</>
                    : t('load_more', 'Load more')}
                </button>
              </div>
            )}
          </div>
        )}
      </div>
    </main>
  );
}
