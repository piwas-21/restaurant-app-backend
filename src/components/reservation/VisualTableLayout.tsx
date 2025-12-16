'use client';

import React, { useState, useEffect } from 'react';
import { useTranslation } from 'react-i18next';
import { useTableHelpers } from '@/hooks/useTableHelpers';
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
  const { t } = useTranslation();
  const { getShapeLabel } = useTableHelpers();
  const [entrancePosition, setEntrancePosition] = useState({ x: 50, y: 10 });
  const [activeTooltipTableId, setActiveTooltipTableId] = useState<string | null>(null);

  useEffect(() => {
    // Load entrance position from localStorage
    const saved = localStorage.getItem('entrancePosition');
    if (saved) {
      try {
        const position = JSON.parse(saved);
        setEntrancePosition(position);
      } catch {
        // Failed to parse, use default position
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

    // Apply rotation if defined
    const rotation = table.rotation || 0;
    const transformStyle = rotation !== 0 ? `rotate(${rotation}deg)` : undefined;

    const handleTableClick = () => {
      // Prevent selection of booked tables
      if (status === 'booked') {
        return;
      }
      // Set this table as the active tooltip
      setActiveTooltipTableId(table.id);
      onSelectTable(table);
    };

  return (
    <div
      key={table.id}
      className={`${styles.table} ${styles[status]} ${shapeClass} ${isLarge ? styles.large : ''} ${status === 'booked' ? styles.disabled : ''}`}
      onClick={handleTableClick}
      style={{
        left: `${leftPercent}%`,
        top: `${topPercent}%`,
        transform: transformStyle,
        cursor: status === 'booked' ? 'not-allowed' : 'pointer',
      }}
    >
      {/* Table content */}
      <div className={styles.tableContent}>
        <span className={styles.tableNumber}>{table.tableNumber}</span>
        <span className={styles.guestCount}>👥 {table.maxGuests}</span>
      </div>
    </div>
    );
  };

  const renderTooltip = () => {
    if (!activeTooltipTableId) return null;

    const table = tables.find(t => t.id === activeTooltipTableId);
    if (!table) return null;

    const shape = table.shape || (table.maxGuests <= 4 ? 'circle' : 'rectangle');
    const leftPercent = ((table.positionX || 0) / CANVAS_WIDTH) * 100;
    const topPercent = ((table.positionY || 0) / CANVAS_HEIGHT) * 100;

    // Position tooltip below if table is in the top 30% of the layout
    const tooltipBelow = topPercent < 30;

    return (
      <div
        className={`${styles.tableTooltipWrapper} ${tooltipBelow ? styles.tooltipBelow : ''}`}
        style={{
          left: `${leftPercent}%`,
          top: `${topPercent}%`,
        }}
      >
        <div className={styles.tableTooltip}>
          <div className={styles.tooltipRow}>
            <span className={styles.tooltipIcon}>👥</span>
            <span>{table.maxGuests} {t('seats', 'seats')}</span>
          </div>
          <div className={styles.tooltipRow}>
            <span className={styles.tooltipIcon}>⬜</span>
            <span>{getShapeLabel(shape)} {t('table', 'table')}</span>
          </div>
          <div className={styles.tooltipRow}>
            <span className={styles.tooltipIcon}>{table.isOutdoor ? '🌳' : '🏠'}</span>
            <span>{table.isOutdoor ? t('outdoor', 'Outdoor') : t('indoor', 'Indoor')}</span>
          </div>
          {table.notes && (
            <div className={styles.tooltipNotes}>
              <span className={styles.tooltipIcon}>💬</span>
              <span>{table.notes}</span>
            </div>
          )}
        </div>
      </div>
    );
  };

  return (
    <div className={styles.container}>
      <div
        className={styles.floorPlan}
        onClick={(e) => {
          // Close tooltip when clicking on the floor plan background
          if (e.target === e.currentTarget) {
            setActiveTooltipTableId(null);
          }
        }}
      >
        {/* Entrance marker */}
        <div
          className={styles.entrance}
          style={{
            left: `${entrancePosition.x}%`,
            top: `${entrancePosition.y}%`,
          }}
          data-label={t('entrance', 'Entrance')}
        >
          🚪
        </div>

        {/* Tables */}
        {tables.map(renderTableShape)}

        {/* Tooltip - rendered separately to ensure proper z-index */}
        {renderTooltip()}
      </div>

      {/* Legend */}
      <div className={styles.legend}>
        <div className={styles.legendItem}>
          <div className={`${styles.legendBox} ${styles.available}`} />
          <span>{t('available', 'Available')}</span>
        </div>
        <div className={styles.legendItem}>
          <div className={`${styles.legendBox} ${styles.booked}`} />
          <span>{t('booked', 'Booked')}</span>
        </div>
        <div className={styles.legendItem}>
          <div className={`${styles.legendBox} ${styles.selected}`} />
          <span>{t('selected', 'Selected')}</span>
        </div>
      </div>
    </div>
  );
}
