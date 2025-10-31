'use client';

import { useState, useEffect, useRef } from 'react';
import { useRouter } from 'next/navigation';
import tableLayoutService from '@/services/tableLayoutService';
import type { TableDto, UpdateTableDto, CreateTableDto } from '@/types/reservation';
import styles from './styles.module.css';

const CANVAS_WIDTH = 600;
const CANVAS_HEIGHT = 500;

// Canvas size presets
const CANVAS_SIZES = {
  small: { width: 800, height: 667, label: 'Small (10-20 tables)' },
  medium: { width: 1000, height: 833, label: 'Medium (20-30 tables)' },
  large: { width: 1200, height: 1000, label: 'Large (30+ tables)' }
};

type CanvasSize = keyof typeof CANVAS_SIZES;

function TableLayoutEditorPage() {
  const router = useRouter();
  const [tables, setTables] = useState<TableDto[]>([]);
  const [selectedTable, setSelectedTable] = useState<TableDto | null>(null);
  const [draggingTable, setDraggingTable] = useState<string | null>(null);
  const [draggingEntrance, setDraggingEntrance] = useState(false);
  const [entrancePosition, setEntrancePosition] = useState({ x: 50, y: 10 }); // Default top-center
  const [dragOffset, setDragOffset] = useState({ x: 0, y: 0 });
  const [loading, setLoading] = useState(true);
  const [saving, setSaving] = useState(false);
  const [message, setMessage] = useState<{ type: 'success' | 'error'; text: string } | null>(null);
  const [canvasSize, setCanvasSize] = useState<CanvasSize>('medium');
  const [showCreateModal, setShowCreateModal] = useState(false);
  const [filters, setFilters] = useState({
    showIndoor: true,
    showOutdoor: true,
    showActive: true,
    showInactive: true,
  });
  const [selectionMode, setSelectionMode] = useState(false);
  const [selectedTableIds, setSelectedTableIds] = useState<Set<string>>(new Set());
  const canvasRef = useRef<HTMLDivElement>(null);

  useEffect(() => {
    loadTables();
    loadEntrancePosition();
  }, []);

  const loadEntrancePosition = () => {
    const saved = localStorage.getItem('entrancePosition');
    if (saved) {
      try {
        const position = JSON.parse(saved);
        setEntrancePosition(position);
      } catch (e) {
        console.error('Failed to load entrance position:', e);
      }
    }
  };

  const saveEntrancePosition = (position: { x: number; y: number }) => {
    localStorage.setItem('entrancePosition', JSON.stringify(position));
  };

  const loadTables = async () => {
    try {
      setLoading(true);
      const data = await tableLayoutService.getAllTables();
      setTables(data);
    } catch (error: any) {
      showMessage('error', error.message || 'Failed to load tables');
    } finally {
      setLoading(false);
    }
  };

  const showMessage = (type: 'success' | 'error', text: string) => {
    setMessage({ type, text });
    setTimeout(() => setMessage(null), 3000);
  };

  const handleMouseDown = (e: React.MouseEvent, table: TableDto) => {
    e.stopPropagation();
    setSelectedTable(table);

    const canvas = canvasRef.current;
    if (!canvas) return;

    const rect = canvas.getBoundingClientRect();
    // Convert pixel position to screen position
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

  const handleEntranceMouseDown = (e: React.MouseEvent) => {
    e.stopPropagation();
    setSelectedTable(null);

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
    if (!canvasRef.current) return;
    if (!draggingTable && !draggingEntrance) return;

    const canvas = canvasRef.current;
    const rect = canvas.getBoundingClientRect();

    const newX = e.clientX - rect.left - dragOffset.x;
    const newY = e.clientY - rect.top - dragOffset.y;

    // Convert screen position to percentage
    const percentX = (newX / rect.width) * 100;
    const percentY = (newY / rect.height) * 100;

    if (draggingEntrance) {
      // Update entrance position (stored as percentages)
      const newPosition = {
        x: Math.max(0, Math.min(100, percentX)),
        y: Math.max(0, Math.min(100, percentY)),
      };
      setEntrancePosition(newPosition);
    } else if (draggingTable) {
      // Convert percentage to pixels for storage
      const pixelX = (percentX / 100) * CANVAS_WIDTH;
      const pixelY = (percentY / 100) * CANVAS_HEIGHT;

      setTables(prev =>
        prev.map(t =>
          t.id === draggingTable
            ? {
                ...t,
                positionX: Math.max(0, Math.min(CANVAS_WIDTH - 50, pixelX)),
                positionY: Math.max(0, Math.min(CANVAS_HEIGHT - 50, pixelY)),
              }
            : t
        )
      );
    }
  };

  const handleMouseUp = () => {
    if (draggingEntrance) {
      saveEntrancePosition(entrancePosition);
      setDraggingEntrance(false);
    }
    setDraggingTable(null);
  };

  const handleSaveLayout = async () => {
    try {
      setSaving(true);
      const updates = tables.map(table => ({
        id: table.id,
        data: {
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
        } as UpdateTableDto,
      }));

      await tableLayoutService.batchUpdateTables(updates);
      showMessage('success', 'Layout saved successfully!');
    } catch (error: any) {
      showMessage('error', error.message || 'Failed to save layout');
    } finally {
      setSaving(false);
    }
  };

  const updateSelectedTable = (updates: Partial<TableDto>) => {
    if (!selectedTable) return;

    setTables(prev =>
      prev.map(t => (t.id === selectedTable.id ? { ...t, ...updates } : t))
    );
    setSelectedTable(prev => (prev ? { ...prev, ...updates } : null));
  };

  const handleDeleteTable = async () => {
    if (!selectedTable) return;

    if (!confirm(`Are you sure you want to delete Table ${selectedTable.tableNumber}? This action cannot be undone.`)) {
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
  };

  const renderTable = (table: TableDto) => {
    const isSelected = selectedTable?.id === table.id;
    const isInSelectionSet = selectedTableIds.has(table.id);
    const shape = table.shape || 'circle';
    const isRound = shape === 'circle';
    const isSquare = shape === 'square';

    const shapeStyle: React.CSSProperties = isRound
      ? { borderRadius: '50%', width: '80px', height: '80px' }
      : isSquare
      ? { width: '60px', height: '60px' }
      : { width: '100px', height: '70px' };

    // Convert pixel positions to percentages (using original canvas size for position calculations)
    const leftPercent = (table.positionX / CANVAS_WIDTH) * 100;
    const topPercent = (table.positionY / CANVAS_HEIGHT) * 100;

    const handleClick = (e: React.MouseEvent) => {
      if (selectionMode) {
        e.stopPropagation();
        toggleTableSelection(table.id);
      } else {
        handleMouseDown(e, table);
      }
    };

    return (
      <div
        key={table.id}
        className={`${styles.table} ${isSelected ? styles.selected : ''} ${
          table.isOutdoor ? styles.outdoor : ''
        } ${selectionMode ? styles.selectionMode : ''} ${
          isInSelectionSet ? styles.inSelectionSet : ''
        }`}
        style={{
          left: `${leftPercent}%`,
          top: `${topPercent}%`,
          ...shapeStyle,
        }}
        onMouseDown={handleClick}
      >
        {selectionMode && (
          <div className={styles.tableCheckbox}>
            <input
              type="checkbox"
              checked={isInSelectionSet}
              onChange={() => toggleTableSelection(table.id)}
              onClick={(e) => e.stopPropagation()}
            />
          </div>
        )}
        <div className={styles.tableLabel}>
          {table.tableNumber}
          <span className={styles.guestCount}>({table.maxGuests})</span>
        </div>
      </div>
    );
  };

  if (loading) {
    return (
      <div className={styles.container}>
        <div className={styles.loading}>Loading tables...</div>
      </div>
    );
  }

  // Calculate statistics
  const totalTables = tables.length;
  const activeTables = tables.filter(t => t.isActive).length;
  const inactiveTables = tables.filter(t => !t.isActive).length;
  const totalCapacity = tables.filter(t => t.isActive).reduce((sum, t) => sum + t.maxGuests, 0);

  // Get current canvas dimensions
  const currentCanvasSize = CANVAS_SIZES[canvasSize];

  // Filter tables based on filter state
  const filteredTables = tables.filter(table => {
    // Filter by indoor/outdoor
    if (table.isOutdoor && !filters.showOutdoor) return false;
    if (!table.isOutdoor && !filters.showIndoor) return false;

    // Filter by active/inactive
    if (table.isActive && !filters.showActive) return false;
    if (!table.isActive && !filters.showInactive) return false;

    return true;
  });

  // Toggle filter
  const toggleFilter = (filterKey: keyof typeof filters) => {
    setFilters(prev => ({
      ...prev,
      [filterKey]: !prev[filterKey]
    }));
  };

  // Toggle selection mode
  const toggleSelectionMode = () => {
    setSelectionMode(prev => !prev);
    setSelectedTableIds(new Set()); // Clear selections when toggling mode
  };

  // Toggle table selection
  const toggleTableSelection = (tableId: string) => {
    setSelectedTableIds(prev => {
      const newSet = new Set(prev);
      if (newSet.has(tableId)) {
        newSet.delete(tableId);
      } else {
        newSet.add(tableId);
      }
      return newSet;
    });
  };

  // Select all visible tables
  const selectAllTables = () => {
    setSelectedTableIds(new Set(filteredTables.map(t => t.id)));
  };

  // Clear all selections
  const clearSelections = () => {
    setSelectedTableIds(new Set());
  };

  // Bulk activate tables
  const bulkActivateTables = async () => {
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
            tableNumber: table.tableNumber,
            maxGuests: table.maxGuests,
            isActive: true,
            isOutdoor: table.isOutdoor,
            positionX: table.positionX,
            positionY: table.positionY,
            width: table.width,
            height: table.height,
            shape: table.shape || 'circle',
            notes: table.notes,
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
  };

  // Bulk deactivate tables
  const bulkDeactivateTables = async () => {
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
            tableNumber: table.tableNumber,
            maxGuests: table.maxGuests,
            isActive: false,
            isOutdoor: table.isOutdoor,
            positionX: table.positionX,
            positionY: table.positionY,
            width: table.width,
            height: table.height,
            shape: table.shape || 'circle',
            notes: table.notes,
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
  };

  // Create Table Modal Component
  const CreateTableModal = () => {
    const [formData, setFormData] = useState({
      tableNumber: '',
      maxGuests: 4,
      shape: 'circle',
      isOutdoor: false,
      isActive: true,
      notes: '',
    });
    const [creating, setCreating] = useState(false);

    const handleSubmit = async (e: React.FormEvent) => {
      e.preventDefault();

      if (!formData.tableNumber.trim()) {
        showMessage('error', 'Please enter a table number');
        return;
      }

      // Check if table number already exists
      if (tables.some(t => t.tableNumber === formData.tableNumber)) {
        showMessage('error', `Table ${formData.tableNumber} already exists`);
        return;
      }

      try {
        setCreating(true);

        // Place new table in center of canvas
        const newTableData: CreateTableDto = {
          tableNumber: formData.tableNumber,
          maxGuests: formData.maxGuests,
          isActive: formData.isActive,
          isOutdoor: formData.isOutdoor,
          positionX: CANVAS_WIDTH / 2 - 40, // Center horizontally
          positionY: CANVAS_HEIGHT / 2 - 40, // Center vertically
          width: formData.shape === 'rectangle' ? 100 : formData.shape === 'square' ? 60 : 80,
          height: formData.shape === 'rectangle' ? 70 : formData.shape === 'square' ? 60 : 80,
          shape: formData.shape,
          notes: formData.notes || undefined,
        };

        const newTable = await tableLayoutService.createTable(newTableData);
        setTables(prev => [...prev, newTable]);
        setSelectedTable(newTable);
        setShowCreateModal(false);
        showMessage('success', `Table ${newTable.tableNumber} created successfully!`);
      } catch (error: any) {
        showMessage('error', error.message || 'Failed to create table');
      } finally {
        setCreating(false);
      }
    };

    return (
      <div className={styles.modalOverlay} onClick={() => setShowCreateModal(false)}>
        <div className={styles.modalContent} onClick={(e) => e.stopPropagation()}>
          <h2>Create New Table</h2>

          <form onSubmit={handleSubmit}>
            <div className={styles.formGroup}>
              <label>Table Number *</label>
              <input
                type="text"
                value={formData.tableNumber}
                onChange={(e) => setFormData(prev => ({ ...prev, tableNumber: e.target.value }))}
                placeholder="e.g., T1, A1, etc."
                required
              />
            </div>

            <div className={styles.formGroup}>
              <label>Max Guests</label>
              <input
                type="number"
                min="1"
                max="20"
                value={formData.maxGuests}
                onChange={(e) => setFormData(prev => ({ ...prev, maxGuests: parseInt(e.target.value) || 1 }))}
              />
            </div>

            <div className={styles.formGroup}>
              <label>Shape</label>
              <select
                value={formData.shape}
                onChange={(e) => setFormData(prev => ({ ...prev, shape: e.target.value }))}
              >
                <option value="circle">Circle</option>
                <option value="square">Square</option>
                <option value="rectangle">Rectangle</option>
              </select>
            </div>

            <div className={styles.formGroup}>
              <label>Location</label>
              <div className={styles.chipGroup}>
                <div className={styles.chip}>
                  <input
                    type="checkbox"
                    id="isOutdoor"
                    checked={formData.isOutdoor}
                    onChange={(e) => setFormData(prev => ({ ...prev, isOutdoor: e.target.checked }))}
                  />
                  <label htmlFor="isOutdoor">Outdoor</label>
                </div>
              </div>
            </div>

            <div className={styles.formGroup}>
              <label>Status</label>
              <div className={styles.chipGroup}>
                <div className={styles.chip}>
                  <input
                    type="checkbox"
                    id="isActive"
                    checked={formData.isActive}
                    onChange={(e) => setFormData(prev => ({ ...prev, isActive: e.target.checked }))}
                  />
                  <label htmlFor="isActive">Active</label>
                </div>
              </div>
            </div>

            <div className={styles.formGroup}>
              <label>Notes (optional)</label>
              <textarea
                value={formData.notes}
                onChange={(e) => setFormData(prev => ({ ...prev, notes: e.target.value }))}
                placeholder="e.g., Near window, Quiet corner, etc."
                rows={3}
                maxLength={500}
              />
              <small style={{ color: 'var(--text-secondary, #888)', fontSize: '0.875rem', marginTop: '0.25rem', display: 'block' }}>
                {formData.notes.length}/500 characters
              </small>
            </div>

            <div className={styles.buttonGroup}>
              <button
                type="button"
                onClick={() => setShowCreateModal(false)}
                className={styles.cancelButton}
              >
                Cancel
              </button>
              <button
                type="submit"
                disabled={creating}
                className={styles.submitButton}
              >
                {creating ? 'Creating...' : 'Create Table'}
              </button>
            </div>
          </form>
        </div>
      </div>
    );
  };

  return (
    <div className={styles.container}>
      {/* Header */}
      <div className={styles.header}>
        <div>
          <h1 className={styles.title}>Table Layout Editor</h1>
          <p className={styles.subtitle}>Drag tables to reposition them on the floor plan</p>
        </div>
        <div className={styles.headerActions}>
          {!selectionMode ? (
            <>
              <div className={styles.canvasSizeSelector}>
                <label>Canvas Size:</label>
                <select
                  value={canvasSize}
                  onChange={(e) => setCanvasSize(e.target.value as CanvasSize)}
                  className={styles.sizeSelect}
                >
                  {Object.entries(CANVAS_SIZES).map(([key, { label }]) => (
                    <option key={key} value={key}>{label}</option>
                  ))}
                </select>
              </div>
              <button
                onClick={() => setShowCreateModal(true)}
                className={styles.createButton}
              >
                + Create Table
              </button>
              <button
                onClick={toggleSelectionMode}
                className={styles.selectionModeButton}
              >
                Select Multiple
              </button>
              <button
                onClick={handleSaveLayout}
                disabled={saving}
                className={styles.saveButton}
              >
                {saving ? 'Saving...' : 'Save Layout'}
              </button>
            </>
          ) : (
            <>
              <div className={styles.selectionInfo}>
                {selectedTableIds.size} table(s) selected
              </div>
              <button
                onClick={selectAllTables}
                className={styles.selectAllButton}
              >
                Select All
              </button>
              <button
                onClick={clearSelections}
                className={styles.clearButton}
                disabled={selectedTableIds.size === 0}
              >
                Clear
              </button>
              <button
                onClick={bulkActivateTables}
                className={styles.activateButton}
                disabled={selectedTableIds.size === 0 || saving}
              >
                Activate Selected
              </button>
              <button
                onClick={bulkDeactivateTables}
                className={styles.deactivateButton}
                disabled={selectedTableIds.size === 0 || saving}
              >
                Deactivate Selected
              </button>
              <button
                onClick={toggleSelectionMode}
                className={styles.doneButton}
              >
                Done
              </button>
            </>
          )}
        </div>
      </div>

      {/* Statistics */}
      <div className={styles.stats}>
        <div className={styles.statCard}>
          <div className={styles.statValue}>{totalTables}</div>
          <div className={styles.statLabel}>Total Tables</div>
        </div>
        <div className={styles.statCard}>
          <div className={styles.statValue}>{activeTables}</div>
          <div className={styles.statLabel}>Active</div>
        </div>
        <div className={styles.statCard}>
          <div className={styles.statValue}>{inactiveTables}</div>
          <div className={styles.statLabel}>Inactive</div>
        </div>
        <div className={styles.statCard}>
          <div className={styles.statValue}>{totalCapacity}</div>
          <div className={styles.statLabel}>Total Capacity</div>
        </div>
      </div>

      {/* Filters */}
      <div className={styles.filters}>
        <div className={styles.filterLabel}>Show:</div>
        <div className={styles.filterChips}>
          <button
            className={`${styles.filterChip} ${filters.showIndoor ? styles.active : ''}`}
            onClick={() => toggleFilter('showIndoor')}
          >
            Indoor
          </button>
          <button
            className={`${styles.filterChip} ${filters.showOutdoor ? styles.active : ''}`}
            onClick={() => toggleFilter('showOutdoor')}
          >
            Outdoor
          </button>
          <span className={styles.filterDivider}>|</span>
          <button
            className={`${styles.filterChip} ${filters.showActive ? styles.active : ''}`}
            onClick={() => toggleFilter('showActive')}
          >
            Active
          </button>
          <button
            className={`${styles.filterChip} ${filters.showInactive ? styles.active : ''}`}
            onClick={() => toggleFilter('showInactive')}
          >
            Inactive
          </button>
        </div>
        <div className={styles.filterCount}>
          {filteredTables.length} of {totalTables} tables shown
        </div>
      </div>

      {/* Message */}
      {message && (
        <div className={`${styles.message} ${styles[message.type]}`}>
          {message.text}
        </div>
      )}

      {/* Main Content */}
      <div className={styles.mainContent}>
        {/* Canvas */}
        <div className={styles.canvasContainer}>
          <div
            ref={canvasRef}
            className={styles.canvas}
            onMouseMove={handleMouseMove}
            onMouseUp={handleMouseUp}
            onMouseLeave={handleMouseUp}
            style={{
              width: `${currentCanvasSize.width}px`,
              height: `${currentCanvasSize.height}px`,
              maxWidth: '100%'
            }}
          >
            {/* Entrance */}
            <div
              className={`${styles.entrance} ${draggingEntrance ? styles.dragging : ''}`}
              style={{
                left: `${entrancePosition.x}%`,
                top: `${entrancePosition.y}%`,
                cursor: 'grab',
              }}
              onMouseDown={handleEntranceMouseDown}
            >
              <div className={styles.entranceDoor} />
              <span className={styles.entranceLabel}>Way in</span>
            </div>

            {/* Tables */}
            {filteredTables.map(renderTable)}

            {/* Grid lines for reference */}
            <div className={styles.gridLines}>
              {Array.from({ length: 10 }).map((_, i) => (
                <div key={`h-${i}`} className={styles.gridLineH} style={{ top: `${i * 10}%` }} />
              ))}
              {Array.from({ length: 10 }).map((_, i) => (
                <div key={`v-${i}`} className={styles.gridLineV} style={{ left: `${i * 10}%` }} />
              ))}
            </div>
          </div>

          <div className={styles.canvasLegend}>
            <div className={styles.legendItem}>
              <div className={`${styles.legendBox} ${styles.indoor}`} />
              <span>Indoor</span>
            </div>
            <div className={styles.legendItem}>
              <div className={`${styles.legendBox} ${styles.outdoor}`} />
              <span>Outdoor</span>
            </div>
          </div>
        </div>

        {/* Properties Panel */}
        <div className={styles.propertiesPanel}>
          <h2 className={styles.panelTitle}>Table Properties</h2>

          {selectedTable ? (
            <div className={styles.properties}>
              <div className={styles.formGroup}>
                <label>Table Number</label>
                <input
                  type="text"
                  value={selectedTable.tableNumber}
                  onChange={(e) => updateSelectedTable({ tableNumber: e.target.value })}
                  className={styles.input}
                />
              </div>

              <div className={styles.formGroup}>
                <label>Max Guests</label>
                <input
                  type="number"
                  min="1"
                  max="20"
                  value={selectedTable.maxGuests}
                  onChange={(e) => updateSelectedTable({ maxGuests: parseInt(e.target.value) || 1 })}
                  className={styles.input}
                />
              </div>

              <div className={styles.formGroup}>
                <label>Shape</label>
                <select
                  value={selectedTable.shape || 'circle'}
                  onChange={(e) => updateSelectedTable({ shape: e.target.value })}
                  className={styles.select}
                >
                  <option value="circle">Circle</option>
                  <option value="square">Square</option>
                  <option value="rectangle">Rectangle</option>
                </select>
              </div>

              <div className={styles.formGroup}>
                <label className={styles.checkboxLabel}>
                  <input
                    type="checkbox"
                    checked={selectedTable.isActive}
                    onChange={(e) => updateSelectedTable({ isActive: e.target.checked })}
                    className={styles.checkbox}
                  />
                  <span>Active</span>
                </label>
              </div>

              <div className={styles.formGroup}>
                <label className={styles.checkboxLabel}>
                  <input
                    type="checkbox"
                    checked={selectedTable.isOutdoor}
                    onChange={(e) => updateSelectedTable({ isOutdoor: e.target.checked })}
                    className={styles.checkbox}
                  />
                  <span>Outdoor</span>
                </label>
              </div>

              <div className={styles.formGroup}>
                <label>Notes (visible to customers)</label>
                <textarea
                  value={selectedTable.notes || ''}
                  onChange={(e) => updateSelectedTable({ notes: e.target.value })}
                  className={styles.textarea}
                  placeholder="e.g., Near window, Quiet corner, etc."
                  rows={3}
                  maxLength={500}
                />
                <small className={styles.charCount}>
                  {(selectedTable.notes || '').length}/500 characters
                </small>
              </div>

              <div className={styles.formGroup}>
                <label>Position X: {selectedTable.positionX.toFixed(1)}px ({((selectedTable.positionX / currentCanvasSize.width) * 100).toFixed(1)}%)</label>
              </div>

              <div className={styles.formGroup}>
                <label>Position Y: {selectedTable.positionY.toFixed(1)}px ({((selectedTable.positionY / currentCanvasSize.height) * 100).toFixed(1)}%)</label>
              </div>

              <div className={styles.formGroup} style={{ marginTop: '1.5rem' }}>
                <button
                  onClick={handleDeleteTable}
                  className={styles.deleteButton}
                >
                  Delete Table
                </button>
              </div>
            </div>
          ) : (
            <div className={styles.noSelection}>
              Click on a table to edit its properties
            </div>
          )}
        </div>
      </div>

      {/* Create Table Modal */}
      {showCreateModal && <CreateTableModal />}
    </div>
  );
}

export default TableLayoutEditorPage;
