import React from 'react';
import type { TableDto } from '@/types/reservation';
import styles from './TableCanvas.module.css';

interface TableCanvasProps {
  canvasRef: React.RefObject<HTMLDivElement | null>;
  tables: TableDto[];
  selectedTable: TableDto | null;
  selectedTableIds: Set<string>;
  draggingTable: string | null;
  draggingEntrance: boolean;
  entrancePosition: { x: number; y: number };
  selectionMode: boolean;
  filters: {
    showIndoor: boolean;
    showOutdoor: boolean;
    showActive: boolean;
    showInactive: boolean;
  };
  canvasSize: { width: number; height: number };
  onTableMouseDown: (e: React.MouseEvent, table: TableDto) => void;
  onTableClick: (table: TableDto) => void;
  onToggleTableSelection: (tableId: string) => void;
  onEntranceMouseDown: (e: React.MouseEvent) => void;
  onMouseMove: (e: React.MouseEvent) => void;
  onMouseUp: () => void;
}

const CANVAS_WIDTH = 600;
const CANVAS_HEIGHT = 500;

export default function TableCanvas({
  canvasRef,
  tables,
  selectedTable,
  selectedTableIds,
  draggingTable,
  draggingEntrance,
  entrancePosition,
  selectionMode,
  filters,
  onTableMouseDown,
  onToggleTableSelection,
  onEntranceMouseDown,
  onMouseMove,
  onMouseUp,
}: TableCanvasProps) {
  const filteredTables = tables.filter(table => {
    if (!filters.showIndoor && !table.isOutdoor) return false;
    if (!filters.showOutdoor && table.isOutdoor) return false;
    if (!filters.showActive && table.isActive) return false;
    if (!filters.showInactive && !table.isActive) return false;
    return true;
  });

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

    const leftPercent = (table.positionX / CANVAS_WIDTH) * 100;
    const topPercent = (table.positionY / CANVAS_HEIGHT) * 100;

    const handleClick = (e: React.MouseEvent) => {
      if (selectionMode) {
        e.stopPropagation();
        onToggleTableSelection(table.id);
      } else {
        onTableMouseDown(e, table);
      }
    };

    return (
      <div
        key={table.id}
        className={`${styles.table} ${isSelected ? styles.selected : ''} ${isInSelectionSet ? styles.inSelection : ''} ${!table.isActive ? styles.inactive : ''}`}
        style={{
          left: `${leftPercent}%`,
          top: `${topPercent}%`,
          ...shapeStyle,
          cursor: draggingTable === table.id ? 'grabbing' : selectionMode ? 'pointer' : 'grab',
        }}
        onMouseDown={handleClick}
      >
        <span className={styles.tableNumber}>{table.tableNumber}</span>
        <span className={styles.guestCount}>👤 {table.maxGuests}</span>
        {table.isOutdoor && <span className={styles.outdoorBadge}>🌤️</span>}
        {!table.isActive && <span className={styles.inactiveBadge}>❌</span>}
        {isInSelectionSet && <span className={styles.checkmark}>✓</span>}
      </div>
    );
  };

  return (
    <div
      ref={canvasRef}
      className={styles.canvas}
      onMouseMove={onMouseMove}
      onMouseUp={onMouseUp}
      onMouseLeave={onMouseUp}
      style={{
        width: '100%',
        paddingBottom: `${(CANVAS_HEIGHT / CANVAS_WIDTH) * 100}%`,
      }}
    >
      <div className={styles.canvasContent}>
        {/* Entrance */}
        <div
          className={`${styles.entrance} ${draggingEntrance ? styles.dragging : ''}`}
          style={{
            left: `${entrancePosition.x}%`,
            top: `${entrancePosition.y}%`,
          }}
          onMouseDown={onEntranceMouseDown}
        >
          🚪 Entrance
        </div>

        {/* Tables */}
        {filteredTables.map(renderTable)}

        {/* Empty state */}
        {filteredTables.length === 0 && (
          <div className={styles.emptyState}>
            <p>No tables match the current filters</p>
            <p className={styles.emptyHint}>Adjust your filters or create a new table</p>
          </div>
        )}
      </div>
    </div>
  );
}
