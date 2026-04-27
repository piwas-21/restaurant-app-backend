'use client';

import { useState, useMemo, useCallback, useEffect, useRef } from 'react';
import { useTranslation } from 'react-i18next';
import { useCashierOrders } from '@/hooks/useCashierOrders';
import { startOfTodayLocal, endOfTodayLocal } from '@/utils/dateRange';
import { useNotification } from '@/hooks/useNotification';
import CashierHeader from '@/components/cashier/CashierHeader';
import OrderTypeNav from '@/components/cashier/OrderTypeNav';
import CashierMainContent from '@/components/cashier/CashierMainContent';
import StatusUpdateDialog from '@/components/cashier/StatusUpdateDialog';
import PaymentDialog from '@/components/cashier/PaymentDialog';
import RefundDialog from '@/components/cashier/RefundDialog';
import CancelOrderDialog from '@/components/cashier/CancelOrderDialog';
import FocusOrderDialog from '@/components/cashier/FocusOrderDialog';
import QuickConfirmModal from '@/components/cashier/QuickConfirmModal';
import NotificationCenter from '@/components/cashier/NotificationCenter';
import QRScannerDialog from '@/components/cashier/QRScannerDialog';
import AutoPrintSettingsModal from '@/components/cashier/AutoPrintSettingsModal';
import CashierDiagnostics from '@/components/cashier/CashierDiagnostics';
import ZReportModal from '@/components/cashier/ZReportModal';
import { OrderType } from '@/types/order';
import { QRCodeValidationResult } from '@/types/userGroupTypes';
import { quickConfirmOrder, quickCancelOrder } from '@/services/cashierService';
import { exportKitchenItemsToPDF, exportOrderToPDF } from '@/utils/pdfExportUtils';
import { AutoPrintSettings, DEFAULT_AUTO_PRINT_SETTINGS } from '@/types/cashier';
import styles from '@/app/styles/CashierPage.module.css';

const TODAY_ONLY_STORAGE_KEY = 'cashier:todayOnly';
const POPUP_GRACE_WINDOW_MS = 5000;

export default function CashierPage() {
  const { t } = useTranslation();

  // Default the cashier view to today's orders only. The cashier can flip
  // to "All time" via the toolbar; choice persists across reloads.
  // SSR-safe: localStorage is read in an effect, not at render time.
  const [todayOnly, setTodayOnly] = useState<boolean>(true);
  useEffect(() => {
    const stored = window.localStorage.getItem(TODAY_ONLY_STORAGE_KEY);
    if (stored !== null) setTodayOnly(stored === 'true');
  }, []);
  useEffect(() => {
    window.localStorage.setItem(TODAY_ONLY_STORAGE_KEY, String(todayOnly));
  }, [todayOnly]);

  // Recompute the "today" window each time the toggle flips so polling
  // continues to use the same instants (avoids midnight drift mid-shift —
  // the cashier crossing midnight is called out as out-of-scope in
  // BUGS-IMPROVEMENTS-PLAN §A3 and tracked under B2 admin shift cutover).
  const dateRange = useMemo(
    () => (todayOnly ? { startDate: startOfTodayLocal(), endDate: endOfTodayLocal() } : undefined),
    [todayOnly],
  );

  // Orders hook
  const {
    orders,
    isConnected,
    isLoading,
    error,
    lastEventTime,
    connectionState,
    refreshOrders,
    updateOrderStatus,
    addPayment,
    refundPayment,
    cancelOrder,
    toggleFocusOrder,
  } = useCashierOrders(dateRange);

  // Notifications hook
  const {
    notifications,
    removeNotification,
    notifyNewOrder,
    notifyOrderUpdate,
    playOrderUpdateSound,
    audioEnabled,
    audioReady,
    audioBlockedByPolicy,
    toggleAudio,
    resumeAudioContext,
    soundType,
    changeSoundType,
    playSoundByType,
    repeatUntilMouseMoves,
    toggleRepeatSound,
  } = useNotification();

  // UI State
  const [selectedOrderId, setSelectedOrderId] = useState<string | null>(null);
  const [searchQuery, setSearchQuery] = useState('');
  const [statusFilter, setStatusFilter] = useState<string>('all');
  const [paymentStatusFilter, setPaymentStatusFilter] = useState<string>('all');
  const [orderTypeFilter, setOrderTypeFilter] = useState<string>('all');
  const [isRefreshing, setIsRefreshing] = useState(false);
  // Auto-print settings (logic is active, UI buttons are commented out in CashierHeader.tsx)
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
  const [showDiagnostics, setShowDiagnostics] = useState(false);
  const [showZReport, setShowZReport] = useState(false);
  const [pendingOrderForConfirm, setPendingOrderForConfirm] = useState<string | null>(null);
  const [dismissedOrders, setDismissedOrders] = useState<Set<string>>(new Set());
  const [pendingOrderQueue, setPendingOrderQueue] = useState<string[]>([]); // Queue for multiple orders

  // Dialog feedback messages
  const [successMessage, setSuccessMessage] = useState<string | null>(null);
  const [errorMessage, setErrorMessage] = useState<string | null>(null);

  // Use ID-based tracking instead of position-based
  const seenOrderIdsRef = useRef<Set<string>>(new Set());
  const previousOrderStatusesRef = useRef<Map<string, string>>(new Map());
  // Grace-window: any order arriving within POPUP_GRACE_WINDOW_MS of mount
  // is silently marked as "seen" so the popup queue doesn't fire for
  // pre-existing orders during the initial REST → SSE handover.
  const mountedAtRef = useRef<number>(Date.now());
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
  const _toggleAutoPrint = () => {
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

  // Notify on new orders using ID-based tracking
  useEffect(() => {
    // Within the grace window after mount, silently mark every order as seen.
    // Covers: initial REST fetch, React Strict Mode double-invoke, and the
    // SSE re-emit of orders that were already in the REST result.
    if (Date.now() - mountedAtRef.current < POPUP_GRACE_WINDOW_MS) {
      orders.forEach((order) => seenOrderIdsRef.current.add(order.id));
      isInitialLoadRef.current = false;
      return;
    }
    isInitialLoadRef.current = false;

    // Find truly new orders using ID-based tracking
    const newOrders = orders.filter((order) => !seenOrderIdsRef.current.has(order.id));

    if (newOrders.length === 0) return;

    const ordersForModal: string[] = [];

    newOrders.forEach((order) => {
      // Mark as seen immediately to prevent duplicates
      seenOrderIdsRef.current.add(order.id);

      // Notify (this triggers sound)
      notifyNewOrder(order.orderNumber || order.id, order.customerName || '');

      // Check if should show modal
      const shouldShowModal =
        order.type !== OrderType.DineIn && order.status === 'Pending' && !dismissedOrders.has(order.id);

      if (shouldShowModal) {
        ordersForModal.push(order.id);
      }

      // Auto-print based on settings
      if (autoPrintSettings.enabled) {
        const shouldPrintType =
          (order.type === OrderType.DineIn && autoPrintSettings.orderTypes.dineIn) ||
          (order.type === OrderType.Takeaway && autoPrintSettings.orderTypes.takeaway) ||
          (order.type === OrderType.Delivery && autoPrintSettings.orderTypes.delivery);

        const orderStatus = order.status?.toLowerCase() || 'pending';
        const shouldPrintStatus =
          autoPrintSettings.orderStatuses[orderStatus as keyof typeof autoPrintSettings.orderStatuses];

        if (shouldPrintType && shouldPrintStatus) {
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

    // Queue orders for modal display (use setTimeout to avoid React state batching issues)
    if (ordersForModal.length > 0) {
      setTimeout(() => {
        setPendingOrderQueue((prev) => [...prev, ...ordersForModal]);
      }, 0);
    }

    // Visual flash effect for new orders (fallback when sound disabled)
    if (!audioEnabled && newOrders.length > 0) {
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
  }, [orders, notifyNewOrder, audioEnabled, dismissedOrders, autoPrintSettings, t]);

  // Process pending order queue for modal display
  useEffect(() => {
    if (pendingOrderQueue.length > 0 && !showQuickConfirmModal) {
      const nextOrderId = pendingOrderQueue[0];
      setPendingOrderForConfirm(nextOrderId);
      setShowQuickConfirmModal(true);
      setPendingOrderQueue((prev) => prev.slice(1));
    }
  }, [pendingOrderQueue, showQuickConfirmModal]);

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

  // Handle status change
  const handleStatusChange = useCallback(
    async (newStatus: string) => {
      if (!selectedOrder) return;

      try {
        const updated = await updateOrderStatus(selectedOrder.id, newStatus);
        setSelectedOrderId(updated.id);
        showSuccess(t('cashier.status_updated') || 'Status updated successfully');
      } catch (err) {
        showError((err as Error).message || t('cashier.status_update_failed') || 'Failed to update status');
      } finally {
        setShowStatusDialog(false);
      }
    },
    [selectedOrder, updateOrderStatus, t],
  );

  // Handle add payment
  const handleAddPayment = useCallback(
    async (paymentData: any) => {
      if (!selectedOrder) return;

      try {
        const updated = await addPayment(selectedOrder.id, paymentData);
        setSelectedOrderId(updated.id);
        showSuccess(t('cashier.payment_added') || 'Payment added successfully');
      } catch (err) {
        showError((err as Error).message || t('cashier.payment_failed') || 'Failed to add payment');
      } finally {
        setShowPaymentDialog(false);
      }
    },
    [selectedOrder, addPayment, t],
  );

  // Handle refund
  const handleRefund = useCallback(
    async (paymentId: string, amount?: number) => {
      if (!selectedOrder) return;

      try {
        const updated = await refundPayment(selectedOrder.id, paymentId, amount);
        setSelectedOrderId(updated.id);
        showSuccess(t('cashier.refund_completed') || 'Refund completed successfully');
      } catch (err) {
        showError((err as Error).message || t('cashier.refund_failed') || 'Failed to process refund');
      } finally {
        setShowRefundDialog(false);
      }
    },
    [selectedOrder, refundPayment, t],
  );

  // Handle cancel order
  const handleCancelOrder = useCallback(
    async (reason?: string) => {
      if (!selectedOrder) return;

      try {
        await cancelOrder(selectedOrder.id, reason);
        setSelectedOrderId(null);
        showSuccess(t('cashier.order_cancelled') || 'Order cancelled successfully');
      } catch (err) {
        showError((err as Error).message || t('cashier.cancel_failed') || 'Failed to cancel order');
      } finally {
        setShowCancelDialog(false);
      }
    },
    [selectedOrder, cancelOrder, t],
  );

  // Handle toggle focus
  const handleToggleFocus = useCallback(
    async (isFocus: boolean, priority?: number, reason?: string) => {
      if (!selectedOrder) return;

      try {
        const updated = await toggleFocusOrder(selectedOrder.id, isFocus, priority, reason);
        setSelectedOrderId(updated.id);
        showSuccess(
          isFocus
            ? t('cashier.order_marked_focus') || 'Order marked as focus'
            : t('cashier.focus_removed') || 'Focus removed',
        );
      } catch (err) {
        showError((err as Error).message || t('cashier.focus_toggle_failed') || 'Failed to toggle focus');
      } finally {
        setShowFocusDialog(false);
      }
    },
    [selectedOrder, toggleFocusOrder, t],
  );

  // Handle quick confirm from modal
  const handleQuickConfirm = useCallback(
    async (orderNumber: string, preparationMinutes: number) => {
      try {
        await quickConfirmOrder(orderNumber, preparationMinutes);
        await refreshOrders();
        showSuccess(`Order ${orderNumber} confirmed with ${preparationMinutes} min preparation time`);
      } catch (err) {
        showError((err as Error).message || 'Failed to confirm order');
        throw err;
      }
    },
    [refreshOrders],
  );

  // Handle quick cancel from modal
  const handleQuickCancel = useCallback(
    async (orderNumber: string) => {
      try {
        await quickCancelOrder(orderNumber);
        await refreshOrders();
        showSuccess(`Order ${orderNumber} has been cancelled`);
      } catch (err) {
        showError((err as Error).message || 'Failed to cancel order');
        throw err;
      }
    },
    [refreshOrders],
  );

  // Handle modal close (dismiss)
  const handleQuickConfirmModalClose = useCallback(() => {
    if (pendingOrderForConfirm) {
      setDismissedOrders((prev) => new Set(prev).add(pendingOrderForConfirm));
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
      <NotificationCenter notifications={notifications} onDismiss={removeNotification} />

      {/* Header */}
      <CashierHeader
        isConnected={isConnected}
        isRefreshing={isRefreshing}
        audioEnabled={audioEnabled}
        audioBlockedByPolicy={audioBlockedByPolicy}
        soundType={soundType}
        repeatUntilMouseMoves={repeatUntilMouseMoves}
        onRefresh={handleRefresh}
        onToggleAudio={toggleAudio}
        onSoundTypeChange={changeSoundType}
        onTestSound={playSoundByType}
        onToggleRepeat={toggleRepeatSound}
        onOpenQRScanner={() => setShowQRScannerDialog(true)}
        onOpenZReport={() => setShowZReport(true)}
        onOpenDiagnostics={() => setShowDiagnostics(!showDiagnostics)}
      />

      {/* Messages */}
      {successMessage && <div className="alert alert-success">{successMessage}</div>}
      {errorMessage && <div className="alert alert-error">{errorMessage}</div>}
      {error && <div className="alert alert-error">{error}</div>}

      {/* Order Type Navigation */}
      <OrderTypeNav activeFilter={orderTypeFilter} onFilterChange={setOrderTypeFilter} />

      {/* Date-range toggle: defaults to today; cashier can flip to all-time */}
      <div className={styles.dateRangeToolbar}>
        <label>
          <input type="checkbox" checked={todayOnly} onChange={(e) => setTodayOnly(e.target.checked)} />{' '}
          {t('cashier.show_todays_orders_only')}
        </label>
      </div>

      {/* Main Content */}
      <CashierMainContent
        filteredOrders={filteredOrders}
        selectedOrder={selectedOrder}
        selectedOrderId={selectedOrderId}
        isLoading={isLoading}
        error={error}
        searchQuery={searchQuery}
        statusFilter={statusFilter}
        paymentStatusFilter={paymentStatusFilter}
        orderTypeFilter={orderTypeFilter}
        onSelectOrder={handleSelectOrder}
        onStatusChange={handleStatusChange}
        onAddPayment={() => setShowPaymentDialog(true)}
        onRefund={() => setShowRefundDialog(true)}
        onCancel={() => setShowCancelDialog(true)}
        onToggleFocus={() => setShowFocusDialog(true)}
        onQuickConfirm={openQuickConfirmModal}
        onSearchChange={setSearchQuery}
        onStatusFilterChange={setStatusFilter}
        onPaymentStatusFilterChange={setPaymentStatusFilter}
        onOrderTypeFilterChange={setOrderTypeFilter}
      />

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
        onApplyDiscount={(_result: QRCodeValidationResult) => {
          showSuccess(t('cashier.discount_info_loaded') || 'Discount information loaded');
        }}
      />

      <QuickConfirmModal
        order={pendingOrderForConfirm ? orders.find((o) => o.id === pendingOrderForConfirm) || null : null}
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

      <ZReportModal isOpen={showZReport} onClose={() => setShowZReport(false)} />

      {/* Diagnostic Panel */}
      {showDiagnostics && (
        <div
          style={{
            position: 'fixed',
            top: '20px',
            right: '20px',
            zIndex: 9999,
            maxWidth: '400px',
            boxShadow: '0 4px 20px rgba(0, 0, 0, 0.15)',
          }}
        >
          <CashierDiagnostics
            sseConnected={isConnected}
            sseConnectionState={connectionState}
            sseLastEventTime={lastEventTime}
            sseError={error}
            audioEnabled={audioEnabled}
            audioReady={audioReady}
            audioBlockedByPolicy={audioBlockedByPolicy}
            onTestSound={() => playSoundByType(soundType)}
            onEnableAudio={resumeAudioContext}
            onRefreshConnection={handleRefresh}
            onClose={() => setShowDiagnostics(false)}
          />
        </div>
      )}
    </div>
  );
}
