'use client';

import React, { useState, useRef, useEffect } from 'react';
import { AdminAuthGuard } from '@/components/admin/AdminAuthGuard';
import TableLayoutHeader from '@/components/admin/table-layout/TableLayoutHeader';
import TableFilters from '@/components/admin/table-layout/TableFilters';
import TableCanvas from '@/components/admin/table-layout/TableCanvas';
import TablePropertiesSidebar from '@/components/admin/table-layout/TablePropertiesSidebar';
import { CreateTableModal } from '@/components/admin/table-layout/CreateTableModal';
import { DeleteTableModal } from '@/components/admin/table-layout/DeleteTableModal';
import TableQRCodeModal from '@/components/admin/tables/TableQRCodeModal';
import { useTableLayout } from '@/hooks/useTableLayout';
import type { TableDto } from '@/types/reservation';
import { generateTableQRCode } from '@/services/tableQRService';
import styles from './TableLayoutEditor.module.css';

// Canvas size presets
const CANVAS_SIZES = {
  small: { width: 800, height: 667, label: 'Small (10-20 tables)' },
  medium: { width: 1000, height: 833, label: 'Medium (20-30 tables)' },
  large: { width: 1200, height: 1000, label: 'Large (30+ tables)' }
};

type CanvasSize = keyof typeof CANVAS_SIZES;

export default function TableLayoutEditorPage() {
  const canvasRef = useRef<HTMLDivElement>(null);
  const [canvasSize, setCanvasSize] = useState<CanvasSize>('medium');
  const [showCreateModal, setShowCreateModal] = useState(false);
  const [selectionMode, setSelectionMode] = useState(false);
  const [filters, setFilters] = useState({
    showIndoor: true,
    showOutdoor: true,
    showActive: true,
    showInactive: true,
  });
  const [showQRModal, setShowQRModal] = useState(false);
  const [qrModalTable, setQRModalTable] = useState<TableDto | null>(null);

  const {
    tables,
    setTables,
    selectedTable,
    setSelectedTable,
    draggingTable,
    setDraggingTable,
    draggingEntrance,
    setDraggingEntrance,
    entrancePosition,
    setEntrancePosition,
    dragOffset,
    setDragOffset,
    loading,
    saving,
    message,
    selectedTableIds,
    setSelectedTableIds,
    showDeleteModal,
    setShowDeleteModal,
    deleteModalData,
    showMessage,
    loadTables,
    loadEntrancePosition,
    saveEntrancePosition,
    updateSelectedTable,
    handleCreateTable,
    handleDeleteTable,
    confirmDeleteTable,
    handleSaveLayout,
    toggleTableSelection,
    bulkActivateTables,
    bulkDeactivateTables,
    bulkDeleteTables,
    confirmBulkDeleteTables,
    CANVAS_WIDTH,
    CANVAS_HEIGHT,
  } = useTableLayout();

  useEffect(() => {
    loadTables();
    loadEntrancePosition();
  }, [loadTables, loadEntrancePosition]);

  const currentCanvasSize = CANVAS_SIZES[canvasSize];

  const handleMouseDown = (e: React.MouseEvent, table: TableDto) => {
    e.stopPropagation();
    setSelectedTable(table);

    const canvas = canvasRef.current;
    if (!canvas) return;

    const rect = canvas.getBoundingClientRect();
    const tablePercentX = (table.positionX / CANVAS_WIDTH) * 100;
    const tablePercentY = (table.positionY / CANVAS_HEIGHT) * 100;
    const tableX = (tablePercentX / 100) * rect.width;
    const tableY = (tablePercentY / 100) * rect.height;

    setDragOffset({
      x: e.clientX - rect.left - tableX,
      y: e.clientY - rect.top - tableY,
    });
    setDraggingTable(table.id);
  };

  const handleTableClick = (table: TableDto) => {
    setSelectedTable(table);
  };

  const handleEntranceMouseDown = (e: React.MouseEvent) => {
    e.stopPropagation();
    const canvas = canvasRef.current;
    if (!canvas) return;

    const rect = canvas.getBoundingClientRect();
    const entranceX = (entrancePosition.x / 100) * rect.width;
    const entranceY = (entrancePosition.y / 100) * rect.height;

    setDragOffset({
      x: e.clientX - rect.left - entranceX,
      y: e.clientY - rect.top - entranceY,
    });
    setDraggingEntrance(true);
  };

  const handleMouseMove = (e: React.MouseEvent) => {
    const canvas = canvasRef.current;
    if (!canvas) return;

    if (draggingTable) {
      const rect = canvas.getBoundingClientRect();
      const x = e.clientX - rect.left - dragOffset.x;
      const y = e.clientY - rect.top - dragOffset.y;

      const percentX = (x / rect.width) * 100;
      const percentY = (y / rect.height) * 100;

      const pixelX = (percentX / 100) * CANVAS_WIDTH;
      const pixelY = (percentY / 100) * CANVAS_HEIGHT;

      const clampedX = Math.max(0, Math.min(CANVAS_WIDTH, pixelX));
      const clampedY = Math.max(0, Math.min(CANVAS_HEIGHT, pixelY));

      setTables(prev =>
        prev.map(t =>
          t.id === draggingTable
            ? { ...t, positionX: clampedX, positionY: clampedY }
            : t
        )
      );
      setSelectedTable(prev =>
        prev && prev.id === draggingTable
          ? { ...prev, positionX: clampedX, positionY: clampedY }
          : prev
      );
    } else if (draggingEntrance) {
      const rect = canvas.getBoundingClientRect();
      const x = e.clientX - rect.left - dragOffset.x;
      const y = e.clientY - rect.top - dragOffset.y;

      const percentX = Math.max(0, Math.min(100, (x / rect.width) * 100));
      const percentY = Math.max(0, Math.min(100, (y / rect.height) * 100));

      setEntrancePosition({ x: percentX, y: percentY });
    }
  };

  const handleMouseUp = () => {
    if (draggingTable) {
      setDraggingTable(null);
    }
    if (draggingEntrance) {
      setDraggingEntrance(false);
      saveEntrancePosition(entrancePosition);
    }
  };

  const toggleSelectionMode = () => {
    setSelectionMode(!selectionMode);
    if (selectionMode) {
      setSelectedTableIds(new Set());
    }
  };

  const handleViewQRCode = () => {
    if (!selectedTable) return;
    // Get the latest table data from the tables array to ensure QR data is up-to-date
    const latestTableData = tables.find(t => t.id === selectedTable.id) || selectedTable;
    setQRModalTable(latestTableData);
    setShowQRModal(true);
  };

  const handleRegenerateQR = async () => {
    if (!qrModalTable) return;

    try {
      const result = await generateTableQRCode(qrModalTable.id);
      const updatedData = {
        qrCodeData: result.qrCodeData,
        qrCodeGeneratedAt: result.qrCodeGeneratedAt
      };

      // Update tables array
      setTables(prev =>
        prev.map(t =>
          t.id === qrModalTable.id
            ? { ...t, ...updatedData }
            : t
        )
      );

      // Update modal table
      setQRModalTable(prev =>
        prev ? { ...prev, ...updatedData } : null
      );

      // Update selected table to keep it in sync
      setSelectedTable(prev =>
        prev && prev.id === qrModalTable.id
          ? { ...prev, ...updatedData }
          : prev
      );

      showMessage('success', 'QR code generated successfully!');
    } catch (error: any) {
      showMessage('error', error.message || 'Failed to generate QR code');
    }
  };

  if (loading) {
    return (
      <AdminAuthGuard>
        <div className={styles.container}>
          <div className={styles.loading}>Loading tables...</div>
        </div>
      </AdminAuthGuard>
    );
  }

  return (
    <AdminAuthGuard>
      <div className={styles.container}>
        {/* Message Toast */}
        {message && (
          <div className={`${styles.message} ${styles[message.type]}`}>
            {message.text}
          </div>
        )}

        {/* Header */}
        <TableLayoutHeader
          canvasSize={canvasSize}
          canvasSizes={CANVAS_SIZES}
          selectionMode={selectionMode}
          selectedCount={selectedTableIds.size}
          saving={saving}
          onCanvasSizeChange={(size) => setCanvasSize(size as CanvasSize)}
          onCreateTable={() => setShowCreateModal(true)}
          onToggleSelectionMode={toggleSelectionMode}
          onSaveLayout={handleSaveLayout}
          onBulkActivate={bulkActivateTables}
          onBulkDeactivate={bulkDeactivateTables}
          onBulkDelete={bulkDeleteTables}
          onCancelSelection={toggleSelectionMode}
        />

        {/* Main Content */}
        <div className={styles.content}>
          {/* Left Sidebar */}
          <div className={styles.leftSidebar}>
            <TableFilters filters={filters} onFilterChange={setFilters} />
          </div>

          {/* Canvas */}
          <div className={styles.canvasContainer}>
            <TableCanvas
              canvasRef={canvasRef}
              tables={tables}
              selectedTable={selectedTable}
              selectedTableIds={selectedTableIds}
              draggingTable={draggingTable}
              draggingEntrance={draggingEntrance}
              entrancePosition={entrancePosition}
              selectionMode={selectionMode}
              filters={filters}
              canvasSize={currentCanvasSize}
              onTableMouseDown={handleMouseDown}
              onTableClick={handleTableClick}
              onToggleTableSelection={toggleTableSelection}
              onEntranceMouseDown={handleEntranceMouseDown}
              onMouseMove={handleMouseMove}
              onMouseUp={handleMouseUp}
            />
          </div>

          {/* Right Sidebar */}
          <div className={styles.rightSidebar}>
            <TablePropertiesSidebar
              selectedTable={selectedTable}
              currentCanvasSize={currentCanvasSize}
              onUpdateTable={updateSelectedTable}
              onDeleteTable={handleDeleteTable}
              onViewQRCode={handleViewQRCode}
            />
          </div>
        </div>

        {/* QR Code Modal */}
        {showQRModal && qrModalTable && (
          <TableQRCodeModal
            isOpen={showQRModal}
            onClose={() => setShowQRModal(false)}
            tableId={qrModalTable.id}
            tableNumber={qrModalTable.tableNumber}
            qrCodeData={qrModalTable.qrCodeData}
            qrCodeGeneratedAt={qrModalTable.qrCodeGeneratedAt}
            onRegenerate={handleRegenerateQR}
          />
        )}

        {/* Create Table Modal */}
        <CreateTableModal
          isOpen={showCreateModal}
          onClose={() => setShowCreateModal(false)}
          onCreateTable={handleCreateTable}
          existingTableNumbers={tables.map(t => t.tableNumber)}
          canvasWidth={CANVAS_WIDTH}
          canvasHeight={CANVAS_HEIGHT}
        />

        {/* Delete Table Modal */}
        <DeleteTableModal
          isOpen={showDeleteModal}
          onClose={() => setShowDeleteModal(false)}
          onConfirm={deleteModalData.tableCount ? confirmBulkDeleteTables : confirmDeleteTable}
          tableNumber={deleteModalData.tableNumber}
          tableCount={deleteModalData.tableCount}
          isDeleting={saving}
        />
      </div>
    </AdminAuthGuard>
  );
}
