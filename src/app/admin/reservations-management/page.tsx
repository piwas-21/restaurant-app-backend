"use client";

import React, { useState, useEffect } from 'react';
import { useRouter } from 'next/navigation';
import { useTranslation } from 'react-i18next';
import { useAuth } from '@/components/AuthContext';
import { reservationService } from '@/services/reservationService';
import { ReservationDto, ReservationStatus, TableDto } from '@/types/reservation';
import { useSnackbar } from 'notistack';
import {
  Calendar,
  RefreshCw,
  Loader2,
  AlertCircle,
  Check,
  X,
  Clock,
  Users,
  MapPin,
  Search,
  Filter,
  List,
  CalendarDays,
  Download,
  FileText,
  CheckSquare,
} from 'lucide-react';
import ReservationCalendar from '@/components/admin/reservations/ReservationCalendar';
import { exportReservationsToCSV, exportReservationsToPDF } from '@/utils/reservationExportUtils';
import ConfirmationModal from '@/components/common/ConfirmationModal';
import ResultModal from '@/components/common/ResultModal';
import styles from './styles.module.css';

type ViewMode = 'list' | 'calendar';

export default function AdminReservationsManagementPage() {
  const { t } = useTranslation();
  const router = useRouter();
  const { user, isLoading: authLoading } = useAuth();
  const { enqueueSnackbar } = useSnackbar();

  const [reservations, setReservations] = useState<ReservationDto[]>([]);
  const [tables, setTables] = useState<TableDto[]>([]);
  const [isLoading, setIsLoading] = useState(true);
  const [error, setError] = useState('');
  const [searchQuery, setSearchQuery] = useState('');
  const [selectedStatus, setSelectedStatus] = useState<ReservationStatus | 'All'>('All');
  const [selectedDate, setSelectedDate] = useState<string>('');
  const [selectedTableId, setSelectedTableId] = useState<string>('All');
  const [viewMode, setViewMode] = useState<ViewMode>('list');
  const [selectedReservation, setSelectedReservation] = useState<ReservationDto | null>(null);
  const [selectedReservationIds, setSelectedReservationIds] = useState<Set<string>>(new Set());

  // Modal states
  const [confirmModal, setConfirmModal] = useState<{ isOpen: boolean; message: string; onConfirm: () => void }>({
    isOpen: false,
    message: '',
    onConfirm: () => {}
  });
  const [resultModal, setResultModal] = useState<{ isOpen: boolean; message: string; isSuccess: boolean }>({
    isOpen: false,
    message: '',
    isSuccess: false
  });

  // Auth check
  useEffect(() => {
    if (authLoading) return;

    if (!user) {
      router.push('/');
      return;
    }

    if (user.role !== 'Admin' && user.role !== 'Staff') {
      router.push('/');
      enqueueSnackbar(t('access_denied', 'Access denied. Admin privileges required.'), {
        variant: 'error',
        anchorOrigin: { vertical: 'bottom', horizontal: 'right' },
      });
      return;
    }

    fetchData();
  }, [user, authLoading, selectedStatus, selectedDate, selectedTableId]);

  const fetchData = async () => {
    try {
      setIsLoading(true);
      setError('');

      // Fetch reservations
      const reservationsParams: any = {};
      if (selectedStatus !== 'All') reservationsParams.status = selectedStatus;
      if (selectedDate) reservationsParams.date = selectedDate;
      if (selectedTableId !== 'All') reservationsParams.tableId = selectedTableId;

      const reservationsResult = await reservationService.getReservations(reservationsParams);
      setReservations(reservationsResult.items);

      // Fetch tables for filter
      const tablesResult = await reservationService.getTables();
      setTables(tablesResult);
    } catch (err: any) {
      console.error('Error fetching data:', err);
      setError(t('failed_to_load_reservations', 'Failed to load reservations'));
      enqueueSnackbar(t('failed_to_load_reservations', 'Failed to load reservations'), {
        variant: 'error',
        anchorOrigin: { vertical: 'bottom', horizontal: 'right' },
      });
    } finally {
      setIsLoading(false);
    }
  };

  const handleConfirm = async (id: string) => {
    try {
      await reservationService.confirmReservation(id);
      enqueueSnackbar(t('reservation_confirmed', 'Reservation confirmed successfully'), {
        variant: 'success',
        anchorOrigin: { vertical: 'bottom', horizontal: 'right' },
      });
      fetchData();
    } catch (err: any) {
      enqueueSnackbar(err.message || t('failed_to_confirm', 'Failed to confirm reservation'), {
        variant: 'error',
        anchorOrigin: { vertical: 'bottom', horizontal: 'right' },
      });
    }
  };

  const handleCancel = async (id: string) => {
    setConfirmModal({
      isOpen: true,
      message: t('confirm_cancel_reservation', 'Are you sure you want to cancel this reservation?'),
      onConfirm: async () => {
        setConfirmModal({ isOpen: false, message: '', onConfirm: () => {} });

        try {
          await reservationService.cancelReservation(id);
          setResultModal({
            isOpen: true,
            message: t('reservation_cancelled', 'Reservation cancelled successfully'),
            isSuccess: true
          });
          fetchData();
        } catch (err: any) {
          setResultModal({
            isOpen: true,
            message: err.message || t('failed_to_cancel', 'Failed to cancel reservation'),
            isSuccess: false
          });
        }
      }
    });
  };

  // Export handlers
  const handleExportCSV = () => {
    const dataToExport = selectedReservationIds.size > 0
      ? filteredReservations.filter(r => selectedReservationIds.has(r.id))
      : filteredReservations;

    setConfirmModal({
      isOpen: true,
      message: t('confirm_export_csv', `Export ${dataToExport.length} reservations to CSV?`),
      onConfirm: () => {
        try {
          exportReservationsToCSV(dataToExport);
          setConfirmModal({ isOpen: false, message: '', onConfirm: () => {} });
          setResultModal({
            isOpen: true,
            message: t('exported_successfully', `Successfully exported ${dataToExport.length} reservations to CSV`),
            isSuccess: true
          });
        } catch (err: any) {
          setConfirmModal({ isOpen: false, message: '', onConfirm: () => {} });
          setResultModal({
            isOpen: true,
            message: t('export_failed', `Failed to export: ${err.message}`),
            isSuccess: false
          });
        }
      }
    });
  };

  const handleExportPDF = () => {
    const dataToExport = selectedReservationIds.size > 0
      ? filteredReservations.filter(r => selectedReservationIds.has(r.id))
      : filteredReservations;

    setConfirmModal({
      isOpen: true,
      message: t('confirm_export_pdf', `Export ${dataToExport.length} reservations to PDF?`),
      onConfirm: () => {
        try {
          exportReservationsToPDF(dataToExport);
          setConfirmModal({ isOpen: false, message: '', onConfirm: () => {} });
          setResultModal({
            isOpen: true,
            message: t('exported_successfully', `Successfully exported ${dataToExport.length} reservations to PDF`),
            isSuccess: true
          });
        } catch (err: any) {
          setConfirmModal({ isOpen: false, message: '', onConfirm: () => {} });
          setResultModal({
            isOpen: true,
            message: t('export_failed', `Failed to export: ${err.message}`),
            isSuccess: false
          });
        }
      }
    });
  };

  // Bulk selection handlers
  const toggleReservationSelection = (id: string) => {
    const newSelection = new Set(selectedReservationIds);
    if (newSelection.has(id)) {
      newSelection.delete(id);
    } else {
      newSelection.add(id);
    }
    setSelectedReservationIds(newSelection);
  };

  const toggleSelectAll = () => {
    if (selectedReservationIds.size === filteredReservations.length) {
      setSelectedReservationIds(new Set());
    } else {
      setSelectedReservationIds(new Set(filteredReservations.map(r => r.id)));
    }
  };

  const handleBulkConfirm = async () => {
    if (selectedReservationIds.size === 0) return;

    setConfirmModal({
      isOpen: true,
      message: t('confirm_bulk_action', `Confirm ${selectedReservationIds.size} reservations?`),
      onConfirm: async () => {
        setConfirmModal({ isOpen: false, message: '', onConfirm: () => {} });

        let successCount = 0;
        for (const id of selectedReservationIds) {
          try {
            await reservationService.confirmReservation(id);
            successCount++;
          } catch (err) {
            console.error(`Failed to confirm reservation ${id}:`, err);
          }
        }

        setResultModal({
          isOpen: true,
          message: t('bulk_confirm_success', `Confirmed ${successCount} of ${selectedReservationIds.size} reservations`),
          isSuccess: successCount > 0
        });

        setSelectedReservationIds(new Set());
        fetchData();
      }
    });
  };

  const handleBulkCancel = async () => {
    if (selectedReservationIds.size === 0) return;

    setConfirmModal({
      isOpen: true,
      message: t('confirm_bulk_cancel', `Cancel ${selectedReservationIds.size} reservations?`),
      onConfirm: async () => {
        setConfirmModal({ isOpen: false, message: '', onConfirm: () => {} });

        let successCount = 0;
        for (const id of selectedReservationIds) {
          try {
            await reservationService.cancelReservation(id);
            successCount++;
          } catch (err) {
            console.error(`Failed to cancel reservation ${id}:`, err);
          }
        }

        setResultModal({
          isOpen: true,
          message: t('bulk_cancel_success', `Cancelled ${successCount} of ${selectedReservationIds.size} reservations`),
          isSuccess: successCount > 0
        });

        setSelectedReservationIds(new Set());
        fetchData();
      }
    });
  };

  // Filter reservations by search query
  const filteredReservations = reservations.filter(reservation => {
    if (!searchQuery.trim()) return true;
    const query = searchQuery.toLowerCase();
    return (
      reservation.customerName.toLowerCase().includes(query) ||
      reservation.customerEmail.toLowerCase().includes(query) ||
      reservation.customerPhone.toLowerCase().includes(query) ||
      reservation.tableNumber.toLowerCase().includes(query)
    );
  });

  const getStatusBadgeClass = (status: ReservationStatus) => {
    switch (status) {
      case ReservationStatus.Confirmed:
        return styles.statusConfirmed;
      case ReservationStatus.Pending:
        return styles.statusPending;
      case ReservationStatus.Cancelled:
        return styles.statusCancelled;
      case ReservationStatus.Completed:
        return styles.statusCompleted;
      case ReservationStatus.NoShow:
        return styles.statusNoShow;
      default:
        return '';
    }
  };

  const formatDate = (dateString: string) => {
    return new Date(dateString).toLocaleDateString('en-US', {
      year: 'numeric',
      month: 'long',
      day: 'numeric',
    });
  };

  const formatTime = (timeString: string) => {
    const parts = timeString.split(':');
    return `${parts[0]}:${parts[1]}`;
  };

  if (isLoading) {
    return (
      <main className={styles.container}>
        <div className={styles.loadingState}>
          <Loader2 size={64} className={styles.spinner} />
          <p>{t('loading_reservations', 'Loading reservations...')}</p>
        </div>
      </main>
    );
  }

  return (
    <main className={styles.container}>
      <div className={styles.content}>
        {/* Header */}
        <div className={styles.header}>
          <div className={styles.titleSection}>
            <h1 className={styles.title}>
              <Calendar size={32} />
              {t('admin_reservations_management', 'Reservations Management')}
            </h1>
            <p className={styles.subtitle}>
              {t('admin_reservations_desc', 'View and manage all table reservations')}
            </p>
          </div>
          <div className={styles.headerActions}>
            <div className={styles.viewToggle}>
              <button
                onClick={() => setViewMode('list')}
                className={`${styles.viewButton} ${viewMode === 'list' ? styles.active : ''}`}
                title={t('list_view', 'List View')}
              >
                <List size={20} />
              </button>
              <button
                onClick={() => setViewMode('calendar')}
                className={`${styles.viewButton} ${viewMode === 'calendar' ? styles.active : ''}`}
                title={t('calendar_view', 'Calendar View')}
              >
                <CalendarDays size={20} />
              </button>
            </div>
            <button onClick={fetchData} className={styles.refreshButton} title={t('refresh', 'Refresh')}>
              <RefreshCw size={20} />
              {t('refresh', 'Refresh')}
            </button>
          </div>
        </div>

        {/* Filters */}
        <div className={styles.filters}>
          <div className={styles.searchBox}>
            <Search size={20} />
            <input
              type="text"
              placeholder={t('search_reservations', 'Search by name, email, phone, or table...')}
              value={searchQuery}
              onChange={(e) => setSearchQuery(e.target.value)}
              className={styles.searchInput}
            />
          </div>

          <div className={styles.filterGroup}>
            <Filter size={16} />
            <select
              value={selectedStatus}
              onChange={(e) => setSelectedStatus(e.target.value as ReservationStatus | 'All')}
              className={styles.filterSelect}
            >
              <option value="All">{t('all_statuses', 'All Statuses')}</option>
              <option value={ReservationStatus.Pending}>{t('status_pending', 'Pending')}</option>
              <option value={ReservationStatus.Confirmed}>{t('status_confirmed', 'Confirmed')}</option>
              <option value={ReservationStatus.Cancelled}>{t('status_cancelled', 'Cancelled')}</option>
              <option value={ReservationStatus.Completed}>{t('status_completed', 'Completed')}</option>
              <option value={ReservationStatus.NoShow}>{t('status_no_show', 'No Show')}</option>
            </select>

            <input
              type="date"
              value={selectedDate}
              onChange={(e) => setSelectedDate(e.target.value)}
              className={styles.filterInput}
            />

            <select
              value={selectedTableId}
              onChange={(e) => setSelectedTableId(e.target.value)}
              className={styles.filterSelect}
            >
              <option value="All">{t('all_tables', 'All Tables')}</option>
              {tables.map(table => (
                <option key={table.id} value={table.id}>
                  {t('table', 'Table')} {table.tableNumber}
                </option>
              ))}
            </select>
          </div>
        </div>

        {/* Statistics */}
        <div className={styles.stats}>
          <div className={styles.statCard}>
            <div className={styles.statValue}>{filteredReservations.length}</div>
            <div className={styles.statLabel}>{t('total_reservations', 'Total')}</div>
          </div>
          <div className={styles.statCard}>
            <div className={styles.statValue}>
              {filteredReservations.filter(r => r.status === ReservationStatus.Pending).length}
            </div>
            <div className={styles.statLabel}>{t('pending', 'Pending')}</div>
          </div>
          <div className={styles.statCard}>
            <div className={styles.statValue}>
              {filteredReservations.filter(r => r.status === ReservationStatus.Confirmed).length}
            </div>
            <div className={styles.statLabel}>{t('confirmed', 'Confirmed')}</div>
          </div>
        </div>

        {/* Export & Bulk Actions */}
        <div className={styles.actionsBar}>
          <div className={styles.exportButtons}>
            <button onClick={handleExportCSV} className={styles.exportButton}>
              <Download size={16} />
              {t('export_csv', 'Export CSV')}
            </button>
            <button onClick={handleExportPDF} className={styles.exportButton}>
              <FileText size={16} />
              {t('export_pdf', 'Export PDF')}
            </button>
          </div>

          {selectedReservationIds.size > 0 && (
            <div className={styles.bulkActionsBar}>
              <span className={styles.selectionCount}>
                {selectedReservationIds.size} {t('selected', 'selected')}
              </span>
              <button onClick={handleBulkConfirm} className={styles.bulkConfirmButton}>
                <Check size={16} />
                {t('confirm_selected', 'Confirm')}
              </button>
              <button onClick={handleBulkCancel} className={styles.bulkCancelButton}>
                <X size={16} />
                {t('cancel_selected', 'Cancel')}
              </button>
              <button
                onClick={() => setSelectedReservationIds(new Set())}
                className={styles.clearSelectionButton}
              >
                {t('clear_selection', 'Clear Selection')}
              </button>
            </div>
          )}
        </div>

        {/* Error State */}
        {error && (
          <div className={styles.errorAlert}>
            <AlertCircle size={20} />
            <p>{error}</p>
          </div>
        )}

        {/* Calendar View */}
        {viewMode === 'calendar' ? (
          <ReservationCalendar
            reservations={filteredReservations}
            onSelectReservation={(reservation) => {
              setSelectedReservation(reservation);
              // Could open a modal here to show reservation details
            }}
          />
        ) : (
          /* List View */
          filteredReservations.length === 0 ? (
            <div className={styles.emptyState}>
              <Calendar size={64} className={styles.emptyIcon} />
              <h2>{t('no_reservations_found', 'No Reservations Found')}</h2>
              <p>{t('no_reservations_match_filters', 'No reservations match your current filters')}</p>
            </div>
          ) : (
            <div className={styles.reservationsList}>
            {filteredReservations.map(reservation => (
              <div key={reservation.id} className={`${styles.reservationCard} ${selectedReservationIds.has(reservation.id) ? styles.selected : ''}`}>
                <div className={styles.cardHeader}>
                  <input
                    type="checkbox"
                    checked={selectedReservationIds.has(reservation.id)}
                    onChange={() => toggleReservationSelection(reservation.id)}
                    className={styles.checkbox}
                  />
                  <div className={styles.customerInfo}>
                    <h3>{reservation.customerName}</h3>
                    <div className={styles.contactInfo}>
                      <span>{reservation.customerEmail}</span>
                      <span>{reservation.customerPhone}</span>
                    </div>
                  </div>
                  <span className={`${styles.statusBadge} ${getStatusBadgeClass(reservation.status)}`}>
                    {reservationService.getStatusLabel(reservation.status)}
                  </span>
                </div>

                <div className={styles.cardBody}>
                  <div className={styles.infoRow}>
                    <div className={styles.infoItem}>
                      <Calendar size={16} />
                      <span>{formatDate(reservation.reservationDate)}</span>
                    </div>
                    <div className={styles.infoItem}>
                      <Clock size={16} />
                      <span>{formatTime(reservation.startTime)} - {formatTime(reservation.endTime)}</span>
                    </div>
                  </div>

                  <div className={styles.infoRow}>
                    <div className={styles.infoItem}>
                      <MapPin size={16} />
                      <span>{t('table', 'Table')} {reservation.tableNumber}</span>
                    </div>
                    <div className={styles.infoItem}>
                      <Users size={16} />
                      <span>{reservation.numberOfGuests} {t('guests', 'guests')}</span>
                    </div>
                  </div>

                  {reservation.specialRequests && (
                    <div className={styles.specialRequests}>
                      <strong>{t('special_requests', 'Special Requests')}:</strong>
                      <p>{reservation.specialRequests}</p>
                    </div>
                  )}
                </div>

                <div className={styles.cardActions}>
                  {reservation.status === ReservationStatus.Pending && (
                    <>
                      <button
                        onClick={() => handleConfirm(reservation.id)}
                        className={styles.confirmButton}
                      >
                        <Check size={16} />
                        {t('confirm', 'Confirm')}
                      </button>
                      <button
                        onClick={() => handleCancel(reservation.id)}
                        className={styles.cancelButton}
                      >
                        <X size={16} />
                        {t('cancel', 'Cancel')}
                      </button>
                    </>
                  )}
                  {reservation.status === ReservationStatus.Confirmed && (
                    <button
                      onClick={() => handleCancel(reservation.id)}
                      className={styles.cancelButton}
                    >
                      <X size={16} />
                      {t('cancel', 'Cancel')}
                    </button>
                  )}
                </div>
              </div>
            ))}
            </div>
          )
        )}
      </div>

      {/* Modals */}
      <ConfirmationModal
        isOpen={confirmModal.isOpen}
        onClose={() => setConfirmModal({ isOpen: false, message: '', onConfirm: () => {} })}
        onConfirm={confirmModal.onConfirm}
        message={confirmModal.message}
      />

      <ResultModal
        isOpen={resultModal.isOpen}
        onClose={() => setResultModal({ isOpen: false, message: '', isSuccess: false })}
        message={resultModal.message}
        isSuccess={resultModal.isSuccess}
      />
    </main>
  );
}
