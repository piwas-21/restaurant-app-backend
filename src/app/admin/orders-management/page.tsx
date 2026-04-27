'use client';

import React, { useState, useEffect, useRef } from 'react';
import { useRouter } from 'next/navigation';
import { useTranslation } from 'react-i18next';
import { useAuth } from '@/components/AuthContext';
import { getOrders, updateOrderStatus, toggleFocusOrder, deleteOrder } from '@/services/orderService';
import { OrderDto, OrderStatus, UpdateOrderStatusCommand, ToggleFocusOrderCommand } from '@/types/order';
import { useSnackbar } from 'notistack';
import OrderDetailsModal from '@/components/admin/OrderDetailsModal';
import DeleteConfirmationModal from '@/components/admin/DeleteConfirmationModal';
import DateRangeFilter from '@/components/admin/DateRangeFilter';
import OrderAnalytics from '@/components/admin/OrderAnalytics';
import AdvancedOrderAnalytics from '@/components/admin/AdvancedOrderAnalytics';
import KeyboardShortcutsModal from '@/components/admin/KeyboardShortcutsModal';
import { OrdersTable } from '@/components/admin/orders/OrdersTable';
import { OrdersFilters } from '@/components/admin/orders/OrdersFilters';
import { BulkActionsBar } from '@/components/admin/orders/BulkActionsBar';
import { OrdersPagination } from '@/components/admin/orders/OrdersPagination';
import { StatusUpdateModal } from '@/components/admin/orders/StatusUpdateModal';
import { FocusOrderModal } from '@/components/admin/orders/FocusOrderModal';
import { BulkStatusUpdateModal } from '@/components/admin/orders/BulkStatusUpdateModal';
import { exportOrdersToCSV } from '@/utils/exportUtils';
import { exportOrdersToPDF } from '@/utils/pdfExportUtils';
import { useOrderFilterPreferences } from '@/hooks/useOrderFilterPreferences';
import { useKeyboardShortcuts, KeyboardShortcut } from '@/hooks/useKeyboardShortcuts';
import { ClipboardList, RefreshCw, Loader2, AlertCircle, RotateCcw, Keyboard } from 'lucide-react';
import styles from '../../styles/AdminOrdersPage.module.css';

export default function AdminOrdersPage() {
  const { t } = useTranslation();
  const router = useRouter();
  const { user, isLoading: authLoading } = useAuth();
  const { enqueueSnackbar } = useSnackbar();

  // Filter preferences with localStorage persistence
  const { preferences, isLoaded, savePreferences, clearPreferences } = useOrderFilterPreferences();

  const [orders, setOrders] = useState<OrderDto[]>([]);
  const [isLoading, setIsLoading] = useState(true);
  const [error, setError] = useState('');

  // Filters
  const [searchQuery, setSearchQuery] = useState('');
  const [selectedStatus, setSelectedStatus] = useState<OrderStatus | 'All'>(preferences.selectedStatus);
  const [selectedPaymentStatus, setSelectedPaymentStatus] = useState<string>(preferences.selectedPaymentStatus);
  const [selectedOrderType, setSelectedOrderType] = useState<string>(preferences.selectedOrderType);
  const [showFocusOnly, setShowFocusOnly] = useState(preferences.showFocusOnly);
  const [dateRangeStart, setDateRangeStart] = useState<string | null>(null);
  const [dateRangeEnd, setDateRangeEnd] = useState<string | null>(null);
  const [sortBy, setSortBy] = useState<'date' | 'amount'>(preferences.sortBy);
  const [sortOrder, setSortOrder] = useState<'asc' | 'desc'>(preferences.sortOrder);

  // Bulk selection
  const [selectedOrderIds, setSelectedOrderIds] = useState<Set<string>>(new Set());

  // Pagination
  const [currentPage, setCurrentPage] = useState(1);
  const [pageSize] = useState(20);

  // Modals
  const [selectedOrder, setSelectedOrder] = useState<OrderDto | null>(null);
  const [showDetailsModal, setShowDetailsModal] = useState(false);
  const [showStatusModal, setShowStatusModal] = useState(false);
  const [showFocusModal, setShowFocusModal] = useState(false);
  const [showBulkStatusModal, setShowBulkStatusModal] = useState(false);
  const [showKeyboardShortcutsModal, setShowKeyboardShortcutsModal] = useState(false);
  const [showDeleteModal, setShowDeleteModal] = useState(false);

  // Bulk status update
  const [isUpdatingBulkStatus, setIsUpdatingBulkStatus] = useState(false);
  const [bulkUpdateProgress, setBulkUpdateProgress] = useState({ current: 0, total: 0 });

  const searchInputRef = useRef<HTMLInputElement>(null);

  // Define keyboard shortcuts
  const keyboardShortcuts: KeyboardShortcut[] = [
    {
      key: 'r',
      description: t('refresh_orders_list', 'Refresh orders list'),
      translationKey: 'refresh_orders_list',
      action: () => fetchOrders(),
    },
    {
      key: 'n',
      description: t('focus_search_input', 'Focus search input'),
      translationKey: 'focus_search_input',
      action: () => searchInputRef.current?.focus(),
    },
    {
      key: 'Escape',
      description: t('close_open_modals', 'Close open modals'),
      translationKey: 'close_open_modals',
      action: () => {
        setShowStatusModal(false);
        setShowFocusModal(false);
        setShowBulkStatusModal(false);
        setShowKeyboardShortcutsModal(false);
        setShowDeleteModal(false);
      },
    },
    {
      key: '?',
      shift: true,
      description: t('show_keyboard_shortcuts', 'Show keyboard shortcuts'),
      translationKey: 'show_keyboard_shortcuts',
      action: () => setShowKeyboardShortcutsModal(true),
    },
  ];

  // Enable keyboard shortcuts
  useKeyboardShortcuts({
    shortcuts: keyboardShortcuts,
    enabled: !showDetailsModal && !showStatusModal && !showFocusModal && !showBulkStatusModal,
  });

  // Initialize filters from saved preferences
  useEffect(() => {
    if (isLoaded) {
      setSelectedStatus(preferences.selectedStatus);
      setSelectedPaymentStatus(preferences.selectedPaymentStatus);
      setSelectedOrderType(preferences.selectedOrderType);
      setShowFocusOnly(preferences.showFocusOnly);
      setSortBy(preferences.sortBy);
      setSortOrder(preferences.sortOrder);
    }
    // eslint-disable-next-line react-hooks/exhaustive-deps
  }, [isLoaded]);

  // Save filter preferences when they change
  useEffect(() => {
    if (isLoaded) {
      savePreferences({
        selectedStatus,
        selectedPaymentStatus,
        selectedOrderType,
        showFocusOnly,
        sortBy,
        sortOrder,
      });
    }
    // eslint-disable-next-line react-hooks/exhaustive-deps
  }, [selectedStatus, selectedPaymentStatus, selectedOrderType, showFocusOnly, sortBy, sortOrder]);

  useEffect(() => {
    // Wait for auth to finish loading before checking user
    if (authLoading) {
      return;
    }

    if (!user) {
      router.push('/');
      return;
    }

    if (user.role !== 'Admin' && user.role !== 'Staff') {
      router.push('/');
      enqueueSnackbar(t('access_denied', 'Access denied. Admin privileges required.'), {
        variant: 'error',
        anchorOrigin: { vertical: 'bottom', horizontal: 'right' },
      });
      return;
    }

    fetchOrders();
    // eslint-disable-next-line react-hooks/exhaustive-deps
  }, [
    user,
    authLoading,
    selectedStatus,
    selectedPaymentStatus,
    selectedOrderType,
    showFocusOnly,
    dateRangeStart,
    dateRangeEnd,
  ]);

  const fetchOrders = async () => {
    try {
      setIsLoading(true);
      setError('');

      const filters: any = {};
      if (selectedStatus !== 'All') filters.status = selectedStatus;
      if (selectedPaymentStatus !== 'All') filters.paymentStatus = selectedPaymentStatus;
      if (selectedOrderType !== 'All') filters.orderType = selectedOrderType;
      if (dateRangeStart) filters.startDate = dateRangeStart;
      if (dateRangeEnd) filters.endDate = dateRangeEnd;

      const result = await getOrders(filters);
      let filteredOrders = result.items;

      if (showFocusOnly) {
        filteredOrders = filteredOrders.filter((order) => order.isFocusOrder);
      }

      if (searchQuery.trim()) {
        const query = searchQuery.toLowerCase();
        filteredOrders = filteredOrders.filter(
          (order) =>
            order.orderNumber.toLowerCase().includes(query) ||
            order.customerName?.toLowerCase().includes(query) ||
            order.customerEmail?.toLowerCase().includes(query) ||
            order.customerPhone?.toLowerCase().includes(query),
        );
      }

      filteredOrders.sort((a, b) => {
        let comparison = 0;
        if (sortBy === 'date') {
          comparison = new Date(b.orderDate).getTime() - new Date(a.orderDate).getTime();
        } else if (sortBy === 'amount') {
          comparison = b.total - a.total;
        }
        return sortOrder === 'desc' ? comparison : -comparison;
      });

      setOrders(filteredOrders);
    } catch (err) {
      // eslint-disable-next-line no-console
      console.error('Error fetching orders:', err);
      setError(t('failed_to_load_orders', 'Failed to load orders'));
      enqueueSnackbar(t('failed_to_load_orders', 'Failed to load orders'), {
        variant: 'error',
        anchorOrigin: { vertical: 'bottom', horizontal: 'right' },
      });
    } finally {
      setIsLoading(false);
    }
  };

  const handleUpdateStatus = async (status: OrderStatus, notes: string) => {
    if (!selectedOrder) return;

    const command: UpdateOrderStatusCommand = {
      newStatus: status,
      notes: notes || undefined,
    };

    await updateOrderStatus(selectedOrder.id, command);

    enqueueSnackbar(t('order_status_updated', 'Order status updated successfully'), {
      variant: 'success',
      anchorOrigin: { vertical: 'bottom', horizontal: 'right' },
    });

    setShowStatusModal(false);
    setSelectedOrder(null);
    fetchOrders();
  };

  const handleToggleFocus = async (isFocusOrder: boolean, priority?: number, reason?: string) => {
    if (!selectedOrder) return;

    const command: ToggleFocusOrderCommand = {
      isFocusOrder,
      priority,
      focusReason: reason,
    };

    await toggleFocusOrder(selectedOrder.id, command);

    enqueueSnackbar(
      isFocusOrder ? t('focus_added', 'Order marked as focus') : t('focus_removed', 'Order removed from focus'),
      {
        variant: 'success',
        anchorOrigin: { vertical: 'bottom', horizontal: 'right' },
      },
    );

    setShowFocusModal(false);
    setSelectedOrder(null);
    fetchOrders();
  };

  const handleDeleteOrder = async () => {
    if (!selectedOrder) return;

    try {
      await deleteOrder(selectedOrder.id);

      enqueueSnackbar(t('order_deleted_success', 'Order deleted successfully'), {
        variant: 'success',
        anchorOrigin: { vertical: 'bottom', horizontal: 'right' },
      });

      setShowDeleteModal(false);
      setSelectedOrder(null);
      fetchOrders();
    } catch {
      enqueueSnackbar(t('order_delete_failed', 'Failed to delete order'), {
        variant: 'error',
        anchorOrigin: { vertical: 'bottom', horizontal: 'right' },
      });
    }
  };

  const handleDateRangeChange = (startDate: string | null, endDate: string | null) => {
    setDateRangeStart(startDate);
    setDateRangeEnd(endDate);
    setCurrentPage(1);
  };

  const handleClearFilters = () => {
    setSearchQuery('');
    setDateRangeStart(null);
    setDateRangeEnd(null);
    setCurrentPage(1);
    clearPreferences();
    enqueueSnackbar(t('filters_cleared', 'All filters cleared'), {
      variant: 'info',
      anchorOrigin: { vertical: 'bottom', horizontal: 'right' },
    });
  };

  const toggleOrderSelection = (orderId: string) => {
    const newSelection = new Set(selectedOrderIds);
    if (newSelection.has(orderId)) {
      newSelection.delete(orderId);
    } else {
      newSelection.add(orderId);
    }
    setSelectedOrderIds(newSelection);
  };

  const toggleSelectAll = () => {
    if (selectedOrderIds.size === paginatedOrders.length) {
      setSelectedOrderIds(new Set());
    } else {
      setSelectedOrderIds(new Set(paginatedOrders.map((o) => o.id)));
    }
  };

  const handleBulkExportCSV = () => {
    const selectedOrders = orders.filter((o) => selectedOrderIds.has(o.id));
    exportOrdersToCSV(selectedOrders, t);
    enqueueSnackbar(`Exported ${selectedOrders.length} orders to CSV`, {
      variant: 'success',
      anchorOrigin: { vertical: 'bottom', horizontal: 'right' },
    });
  };

  const handleBulkExportPDF = () => {
    const selectedOrders = orders.filter((o) => selectedOrderIds.has(o.id));
    exportOrdersToPDF(selectedOrders, t);
    enqueueSnackbar(`Exported ${selectedOrders.length} orders to PDF`, {
      variant: 'success',
      anchorOrigin: { vertical: 'bottom', horizontal: 'right' },
    });
  };

  const handleClearSelection = () => {
    setSelectedOrderIds(new Set());
  };

  const handleBulkStatusUpdate = async (status: OrderStatus, notes: string) => {
    setIsUpdatingBulkStatus(true);
    const selectedOrders = orders.filter((o) => selectedOrderIds.has(o.id));
    const total = selectedOrders.length;
    let successCount = 0;
    let failCount = 0;

    setBulkUpdateProgress({ current: 0, total });

    for (let i = 0; i < selectedOrders.length; i++) {
      const order = selectedOrders[i];
      try {
        const command: UpdateOrderStatusCommand = { newStatus: status, notes };
        await updateOrderStatus(order.id, command);
        successCount++;
      } catch {
        failCount++;
      }
      setBulkUpdateProgress({ current: i + 1, total });
    }

    setIsUpdatingBulkStatus(false);
    setShowBulkStatusModal(false);
    await fetchOrders();
    setSelectedOrderIds(new Set());

    if (failCount === 0) {
      enqueueSnackbar(`Successfully updated ${successCount} order${successCount > 1 ? 's' : ''}`, {
        variant: 'success',
        anchorOrigin: { vertical: 'bottom', horizontal: 'right' },
      });
    } else {
      enqueueSnackbar(`Updated ${successCount} orders, ${failCount} failed`, {
        variant: 'warning',
        anchorOrigin: { vertical: 'bottom', horizontal: 'right' },
      });
    }
  };

  const handleSortChange = (newSortBy: 'date' | 'amount', newSortOrder: 'asc' | 'desc') => {
    setSortBy(newSortBy);
    setSortOrder(newSortOrder);
  };

  // Pagination
  const totalPages = Math.ceil(orders.length / pageSize);
  const paginatedOrders = orders.slice((currentPage - 1) * pageSize, currentPage * pageSize);

  const hasActiveFilters =
    selectedStatus !== 'All' ||
    selectedPaymentStatus !== 'All' ||
    selectedOrderType !== 'All' ||
    showFocusOnly ||
    searchQuery.trim() ||
    dateRangeStart ||
    sortBy !== 'date' ||
    sortOrder !== 'desc';

  if (isLoading) {
    return (
      <main className={styles.container}>
        <div className={styles.loadingState}>
          <Loader2 size={64} className={styles.spinner} />
          <p suppressHydrationWarning>{t('loading_orders', 'Loading orders...')}</p>
        </div>
      </main>
    );
  }

  return (
    <main className={styles.container}>
      <div className={styles.content}>
        {/* Header */}
        <div className={styles.header}>
          <div className={styles.titleSection}>
            <h1 className={styles.title}>
              <ClipboardList size={32} />
              {t('admin_orders_management', 'Orders Management')}
            </h1>
            <p className={styles.subtitle}>{t('admin_orders_desc', 'View and manage all restaurant orders')}</p>
          </div>
          <div className={styles.headerButtons}>
            <button
              onClick={() => setShowKeyboardShortcutsModal(true)}
              className={styles.keyboardButton}
              title={t('keyboard_shortcuts', 'Keyboard Shortcuts (Press ?)')}
            >
              <Keyboard size={20} />
            </button>
            <button onClick={fetchOrders} className={styles.refreshButton} title={t('refresh', 'Refresh')}>
              <RefreshCw size={20} />
              {t('refresh', 'Refresh')}
            </button>
          </div>
        </div>

        {/* Filters */}
        <OrdersFilters
          searchQuery={searchQuery}
          onSearchChange={setSearchQuery}
          selectedStatus={selectedStatus}
          onStatusChange={setSelectedStatus}
          selectedPaymentStatus={selectedPaymentStatus}
          onPaymentStatusChange={setSelectedPaymentStatus}
          selectedOrderType={selectedOrderType}
          onOrderTypeChange={setSelectedOrderType}
          showFocusOnly={showFocusOnly}
          onShowFocusOnlyChange={setShowFocusOnly}
          totalOrders={orders.length}
          displayedOrders={paginatedOrders.length}
          searchInputRef={searchInputRef}
        />

        {/* Date Range Filter */}
        <DateRangeFilter onDateRangeChange={handleDateRangeChange} />

        {/* Clear Filters Button */}
        {hasActiveFilters && (
          <div className={styles.clearFiltersSection}>
            <button onClick={handleClearFilters} className={styles.clearFiltersButton}>
              <RotateCcw size={16} />
              {t('clear_all_filters', 'Clear All Filters')}
            </button>
          </div>
        )}

        {/* Analytics Cards */}
        <OrderAnalytics orders={orders} />

        {/* Advanced Analytics Charts */}
        <AdvancedOrderAnalytics orders={orders} />

        {/* Sort Controls */}
        <div className={styles.sortControls}>
          <label className={styles.sortLabel}>{t('sort_by', 'Sort by')}:</label>
          <select
            value={`${sortBy}-${sortOrder}`}
            onChange={(e) => {
              const [newSortBy, newSortOrder] = e.target.value.split('-') as ['date' | 'amount', 'asc' | 'desc'];
              handleSortChange(newSortBy, newSortOrder);
            }}
            className={styles.sortDropdown}
          >
            <option value="date-desc">{t('sort_newest_first', 'Newest First')}</option>
            <option value="date-asc">{t('sort_oldest_first', 'Oldest First')}</option>
            <option value="amount-desc">{t('sort_highest_amount', 'Highest Amount')}</option>
            <option value="amount-asc">{t('sort_lowest_amount', 'Lowest Amount')}</option>
          </select>
        </div>

        {/* Bulk Actions */}
        {selectedOrderIds.size > 0 && (
          <BulkActionsBar
            selectedCount={selectedOrderIds.size}
            onExportCSV={handleBulkExportCSV}
            onExportPDF={handleBulkExportPDF}
            onUpdateStatus={() => setShowBulkStatusModal(true)}
            onClearSelection={handleClearSelection}
          />
        )}

        {/* Error State */}
        {error && (
          <div className={styles.errorAlert}>
            <AlertCircle size={20} />
            <p>{error}</p>
          </div>
        )}

        {/* Orders Table */}
        {orders.length === 0 ? (
          <div className={styles.emptyState}>
            <ClipboardList size={64} className={styles.emptyIcon} />
            <h2>{t('no_orders_found', 'No Orders Found')}</h2>
            <p>{t('no_orders_match_filters', 'No orders match your current filters')}</p>
          </div>
        ) : (
          <>
            <OrdersTable
              orders={paginatedOrders}
              selectedOrderIds={selectedOrderIds}
              onToggleSelection={toggleOrderSelection}
              onToggleSelectAll={toggleSelectAll}
              onViewDetails={(order) => {
                setSelectedOrder(order);
                setShowDetailsModal(true);
              }}
              onUpdateStatus={(order) => {
                setSelectedOrder(order);
                setShowStatusModal(true);
              }}
              onToggleFocus={(order) => {
                setSelectedOrder(order);
                setShowFocusModal(true);
              }}
              onDeleteOrder={(order) => {
                setSelectedOrder(order);
                setShowDeleteModal(true);
              }}
            />

            <OrdersPagination currentPage={currentPage} totalPages={totalPages} onPageChange={setCurrentPage} />
          </>
        )}

        {/* Modals */}
        {showStatusModal && selectedOrder && (
          <StatusUpdateModal
            order={selectedOrder}
            onClose={() => {
              setShowStatusModal(false);
              setSelectedOrder(null);
            }}
            onConfirm={handleUpdateStatus}
          />
        )}

        {showFocusModal && selectedOrder && (
          <FocusOrderModal
            order={selectedOrder}
            onClose={() => {
              setShowFocusModal(false);
              setSelectedOrder(null);
            }}
            onConfirm={handleToggleFocus}
          />
        )}

        {showDetailsModal && selectedOrder && (
          <OrderDetailsModal
            order={selectedOrder}
            onClose={() => {
              setShowDetailsModal(false);
              setSelectedOrder(null);
            }}
            onOrderUpdated={(updatedOrder) => {
              setOrders((prevOrders) => prevOrders.map((o) => (o.id === updatedOrder.id ? updatedOrder : o)));
              setShowDetailsModal(false);
              setSelectedOrder(null);
              enqueueSnackbar('Order updated successfully', {
                variant: 'success',
                anchorOrigin: { vertical: 'bottom', horizontal: 'right' },
              });
            }}
          />
        )}

        {showBulkStatusModal && (
          <BulkStatusUpdateModal
            selectedCount={selectedOrderIds.size}
            onClose={() => setShowBulkStatusModal(false)}
            onConfirm={handleBulkStatusUpdate}
            progress={bulkUpdateProgress}
            isUpdating={isUpdatingBulkStatus}
          />
        )}

        {showKeyboardShortcutsModal && (
          <KeyboardShortcutsModal shortcuts={keyboardShortcuts} onClose={() => setShowKeyboardShortcutsModal(false)} />
        )}

        {showDeleteModal && selectedOrder && (
          <DeleteConfirmationModal
            order={selectedOrder}
            onClose={() => {
              setShowDeleteModal(false);
              setSelectedOrder(null);
            }}
            onConfirm={handleDeleteOrder}
          />
        )}
      </div>
    </main>
  );
}
