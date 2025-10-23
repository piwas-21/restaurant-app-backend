"use client";

import React, { useState, useEffect } from 'react';
import { useRouter } from 'next/navigation';
import { useTranslation } from 'react-i18next';
import { useAuth } from '@/components/AuthContext';
import { getOrders, updateOrderStatus, toggleFocusOrder } from '@/services/orderService';
import { OrderDto, OrderStatus, UpdateOrderStatusCommand, ToggleFocusOrderCommand } from '@/types/order';
import { useSnackbar } from 'notistack';
import {
  ClipboardList,
  Filter,
  Search,
  RefreshCw,
  Loader2,
  AlertCircle,
  Eye,
  Star,
  X,
  CheckCircle,
  Clock,
  Package,
  Truck,
  Store,
  UtensilsCrossed,
  ChevronLeft,
  ChevronRight,
} from 'lucide-react';
import styles from '../../styles/AdminOrdersPage.module.css';

export default function AdminOrdersPage() {
  const { t } = useTranslation();
  const router = useRouter();
  const { user } = useAuth();
  const { enqueueSnackbar } = useSnackbar();

  const [orders, setOrders] = useState<OrderDto[]>([]);
  const [isLoading, setIsLoading] = useState(true);
  const [error, setError] = useState('');

  // Filters
  const [searchQuery, setSearchQuery] = useState('');
  const [selectedStatus, setSelectedStatus] = useState<OrderStatus | 'All'>('All');
  const [selectedPaymentStatus, setSelectedPaymentStatus] = useState<string>('All');
  const [selectedOrderType, setSelectedOrderType] = useState<string>('All');
  const [showFocusOnly, setShowFocusOnly] = useState(false);

  // Pagination
  const [currentPage, setCurrentPage] = useState(1);
  const [pageSize] = useState(20);

  // Modals
  const [selectedOrder, setSelectedOrder] = useState<OrderDto | null>(null);
  const [showStatusModal, setShowStatusModal] = useState(false);
  const [newStatus, setNewStatus] = useState<OrderStatus>('Pending');
  const [statusNotes, setStatusNotes] = useState('');
  const [isUpdatingStatus, setIsUpdatingStatus] = useState(false);

  const [showFocusModal, setShowFocusModal] = useState(false);
  const [focusReason, setFocusReason] = useState('');
  const [focusPriority, setFocusPriority] = useState(1);
  const [isTogglingFocus, setIsTogglingFocus] = useState(false);

  useEffect(() => {
    if (!user) {
      router.push('/login');
      return;
    }

    // Check if user is admin or staff
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
  }, [user, selectedStatus, selectedPaymentStatus, selectedOrderType, showFocusOnly]);

  const fetchOrders = async () => {
    try {
      setIsLoading(true);
      setError('');

      const filters: any = {};
      if (selectedStatus !== 'All') filters.status = selectedStatus;
      if (selectedPaymentStatus !== 'All') filters.paymentStatus = selectedPaymentStatus;
      if (selectedOrderType !== 'All') filters.orderType = selectedOrderType;

      const result = await getOrders(filters);

      let filteredOrders = result.items;

      // Filter by focus orders
      if (showFocusOnly) {
        filteredOrders = filteredOrders.filter(order => order.isFocusOrder);
      }

      // Filter by search query
      if (searchQuery.trim()) {
        const query = searchQuery.toLowerCase();
        filteredOrders = filteredOrders.filter(order =>
          order.orderNumber.toLowerCase().includes(query) ||
          order.customerName?.toLowerCase().includes(query) ||
          order.customerEmail?.toLowerCase().includes(query) ||
          order.customerPhone?.toLowerCase().includes(query)
        );
      }

      // Sort by date (newest first)
      filteredOrders.sort((a, b) =>
        new Date(b.orderDate).getTime() - new Date(a.orderDate).getTime()
      );

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

  const handleUpdateStatus = async () => {
    if (!selectedOrder) return;

    try {
      setIsUpdatingStatus(true);

      const command: UpdateOrderStatusCommand = {
        status: newStatus,
        notes: statusNotes || undefined,
      };

      await updateOrderStatus(selectedOrder.id, command);

      enqueueSnackbar(t('order_status_updated', 'Order status updated successfully'), {
        variant: 'success',
        anchorOrigin: { vertical: 'bottom', horizontal: 'right' },
      });

      setShowStatusModal(false);
      setSelectedOrder(null);
      setStatusNotes('');
      fetchOrders();
    } catch (err) {
      // eslint-disable-next-line no-console
      console.error('Error updating status:', err);
      enqueueSnackbar(t('failed_to_update_status', 'Failed to update order status'), {
        variant: 'error',
        anchorOrigin: { vertical: 'bottom', horizontal: 'right' },
      });
    } finally {
      setIsUpdatingStatus(false);
    }
  };

  const handleToggleFocus = async () => {
    if (!selectedOrder) return;

    try {
      setIsTogglingFocus(true);

      const command: ToggleFocusOrderCommand = {
        isFocusOrder: !selectedOrder.isFocusOrder,
        priority: selectedOrder.isFocusOrder ? undefined : focusPriority,
        focusReason: selectedOrder.isFocusOrder ? undefined : focusReason || undefined,
      };

      await toggleFocusOrder(selectedOrder.id, command);

      enqueueSnackbar(
        selectedOrder.isFocusOrder
          ? t('focus_removed', 'Order removed from focus')
          : t('focus_added', 'Order marked as focus'),
        {
          variant: 'success',
          anchorOrigin: { vertical: 'bottom', horizontal: 'right' },
        }
      );

      setShowFocusModal(false);
      setSelectedOrder(null);
      setFocusReason('');
      setFocusPriority(1);
      fetchOrders();
    } catch (err) {
      // eslint-disable-next-line no-console
      console.error('Error toggling focus:', err);
      enqueueSnackbar(t('failed_to_toggle_focus', 'Failed to update focus status'), {
        variant: 'error',
        anchorOrigin: { vertical: 'bottom', horizontal: 'right' },
      });
    } finally {
      setIsTogglingFocus(false);
    }
  };

  const formatPrice = (price: number) => {
    return new Intl.NumberFormat('de-CH', {
      style: 'currency',
      currency: 'CHF',
    }).format(price);
  };

  const formatDate = (dateString: string) => {
    return new Date(dateString).toLocaleString('de-CH', {
      day: '2-digit',
      month: '2-digit',
      year: 'numeric',
      hour: '2-digit',
      minute: '2-digit',
    });
  };

  const getOrderTypeIcon = (orderType: string) => {
    switch (orderType) {
      case 'DineIn':
        return <UtensilsCrossed size={16} />;
      case 'Takeaway':
        return <Store size={16} />;
      case 'Delivery':
        return <Truck size={16} />;
      default:
        return <Package size={16} />;
    }
  };

  const getOrderTypeLabel = (orderType: string) => {
    switch (orderType) {
      case 'DineIn':
        return t('order_type_dine_in', 'Dine In');
      case 'Takeaway':
        return t('order_type_takeaway', 'Takeaway');
      case 'Delivery':
        return t('order_type_delivery', 'Delivery');
      default:
        return orderType;
    }
  };

  const getStatusLabel = (status: string) => {
    switch (status) {
      case 'Pending':
        return t('order_status_pending', 'Pending');
      case 'Confirmed':
        return t('order_status_confirmed', 'Confirmed');
      case 'Preparing':
        return t('order_status_preparing', 'Preparing');
      case 'Ready':
        return t('order_status_ready', 'Ready');
      case 'InTransit':
        return t('order_status_in_transit', 'In Transit');
      case 'Delivered':
        return t('order_status_delivered', 'Delivered');
      case 'Completed':
        return t('order_status_completed', 'Completed');
      case 'Cancelled':
        return t('order_status_cancelled', 'Cancelled');
      default:
        return status;
    }
  };

  const getStatusColor = (status: string) => {
    switch (status) {
      case 'Pending':
      case 'Confirmed':
        return styles.statusPending;
      case 'Preparing':
        return styles.statusPreparing;
      case 'Ready':
      case 'InTransit':
        return styles.statusReady;
      case 'Delivered':
      case 'Completed':
        return styles.statusCompleted;
      case 'Cancelled':
        return styles.statusCancelled;
      default:
        return '';
    }
  };

  const statusOptions: OrderStatus[] = [
    'Pending',
    'Confirmed',
    'Preparing',
    'Ready',
    'InTransit',
    'Delivered',
    'Completed',
    'Cancelled',
  ];

  // Pagination
  const totalPages = Math.ceil(orders.length / pageSize);
  const paginatedOrders = orders.slice(
    (currentPage - 1) * pageSize,
    currentPage * pageSize
  );

  if (isLoading) {
    return (
      <main className={styles.container}>
        <div className={styles.loadingState}>
          <Loader2 size={64} className={styles.spinner} />
          <p>{t('loading_orders', 'Loading orders...')}</p>
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
            <p className={styles.subtitle}>
              {t('admin_orders_desc', 'View and manage all restaurant orders')}
            </p>
          </div>
          <button onClick={fetchOrders} className={styles.refreshButton} title={t('refresh', 'Refresh')}>
            <RefreshCw size={20} />
            {t('refresh', 'Refresh')}
          </button>
        </div>

        {/* Filters */}
        <div className={styles.filtersSection}>
          {/* Search */}
          <div className={styles.searchBox}>
            <Search size={18} />
            <input
              type="text"
              placeholder={t('search_orders', 'Search by order number, customer name, email, or phone...')}
              value={searchQuery}
              onChange={(e) => setSearchQuery(e.target.value)}
              className={styles.searchInput}
            />
          </div>

          {/* Filter Row */}
          <div className={styles.filterRow}>
            <div className={styles.filterGroup}>
              <Filter size={16} />
              <select
                value={selectedStatus}
                onChange={(e) => setSelectedStatus(e.target.value as OrderStatus | 'All')}
                className={styles.filterSelect}
              >
                <option value="All">{t('all_statuses', 'All Statuses')}</option>
                {statusOptions.map((status) => (
                  <option key={status} value={status}>
                    {getStatusLabel(status)}
                  </option>
                ))}
              </select>
            </div>

            <div className={styles.filterGroup}>
              <select
                value={selectedPaymentStatus}
                onChange={(e) => setSelectedPaymentStatus(e.target.value)}
                className={styles.filterSelect}
              >
                <option value="All">{t('all_payment_statuses', 'All Payment Statuses')}</option>
                <option value="Pending">{t('payment_status_pending', 'Pending')}</option>
                <option value="Paid">{t('payment_status_paid', 'Paid')}</option>
                <option value="PartiallyPaid">{t('payment_status_partially_paid', 'Partially Paid')}</option>
                <option value="Refunded">{t('payment_status_refunded', 'Refunded')}</option>
              </select>
            </div>

            <div className={styles.filterGroup}>
              <select
                value={selectedOrderType}
                onChange={(e) => setSelectedOrderType(e.target.value)}
                className={styles.filterSelect}
              >
                <option value="All">{t('all_order_types', 'All Order Types')}</option>
                <option value="DineIn">{t('order_type_dine_in', 'Dine In')}</option>
                <option value="Takeaway">{t('order_type_takeaway', 'Takeaway')}</option>
                <option value="Delivery">{t('order_type_delivery', 'Delivery')}</option>
              </select>
            </div>

            <label className={styles.checkboxLabel}>
              <input
                type="checkbox"
                checked={showFocusOnly}
                onChange={(e) => setShowFocusOnly(e.target.checked)}
                className={styles.checkbox}
              />
              <Star size={16} />
              {t('focus_orders_only', 'Focus Orders Only')}
            </label>
          </div>

          <div className={styles.resultsInfo}>
            {t('showing_orders', `Showing ${paginatedOrders.length} of ${orders.length} orders`)}
          </div>
        </div>

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
            <div className={styles.tableWrapper}>
              <table className={styles.ordersTable}>
                <thead>
                  <tr>
                    <th>{t('order_number', 'Order #')}</th>
                    <th>{t('customer', 'Customer')}</th>
                    <th>{t('type', 'Type')}</th>
                    <th>{t('status', 'Status')}</th>
                    <th>{t('payment', 'Payment')}</th>
                    <th>{t('total', 'Total')}</th>
                    <th>{t('date', 'Date')}</th>
                    <th>{t('actions', 'Actions')}</th>
                  </tr>
                </thead>
                <tbody>
                  {paginatedOrders.map((order) => (
                    <tr key={order.id} className={order.isFocusOrder ? styles.focusRow : ''}>
                      <td>
                        <div className={styles.orderNumberCell}>
                          {order.isFocusOrder && <Star size={14} className={styles.focusIcon} />}
                          <span className={styles.orderNumber}>{order.orderNumber}</span>
                        </div>
                      </td>
                      <td>
                        <div className={styles.customerCell}>
                          <span className={styles.customerName}>{order.customerName || 'N/A'}</span>
                          <span className={styles.customerEmail}>{order.customerEmail}</span>
                        </div>
                      </td>
                      <td>
                        <div className={styles.typeCell}>
                          {getOrderTypeIcon(order.type)}
                          <span>{getOrderTypeLabel(order.type)}</span>
                        </div>
                      </td>
                      <td>
                        <span className={`${styles.statusBadge} ${getStatusColor(order.status)}`}>
                          {getStatusLabel(order.status)}
                        </span>
                      </td>
                      <td>
                        <span className={styles.paymentStatus}>
                          {order.isFullyPaid ? (
                            <><CheckCircle size={14} /> {t('paid', 'Paid')}</>
                          ) : (
                            <><Clock size={14} /> {t('pending', 'Pending')}</>
                          )}
                        </span>
                      </td>
                      <td className={styles.totalCell}>{formatPrice(order.total)}</td>
                      <td className={styles.dateCell}>{formatDate(order.orderDate)}</td>
                      <td>
                        <div className={styles.actionsCell}>
                          <button
                            onClick={() => router.push(`/checkout/confirmation?orderId=${order.id}&orderNumber=${order.orderNumber}`)}
                            className={styles.actionButton}
                            title={t('view_details', 'View Details')}
                          >
                            <Eye size={16} />
                          </button>
                          <button
                            onClick={() => {
                              setSelectedOrder(order);
                              setNewStatus(order.status as OrderStatus);
                              setShowStatusModal(true);
                            }}
                            className={styles.actionButton}
                            title={t('update_status', 'Update Status')}
                          >
                            <RefreshCw size={16} />
                          </button>
                          <button
                            onClick={() => {
                              setSelectedOrder(order);
                              setShowFocusModal(true);
                            }}
                            className={`${styles.actionButton} ${order.isFocusOrder ? styles.focusActive : ''}`}
                            title={order.isFocusOrder ? t('remove_focus', 'Remove Focus') : t('mark_as_focus', 'Mark as Focus')}
                          >
                            <Star size={16} />
                          </button>
                        </div>
                      </td>
                    </tr>
                  ))}
                </tbody>
              </table>
            </div>

            {/* Pagination */}
            {totalPages > 1 && (
              <div className={styles.pagination}>
                <button
                  onClick={() => setCurrentPage(prev => Math.max(1, prev - 1))}
                  disabled={currentPage === 1}
                  className={styles.paginationButton}
                >
                  <ChevronLeft size={18} />
                  {t('previous', 'Previous')}
                </button>
                <div className={styles.pageInfo}>
                  {t('page_info', `Page ${currentPage} of ${totalPages}`)}
                </div>
                <button
                  onClick={() => setCurrentPage(prev => Math.min(totalPages, prev + 1))}
                  disabled={currentPage === totalPages}
                  className={styles.paginationButton}
                >
                  {t('next', 'Next')}
                  <ChevronRight size={18} />
                </button>
              </div>
            )}
          </>
        )}

        {/* Status Update Modal */}
        {showStatusModal && selectedOrder && (
          <div className={styles.modal} onClick={() => setShowStatusModal(false)}>
            <div className={styles.modalContent} onClick={(e) => e.stopPropagation()}>
              <div className={styles.modalHeader}>
                <h2>{t('update_order_status', 'Update Order Status')}</h2>
                <button onClick={() => setShowStatusModal(false)} className={styles.closeButton}>
                  <X size={20} />
                </button>
              </div>
              <div className={styles.modalBody}>
                <p className={styles.orderInfo}>
                  {t('order', 'Order')} #{selectedOrder.orderNumber}
                </p>
                <div className={styles.formGroup}>
                  <label htmlFor="status">{t('new_status', 'New Status')}:</label>
                  <select
                    id="status"
                    value={newStatus}
                    onChange={(e) => setNewStatus(e.target.value as OrderStatus)}
                    className={styles.formSelect}
                  >
                    {statusOptions.map((status) => (
                      <option key={status} value={status}>
                        {getStatusLabel(status)}
                      </option>
                    ))}
                  </select>
                </div>
                <div className={styles.formGroup}>
                  <label htmlFor="notes">{t('notes_optional', 'Notes (Optional)')}:</label>
                  <textarea
                    id="notes"
                    value={statusNotes}
                    onChange={(e) => setStatusNotes(e.target.value)}
                    placeholder={t('status_notes_placeholder', 'Add any notes about this status change...')}
                    className={styles.formTextarea}
                    rows={3}
                  />
                </div>
              </div>
              <div className={styles.modalFooter}>
                <button
                  onClick={() => setShowStatusModal(false)}
                  className={styles.cancelButton}
                  disabled={isUpdatingStatus}
                >
                  {t('cancel', 'Cancel')}
                </button>
                <button
                  onClick={handleUpdateStatus}
                  className={styles.confirmButton}
                  disabled={isUpdatingStatus}
                >
                  {isUpdatingStatus ? (
                    <>
                      <Loader2 size={18} className={styles.spinner} />
                      {t('updating', 'Updating...')}
                    </>
                  ) : (
                    <>
                      <CheckCircle size={18} />
                      {t('update_status', 'Update Status')}
                    </>
                  )}
                </button>
              </div>
            </div>
          </div>
        )}

        {/* Focus Order Modal */}
        {showFocusModal && selectedOrder && (
          <div className={styles.modal} onClick={() => setShowFocusModal(false)}>
            <div className={styles.modalContent} onClick={(e) => e.stopPropagation()}>
              <div className={styles.modalHeader}>
                <h2>
                  {selectedOrder.isFocusOrder
                    ? t('remove_focus_order', 'Remove Focus Order')
                    : t('mark_as_focus_order', 'Mark as Focus Order')}
                </h2>
                <button onClick={() => setShowFocusModal(false)} className={styles.closeButton}>
                  <X size={20} />
                </button>
              </div>
              <div className={styles.modalBody}>
                <p className={styles.orderInfo}>
                  {t('order', 'Order')} #{selectedOrder.orderNumber}
                </p>
                {!selectedOrder.isFocusOrder && (
                  <>
                    <div className={styles.formGroup}>
                      <label htmlFor="priority">{t('priority', 'Priority')}:</label>
                      <input
                        type="number"
                        id="priority"
                        value={focusPriority}
                        onChange={(e) => setFocusPriority(parseInt(e.target.value, 10))}
                        min="1"
                        max="10"
                        className={styles.formInput}
                      />
                      <small>{t('priority_hint', '1 = Highest priority, 10 = Lowest priority')}</small>
                    </div>
                    <div className={styles.formGroup}>
                      <label htmlFor="focusReason">{t('reason_optional', 'Reason (Optional)')}:</label>
                      <textarea
                        id="focusReason"
                        value={focusReason}
                        onChange={(e) => setFocusReason(e.target.value)}
                        placeholder={t('focus_reason_placeholder', 'Why is this order a priority?')}
                        className={styles.formTextarea}
                        rows={3}
                      />
                    </div>
                  </>
                )}
                {selectedOrder.isFocusOrder && (
                  <p className={styles.confirmMessage}>
                    {t('remove_focus_confirm', 'Are you sure you want to remove this order from focus orders?')}
                  </p>
                )}
              </div>
              <div className={styles.modalFooter}>
                <button
                  onClick={() => setShowFocusModal(false)}
                  className={styles.cancelButton}
                  disabled={isTogglingFocus}
                >
                  {t('cancel', 'Cancel')}
                </button>
                <button
                  onClick={handleToggleFocus}
                  className={selectedOrder.isFocusOrder ? styles.removeButton : styles.confirmButton}
                  disabled={isTogglingFocus}
                >
                  {isTogglingFocus ? (
                    <>
                      <Loader2 size={18} className={styles.spinner} />
                      {t('processing', 'Processing...')}
                    </>
                  ) : selectedOrder.isFocusOrder ? (
                    <>
                      <X size={18} />
                      {t('remove_focus', 'Remove Focus')}
                    </>
                  ) : (
                    <>
                      <Star size={18} />
                      {t('mark_as_focus', 'Mark as Focus')}
                    </>
                  )}
                </button>
              </div>
            </div>
          </div>
        )}
      </div>
    </main>
  );
}
