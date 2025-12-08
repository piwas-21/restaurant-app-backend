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
  onTableMouseDown: (e: React.MouseEvent, table: TableDto) => void;
  onToggleTableSelection: (tableId: string) => void;
  onEntranceMouseDown: (e: React.MouseEvent) => void;
  onMouseMove: (e: React.MouseEvent) => void;
  onMouseUp: () => void;
  onRotationStart?: (tableId: string, e: React.MouseEvent) => void;
  onRotationMove?: (e: React.MouseEvent) => void;
  onRotationEnd?: () => void;
  rotatingTable?: string | null;
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
  onRotationStart,
  onRotationMove,
  onRotationEnd,
  rotatingTable,
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
    const isRotating = rotatingTable === table.id;
    const shape = table.shape || 'circle';
    const isRound = shape === 'circle';
    const isSquare = shape === 'square';

    const shapeStyle: React.CSSProperties = isRound
      ? { borderRadius: '50%', width: '8%', height: '10%' }
      : isSquare
      ? { width: '8%', height: '8%' }
      : { width: '12%', height: '14%' };

    const leftPercent = (table.positionX / CANVAS_WIDTH) * 100;
    const topPercent = (table.positionY / CANVAS_HEIGHT) * 100;

    // Apply rotation if defined
    const rotation = table.rotation || 0;
    const transformStyle = rotation !== 0 ? `rotate(${rotation}deg)` : undefined;

    const handleClick = (e: React.MouseEvent) => {
      if (selectionMode) {
        e.stopPropagation();
        onToggleTableSelection(table.id);
      } else {
        onTableMouseDown(e, table);
      }
    };

    const handleRotationHandleMouseDown = (e: React.MouseEvent) => {
      e.stopPropagation();
      if (onRotationStart) {
        onRotationStart(table.id, e);
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
          transform: transformStyle,
          cursor: draggingTable === table.id ? 'grabbing' : selectionMode ? 'pointer' : 'grab',
        }}
        onMouseDown={handleClick}
      >
        <span className={styles.tableNumber}>{table.tableNumber}</span>
        <span className={styles.guestCount}>👤 {table.maxGuests}</span>
        {table.isOutdoor && <span className={styles.outdoorBadge}>🌤️</span>}
        {!table.isActive && <span className={styles.inactiveBadge}>❌</span>}
        {isInSelectionSet && <span className={styles.checkmark}>✓</span>}

        {/* Rotation Handle - only show for non-round tables when selected */}
        {isSelected && !isRound && (
          <div
            className={`${styles.rotationHandle} ${isRotating ? styles.rotating : ''}`}
            onMouseDown={handleRotationHandleMouseDown}
            title="Drag to rotate"
          >
            ↻
          </div>
        )}
      </div>
    );
  };

  return (
    <div
      ref={canvasRef}
      className={styles.canvas}
      onMouseMove={(e) => {
        if (rotatingTable && onRotationMove) {
          onRotationMove(e);
        } else {
          onMouseMove(e);
        }
      }}
      onMouseUp={() => {
        if (rotatingTable && onRotationEnd) {
          onRotationEnd();
        } else {
          onMouseUp();
        }
      }}
      onMouseLeave={() => {
        if (rotatingTable && onRotationEnd) {
          onRotationEnd();
        } else {
          onMouseUp();
        }
      }}
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
          🚪
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
