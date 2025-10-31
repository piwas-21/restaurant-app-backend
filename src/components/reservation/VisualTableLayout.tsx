'use client';

import React, { useState, useEffect } from 'react';
import { TableDto } from '@/types/reservation';
import styles from './VisualTableLayout.module.css';

// Canvas dimensions used for position conversion (must match backend seeder)
const CANVAS_WIDTH = 600;
const CANVAS_HEIGHT = 500;

interface VisualTableLayoutProps {
  tables: TableDto[];
  selectedTableIds?: string[];
  onSelectTable: (table: TableDto) => void;
  bookedTableIds?: string[];
}

export default function VisualTableLayout({
  tables,
  selectedTableIds = [],
  onSelectTable,
  bookedTableIds = []
}: VisualTableLayoutProps) {
  const [entrancePosition, setEntrancePosition] = useState({ x: 50, y: 10 });
  const [hoveredTable, setHoveredTable] = useState<TableDto | null>(null);

  useEffect(() => {
    // Load entrance position from localStorage
    const saved = localStorage.getItem('entrancePosition');
    if (saved) {
      try {
        const position = JSON.parse(saved);
        setEntrancePosition(position);
      } catch (e) {
        console.error('Failed to load entrance position:', e);
      }
    }
  }, []);

  const getTableStatus = (table: TableDto): 'available' | 'booked' | 'selected' => {
    if (selectedTableIds.includes(table.id)) return 'selected';
    if (bookedTableIds.includes(table.id)) return 'booked';
    return 'available';
  };

  const renderTableShape = (table: TableDto) => {
    const status = getTableStatus(table);
    const isClickable = status === 'available' || status === 'selected';

    // Use shape from backend, fallback to capacity-based logic
    const shape = table.shape || (table.maxGuests <= 4 ? 'circle' : 'rectangle');
    const isRound = shape === 'circle';
    const isSquare = shape === 'square';
    const isLarge = table.maxGuests > 6;

    // Apply shape styles
    let shapeClass = styles.rectangular; // default
    if (isRound) shapeClass = styles.round;
    if (isSquare) shapeClass = styles.square;

    // Convert pixel positions to percentages
    const leftPercent = ((table.positionX || 0) / CANVAS_WIDTH) * 100;
    const topPercent = ((table.positionY || 0) / CANVAS_HEIGHT) * 100;

    const showTooltip = hoveredTable?.id === table.id || status === 'selected';

    return (
      <div
        key={table.id}
        className={`${styles.table} ${styles[status]} ${shapeClass} ${isLarge ? styles.large : ''} ${!isClickable ? styles.disabled : ''}`}
        onClick={() => isClickable && onSelectTable(table)}
        onMouseEnter={() => setHoveredTable(table)}
        onMouseLeave={() => setHoveredTable(null)}
        style={{
          left: `${leftPercent}%`,
          top: `${topPercent}%`,
        }}
      >
        {/* Table content */}
        <div className={styles.tableContent}>
          <span className={styles.tableLabel}>Table {table.tableNumber}</span>
        </div>

        {/* Tooltip on hover or selection */}
        {showTooltip && (
          <div className={styles.tableTooltip}>
            <div className={styles.tooltipRow}>
              <span className={styles.tooltipIcon}>👥</span>
              <span>{table.maxGuests} seats</span>
            </div>
            <div className={styles.tooltipRow}>
              <span className={styles.tooltipIcon}>⬜</span>
              <span>{shape.charAt(0).toUpperCase() + shape.slice(1)} table</span>
            </div>
            <div className={styles.tooltipRow}>
              <span className={styles.tooltipIcon}>{table.isOutdoor ? '🌳' : '🏠'}</span>
              <span>{table.isOutdoor ? 'Outdoor' : 'Indoor'}</span>
            </div>
            {table.notes && (
              <div className={styles.tooltipNotes}>
                <span className={styles.tooltipIcon}>💬</span>
                <span>{table.notes}</span>
              </div>
            )}
          </div>
        )}
      </div>
    );
  };

  return (
    <div className={styles.container}>
      <div className={styles.floorPlan}>
        {/* Entrance marker */}
        <div
          className={styles.entrance}
          style={{
            left: `${entrancePosition.x}%`,
            top: `${entrancePosition.y}%`,
          }}
        >
          <div className={styles.entranceDoor} />
          <span className={styles.entranceLabel}>Way in</span>
        </div>

        {/* Tables */}
        {tables.map(renderTableShape)}
      </div>

      {/* Legend */}
      <div className={styles.legend}>
        <div className={styles.legendItem}>
          <div className={`${styles.legendBox} ${styles.available}`} />
          <span>Available</span>
        </div>
        <div className={styles.legendItem}>
          <div className={`${styles.legendBox} ${styles.booked}`} />
          <span>Booked</span>
        </div>
        <div className={styles.legendItem}>
          <div className={`${styles.legendBox} ${styles.selected}`} />
          <span>Selected</span>
        </div>
      </div>
    </div>
  );
}
