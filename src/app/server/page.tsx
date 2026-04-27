'use client';

import React, { useState, useMemo, useCallback } from 'react';
import { useTranslation } from 'react-i18next';
import { useServerOrders } from '@/hooks/useServerOrders';
import { ServerHeader, TableGridView, ActiveOrdersPanel, TableDetailsModal, TakeOrderModal } from '@/components/server';
import styles from '../styles/ServerPage.module.css';

export default function ServerPage() {
  const { t } = useTranslation();
  const {
    orders,
    tables,
    isConnected,
    isLoading,
    error,
    lastEventTime,
    connectionState,
    updateOrderStatus,
    getOrdersForTable,
    refreshOrders,
    refreshTables,
  } = useServerOrders();

  const [selectedTableNumber, setSelectedTableNumber] = useState<string | null>(null);
  const [statusFilter, setStatusFilter] = useState<string>('active');
  const [showTableModal, setShowTableModal] = useState(false);
  const [showTakeOrderModal, setShowTakeOrderModal] = useState(false);

  // Filter orders based on status filter
  const filteredOrders = useMemo(() => {
    if (statusFilter === 'active') {
      return orders.filter((order) => !['Completed', 'Cancelled'].includes(order.status));
    }
    if (statusFilter === 'all') {
      return orders;
    }
    return orders.filter((order) => order.status === statusFilter);
  }, [orders, statusFilter]);

  // Get selected table data
  const selectedTable = useMemo(() => {
    if (!selectedTableNumber) return null;
    return tables.find((t) => t.tableNumber === selectedTableNumber) || null;
  }, [tables, selectedTableNumber]);

  // Handle table selection
  const handleSelectTable = useCallback(
    (tableNumber: string) => {
      if (selectedTableNumber === tableNumber) {
        // Double-click opens modal
        setShowTableModal(true);
      } else {
        setSelectedTableNumber(tableNumber);
      }
    },
    [selectedTableNumber],
  );

  // Handle status change
  const handleStatusChange = useCallback(
    async (orderId: string, status: string) => {
      try {
        await updateOrderStatus(orderId, status);
      } catch (err) {
        console.error('Failed to update order status:', err);
      }
    },
    [updateOrderStatus],
  );

  // Clear selection
  const handleClearSelection = useCallback(() => {
    setSelectedTableNumber(null);
    setShowTableModal(false);
    setShowTakeOrderModal(false);
  }, []);

  // Handle take order button
  const handleTakeOrder = useCallback(() => {
    setShowTableModal(false);
    setShowTakeOrderModal(true);
  }, []);

  // Handle order created
  const handleOrderCreated = useCallback(() => {
    refreshOrders();
    refreshTables();
  }, [refreshOrders, refreshTables]);

  // Handle table status changed - refresh both orders and tables to sync data
  const handleTableStatusChanged = useCallback(async () => {
    await refreshOrders();
    await refreshTables();
  }, [refreshOrders, refreshTables]);

  return (
    <main className={styles.pageContainer}>
      <ServerHeader
        isConnected={isConnected}
        connectionState={connectionState}
        lastEventTime={lastEventTime}
        error={error}
        statusFilter={statusFilter}
        onStatusFilterChange={setStatusFilter}
      />

      <div className={styles.mainContent}>
        {/* Left Panel - Table Grid */}
        <div className={styles.tablesPanel}>
          <TableGridView
            tables={tables}
            selectedTableNumber={selectedTableNumber}
            onSelectTable={handleSelectTable}
            isLoading={isLoading && tables.length === 0}
          />
        </div>

        {/* Right Panel - Orders */}
        <div className={styles.ordersPanel}>
          {selectedTableNumber && (
            <button className={styles.clearSelectionButton} onClick={handleClearSelection}>
              ← {t('server.show_all_orders', 'Show All Orders')}
            </button>
          )}
          <ActiveOrdersPanel
            orders={filteredOrders}
            selectedTableNumber={selectedTableNumber}
            onStatusChange={handleStatusChange}
            isLoading={isLoading}
            error={error}
          />
        </div>
      </div>

      {/* Table Details Modal */}
      {showTableModal && selectedTable && (
        <TableDetailsModal
          table={selectedTable}
          orders={getOrdersForTable(selectedTable.tableNumber)}
          onClose={() => setShowTableModal(false)}
          onUpdateOrderStatus={handleStatusChange}
          onTakeOrder={handleTakeOrder}
          onTableStatusChanged={handleTableStatusChanged}
        />
      )}

      {/* Take Order Modal */}
      {showTakeOrderModal && selectedTableNumber && (
        <TakeOrderModal
          tableNumber={selectedTableNumber}
          onClose={() => setShowTakeOrderModal(false)}
          onOrderCreated={handleOrderCreated}
        />
      )}
    </main>
  );
}
