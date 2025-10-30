'use client';

import React, { useState, useEffect } from 'react';
import { useTranslation } from 'react-i18next';
import {
  Table2,
  Plus,
  Edit2,
  Trash2,
  RefreshCw,
  CheckCircle,
  XCircle,
  AlertCircle,
  Home,
  Sun
} from 'lucide-react';
import { enqueueSnackbar } from 'notistack';
import { reservationService } from '@/services/reservationService';
import { TableDto } from '@/types/reservation';
import ConfirmationModal from '@/components/common/ConfirmationModal';
import ResultModal from '@/components/common/ResultModal';
import styles from './styles.module.css';

export default function TablesManagementPage() {
  const { t } = useTranslation();

  const [tables, setTables] = useState<TableDto[]>([]);
  const [loading, setLoading] = useState(true);
  const [error, setError] = useState<string | null>(null);
  const [filterLocation, setFilterLocation] = useState<'all' | 'indoor' | 'outdoor'>('all');
  const [filterStatus, setFilterStatus] = useState<'all' | 'active' | 'inactive'>('all');
  const [showCreateModal, setShowCreateModal] = useState(false);
  const [showEditModal, setShowEditModal] = useState(false);
  const [selectedTable, setSelectedTable] = useState<TableDto | null>(null);

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

  // Fetch tables
  const fetchTables = async () => {
    try {
      setLoading(true);
      setError(null);
      const data = await reservationService.getTables();
      setTables(data);
    } catch (err: any) {
      setError(err.message || t('failed_to_load_tables', 'Failed to load tables'));
      enqueueSnackbar(t('failed_to_load_tables', 'Failed to load tables'), {
        variant: 'error'
      });
    } finally {
      setLoading(false);
    }
  };

  useEffect(() => {
    fetchTables();
  }, []);

  // Filter tables
  const filteredTables = tables.filter(table => {
    const locationMatch =
      filterLocation === 'all' ||
      (filterLocation === 'indoor' && !table.isOutdoor) ||
      (filterLocation === 'outdoor' && table.isOutdoor);

    const statusMatch =
      filterStatus === 'all' ||
      (filterStatus === 'active' && table.isActive) ||
      (filterStatus === 'inactive' && !table.isActive);

    return locationMatch && statusMatch;
  });

  // Statistics
  const totalTables = tables.length;
  const activeTables = tables.filter(t => t.isActive).length;
  const inactiveTables = tables.filter(t => !t.isActive).length;
  const indoorTables = tables.filter(t => !t.isOutdoor).length;
  const outdoorTables = tables.filter(t => t.isOutdoor).length;
  const totalCapacity = tables.filter(t => t.isActive).reduce((sum, t) => sum + t.maxGuests, 0);

  // Handle delete table
  const handleDeleteTable = async (tableId: string) => {
    setConfirmModal({
      isOpen: true,
      message: t('confirm_delete_table', 'Are you sure you want to delete this table?'),
      onConfirm: async () => {
        setConfirmModal({ isOpen: false, message: '', onConfirm: () => {} });

        try {
          await reservationService.deleteTable(tableId);
          setResultModal({
            isOpen: true,
            message: t('table_deleted_successfully', 'Table deleted successfully'),
            isSuccess: true
          });
          fetchTables();
        } catch (err: any) {
          setResultModal({
            isOpen: true,
            message: err.message || t('failed_to_delete_table', 'Failed to delete table'),
            isSuccess: false
          });
        }
      }
    });
  };

  // Handle toggle active status
  const handleToggleStatus = async (table: TableDto) => {
    try {
      await reservationService.updateTable(table.id, {
        tableNumber: table.tableNumber,
        maxGuests: table.maxGuests,
        isActive: !table.isActive,
        isOutdoor: table.isOutdoor,
        positionX: table.positionX,
        positionY: table.positionY,
        width: table.width,
        height: table.height
      });
      enqueueSnackbar(
        t('table_status_updated', `Table ${table.isActive ? 'deactivated' : 'activated'} successfully`),
        { variant: 'success' }
      );
      fetchTables();
    } catch (err: any) {
      enqueueSnackbar(err.message || t('failed_to_update_table', 'Failed to update table status'), {
        variant: 'error'
      });
    }
  };

  // Handle edit
  const handleEdit = (table: TableDto) => {
    setSelectedTable(table);
    setShowEditModal(true);
  };

  if (loading) {
    return (
      <div className={styles.container}>
        <div className={styles.content}>
          <div className={styles.loadingState}>
            <RefreshCw className={styles.spinner} size={48} />
            <p>{t('loading_tables', 'Loading tables...')}</p>
          </div>
        </div>
      </div>
    );
  }

  return (
    <div className={styles.container}>
      <div className={styles.content}>
        {/* Header */}
        <div className={styles.header}>
          <div className={styles.titleSection}>
            <h1 className={styles.title}>
              <Table2 size={32} />
              {t('table_management', 'Table Management')}
            </h1>
            <p className={styles.subtitle}>
              {t('table_management_subtitle', 'Manage restaurant tables and seating capacity')}
            </p>
          </div>
          <div className={styles.headerActions}>
            <button onClick={fetchTables} className={styles.refreshButton}>
              <RefreshCw size={18} />
              {t('refresh', 'Refresh')}
            </button>
            <button onClick={() => setShowCreateModal(true)} className={styles.createButton}>
              <Plus size={18} />
              {t('create_table', 'Create Table')}
            </button>
          </div>
        </div>

        {/* Error Alert */}
        {error && (
          <div className={styles.errorAlert}>
            <AlertCircle size={20} />
            <span>{error}</span>
          </div>
        )}

        {/* Statistics */}
        <div className={styles.stats}>
          <div className={styles.statCard}>
            <div className={styles.statValue}>{totalTables}</div>
            <div className={styles.statLabel}>{t('total_tables', 'Total Tables')}</div>
          </div>
          <div className={styles.statCard}>
            <div className={styles.statValue}>{activeTables}</div>
            <div className={styles.statLabel}>{t('active_tables', 'Active Tables')}</div>
          </div>
          <div className={styles.statCard}>
            <div className={styles.statValue}>{inactiveTables}</div>
            <div className={styles.statLabel}>{t('inactive_tables', 'Inactive Tables')}</div>
          </div>
          <div className={styles.statCard}>
            <div className={styles.statValue}>{indoorTables}</div>
            <div className={styles.statLabel}>{t('indoor_tables', 'Indoor Tables')}</div>
          </div>
          <div className={styles.statCard}>
            <div className={styles.statValue}>{outdoorTables}</div>
            <div className={styles.statLabel}>{t('outdoor_tables', 'Outdoor Tables')}</div>
          </div>
          <div className={styles.statCard}>
            <div className={styles.statValue}>{totalCapacity}</div>
            <div className={styles.statLabel}>{t('total_capacity', 'Total Capacity')}</div>
          </div>
        </div>

        {/* Filters */}
        <div className={styles.filters}>
          <div className={styles.filterGroup}>
            <Home size={18} />
            <select
              value={filterLocation}
              onChange={(e) => setFilterLocation(e.target.value as any)}
              className={styles.filterSelect}
            >
              <option value="all">{t('all_locations', 'All Locations')}</option>
              <option value="indoor">{t('indoor', 'Indoor')}</option>
              <option value="outdoor">{t('outdoor', 'Outdoor')}</option>
            </select>
          </div>

          <div className={styles.filterGroup}>
            <CheckCircle size={18} />
            <select
              value={filterStatus}
              onChange={(e) => setFilterStatus(e.target.value as any)}
              className={styles.filterSelect}
            >
              <option value="all">{t('all_statuses', 'All Statuses')}</option>
              <option value="active">{t('active', 'Active')}</option>
              <option value="inactive">{t('inactive', 'Inactive')}</option>
            </select>
          </div>
        </div>

        {/* Tables Grid */}
        {filteredTables.length === 0 ? (
          <div className={styles.emptyState}>
            <Table2 className={styles.emptyIcon} size={64} />
            <h2>{t('no_tables_found', 'No tables found')}</h2>
            <p>{t('no_tables_description', 'Try adjusting your filters or create a new table')}</p>
          </div>
        ) : (
          <div className={styles.tablesGrid}>
            {filteredTables.map(table => (
              <div key={table.id} className={styles.tableCard}>
                <div className={styles.cardHeader}>
                  <div className={styles.tableInfo}>
                    <h3>{t('table', 'Table')} {table.tableNumber}</h3>
                    <div className={styles.tableMeta}>
                      {table.isOutdoor ? (
                        <span className={styles.locationBadge}>
                          <Sun size={14} />
                          {t('outdoor', 'Outdoor')}
                        </span>
                      ) : (
                        <span className={styles.locationBadge}>
                          <Home size={14} />
                          {t('indoor', 'Indoor')}
                        </span>
                      )}
                      {table.isActive ? (
                        <span className={styles.statusActive}>
                          <CheckCircle size={14} />
                          {t('active', 'Active')}
                        </span>
                      ) : (
                        <span className={styles.statusInactive}>
                          <XCircle size={14} />
                          {t('inactive', 'Inactive')}
                        </span>
                      )}
                    </div>
                  </div>
                </div>

                <div className={styles.cardBody}>
                  <div className={styles.capacityInfo}>
                    <span className={styles.capacityLabel}>{t('max_guests', 'Max Guests')}:</span>
                    <span className={styles.capacityValue}>{table.maxGuests}</span>
                  </div>
                </div>

                <div className={styles.cardActions}>
                  <button
                    onClick={() => handleToggleStatus(table)}
                    className={table.isActive ? styles.deactivateButton : styles.activateButton}
                  >
                    {table.isActive ? t('deactivate', 'Deactivate') : t('activate', 'Activate')}
                  </button>
                  <button
                    onClick={() => handleEdit(table)}
                    className={styles.editButton}
                  >
                    <Edit2 size={16} />
                    {t('edit', 'Edit')}
                  </button>
                  <button
                    onClick={() => handleDeleteTable(table.id)}
                    className={styles.deleteButton}
                  >
                    <Trash2 size={16} />
                    {t('delete', 'Delete')}
                  </button>
                </div>
              </div>
            ))}
          </div>
        )}
      </div>

      {/* Create Modal */}
      {showCreateModal && (
        <CreateTableModal
          onClose={() => setShowCreateModal(false)}
          onSuccess={() => {
            setShowCreateModal(false);
            fetchTables();
          }}
        />
      )}

      {/* Edit Modal */}
      {showEditModal && selectedTable && (
        <EditTableModal
          table={selectedTable}
          onClose={() => {
            setShowEditModal(false);
            setSelectedTable(null);
          }}
          onSuccess={() => {
            setShowEditModal(false);
            setSelectedTable(null);
            fetchTables();
          }}
        />
      )}

      {/* Confirmation and Result Modals */}
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
    </div>
  );
}

// Create Table Modal Component
function CreateTableModal({ onClose, onSuccess }: { onClose: () => void; onSuccess: () => void }) {
  const { t } = useTranslation();
  const [formData, setFormData] = useState({
    tableNumber: '',
    maxGuests: 2,
    isActive: true,
    isOutdoor: false,
    positionX: 0,
    positionY: 0,
    width: 100,
    height: 100
  });
  const [submitting, setSubmitting] = useState(false);

  const handleSubmit = async (e: React.FormEvent) => {
    e.preventDefault();

    if (!formData.tableNumber.trim()) {
      enqueueSnackbar(t('table_number_required', 'Table number is required'), { variant: 'error' });
      return;
    }

    try {
      setSubmitting(true);
      await reservationService.createTable(formData);
      enqueueSnackbar(t('table_created_successfully', 'Table created successfully'), {
        variant: 'success'
      });
      onSuccess();
    } catch (err: any) {
      enqueueSnackbar(err.message || t('failed_to_create_table', 'Failed to create table'), {
        variant: 'error'
      });
    } finally {
      setSubmitting(false);
    }
  };

  return (
    <div className={styles.modalOverlay} onClick={onClose}>
      <div className={styles.modal} onClick={(e) => e.stopPropagation()}>
        <div className={styles.modalHeader}>
          <h2>{t('create_table', 'Create Table')}</h2>
          <button onClick={onClose} className={styles.closeButton}>×</button>
        </div>

        <form onSubmit={handleSubmit} className={styles.modalBody}>
          <div className={styles.formGroup}>
            <label>{t('table_number', 'Table Number')}</label>
            <input
              type="text"
              value={formData.tableNumber}
              onChange={(e) => setFormData({ ...formData, tableNumber: e.target.value })}
              placeholder={t('enter_table_number', 'e.g., 1, 11a, 11b')}
              className={styles.input}
              required
            />
          </div>

          <div className={styles.formGroup}>
            <label>{t('max_guests', 'Max Guests')}</label>
            <input
              type="number"
              value={formData.maxGuests}
              onChange={(e) => setFormData({ ...formData, maxGuests: parseInt(e.target.value) })}
              min="1"
              max="20"
              className={styles.input}
              required
            />
          </div>

          <div className={styles.formGroup}>
            <label className={styles.checkboxLabel}>
              <input
                type="checkbox"
                checked={formData.isOutdoor}
                onChange={(e) => setFormData({ ...formData, isOutdoor: e.target.checked })}
                className={styles.checkbox}
              />
              {t('outdoor_table', 'Outdoor Table')}
            </label>
          </div>

          <div className={styles.formGroup}>
            <label className={styles.checkboxLabel}>
              <input
                type="checkbox"
                checked={formData.isActive}
                onChange={(e) => setFormData({ ...formData, isActive: e.target.checked })}
                className={styles.checkbox}
              />
              {t('active', 'Active')}
            </label>
          </div>

          <div className={styles.modalActions}>
            <button type="button" onClick={onClose} className={styles.cancelButton}>
              {t('cancel', 'Cancel')}
            </button>
            <button type="submit" disabled={submitting} className={styles.submitButton}>
              {submitting ? t('creating', 'Creating...') : t('create', 'Create')}
            </button>
          </div>
        </form>
      </div>
    </div>
  );
}

// Edit Table Modal Component
function EditTableModal({
  table,
  onClose,
  onSuccess
}: {
  table: TableDto;
  onClose: () => void;
  onSuccess: () => void;
}) {
  const { t } = useTranslation();
  const [formData, setFormData] = useState({
    tableNumber: table.tableNumber,
    maxGuests: table.maxGuests,
    isActive: table.isActive,
    isOutdoor: table.isOutdoor,
    positionX: table.positionX,
    positionY: table.positionY,
    width: table.width,
    height: table.height
  });
  const [submitting, setSubmitting] = useState(false);

  const handleSubmit = async (e: React.FormEvent) => {
    e.preventDefault();

    if (!formData.tableNumber.trim()) {
      enqueueSnackbar(t('table_number_required', 'Table number is required'), { variant: 'error' });
      return;
    }

    try {
      setSubmitting(true);
      await reservationService.updateTable(table.id, formData);
      enqueueSnackbar(t('table_updated_successfully', 'Table updated successfully'), {
        variant: 'success'
      });
      onSuccess();
    } catch (err: any) {
      enqueueSnackbar(err.message || t('failed_to_update_table', 'Failed to update table'), {
        variant: 'error'
      });
    } finally {
      setSubmitting(false);
    }
  };

  return (
    <div className={styles.modalOverlay} onClick={onClose}>
      <div className={styles.modal} onClick={(e) => e.stopPropagation()}>
        <div className={styles.modalHeader}>
          <h2>{t('edit_table', 'Edit Table')}</h2>
          <button onClick={onClose} className={styles.closeButton}>×</button>
        </div>

        <form onSubmit={handleSubmit} className={styles.modalBody}>
          <div className={styles.formGroup}>
            <label>{t('table_number', 'Table Number')}</label>
            <input
              type="text"
              value={formData.tableNumber}
              onChange={(e) => setFormData({ ...formData, tableNumber: e.target.value })}
              placeholder={t('enter_table_number', 'e.g., 1, 11a, 11b')}
              className={styles.input}
              required
            />
          </div>

          <div className={styles.formGroup}>
            <label>{t('max_guests', 'Max Guests')}</label>
            <input
              type="number"
              value={formData.maxGuests}
              onChange={(e) => setFormData({ ...formData, maxGuests: parseInt(e.target.value) })}
              min="1"
              max="20"
              className={styles.input}
              required
            />
          </div>

          <div className={styles.formGroup}>
            <label className={styles.checkboxLabel}>
              <input
                type="checkbox"
                checked={formData.isOutdoor}
                onChange={(e) => setFormData({ ...formData, isOutdoor: e.target.checked })}
                className={styles.checkbox}
              />
              {t('outdoor_table', 'Outdoor Table')}
            </label>
          </div>

          <div className={styles.formGroup}>
            <label className={styles.checkboxLabel}>
              <input
                type="checkbox"
                checked={formData.isActive}
                onChange={(e) => setFormData({ ...formData, isActive: e.target.checked })}
                className={styles.checkbox}
              />
              {t('active', 'Active')}
            </label>
          </div>

          <div className={styles.modalActions}>
            <button type="button" onClick={onClose} className={styles.cancelButton}>
              {t('cancel', 'Cancel')}
            </button>
            <button type="submit" disabled={submitting} className={styles.submitButton}>
              {submitting ? t('updating', 'Updating...') : t('update', 'Update')}
            </button>
          </div>
        </form>
      </div>
    </div>
  );
}
