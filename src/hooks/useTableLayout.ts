import { useState, useCallback } from 'react';
import { useTranslation } from 'react-i18next';
import type { TableDto, UpdateTableDto, CreateTableDto } from '@/types/reservation';
import tableLayoutService from '@/services/tableLayoutService';

const CANVAS_WIDTH = 600;
const CANVAS_HEIGHT = 500;

export function useTableLayout() {
  const { t } = useTranslation();
  const [tables, setTables] = useState<TableDto[]>([]);
  const [selectedTable, setSelectedTable] = useState<TableDto | null>(null);
  const [draggingTable, setDraggingTable] = useState<string | null>(null);
  const [draggingEntrance, setDraggingEntrance] = useState(false);
  const [entrancePosition, setEntrancePosition] = useState({ x: 50, y: 10 });
  const [dragOffset, setDragOffset] = useState({ x: 0, y: 0 });
  const [loading, setLoading] = useState(true);
  const [saving, setSaving] = useState(false);
  const [message, setMessage] = useState<{ type: 'success' | 'error'; text: string } | null>(null);
  const [selectedTableIds, setSelectedTableIds] = useState<Set<string>>(new Set());
  const [showDeleteModal, setShowDeleteModal] = useState(false);
  const [deleteModalData, setDeleteModalData] = useState<{ tableNumber?: string; tableCount?: number }>({});

  const showMessage = useCallback((type: 'success' | 'error', text: string) => {
    setMessage({ type, text });
    setTimeout(() => setMessage(null), 3000);
  }, []);

  const loadTables = useCallback(async () => {
    try {
      setLoading(true);
      const data = await tableLayoutService.getAllTables();
      setTables(data);
    } catch (error: any) {
      showMessage('error', error.message || t('failed_to_load_tables', 'Failed to load tables'));
    } finally {
      setLoading(false);
    }
  }, [showMessage, t]);

  const loadEntrancePosition = useCallback(() => {
    const saved = localStorage.getItem('entrancePosition');
    if (saved) {
      try {
        const position = JSON.parse(saved);
        setEntrancePosition(position);
      } catch {
        // Use default
      }
    }
  }, []);

  const saveEntrancePosition = useCallback((position: { x: number; y: number }) => {
    localStorage.setItem('entrancePosition', JSON.stringify(position));
  }, []);

  const updateSelectedTable = useCallback((updates: Partial<TableDto>) => {
    setSelectedTable(prev => (prev ? { ...prev, ...updates } : null));
    setTables(prev =>
      prev.map(t => (t.id === selectedTable?.id ? { ...t, ...updates } : t))
    );
  }, [selectedTable?.id]);

  const handleDeleteTable = useCallback(async () => {
    if (!selectedTable) return;

    setDeleteModalData({ tableNumber: selectedTable.tableNumber });
    setShowDeleteModal(true);
  }, [selectedTable]);

  const confirmDeleteTable = useCallback(async () => {
    if (!selectedTable) return;

    try {
      setSaving(true);
      await tableLayoutService.deleteTable(selectedTable.id);
      setTables(prev => prev.filter(t => t.id !== selectedTable.id));
      setSelectedTable(null);
      setShowDeleteModal(false);
      showMessage('success', t('table_deleted_successfully', 'Table {{tableNumber}} deleted successfully!').replace('{{tableNumber}}', selectedTable.tableNumber));
    } catch (error: any) {
      showMessage('error', error.message || t('failed_to_delete_table', 'Failed to delete table'));
    } finally {
      setSaving(false);
    }
  }, [selectedTable, showMessage, t]);

  const handleCreateTable = useCallback(async (tableData: CreateTableDto): Promise<TableDto> => {
    try {
      const newTable = await tableLayoutService.createTable(tableData);
      setTables(prev => [...prev, newTable]);
      setSelectedTable(newTable);
      showMessage('success', t('table_created_successfully', 'Table {{tableNumber}} created successfully!').replace('{{tableNumber}}', newTable.tableNumber));
      return newTable;
    } catch (error: any) {
      showMessage('error', error.message || t('failed_to_create_table', 'Failed to create table'));
      throw error;
    }
  }, [showMessage, t]);

  const handleSaveLayout = useCallback(async () => {
    try {
      setSaving(true);
      const updates = tables.map(async (table) => {
        const updateData: UpdateTableDto = {
          tableNumber: table.tableNumber,
          maxGuests: table.maxGuests,
          isActive: table.isActive,
          isOutdoor: table.isOutdoor,
          positionX: table.positionX,
          positionY: table.positionY,
          width: table.width,
          height: table.height,
          shape: table.shape || 'circle',
          rotation: table.rotation || 0,
          notes: table.notes,
        };
        await tableLayoutService.updateTable(table.id, updateData);
      });

      await Promise.all(updates);
      await loadTables();
      showMessage('success', t('layout_saved_successfully', 'Layout saved successfully!'));
    } catch (error: any) {
      showMessage('error', error.message || t('failed_to_save_layout', 'Failed to save layout'));
    } finally {
      setSaving(false);
    }
  }, [tables, loadTables, showMessage, t]);

  const toggleTableSelection = useCallback((tableId: string) => {
    setSelectedTableIds(prev => {
      const newSet = new Set(prev);
      if (newSet.has(tableId)) {
        newSet.delete(tableId);
      } else {
        newSet.add(tableId);
      }
      return newSet;
    });
  }, []);

  const bulkActivateTables = useCallback(async () => {
    if (selectedTableIds.size === 0) {
      showMessage('error', t('no_tables_selected', 'No tables selected'));
      return;
    }

    try {
      setSaving(true);
      const updates = Array.from(selectedTableIds).map(async (tableId) => {
        const table = tables.find(t => t.id === tableId);
        if (table && !table.isActive) {
          const updateData: UpdateTableDto = {
            ...table,
            isActive: true,
            shape: table.shape || 'circle',
          };
          await tableLayoutService.updateTable(tableId, updateData);
          return tableId;
        }
        return null;
      });

      const updated = (await Promise.all(updates)).filter(Boolean);
      if (updated.length > 0) {
        await loadTables();
        showMessage('success', t('tables_activated', 'Activated {{count}} table(s)').replace('{{count}}', updated.length.toString()));
        setSelectedTableIds(new Set());
      } else {
        showMessage('error', t('no_inactive_tables', 'No inactive tables to activate'));
      }
    } catch (error: any) {
      showMessage('error', error.message || t('failed_to_activate_tables', 'Failed to activate tables'));
    } finally {
      setSaving(false);
    }
  }, [selectedTableIds, tables, loadTables, showMessage, t]);

  const bulkDeactivateTables = useCallback(async () => {
    if (selectedTableIds.size === 0) {
      showMessage('error', t('no_tables_selected', 'No tables selected'));
      return;
    }

    try {
      setSaving(true);
      const updates = Array.from(selectedTableIds).map(async (tableId) => {
        const table = tables.find(t => t.id === tableId);
        if (table && table.isActive) {
          const updateData: UpdateTableDto = {
            ...table,
            isActive: false,
            shape: table.shape || 'circle',
          };
          await tableLayoutService.updateTable(tableId, updateData);
          return tableId;
        }
        return null;
      });

      const updated = (await Promise.all(updates)).filter(Boolean);
      if (updated.length > 0) {
        await loadTables();
        showMessage('success', t('tables_deactivated', 'Deactivated {{count}} table(s)').replace('{{count}}', updated.length.toString()));
        setSelectedTableIds(new Set());
      } else {
        showMessage('error', t('no_active_tables', 'No active tables to deactivate'));
      }
    } catch (error: any) {
      showMessage('error', error.message || t('failed_to_deactivate_tables', 'Failed to deactivate tables'));
    } finally {
      setSaving(false);
    }
  }, [selectedTableIds, tables, loadTables, showMessage, t]);

  const bulkDeleteTables = useCallback(async () => {
    if (selectedTableIds.size === 0) {
      showMessage('error', t('no_tables_selected', 'No tables selected'));
      return;
    }

    setDeleteModalData({ tableCount: selectedTableIds.size });
    setShowDeleteModal(true);
  }, [selectedTableIds.size, showMessage, t]);

  const confirmBulkDeleteTables = useCallback(async () => {
    if (selectedTableIds.size === 0) return;

    try {
      setSaving(true);
      const deletes = Array.from(selectedTableIds).map(async (tableId) => {
        await tableLayoutService.deleteTable(tableId);
        return tableId;
      });

      const count = selectedTableIds.size;
      await Promise.all(deletes);
      await loadTables();
      setShowDeleteModal(false);
      showMessage('success', t('tables_deleted', 'Deleted {{count}} table(s)').replace('{{count}}', count.toString()));
      setSelectedTableIds(new Set());
    } catch (error: any) {
      showMessage('error', error.message || t('failed_to_delete_tables', 'Failed to delete tables'));
    } finally {
      setSaving(false);
    }
  }, [selectedTableIds, loadTables, showMessage, t]);

  return {
    // State
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

    // Actions
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

    // Constants
    CANVAS_WIDTH,
    CANVAS_HEIGHT,
  };
}
