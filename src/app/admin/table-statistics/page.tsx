'use client';

import { useState, useEffect } from 'react';
import { useRouter } from 'next/navigation';
import { useTranslation } from 'react-i18next';
import { useTableHelpers } from '@/hooks/useTableHelpers';
import tableLayoutService from '@/services/tableLayoutService';
import { reservationService } from '@/services/reservationService';
import type { TableDto, ReservationDto } from '@/types/reservation';
import { ReservationStatus } from '@/types/reservation';
import { AdminAuthGuard } from '@/components/admin/AdminAuthGuard';
import styles from './styles.module.css';

interface TableReservationStats {
  tableId: string;
  tableNumber: string;
  totalReservations: number;
  confirmedReservations: number;
  completedReservations: number;
  cancelledReservations: number;
  noShowReservations: number;
  averagePartySize: number;
}

function TableStatisticsPage() {
  const router = useRouter();
  const { t } = useTranslation();
  const { getShapeLabel } = useTableHelpers();
  const [tables, setTables] = useState<TableDto[]>([]);
  const [reservations, setReservations] = useState<ReservationDto[]>([]);
  const [loading, setLoading] = useState(true);

  useEffect(() => {
    loadData();
  }, []);

  const loadData = async () => {
    try {
      setLoading(true);
      const [tablesData, reservationsData] = await Promise.all([
        tableLayoutService.getAllTables(),
        reservationService.getReservations({ pageSize: 1000 }), // Fetch large batch
      ]);
      setTables(tablesData);
      setReservations(reservationsData.items || []);
    } catch (error) {
      console.error('Failed to load data:', error);
    } finally {
      setLoading(false);
    }
  };

  if (loading) {
    return (
      <div className={styles.container}>
        <div className={styles.loading}>{t('loading_statistics', 'Loading statistics...')}</div>
      </div>
    );
  }

  // Calculate statistics
  const totalTables = tables.length;
  const activeTables = tables.filter((t) => t.isActive).length;
  const inactiveTables = tables.filter((t) => !t.isActive).length;
  const indoorTables = tables.filter((t) => !t.isOutdoor).length;
  const outdoorTables = tables.filter((t) => t.isOutdoor).length;
  const totalCapacity = tables.reduce((sum, t) => sum + t.maxGuests, 0);
  const activeCapacity = tables.filter((t) => t.isActive).reduce((sum, t) => sum + t.maxGuests, 0);
  const _averageCapacity = totalTables > 0 ? Math.round(totalCapacity / totalTables) : 0;

  // Table shapes breakdown
  const circularTables = tables.filter((t) => t.shape === 'circle').length;
  const squareTables = tables.filter((t) => t.shape === 'square').length;
  const rectangularTables = tables.filter((t) => t.shape === 'rectangle').length;

  // Capacity distribution
  const smallTables = tables.filter((t) => t.maxGuests <= 2).length;
  const mediumTables = tables.filter((t) => t.maxGuests > 2 && t.maxGuests <= 4).length;
  const largeTables = tables.filter((t) => t.maxGuests > 4 && t.maxGuests <= 6).length;
  const extraLargeTables = tables.filter((t) => t.maxGuests > 6).length;

  // Calculate percentages for visual bars
  const activePercentage = totalTables > 0 ? (activeTables / totalTables) * 100 : 0;
  const indoorPercentage = totalTables > 0 ? (indoorTables / totalTables) * 100 : 0;
  const outdoorPercentage = totalTables > 0 ? (outdoorTables / totalTables) * 100 : 0;

  // Calculate reservation statistics
  const totalReservations = reservations.length;
  const confirmedReservations = reservations.filter((r) => r.status === ReservationStatus.Confirmed).length;
  const completedReservations = reservations.filter((r) => r.status === ReservationStatus.Completed).length;
  const cancelledReservations = reservations.filter((r) => r.status === ReservationStatus.Cancelled).length;
  const noShowReservations = reservations.filter((r) => r.status === ReservationStatus.NoShow).length;

  // Calculate per-table reservation statistics
  const tableReservationStats: TableReservationStats[] = tables.map((table) => {
    const tableReservations = reservations.filter((r) => r.tableId === table.id);
    const totalTableReservations = tableReservations.length;

    return {
      tableId: table.id,
      tableNumber: table.tableNumber,
      totalReservations: totalTableReservations,
      confirmedReservations: tableReservations.filter((r) => r.status === ReservationStatus.Confirmed).length,
      completedReservations: tableReservations.filter((r) => r.status === ReservationStatus.Completed).length,
      cancelledReservations: tableReservations.filter((r) => r.status === ReservationStatus.Cancelled).length,
      noShowReservations: tableReservations.filter((r) => r.status === ReservationStatus.NoShow).length,
      averagePartySize:
        totalTableReservations > 0
          ? Math.round(tableReservations.reduce((sum, r) => sum + r.numberOfGuests, 0) / totalTableReservations)
          : 0,
    };
  });

  // Find most and least popular tables
  const sortedByReservations = [...tableReservationStats].sort((a, b) => b.totalReservations - a.totalReservations);
  const mostPopularTables = sortedByReservations.slice(0, 5);
  const _leastPopularTables = sortedByReservations
    .filter((t) => t.totalReservations > 0)
    .slice(-5)
    .reverse();

  return (
    <AdminAuthGuard>
      <div className={styles.container}>
        {/* Header */}
        <div className={styles.header}>
          <div>
            <h1 className={styles.title}>{t('table_statistics', 'Table Statistics')}</h1>
            <p className={styles.subtitle}>
              {t('table_statistics_desc', 'Overview of your restaurant table inventory and capacity')}
            </p>
          </div>
          <div className={styles.headerActions}>
            <button onClick={() => router.push('/admin/table-layout-editor')} className={styles.layoutButton}>
              {t('manage_layout', 'Manage Layout')}
            </button>
          </div>
        </div>

        {/* Key Metrics */}
        <div className={styles.metricsGrid}>
          <div className={styles.metricCard}>
            <div className={styles.metricIcon}>🪑</div>
            <div className={styles.metricContent}>
              <div className={styles.metricValue}>{totalTables}</div>
              <div className={styles.metricLabel}>{t('total_tables', 'Total Tables')}</div>
            </div>
          </div>

          <div className={styles.metricCard}>
            <div className={styles.metricIcon}>✅</div>
            <div className={styles.metricContent}>
              <div className={styles.metricValue}>{activeTables}</div>
              <div className={styles.metricLabel}>{t('active_tables', 'Active Tables')}</div>
              <div className={styles.metricSubtext}>
                {t('of_total', '{{percent}}% of total', { percent: activePercentage.toFixed(1) })}
              </div>
            </div>
          </div>

          <div className={styles.metricCard}>
            <div className={styles.metricIcon}>👥</div>
            <div className={styles.metricContent}>
              <div className={styles.metricValue}>{activeCapacity}</div>
              <div className={styles.metricLabel}>{t('active_seating_capacity', 'Active Seating Capacity')}</div>
              <div className={styles.metricSubtext}>
                {t('total_seats', 'Total: {{count}} seats', { count: totalCapacity })}
              </div>
            </div>
          </div>

          <div className={styles.metricCard}>
            <div className={styles.metricIcon}>📅</div>
            <div className={styles.metricContent}>
              <div className={styles.metricValue}>{totalReservations}</div>
              <div className={styles.metricLabel}>{t('total_reservations', 'Total Reservations')}</div>
              <div className={styles.metricSubtext}>
                {t('completed_count', '{{count}} completed', { count: completedReservations })}
              </div>
            </div>
          </div>
        </div>

        {/* Charts Section */}
        <div className={styles.chartsGrid}>
          {/* Status Breakdown */}
          <div className={styles.chartCard}>
            <h3 className={styles.chartTitle}>{t('table_status', 'Table Status')}</h3>
            <div className={styles.chartContent}>
              <div className={styles.statusBreakdown}>
                <div className={styles.statusItem}>
                  <div className={styles.statusHeader}>
                    <span className={styles.statusLabel}>{t('active_tables', 'Active Tables')}</span>
                    <span className={styles.statusValue}>{activeTables}</span>
                  </div>
                  <div className={styles.progressBar}>
                    <div
                      className={styles.progressFill}
                      style={{ width: `${activePercentage}%`, backgroundColor: '#4caf50' }}
                    />
                  </div>
                  <div className={styles.statusPercentage}>{activePercentage.toFixed(1)}%</div>
                </div>

                <div className={styles.statusItem}>
                  <div className={styles.statusHeader}>
                    <span className={styles.statusLabel}>{t('inactive_tables', 'Inactive Tables')}</span>
                    <span className={styles.statusValue}>{inactiveTables}</span>
                  </div>
                  <div className={styles.progressBar}>
                    <div
                      className={styles.progressFill}
                      style={{ width: `${100 - activePercentage}%`, backgroundColor: '#ff9800' }}
                    />
                  </div>
                  <div className={styles.statusPercentage}>{(100 - activePercentage).toFixed(1)}%</div>
                </div>
              </div>
            </div>
          </div>

          {/* Location Breakdown */}
          <div className={styles.chartCard}>
            <h3 className={styles.chartTitle}>{t('table_location', 'Table Location')}</h3>
            <div className={styles.chartContent}>
              <div className={styles.statusBreakdown}>
                <div className={styles.statusItem}>
                  <div className={styles.statusHeader}>
                    <span className={styles.statusLabel}>{t('indoor_tables', 'Indoor Tables')}</span>
                    <span className={styles.statusValue}>{indoorTables}</span>
                  </div>
                  <div className={styles.progressBar}>
                    <div
                      className={styles.progressFill}
                      style={{ width: `${indoorPercentage}%`, backgroundColor: '#2196f3' }}
                    />
                  </div>
                  <div className={styles.statusPercentage}>{indoorPercentage.toFixed(1)}%</div>
                </div>

                <div className={styles.statusItem}>
                  <div className={styles.statusHeader}>
                    <span className={styles.statusLabel}>{t('outdoor_tables', 'Outdoor Tables')}</span>
                    <span className={styles.statusValue}>{outdoorTables}</span>
                  </div>
                  <div className={styles.progressBar}>
                    <div
                      className={styles.progressFill}
                      style={{ width: `${outdoorPercentage}%`, backgroundColor: '#8bc34a' }}
                    />
                  </div>
                  <div className={styles.statusPercentage}>{outdoorPercentage.toFixed(1)}%</div>
                </div>
              </div>
            </div>
          </div>
        </div>

        {/* Detailed Breakdowns */}
        <div className={styles.detailsGrid}>
          {/* Shape Distribution */}
          <div className={styles.detailCard}>
            <h3 className={styles.detailTitle}>{t('table_shapes', 'Table Shapes')}</h3>
            <div className={styles.detailList}>
              <div className={styles.detailItem}>
                <div className={styles.detailLabel}>
                  <span className={styles.shapeIcon}>⭕</span>
                  {t('circular', 'Circular')}
                </div>
                <div className={styles.detailValue}>{circularTables}</div>
              </div>
              <div className={styles.detailItem}>
                <div className={styles.detailLabel}>
                  <span className={styles.shapeIcon}>⬜</span>
                  {t('square', 'Square')}
                </div>
                <div className={styles.detailValue}>{squareTables}</div>
              </div>
              <div className={styles.detailItem}>
                <div className={styles.detailLabel}>
                  <span className={styles.shapeIcon}>▭</span>
                  {t('rectangular', 'Rectangular')}
                </div>
                <div className={styles.detailValue}>{rectangularTables}</div>
              </div>
            </div>
          </div>

          {/* Capacity Distribution */}
          <div className={styles.detailCard}>
            <h3 className={styles.detailTitle}>{t('capacity_distribution', 'Capacity Distribution')}</h3>
            <div className={styles.detailList}>
              <div className={styles.detailItem}>
                <div className={styles.detailLabel}>{t('small_tables', 'Small (1-2 seats)')}</div>
                <div className={styles.detailValue}>{smallTables}</div>
              </div>
              <div className={styles.detailItem}>
                <div className={styles.detailLabel}>{t('medium_tables', 'Medium (3-4 seats)')}</div>
                <div className={styles.detailValue}>{mediumTables}</div>
              </div>
              <div className={styles.detailItem}>
                <div className={styles.detailLabel}>{t('large_tables', 'Large (5-6 seats)')}</div>
                <div className={styles.detailValue}>{largeTables}</div>
              </div>
              <div className={styles.detailItem}>
                <div className={styles.detailLabel}>{t('extra_large_tables', 'Extra Large (7+ seats)')}</div>
                <div className={styles.detailValue}>{extraLargeTables}</div>
              </div>
            </div>
          </div>

          {/* Reservation Stats */}
          <div className={styles.detailCard}>
            <h3 className={styles.detailTitle}>{t('reservation_status', 'Reservation Status')}</h3>
            <div className={styles.detailList}>
              <div className={styles.detailItem}>
                <div className={styles.detailLabel}>
                  <span className={styles.statusDot} style={{ backgroundColor: '#10b981' }}></span>
                  {t('confirmed', 'Confirmed')}
                </div>
                <div className={styles.detailValue}>{confirmedReservations}</div>
              </div>
              <div className={styles.detailItem}>
                <div className={styles.detailLabel}>
                  <span className={styles.statusDot} style={{ backgroundColor: '#6b7280' }}></span>
                  {t('completed', 'Completed')}
                </div>
                <div className={styles.detailValue}>{completedReservations}</div>
              </div>
              <div className={styles.detailItem}>
                <div className={styles.detailLabel}>
                  <span className={styles.statusDot} style={{ backgroundColor: '#ef4444' }}></span>
                  {t('cancelled', 'Cancelled')}
                </div>
                <div className={styles.detailValue}>{cancelledReservations}</div>
              </div>
              <div className={styles.detailItem}>
                <div className={styles.detailLabel}>
                  <span className={styles.statusDot} style={{ backgroundColor: '#ef4444' }}></span>
                  {t('no_show', 'No Show')}
                </div>
                <div className={styles.detailValue}>{noShowReservations}</div>
              </div>
            </div>
          </div>
        </div>

        {/* Most Popular Tables */}
        {mostPopularTables.length > 0 && (
          <div className={styles.popularTablesSection}>
            <h3 className={styles.sectionTitle}>{t('most_popular_tables', 'Most Popular Tables')}</h3>
            <div className={styles.popularTablesGrid}>
              {mostPopularTables.map((tableStat, index) => (
                <div key={tableStat.tableId} className={styles.popularTableCard}>
                  <div className={styles.rankBadge}>#{index + 1}</div>
                  <div className={styles.popularTableContent}>
                    <div className={styles.popularTableNumber}>{tableStat.tableNumber}</div>
                    <div className={styles.popularTableStats}>
                      <div className={styles.popularStat}>
                        <span className={styles.popularStatValue}>{tableStat.totalReservations}</span>
                        <span className={styles.popularStatLabel}>{t('reservations', 'reservations')}</span>
                      </div>
                      <div className={styles.popularStat}>
                        <span className={styles.popularStatValue}>{tableStat.averagePartySize}</span>
                        <span className={styles.popularStatLabel}>{t('avg_party_size', 'avg party size')}</span>
                      </div>
                    </div>
                    <div className={styles.reservationBreakdown}>
                      <span className={styles.breakdownItem} style={{ color: '#10b981' }}>
                        ✓ {tableStat.completedReservations}
                      </span>
                      <span className={styles.breakdownItem} style={{ color: '#ef4444' }}>
                        ✗ {tableStat.cancelledReservations}
                      </span>
                    </div>
                  </div>
                </div>
              ))}
            </div>
          </div>
        )}

        {/* Tables List */}
        <div className={styles.tablesSection}>
          <h3 className={styles.sectionTitle}>{t('all_tables_with_stats', 'All Tables with Reservation Stats')}</h3>
          <div className={styles.tableGrid}>
            {tables.map((table) => {
              const stats = tableReservationStats.find((s) => s.tableId === table.id);
              return (
                <div key={table.id} className={`${styles.tableCard} ${!table.isActive ? styles.inactive : ''}`}>
                  <div className={styles.tableCardHeader}>
                    <span className={styles.tableNumber}>{table.tableNumber}</span>
                    <span className={`${styles.tableBadge} ${table.isActive ? styles.active : styles.inactiveStatus}`}>
                      {table.isActive ? t('active', 'Active') : t('inactive', 'Inactive')}
                    </span>
                  </div>
                  <div className={styles.tableCardDetails}>
                    <div className={styles.tableDetail}>
                      <span className={styles.tableDetailLabel}>{t('capacity', 'Capacity')}:</span>
                      <span className={styles.tableDetailValue}>
                        {t('seats_count', '{{count}} seats', { count: table.maxGuests })}
                      </span>
                    </div>
                    <div className={styles.tableDetail}>
                      <span className={styles.tableDetailLabel}>{t('shape', 'Shape')}:</span>
                      <span className={styles.tableDetailValue}>{getShapeLabel(table.shape)}</span>
                    </div>
                    <div className={styles.tableDetail}>
                      <span className={styles.tableDetailLabel}>{t('location', 'Location')}:</span>
                      <span className={styles.tableDetailValue}>
                        {table.isOutdoor ? t('outdoor', 'Outdoor') : t('indoor', 'Indoor')}
                      </span>
                    </div>
                  </div>
                  {stats && stats.totalReservations > 0 && (
                    <div className={styles.tableReservationStats}>
                      <div className={styles.reservationStatsHeader}>📊 {t('reservations', 'Reservations')}</div>
                      <div className={styles.reservationStatsGrid}>
                        <div className={styles.reservationStatItem}>
                          <span className={styles.reservationStatValue}>{stats.totalReservations}</span>
                          <span className={styles.reservationStatLabel}>{t('total', 'Total')}</span>
                        </div>
                        <div className={styles.reservationStatItem}>
                          <span className={styles.reservationStatValue}>{stats.completedReservations}</span>
                          <span className={styles.reservationStatLabel}>{t('completed', 'Completed')}</span>
                        </div>
                        <div className={styles.reservationStatItem}>
                          <span className={styles.reservationStatValue}>{stats.averagePartySize}</span>
                          <span className={styles.reservationStatLabel}>{t('avg_party', 'Avg Party')}</span>
                        </div>
                      </div>
                    </div>
                  )}
                  {table.notes && (
                    <div className={styles.tableNotes}>
                      <span className={styles.notesIcon}>📝</span>
                      {table.notes}
                    </div>
                  )}
                </div>
              );
            })}
          </div>
        </div>
      </div>
    </AdminAuthGuard>
  );
}

export default TableStatisticsPage;
