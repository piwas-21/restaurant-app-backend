'use client';

import { useState, useEffect, useRef } from 'react';
import { useRouter } from 'next/navigation';
import tableLayoutService from '@/services/tableLayoutService';
import type { TableDto, UpdateTableDto } from '@/types/reservation';
import styles from './styles.module.css';

const CANVAS_WIDTH = 600;
const CANVAS_HEIGHT = 500;

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

  const renderTable = (table: TableDto) => {
    const isSelected = selectedTable?.id === table.id;
    const shape = table.shape || 'circle';
    const isRound = shape === 'circle';
    const isSquare = shape === 'square';

    const shapeStyle: React.CSSProperties = isRound
      ? { borderRadius: '50%', width: '80px', height: '80px' }
      : isSquare
      ? { width: '60px', height: '60px' }
      : { width: '100px', height: '70px' };

    // Convert pixel positions to percentages
    const leftPercent = (table.positionX / CANVAS_WIDTH) * 100;
    const topPercent = (table.positionY / CANVAS_HEIGHT) * 100;

    return (
      <div
        key={table.id}
        className={`${styles.table} ${isSelected ? styles.selected : ''} ${
          table.isOutdoor ? styles.outdoor : ''
        }`}
        style={{
          left: `${leftPercent}%`,
          top: `${topPercent}%`,
          ...shapeStyle,
        }}
        onMouseDown={(e) => handleMouseDown(e, table)}
      >
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

  return (
    <div className={styles.container}>
      {/* Header */}
      <div className={styles.header}>
        <div>
          <h1 className={styles.title}>Table Layout Editor</h1>
          <p className={styles.subtitle}>Drag tables to reposition them on the floor plan</p>
        </div>
        <div className={styles.headerActions}>
          <button
            onClick={handleSaveLayout}
            disabled={saving}
            className={styles.saveButton}
          >
            {saving ? 'Saving...' : 'Save Layout'}
          </button>
          <button onClick={() => router.push('/admin')} className={styles.backButton}>
            Back to Dashboard
          </button>
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
            {tables.map(renderTable)}

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
                <label>Position X: {selectedTable.positionX.toFixed(1)}px ({((selectedTable.positionX / CANVAS_WIDTH) * 100).toFixed(1)}%)</label>
              </div>

              <div className={styles.formGroup}>
                <label>Position Y: {selectedTable.positionY.toFixed(1)}px ({((selectedTable.positionY / CANVAS_HEIGHT) * 100).toFixed(1)}%)</label>
              </div>
            </div>
          ) : (
            <div className={styles.noSelection}>
              Click on a table to edit its properties
            </div>
          )}
        </div>
      </div>
    </div>
  );
}

export default TableLayoutEditorPage;
