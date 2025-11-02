import { useState, useCallback } from 'react';
import type { TableDto, UpdateTableDto, CreateTableDto } from '@/types/reservation';
import tableLayoutService from '@/services/tableLayoutService';

const CANVAS_WIDTH = 600;
const CANVAS_HEIGHT = 500;

export function useTableLayout() {
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
      showMessage('error', error.message || 'Failed to load tables');
    } finally {
      setLoading(false);
    }
  }, [showMessage]);

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

    if (!confirm(`Are you sure you want to delete Table ${selectedTable.tableNumber}?`)) {
      return;
    }

    try {
      await tableLayoutService.deleteTable(selectedTable.id);
      setTables(prev => prev.filter(t => t.id !== selectedTable.id));
      setSelectedTable(null);
      showMessage('success', `Table ${selectedTable.tableNumber} deleted successfully!`);
    } catch (error: any) {
      showMessage('error', error.message || 'Failed to delete table');
    }
  }, [selectedTable, showMessage]);

  const handleCreateTable = useCallback(async (tableData: CreateTableDto): Promise<TableDto> => {
    try {
      const newTable = await tableLayoutService.createTable(tableData);
      setTables(prev => [...prev, newTable]);
      setSelectedTable(newTable);
      showMessage('success', `Table ${newTable.tableNumber} created successfully!`);
      return newTable;
    } catch (error: any) {
      showMessage('error', error.message || 'Failed to create table');
      throw error;
    }
  }, [showMessage]);

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
          notes: table.notes,
        };
        await tableLayoutService.updateTable(table.id, updateData);
      });

      await Promise.all(updates);
      await loadTables();
      showMessage('success', 'Layout saved successfully!');
    } catch (error: any) {
      showMessage('error', error.message || 'Failed to save layout');
    } finally {
      setSaving(false);
    }
  }, [tables, loadTables, showMessage]);

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
      showMessage('error', 'No tables selected');
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
        showMessage('success', `Activated ${updated.length} table(s)`);
        setSelectedTableIds(new Set());
      } else {
        showMessage('error', 'No inactive tables to activate');
      }
    } catch (error: any) {
      showMessage('error', error.message || 'Failed to activate tables');
    } finally {
      setSaving(false);
    }
  }, [selectedTableIds, tables, loadTables, showMessage]);

  const bulkDeactivateTables = useCallback(async () => {
    if (selectedTableIds.size === 0) {
      showMessage('error', 'No tables selected');
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
        showMessage('success', `Deactivated ${updated.length} table(s)`);
        setSelectedTableIds(new Set());
      } else {
        showMessage('error', 'No active tables to deactivate');
      }
    } catch (error: any) {
      showMessage('error', error.message || 'Failed to deactivate tables');
    } finally {
      setSaving(false);
    }
  }, [selectedTableIds, tables, loadTables, showMessage]);

  const bulkDeleteTables = useCallback(async () => {
    if (selectedTableIds.size === 0) {
      showMessage('error', 'No tables selected');
      return;
    }

    if (!confirm(`Are you sure you want to delete ${selectedTableIds.size} table(s)?`)) {
      return;
    }

    try {
      setSaving(true);
      const deletes = Array.from(selectedTableIds).map(async (tableId) => {
        await tableLayoutService.deleteTable(tableId);
        return tableId;
      });

      await Promise.all(deletes);
      await loadTables();
      showMessage('success', `Deleted ${selectedTableIds.size} table(s)`);
      setSelectedTableIds(new Set());
    } catch (error: any) {
      showMessage('error', error.message || 'Failed to delete tables');
    } finally {
      setSaving(false);
    }
  }, [selectedTableIds, loadTables, showMessage]);

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

    // Actions
    showMessage,
    loadTables,
    loadEntrancePosition,
    saveEntrancePosition,
    updateSelectedTable,
    handleCreateTable,
    handleDeleteTable,
    handleSaveLayout,
    toggleTableSelection,
    bulkActivateTables,
    bulkDeactivateTables,
    bulkDeleteTables,

    // Constants
    CANVAS_WIDTH,
    CANVAS_HEIGHT,
  };
}
