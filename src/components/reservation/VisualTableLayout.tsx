'use client';

import React from 'react';
import { TableDto } from '@/types/reservation';
import styles from './VisualTableLayout.module.css';

interface VisualTableLayoutProps {
  tables: TableDto[];
  selectedTableId?: string;
  onSelectTable: (table: TableDto) => void;
  bookedTableIds?: string[];
}

export default function VisualTableLayout({
  tables,
  selectedTableId,
  onSelectTable,
  bookedTableIds = []
}: VisualTableLayoutProps) {

  const getTableStatus = (table: TableDto): 'available' | 'booked' | 'selected' => {
    if (selectedTableId === table.id) return 'selected';
    if (bookedTableIds.includes(table.id)) return 'booked';
    return 'available';
  };

  const renderTableShape = (table: TableDto) => {
    const status = getTableStatus(table);
    const isClickable = status === 'available' || status === 'selected';

    // Determine table shape based on capacity
    const isRound = table.maxGuests <= 4;
    const isLarge = table.maxGuests > 6;

    return (
      <div
        key={table.id}
        className={`${styles.table} ${styles[status]} ${isRound ? styles.round : styles.rectangular} ${isLarge ? styles.large : ''} ${!isClickable ? styles.disabled : ''}`}
        onClick={() => isClickable && onSelectTable(table)}
        style={{
          left: `${table.positionX || 0}%`,
          top: `${table.positionY || 0}%`,
        }}
      >
        {/* Chairs around table */}
        {renderChairs(table, isRound)}

        {/* Table content */}
        <div className={styles.tableContent}>
          <span className={styles.tableLabel}>Table {table.tableNumber}</span>
        </div>
      </div>
    );
  };

  const renderChairs = (table: TableDto, isRound: boolean) => {
    const chairCount = Math.min(table.maxGuests, isRound ? 4 : 8);
    const chairs = [];

    if (isRound) {
      // For round tables, place chairs around
      const positions = ['top', 'right', 'bottom', 'left'];
      for (let i = 0; i < chairCount; i++) {
        chairs.push(
          <div key={i} className={`${styles.chair} ${styles[positions[i]]}`} />
        );
      }
    } else {
      // For rectangular tables, place chairs on sides
      const chairsPerSide = Math.ceil(chairCount / 2);
      for (let i = 0; i < chairsPerSide; i++) {
        chairs.push(
          <div key={`top-${i}`} className={`${styles.chair} ${styles.top}`} style={{ left: `${20 + (i * 60/chairsPerSide)}%` }} />
        );
      }
      for (let i = 0; i < Math.floor(chairCount / 2); i++) {
        chairs.push(
          <div key={`bottom-${i}`} className={`${styles.chair} ${styles.bottom}`} style={{ left: `${20 + (i * 60/Math.floor(chairCount / 2))}%` }} />
        );
      }
    }

    return chairs;
  };

  // Normalize pixel positions to percentages
  // Backend stores positions in pixels assuming 800x600 canvas
  const normalizePosition = (pixelValue: number, maxPixels: number): number => {
    // Convert pixel value to percentage (0-100)
    return (pixelValue / maxPixels) * 100;
  };

  // Default layout if no positions are set
  const getDefaultPosition = (index: number, total: number): { x: number; y: number } => {
    const cols = Math.ceil(Math.sqrt(total));
    const row = Math.floor(index / cols);
    const col = index % cols;

    return {
      x: 10 + (col * 80 / cols),
      y: 10 + (row * 80 / Math.ceil(total / cols))
    };
  };

  // Apply default positions if tables don't have positions, or normalize pixel positions
  const tablesWithPositions = tables.map((table, index) => {
    if (table.positionX === undefined || table.positionY === undefined) {
      const defaultPos = getDefaultPosition(index, tables.length);
      return {
        ...table,
        positionX: defaultPos.x,
        positionY: defaultPos.y
      };
    }

    // Normalize positions: backend uses pixels (max ~800x600), convert to percentages
    const normalizedX = table.positionX > 100 ? normalizePosition(table.positionX, 800) : table.positionX;
    const normalizedY = table.positionY > 100 ? normalizePosition(table.positionY, 600) : table.positionY;

    return {
      ...table,
      positionX: normalizedX,
      positionY: normalizedY
    };
  });

  return (
    <div className={styles.container}>
      <div className={styles.floorPlan}>
        {/* Entrance marker */}
        <div className={styles.entrance}>
          <div className={styles.entranceDoor} />
          <span className={styles.entranceLabel}>Way in</span>
        </div>

        {/* Tables */}
        {tablesWithPositions.map(renderTableShape)}
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
