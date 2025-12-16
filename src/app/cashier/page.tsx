'use client';

import { useState, useMemo, useCallback, useEffect, useRef } from 'react';
import { useTranslation } from 'react-i18next';
import { useCashierOrders } from '@/hooks/useCashierOrders';
import { useNotification } from '@/hooks/useNotification';
import OrderList from '@/components/cashier/OrderList';
import OrderDetails from '@/components/cashier/OrderDetails';
import StatusUpdateDialog from '@/components/cashier/StatusUpdateDialog';
import PaymentDialog from '@/components/cashier/PaymentDialog';
import RefundDialog from '@/components/cashier/RefundDialog';
import CancelOrderDialog from '@/components/cashier/CancelOrderDialog';
import FocusOrderDialog from '@/components/cashier/FocusOrderDialog';
import QuickConfirmModal from '@/components/cashier/QuickConfirmModal';
import NotificationCenter from '@/components/cashier/NotificationCenter';
import { OrderType } from '@/types/order';
import { QRCodeValidationResult } from '@/types/userGroupTypes';
import styles from '@/app/styles/CashierPage.module.css';
import { RefreshCw, QrCode, Printer, Settings } from 'lucide-react';
import QRScannerDialog from '@/components/cashier/QRScannerDialog';
import AutoPrintSettingsModal from '@/components/cashier/AutoPrintSettingsModal';
import { quickConfirmOrder, quickCancelOrder } from '@/services/cashierService';
import { exportKitchenItemsToPDF, exportOrderToPDF } from '@/utils/pdfExportUtils';
import { AutoPrintSettings, DEFAULT_AUTO_PRINT_SETTINGS } from '@/types/cashier';

export default function CashierPage() {
  const { t } = useTranslation();
  const {
    orders,
    isConnected,
    isLoading,
    error,
    refreshOrders,
    updateOrderStatus,
    addPayment,
    refundPayment,
    cancelOrder,
    toggleFocusOrder,
  } = useCashierOrders();

  // UI State
  const [selectedOrderId, setSelectedOrderId] = useState<string | null>(null);
  const [searchQuery, setSearchQuery] = useState('');
  const [statusFilter, setStatusFilter] = useState<string>('all');
  const [paymentStatusFilter, setPaymentStatusFilter] = useState<string>('all');
  const [orderTypeFilter, setOrderTypeFilter] = useState<string>('all');
  const [isRefreshing, setIsRefreshing] = useState(false);
  const [autoPrintSettings, setAutoPrintSettings] = useState<AutoPrintSettings>(DEFAULT_AUTO_PRINT_SETTINGS);
  const [showAutoPrintSettings, setShowAutoPrintSettings] = useState(false);

  // Dialog State
  const [showStatusDialog, setShowStatusDialog] = useState(false);
  const [showPaymentDialog, setShowPaymentDialog] = useState(false);
  const [showRefundDialog, setShowRefundDialog] = useState(false);
  const [showCancelDialog, setShowCancelDialog] = useState(false);
  const [showFocusDialog, setShowFocusDialog] = useState(false);
  const [showQRScannerDialog, setShowQRScannerDialog] = useState(false);
  const [showQuickConfirmModal, setShowQuickConfirmModal] = useState(false);
  const [pendingOrderForConfirm, setPendingOrderForConfirm] = useState<string | null>(null);
  const [dismissedOrders, setDismissedOrders] = useState<Set<string>>(new Set());

  // Dialog feedback messages
  const [successMessage, setSuccessMessage] = useState<string | null>(null);
  const [errorMessage, setErrorMessage] = useState<string | null>(null);

  // Notifications
  const {
    notifications,
    removeNotification,
    notifyNewOrder,
    notifyOrderUpdate,
    playOrderUpdateSound,
    audioEnabled,
    toggleAudio,
  } = useNotification();
  const previousOrderCountRef = useRef(0);
  const previousOrderStatusesRef = useRef<Map<string, string>>(new Map());
  const isInitialLoadRef = useRef(true);

  // Load auto-print settings from local storage
  useEffect(() => {
    const savedSettings = localStorage.getItem('cashier_auto_print_settings');
    if (savedSettings) {
      try {
        const parsed = JSON.parse(savedSettings);
        setAutoPrintSettings(parsed);
      } catch (e) {
        console.error('Failed to parse auto-print settings:', e);
      }
    }
  }, []);

  // Toggle auto-print enabled/disabled
  const toggleAutoPrint = () => {
    const newSettings = {
      ...autoPrintSettings,
      enabled: !autoPrintSettings.enabled,
    };
    setAutoPrintSettings(newSettings);
    localStorage.setItem('cashier_auto_print_settings', JSON.stringify(newSettings));
    if (newSettings.enabled) {
      showSuccess(t('cashier.enable_auto_print') || 'Auto print enabled');
    } else {
      showSuccess(t('cashier.disable_auto_print') || 'Auto print disabled');
    }
  };

  // Save auto-print settings
  const saveAutoPrintSettings = (settings: AutoPrintSettings) => {
    setAutoPrintSettings(settings);
    localStorage.setItem('cashier_auto_print_settings', JSON.stringify(settings));
    showSuccess(t('cashier.settings_saved') || 'Auto print settings saved');
  };

  // Get selected order
  const selectedOrder = useMemo(() => {
    return orders.find((o) => o.id === selectedOrderId) || null;
  }, [orders, selectedOrderId]);

  // Filter and search orders
  const filteredOrders = useMemo(() => {
    return orders.filter((order) => {
      // Status filter
      if (statusFilter !== 'all' && order.status !== statusFilter) {
        return false;
      }

      // Payment status filter
      if (paymentStatusFilter !== 'all' && order.paymentStatus !== paymentStatusFilter) {
        return false;
      }

      // Order type filter
      if (orderTypeFilter !== 'all' && order.type !== orderTypeFilter) {
        return false;
      }

      // Search query - match order number or customer name/email
      if (searchQuery.trim()) {
        const query = searchQuery.toLowerCase();
        const matchesOrderNumber = order.orderNumber?.toLowerCase().includes(query);
        const matchesCustomerName = order.customerName?.toLowerCase().includes(query);
        const matchesCustomerEmail = order.customerEmail?.toLowerCase().includes(query);

        if (!matchesOrderNumber && !matchesCustomerName && !matchesCustomerEmail) {
          return false;
        }
      }

      return true;
    });
  }, [orders, searchQuery, statusFilter, paymentStatusFilter, orderTypeFilter]);

  // Clear selected order when filters change and selection is no longer valid
  useEffect(() => {
    if (selectedOrderId && !filteredOrders.find((o) => o.id === selectedOrderId)) {
      setSelectedOrderId(null);
    }
  }, [orderTypeFilter, statusFilter, paymentStatusFilter, searchQuery, filteredOrders, selectedOrderId]);

  // Notify on new orders
  useEffect(() => {
    // Skip notification on initial load
    if (isInitialLoadRef.current) {
      isInitialLoadRef.current = false;
      previousOrderCountRef.current = orders.length;
      return;
    }

    // Check if new orders were added
    if (orders.length > previousOrderCountRef.current) {
      const newOrders = orders.slice(
        0,
        orders.length - previousOrderCountRef.current
      );

      newOrders.forEach((order) => {
        notifyNewOrder(
          order.orderNumber || order.id,
          order.customerName || ''
        );

        // Auto-show quick-confirm modal for non-dine-in orders
        if (
          order.type !== OrderType.DineIn &&
          order.status === 'Pending' &&
          !dismissedOrders.has(order.id)
        ) {
          setPendingOrderForConfirm(order.id);
          setShowQuickConfirmModal(true);
        }

        // Auto-print based on settings
        if (autoPrintSettings.enabled) {
          // Check if order type matches
          const shouldPrintType = 
            (order.type === OrderType.DineIn && autoPrintSettings.orderTypes.dineIn) ||
            (order.type === OrderType.Takeaway && autoPrintSettings.orderTypes.takeaway) ||
            (order.type === OrderType.Delivery && autoPrintSettings.orderTypes.delivery);
          
          // Check if order status matches
          const orderStatus = order.status?.toLowerCase() || 'pending';
          const shouldPrintStatus = autoPrintSettings.orderStatuses[orderStatus as keyof typeof autoPrintSettings.orderStatuses];
          
          if (shouldPrintType && shouldPrintStatus) {
            // Print all selected content types
            if (autoPrintSettings.printContent.all) {
              exportKitchenItemsToPDF(order, 'All', t);
            }
            if (autoPrintSettings.printContent.frontKitchen) {
              exportKitchenItemsToPDF(order, 'FrontKitchen', t);
            }
            if (autoPrintSettings.printContent.backKitchen) {
              exportKitchenItemsToPDF(order, 'BackKitchen', t);
            }
            if (autoPrintSettings.printContent.bill) {
              exportOrderToPDF(order, t);
            }
          }
        }
      });

      // Visual flash effect for new orders (fallback when sound disabled)
      if (!audioEnabled) {
        const flashEl = document.createElement('div');
        flashEl.style.cssText = `
          position: fixed;
          top: 0;
          left: 0;
          right: 0;
          bottom: 0;
          background: rgba(255, 152, 0, 0.3);
          pointer-events: none;
          z-index: 9999;
          animation: flash 0.5s ease-out;
        `;
        document.body.appendChild(flashEl);
        setTimeout(() => flashEl.remove(), 500);
      }
    }

    previousOrderCountRef.current = orders.length;
  }, [orders, notifyNewOrder, audioEnabled, dismissedOrders]);

  // Monitor order status changes (e.g., customer approvals)
  useEffect(() => {
    // Skip on initial load
    if (isInitialLoadRef.current) return;

    orders.forEach((order) => {
      const previousStatus = previousOrderStatusesRef.current.get(order.id);
      
      // If status changed (and we had a previous status)
      if (previousStatus && previousStatus !== order.status) {
        // Notify about the status change
        notifyOrderUpdate(order.orderNumber, order.status);
        playOrderUpdateSound();
      }
      
      // Update the tracked status
      previousOrderStatusesRef.current.set(order.id, order.status);
    });
  }, [orders, notifyOrderUpdate, playOrderUpdateSound]);

  // Handle refresh
  const handleRefresh = useCallback(async () => {
    setIsRefreshing(true);
    try {
      await refreshOrders();
      showSuccess(t('cashier.orders_refreshed') || 'Orders refreshed');
    } catch {
      showError(t('cashier.refresh_failed') || 'Failed to refresh orders');
    } finally {
      setIsRefreshing(false);
    }
  }, [refreshOrders, t]);

  // Handle order selection
  const handleSelectOrder = useCallback((orderId: string) => {
    setSelectedOrderId(orderId);
  }, []);

  // Handle status change (called from OrderDetails button)
  const handleStatusChange = useCallback(async (newStatus: string) => {
    if (!selectedOrder) return;

    try {
      const updated = await updateOrderStatus(selectedOrder.id, newStatus);
      setSelectedOrderId(updated.id); // Keep selected
      showSuccess(t('cashier.status_updated') || 'Status updated successfully');
    } catch (err) {
      showError((err as Error).message || t('cashier.status_update_failed') || 'Failed to update status');
    } finally {
      setShowStatusDialog(false);
    }
  }, [selectedOrder, updateOrderStatus, t]);

  // Handle add payment (called from OrderDetails button)
  const handleAddPayment = useCallback(async (paymentData: any) => {
    if (!selectedOrder) return;

    try {
      const updated = await addPayment(selectedOrder.id, paymentData);
      setSelectedOrderId(updated.id); // Keep selected
      showSuccess(t('cashier.payment_added') || 'Payment added successfully');
    } catch (err) {
      showError((err as Error).message || t('cashier.payment_failed') || 'Failed to add payment');
    } finally {
      setShowPaymentDialog(false);
    }
  }, [selectedOrder, addPayment, t]);

  // Handle refund (called from OrderDetails button)
  const handleRefund = useCallback(async (paymentId: string, amount?: number) => {
    if (!selectedOrder) return;

    try {
      const updated = await refundPayment(selectedOrder.id, paymentId, amount);
      setSelectedOrderId(updated.id); // Keep selected
      showSuccess(t('cashier.refund_completed') || 'Refund completed successfully');
    } catch (err) {
      showError((err as Error).message || t('cashier.refund_failed') || 'Failed to process refund');
    } finally {
      setShowRefundDialog(false);
    }
  }, [selectedOrder, refundPayment, t]);

  // Handle cancel order (called from OrderDetails button)
  const handleCancelOrder = useCallback(async (reason?: string) => {
    if (!selectedOrder) return;

    try {
      await cancelOrder(selectedOrder.id, reason);
      setSelectedOrderId(null); // Deselect
      showSuccess(t('cashier.order_cancelled') || 'Order cancelled successfully');
    } catch (err) {
      showError((err as Error).message || t('cashier.cancel_failed') || 'Failed to cancel order');
    } finally {
      setShowCancelDialog(false);
    }
  }, [selectedOrder, cancelOrder, t]);

  // Handle toggle focus
  const handleToggleFocus = useCallback(async (isFocus: boolean, priority?: number, reason?: string) => {
    if (!selectedOrder) return;

    try {
      const updated = await toggleFocusOrder(selectedOrder.id, isFocus, priority, reason);
      setSelectedOrderId(updated.id); // Keep selected
      showSuccess(
        isFocus
          ? t('cashier.order_marked_focus') || 'Order marked as focus'
          : t('cashier.focus_removed') || 'Focus removed'
      );
    } catch (err) {
      showError((err as Error).message || t('cashier.focus_toggle_failed') || 'Failed to toggle focus');
    } finally {
      setShowFocusDialog(false);
    }
  }, [selectedOrder, toggleFocusOrder, t]);

  // Handle quick confirm from modal
  const handleQuickConfirm = useCallback(async (orderNumber: string, preparationMinutes: number) => {
    try {
      await quickConfirmOrder(orderNumber, preparationMinutes);
      await refreshOrders(); // Refresh to get updated order
      showSuccess(`Order ${orderNumber} confirmed with ${preparationMinutes} min preparation time`);
    } catch (err) {
      showError((err as Error).message || 'Failed to confirm order');
      throw err; // Re-throw so modal can handle
    }
  }, [refreshOrders]);

  // Handle quick cancel from modal
  const handleQuickCancel = useCallback(async (orderNumber: string) => {
    try {
      await quickCancelOrder(orderNumber);
      await refreshOrders(); // Refresh to get updated order
      showSuccess(`Order ${orderNumber} has been cancelled`);
    } catch (err) {
      showError((err as Error).message || 'Failed to cancel order');
      throw err; // Re-throw so modal can handle
    }
  }, [refreshOrders]);

  // Handle modal close (dismiss)
  const handleQuickConfirmModalClose = useCallback(() => {
    if (pendingOrderForConfirm) {
      // Mark as dismissed so it doesn't auto-popup again
      setDismissedOrders(prev => new Set(prev).add(pendingOrderForConfirm));
    }
    setShowQuickConfirmModal(false);
    setPendingOrderForConfirm(null);
  }, [pendingOrderForConfirm]);

  // Open quick-confirm modal manually
  const openQuickConfirmModal = useCallback((orderId: string) => {
    setPendingOrderForConfirm(orderId);
    setShowQuickConfirmModal(true);
  }, []);

  // Utility functions
  const showSuccess = (message: string) => {
    setSuccessMessage(message);
    setTimeout(() => setSuccessMessage(null), 3000);
  };

  const showError = (message: string) => {
    setErrorMessage(message);
    setTimeout(() => setErrorMessage(null), 5000);
  };

  return (
    <div className={styles.pageWrapper}>
      {/* Notifications */}
      <NotificationCenter
        notifications={notifications}
        onDismiss={removeNotification}
      />

      {/* Top Header */}
      <div className={styles.topHeader}>
        <div className={styles.headerLeft}>
          <h1 className={styles.headerTitle}>{t('cashier.title') || 'Cashier'}</h1>
        </div>

        <div className={styles.headerRight}>
          <div
            className={`${styles.connectionStatus} ${
              isConnected ? styles.connectionStatusConnected : styles.connectionStatusDisconnected
            }`}
          >
            <div
              className={`${styles.connectionDot} ${
                isConnected ? styles.connectionDotConnected : styles.connectionDotDisconnected
              }`}
            />
            <span>
              {isConnected ? t('cashier.connected') || 'Connected' : t('cashier.disconnected') || 'Disconnected'}
            </span>
          </div>

          <button
            className={styles.refreshButton}
            onClick={toggleAudio}
            title={audioEnabled 
              ? (t('cashier.disable_sound') || 'Disable notification sounds')
              : (t('cashier.enable_sound') || 'Enable notification sounds')
            }
            style={{ 
              marginRight: '10px',
              backgroundColor: audioEnabled ? '#4caf50' : '#ff9800',
              color: 'white'
            }}
          >
            {audioEnabled ? '🔕' : '🔔'} {audioEnabled 
              ? (t('cashier.disable_sound') || 'Disable Sound')
              : (t('cashier.enable_sound') || 'Enable Sound')
            }
          </button>

          {/* <button
            className={styles.refreshButton}
            onClick={toggleAutoPrint}
            title={autoPrintSettings.enabled 
              ? (t('cashier.disable_auto_print') || 'Disable Auto Print')
              : (t('cashier.enable_auto_print') || 'Enable Auto Print')
            }
            style={{ 
              marginRight: '10px',
              backgroundColor: autoPrintSettings.enabled ? '#2196f3' : '#607d8b',
              color: 'white'
            }}
          >
            <Printer size={16} />
            {t('cashier.auto_print') || 'Auto Print'}
          </button>

          {autoPrintSettings.enabled && (
            <button
              className={styles.refreshButton}
              onClick={() => setShowAutoPrintSettings(true)}
              title={t('cashier.auto_print_settings') || 'Auto Print Settings'}
              style={{ 
                marginRight: '10px',
                backgroundColor: '#4caf50',
                color: 'white'
              }}
            >
              <Settings size={16} />
            </button>
          )} */}

          <button
            className={styles.refreshButton}
            onClick={() => setShowQRScannerDialog(true)}
            title={t('cashier.scan_qr_code') || 'Scan QR Code'}
            style={{ marginRight: '10px' }}
          >
            <QrCode size={16} />
            {t('cashier.scan_qr') || 'Scan QR'}
          </button>

          <button
            className={`${styles.refreshButton} ${isRefreshing ? 'loading' : ''}`}
            onClick={handleRefresh}
            disabled={isRefreshing}
            title={t('cashier.refresh') || 'Refresh orders'}
          >
            <RefreshCw size={16} />
            {isRefreshing ? t('cashier.refreshing') || 'Refreshing...' : t('cashier.refresh') || 'Refresh'}
          </button>
        </div>
      </div>

      {/* Messages */}
      {successMessage && <div className="alert alert-success">{successMessage}</div>}
      {errorMessage && <div className="alert alert-error">{errorMessage}</div>}
      {error && <div className="alert alert-error">{error}</div>}

      {/* Order Type Navigation Tabs */}
      <div className={styles.orderTypeNav}>
        <button
          className={`${styles.navTab} ${orderTypeFilter === 'all' ? styles.navTabActive : ''}`}
          onClick={() => setOrderTypeFilter('all')}
        >
          All Orders
        </button>
        <button
          className={`${styles.navTab} ${orderTypeFilter === OrderType.DineIn ? styles.navTabActive : ''}`}
          onClick={() => setOrderTypeFilter(OrderType.DineIn)}
        >
          Dine In
        </button>
        <button
          className={`${styles.navTab} ${orderTypeFilter === OrderType.Takeaway ? styles.navTabActive : ''}`}
          onClick={() => setOrderTypeFilter(OrderType.Takeaway)}
        >
          Takeaway
        </button>
        <button
          className={`${styles.navTab} ${orderTypeFilter === OrderType.Delivery ? styles.navTabActive : ''}`}
          onClick={() => setOrderTypeFilter(OrderType.Delivery)}
        >
          Delivery
        </button>
      </div>

      <div className={styles.container}>
        {/* Main Content */}
        <div className={styles.content}>
        {/* Sidebar - Order List */}
        <div className={styles.sidebar}>
          <div className={styles.sidebarHeader}>
            <h2 className={styles.sidebarTitle}>{t('cashier.orders') || 'Orders'}</h2>

            {/* Search and Filters */}
            <div className={styles.filterBar}>
              <input
                type="text"
                className={styles.searchInput}
                placeholder={t('cashier.search_placeholder') || 'Search by order # or customer...'}
                value={searchQuery}
                onChange={(e) => setSearchQuery(e.target.value)}
              />

              <div className={styles.filterSelects}>
                <select
                  className={styles.filterSelect}
                  value={statusFilter}
                  onChange={(e) => setStatusFilter(e.target.value)}
                  title={t('cashier.filter_status') || 'Filter by status'}
                >
                  <option value="all">All Statuses</option>
                  <option value="Pending">Pending</option>
                  <option value="Confirmed">Confirmed</option>
                  <option value="Preparing">Preparing</option>
                  <option value="Ready">Ready</option>
                  <option value="Completed">Completed</option>
                  <option value="Cancelled">Cancelled</option>
                </select>

                <select
                  className={styles.filterSelect}
                  value={paymentStatusFilter}
                  onChange={(e) => setPaymentStatusFilter(e.target.value)}
                  title={t('cashier.filter_payment_status') || 'Filter by payment status'}
                >
                  <option value="all">All Payment Statuses</option>
                  <option value="Pending">Pending</option>
                  <option value="Paid">Paid</option>
                  <option value="PartiallyPaid">Partially Paid</option>
                  <option value="Refunded">Refunded</option>
                </select>

                <select
                  className={styles.filterSelect}
                  value={orderTypeFilter}
                  onChange={(e) => setOrderTypeFilter(e.target.value)}
                  title={t('cashier.filter_type') || 'Filter by order type'}
                >
                  <option value="all">All Types</option>
                  <option value={OrderType.DineIn}>Dine In</option>
                  <option value={OrderType.Takeaway}>Takeaway</option>
                  <option value={OrderType.Delivery}>Delivery</option>
                </select>
              </div>
            </div>
          </div>

          {/* Order List */}
          {isLoading && !orders.length ? (
            <div className={styles.orderListEmpty}>
              <span>{t('cashier.loading') || 'Loading orders...'}</span>
            </div>
          ) : filteredOrders.length === 0 ? (
            <div className={styles.orderListEmpty}>
              <span>{t('cashier.no_orders') || 'No orders found'}</span>
            </div>
          ) : (
            <div className={styles.orderList}>
              <OrderList
                orders={filteredOrders}
                selectedOrderId={selectedOrderId}
                onSelectOrder={handleSelectOrder}
                isLoading={isLoading}
                error={error}
              />
            </div>
          )}
        </div>

        {/* Main Area - Order Details */}
        <div className={styles.main}>
          <div className={styles.detailsContainer}>
            {/* Order Details */}
            {selectedOrder ? (
              <OrderDetails
                order={selectedOrder}
                onStatusChange={handleStatusChange}
                onAddPayment={() => setShowPaymentDialog(true)}
                onRefund={() => setShowRefundDialog(true)}
                onCancel={() => setShowCancelDialog(true)}
                onToggleFocus={() => setShowFocusDialog(true)}
                onQuickConfirm={openQuickConfirmModal}
              />
            ) : (
              <div className={styles.noOrderSelected}>
                <span>{t('cashier.select_order') || 'Select an order to view details'}</span>
              </div>
            )}
          </div>
        </div>
      </div>

      {/* Dialogs */}
      <StatusUpdateDialog
        order={selectedOrder}
        isOpen={showStatusDialog}
        onClose={() => setShowStatusDialog(false)}
        onConfirm={handleStatusChange}
      />

      <PaymentDialog
        order={selectedOrder}
        isOpen={showPaymentDialog}
        onClose={() => setShowPaymentDialog(false)}
        onConfirm={handleAddPayment}
      />

      <RefundDialog
        order={selectedOrder}
        isOpen={showRefundDialog}
        onClose={() => setShowRefundDialog(false)}
        onConfirm={handleRefund}
      />

      <CancelOrderDialog
        order={selectedOrder}
        isOpen={showCancelDialog}
        onClose={() => setShowCancelDialog(false)}
        onConfirm={handleCancelOrder}
      />

      <FocusOrderDialog
        order={selectedOrder}
        isOpen={showFocusDialog}
        onClose={() => setShowFocusDialog(false)}
        onConfirm={handleToggleFocus}
      />

      <QRScannerDialog
        isOpen={showQRScannerDialog}
        onClose={() => setShowQRScannerDialog(false)}
        onApplyDiscount={(result: QRCodeValidationResult) => {
          // In a real implementation, this would apply the discount to the selected order
          // For now, we just show a success message
          showSuccess(t('cashier.discount_info_loaded') || 'Discount information loaded');
        }}
      />

      <QuickConfirmModal
        order={pendingOrderForConfirm ? orders.find(o => o.id === pendingOrderForConfirm) || null : null}
        isOpen={showQuickConfirmModal}
        onClose={handleQuickConfirmModalClose}
        onConfirm={handleQuickConfirm}
        onCancel={handleQuickCancel}
      />

      <AutoPrintSettingsModal
        isOpen={showAutoPrintSettings}
        onClose={() => setShowAutoPrintSettings(false)}
        settings={autoPrintSettings}
        onSave={saveAutoPrintSettings}
      />
      </div>
    </div>
  );
}
