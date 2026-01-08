"use client";

import React, { useState, useEffect } from 'react';
import { useRouter } from 'next/navigation';
import { useTranslation } from 'react-i18next';
import { useAuth } from '@/components/AuthContext';
import { reservationService } from '@/services/reservationService';
import { ReservationDto, ReservationStatus, TableDto } from '@/types/reservation';
import { useSnackbar } from 'notistack';
import { Loader2, AlertCircle, Calendar } from 'lucide-react';
import ReservationCalendar from '@/components/admin/reservations/ReservationCalendar';
import { ReservationsHeader } from '@/components/admin/reservations/ReservationsHeader';
import { ReservationsFilters } from '@/components/admin/reservations/ReservationsFilters';
import { ReservationsStats } from '@/components/admin/reservations/ReservationsStats';
import { ReservationsActions } from '@/components/admin/reservations/ReservationsActions';
import { ReservationsList } from '@/components/admin/reservations/ReservationsList';
import { exportReservationsToCSV, exportReservationsToPDF } from '@/utils/reservationExportUtils';
import ConfirmationModal from '@/components/common/ConfirmationModal';
import ResultModal from '@/components/common/ResultModal';
import Pagination from '@/components/common/Pagination';
import styles from './styles.module.css';

type ViewMode = 'list' | 'calendar';

export default function AdminReservationsManagementPage() {
  const { t } = useTranslation();
  const router = useRouter();
  const { user, isLoading: authLoading } = useAuth();
  const { enqueueSnackbar } = useSnackbar();

  const [reservations, setReservations] = useState<ReservationDto[]>([]);
  const [allReservationsCount, setAllReservationsCount] = useState({ total: 0, pending: 0, confirmed: 0 });
  const [tables, setTables] = useState<TableDto[]>([]);
  const [isLoading, setIsLoading] = useState(true);
  const [error, setError] = useState('');
  const [searchQuery, setSearchQuery] = useState('');
  const [selectedStatus, setSelectedStatus] = useState<ReservationStatus | 'All'>('All');
  const [selectedDate, setSelectedDate] = useState<string>('');
  const [selectedTableId, setSelectedTableId] = useState<string>('All');
  const [viewMode, setViewMode] = useState<ViewMode>('list');
  const [selectedReservationIds, setSelectedReservationIds] = useState<Set<string>>(new Set());

  // Pagination state
  const [currentPage, setCurrentPage] = useState(1);
  const [totalPages, setTotalPages] = useState(1);
  const [totalCount, setTotalCount] = useState(0);
  const pageSize = 20;

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
    // eslint-disable-next-line react-hooks/exhaustive-deps
  }, [user, authLoading, selectedStatus, selectedDate, selectedTableId, currentPage]);

  const fetchData = async () => {
    try {
      setIsLoading(true);
      setError('');

      // Fetch ALL reservations for stats (no filters)
      const allReservationsResult = await reservationService.getReservations({});
      const allItems = allReservationsResult.items;
      setAllReservationsCount({
        total: allItems.length,
        pending: allItems.filter(r => r.status === ReservationStatus.Pending).length,
        confirmed: allItems.filter(r => r.status === ReservationStatus.Confirmed).length
      });

      // Fetch filtered reservations for display with pagination
      const reservationsParams: any = {
        page: currentPage,
        pageSize: pageSize
      };
      if (selectedStatus !== 'All') reservationsParams.status = selectedStatus;
      if (selectedDate) reservationsParams.date = selectedDate;
      if (selectedTableId !== 'All') reservationsParams.tableId = selectedTableId;

      const reservationsResult = await reservationService.getReservations(reservationsParams);
      setReservations(reservationsResult.items);
      setTotalPages(reservationsResult.totalPages || 1);
      setTotalCount(reservationsResult.totalCount || 0);

      // Fetch tables for filter
      const tablesResult = await reservationService.getTables();
      setTables(tablesResult);
    } catch (err: any) {
      // eslint-disable-next-line no-console
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

  const handleDelete = async (id: string) => {
    setConfirmModal({
      isOpen: true,
      message: t('confirm_delete_reservation', 'Are you sure you want to permanently delete this reservation? This action cannot be undone.'),
      onConfirm: async () => {
        setConfirmModal({ isOpen: false, message: '', onConfirm: () => {} });

        try {
          await reservationService.deleteReservation(id);
          setResultModal({
            isOpen: true,
            message: t('reservation_deleted_success', 'Reservation deleted successfully'),
            isSuccess: true
          });
          fetchData();
        } catch (err: any) {
          setResultModal({
            isOpen: true,
            message: err.message || t('failed_to_delete_reservation', 'Failed to delete reservation'),
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
            // eslint-disable-next-line no-console
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
            // eslint-disable-next-line no-console
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

  const handleBulkDelete = async () => {
    if (selectedReservationIds.size === 0) return;

    setConfirmModal({
      isOpen: true,
      message: t('confirm_bulk_delete', `Permanently delete ${selectedReservationIds.size} reservations?`),
      onConfirm: async () => {
        setConfirmModal({ isOpen: false, message: '', onConfirm: () => {} });

        let successCount = 0;
        for (const id of selectedReservationIds) {
          try {
            await reservationService.deleteReservation(id);
            successCount++;
          } catch (err) {
            // eslint-disable-next-line no-console
            console.error(`Failed to delete reservation ${id}:`, err);
          }
        }

        setResultModal({
          isOpen: true,
          message: t('bulk_delete_success', `Deleted ${successCount} of ${selectedReservationIds.size} reservations`),
          isSuccess: successCount > 0
        });

        setSelectedReservationIds(new Set());
        fetchData();
      }
    });
  };

  // Filter handlers that reset pagination
  const handleStatusChange = (status: ReservationStatus | 'All') => {
    setSelectedStatus(status);
    setCurrentPage(1);
  };

  const handleDateChange = (date: string) => {
    setSelectedDate(date);
    setCurrentPage(1);
  };

  const handleTableChange = (tableId: string) => {
    setSelectedTableId(tableId);
    setCurrentPage(1);
  };

  const handlePageChange = (page: number) => {
    setCurrentPage(page);
    window.scrollTo({ top: 0, behavior: 'smooth' });
  };

  // Filter reservations by search query
  const filteredReservations = reservations.filter(reservation => {
    if (!searchQuery.trim()) return true;
    const query = searchQuery.toLowerCase();
    return (
      reservation.customerName.toLowerCase().includes(query) ||
      reservation.customerEmail.toLowerCase().includes(query) ||
      (reservation.customerPhone && reservation.customerPhone.toLowerCase().includes(query)) ||
      reservation.tableNumber.toLowerCase().includes(query)
    );
  });

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
        <ReservationsHeader
          title={t('admin_reservations_management', 'Reservations Management')}
          subtitle={t('admin_reservations_desc', 'View and manage all table reservations')}
          viewMode={viewMode}
          onViewModeChange={setViewMode}
          onRefresh={fetchData}
          listViewLabel={t('list_view', 'List View')}
          calendarViewLabel={t('calendar_view', 'Calendar View')}
          refreshLabel={t('refresh', 'Refresh')}
        />

        {/* Filters */}
        <ReservationsFilters
          searchQuery={searchQuery}
          onSearchChange={setSearchQuery}
          selectedStatus={selectedStatus}
          onStatusChange={handleStatusChange}
          selectedDate={selectedDate}
          onDateChange={handleDateChange}
          selectedTableId={selectedTableId}
          onTableChange={handleTableChange}
          tables={tables}
          searchPlaceholder={t('search_reservations', 'Search by name, email, phone, or table...')}
          allStatusesLabel={t('all_statuses', 'All Statuses')}
          statusLabels={{
            pending: t('status_pending', 'Pending'),
            confirmed: t('status_confirmed', 'Confirmed'),
            cancelled: t('status_cancelled', 'Cancelled'),
            completed: t('status_completed', 'Completed'),
            noShow: t('status_no_show', 'No Show'),
          }}
          allTablesLabel={t('all_tables', 'All Tables')}
          tableLabel={t('table', 'Table')}
        />

        {/* Statistics */}
        <ReservationsStats
          total={allReservationsCount.total}
          pending={allReservationsCount.pending}
          confirmed={allReservationsCount.confirmed}
          totalLabel={t('total_reservations', 'Total')}
          pendingLabel={t('pending', 'Pending')}
          confirmedLabel={t('confirmed', 'Confirmed')}
        />

        {/* Export & Bulk Actions */}
        <ReservationsActions
          selectedCount={selectedReservationIds.size}
          onExportCSV={handleExportCSV}
          onExportPDF={handleExportPDF}
          onBulkConfirm={handleBulkConfirm}
          onBulkCancel={handleBulkCancel}
          onBulkDelete={handleBulkDelete}
          onClearSelection={() => setSelectedReservationIds(new Set())}
          exportCSVLabel={t('export_csv', 'Export CSV')}
          exportPDFLabel={t('export_pdf', 'Export PDF')}
          selectedLabel={t('selected', 'selected')}
          confirmSelectedLabel={t('confirm_selected', 'Confirm')}
          cancelSelectedLabel={t('cancel_selected', 'Cancel')}
          deleteSelectedLabel={t('delete_selected', 'Delete')}
          clearSelectionLabel={t('clear_selection', 'Clear Selection')}
        />

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
              toggleReservationSelection(reservation.id);
            }}
            selectedReservationIds={selectedReservationIds}
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
            <ReservationsList
              reservations={filteredReservations}
              selectedReservationIds={selectedReservationIds}
              onToggleSelection={toggleReservationSelection}
              onConfirm={handleConfirm}
              onCancel={handleCancel}
              onDelete={handleDelete}
              tableLabel={t('table', 'Table')}
              guestsLabel={t('guests', 'guests')}
              specialRequestsLabel={t('special_requests', 'Special Requests')}
              confirmLabel={t('confirm', 'Confirm')}
              cancelLabel={t('cancel', 'Cancel')}
              deleteLabel={t('delete_reservation', 'Delete')}
            />
          )
        )}

        {/* Pagination - Only show in list view */}
        {viewMode === 'list' && !isLoading && filteredReservations.length > 0 && totalPages > 1 && (
          <>
            <Pagination
              currentPage={currentPage}
              totalPages={totalPages}
              onPageChange={handlePageChange}
              isLoading={isLoading}
            />

            {/* Pagination Info */}
            {totalCount > 0 && (
              <p style={{ textAlign: 'center', marginTop: '1rem', color: '#666' }}>
                {t('showing_items', {
                  start: (currentPage - 1) * pageSize + 1,
                  end: Math.min(currentPage * pageSize, totalCount),
                  total: totalCount,
                  defaultValue: `Showing ${(currentPage - 1) * pageSize + 1}-${Math.min(currentPage * pageSize, totalCount)} of ${totalCount} items`
                })}
              </p>
            )}
          </>
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
