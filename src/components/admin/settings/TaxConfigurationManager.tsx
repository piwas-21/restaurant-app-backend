'use client';

import React, { useState, useEffect } from 'react';
import { useSnackbar } from 'notistack';
import { useTranslation } from 'react-i18next';
import { Trash2, Edit, Plus, DollarSign, ToggleLeft, ToggleRight } from 'lucide-react';
import ConfirmationModal from '@/components/common/ConfirmationModal';
import { adminTaxConfigurationService } from '@/services/adminTaxConfigurationService';
import type { TaxConfiguration } from '@/services/adminTaxConfigurationService';
import { OrderType } from '@/types/order';
import styles from './TaxConfigurationManager.module.css';

interface TaxFormData {
  name: string;
  rate: number;
  isEnabled: boolean;
  description: string;
  applicableOrderTypes: OrderType[];
}

export default function TaxConfigurationManager() {
  const { enqueueSnackbar } = useSnackbar();
  const { t } = useTranslation();
  const [taxConfigs, setTaxConfigs] = useState<TaxConfiguration[]>([]);
  const [loading, setLoading] = useState(true);
  const [isFormOpen, setIsFormOpen] = useState(false);
  const [editingConfig, setEditingConfig] = useState<TaxConfiguration | null>(null);
  const [isDeleteModalOpen, setIsDeleteModalOpen] = useState(false);
  const [deletingTaxId, setDeletingTaxId] = useState<string | null>(null);
  const [rateInput, setRateInput] = useState<string>('0');
  const [isRateValid, setIsRateValid] = useState(true);
  const [formData, setFormData] = useState<TaxFormData>({
    name: '',
    rate: 0,
    isEnabled: false,
    description: '',
    applicableOrderTypes: [],
  });

  useEffect(() => {
    fetchTaxConfigs();
    // Mount-only initial fetch; see OrderTypeManager comment.
    // eslint-disable-next-line react-hooks/exhaustive-deps
  }, []);

  const fetchTaxConfigs = async () => {
    try {
      setLoading(true);
      const data = await adminTaxConfigurationService.getAllTaxConfigurations();
      setTaxConfigs(data);
    } catch {
      enqueueSnackbar(t('tax_failed_to_load', 'Failed to load tax configurations'), { variant: 'error' });
    } finally {
      setLoading(false);
    }
  };

  const handleCreate = () => {
    setEditingConfig(null);
    setRateInput('');
    setIsRateValid(true);
    setFormData({
      name: '',
      rate: 0,
      isEnabled: false,
      description: '',
      applicableOrderTypes: [],
    });
    setIsFormOpen(true);
  };

  const handleEdit = (config: TaxConfiguration) => {
    setEditingConfig(config);
    setRateInput(config.rate.toString());
    setIsRateValid(true);
    setFormData({
      name: config.name,
      rate: config.rate,
      isEnabled: config.isEnabled,
      description: config.description,
      applicableOrderTypes: config.applicableOrderTypes || [],
    });
    setIsFormOpen(true);
  };

  const handleDelete = async (id: string) => {
    setDeletingTaxId(id);
    setIsDeleteModalOpen(true);
  };

  const confirmDelete = async () => {
    if (!deletingTaxId) return;

    try {
      await adminTaxConfigurationService.deleteTaxConfiguration(deletingTaxId);
      enqueueSnackbar(t('tax_deleted_successfully', 'Tax configuration deleted successfully'), { variant: 'success' });
      fetchTaxConfigs();
    } catch {
      enqueueSnackbar(t('tax_failed_to_delete', 'Failed to delete tax configuration'), { variant: 'error' });
    } finally {
      setIsDeleteModalOpen(false);
      setDeletingTaxId(null);
    }
  };

  const handleSubmit = async (e: React.FormEvent) => {
    e.preventDefault();

    try {
      if (editingConfig) {
        await adminTaxConfigurationService.updateTaxConfiguration({
          ...formData,
          id: editingConfig.id,
        });
      } else {
        await adminTaxConfigurationService.createTaxConfiguration(formData);
      }

      enqueueSnackbar(
        t(
          editingConfig ? 'tax_updated_successfully' : 'tax_created_successfully',
          `Tax configuration ${editingConfig ? 'updated' : 'created'} successfully`,
        ),
        { variant: 'success' },
      );

      setIsFormOpen(false);
      fetchTaxConfigs();
    } catch {
      enqueueSnackbar(t('tax_failed_to_save', 'Failed to save tax configuration'), { variant: 'error' });
    }
  };

  const handleToggle = async (config: TaxConfiguration) => {
    try {
      await adminTaxConfigurationService.updateTaxConfiguration({
        id: config.id,
        name: config.name,
        rate: config.rate,
        isEnabled: !config.isEnabled,
        description: config.description,
        applicableOrderTypes: config.applicableOrderTypes || [],
      });

      enqueueSnackbar(
        t(
          config.isEnabled ? 'tax_disabled_successfully' : 'tax_enabled_successfully',
          `Tax ${!config.isEnabled ? 'enabled' : 'disabled'} successfully`,
        ),
        { variant: 'success' },
      );

      fetchTaxConfigs();
    } catch {
      enqueueSnackbar(t('tax_failed_to_toggle', 'Failed to toggle tax configuration'), { variant: 'error' });
    }
  };

  const handleRateChange = (value: string) => {
    setRateInput(value);

    if (value === '') {
      setIsRateValid(true);
      setFormData({ ...formData, rate: 0 });
      return;
    }

    const numValue = parseFloat(value);
    if (isNaN(numValue) || numValue < 0 || numValue > 100) {
      setIsRateValid(false);
      return;
    }

    setIsRateValid(true);
    setFormData({ ...formData, rate: numValue });
  };

  const getOrderTypeLabel = (orderType: OrderType): string => {
    switch (orderType) {
      case OrderType.DineIn:
        return t('order_type_dine_in', 'Dine-In');
      case OrderType.Takeaway:
        return t('order_type_takeaway', 'Takeaway');
      case OrderType.Delivery:
        return t('order_type_delivery', 'Delivery');
      default:
        return orderType;
    }
  };

  if (loading) {
    return (
      <div className={styles.loading}>
        <p>{t('loading', 'Loading...')}</p>
      </div>
    );
  }

  return (
    <div className={styles.container}>
      <div className={styles.header}>
        <button onClick={handleCreate} className={styles.createButton}>
          <Plus size={18} />
          {t('add_tax_configuration', 'Add Tax Configuration')}
        </button>
      </div>

      <div className={styles.configList}>
        {taxConfigs.length === 0 ? (
          <div className={styles.emptyState}>
            <DollarSign size={48} />
            <p>{t('no_tax_configurations', 'No tax configurations found')}</p>
            <button onClick={handleCreate} className={styles.emptyButton}>
              {t('create_first_tax_configuration', 'Create First Tax Configuration')}
            </button>
          </div>
        ) : (
          taxConfigs.map((config) => (
            <div
              key={config.id}
              className={`${styles.configCard} ${config.isEnabled ? styles.enabled : styles.disabled}`}
            >
              <div className={styles.configHeader}>
                <div className={styles.configInfo}>
                  <h3 className={styles.configName}>{config.name}</h3>
                  <p className={styles.configDescription}>{config.description}</p>
                </div>
                <div className={styles.configStatus}>
                  {config.isEnabled ? (
                    <span className={styles.statusBadge}>{t('active', 'Active')}</span>
                  ) : (
                    <span className={`${styles.statusBadge} ${styles.inactive}`}>{t('inactive', 'Inactive')}</span>
                  )}
                </div>
              </div>

              <div className={styles.configDetails}>
                <div className={styles.rateDisplay}>
                  <span className={styles.rateLabel}>{t('rate', 'Rate')}:</span>
                  <span className={styles.rateValue}>{config.rate.toFixed(2)}%</span>
                </div>
                <div className={styles.orderTypesDisplay}>
                  <span className={styles.orderTypesLabel}>{t('applies_to', 'Applies to')}:</span>
                  <div className={styles.orderTypesBadges}>
                    {config.applicableOrderTypes && config.applicableOrderTypes.length > 0 ? (
                      config.applicableOrderTypes.map((type) => (
                        <span key={type} className={styles.orderTypeBadge}>
                          {getOrderTypeLabel(type)}
                        </span>
                      ))
                    ) : (
                      <span className={styles.orderTypeBadge} style={{ opacity: 0.5 }}>
                        {t('no_order_types_selected', 'No order types selected')}
                      </span>
                    )}
                  </div>
                </div>
              </div>

              <div className={styles.configActions}>
                <button
                  onClick={() => handleToggle(config)}
                  className={styles.toggleButton}
                  title={config.isEnabled ? t('disable', 'Disable') : t('enable', 'Enable')}
                >
                  {config.isEnabled ? (
                    <>
                      <ToggleRight size={18} />
                      {t('disable', 'Disable')}
                    </>
                  ) : (
                    <>
                      <ToggleLeft size={18} />
                      {t('enable', 'Enable')}
                    </>
                  )}
                </button>
                <button onClick={() => handleEdit(config)} className={styles.editButton} title={t('edit', 'Edit')}>
                  <Edit size={16} />
                </button>
                <button
                  onClick={() => handleDelete(config.id)}
                  className={styles.deleteButton}
                  title={t('delete', 'Delete')}
                >
                  <Trash2 size={16} />
                </button>
              </div>
            </div>
          ))
        )}
      </div>

      {/* Form Modal - Continued in next message due to length */}
      {isFormOpen && (
        <div className={styles.modal}>
          <div className={styles.modalContent}>
            <div className={styles.modalHeader}>
              <h2>
                {editingConfig
                  ? t('edit_tax_configuration', 'Edit Tax Configuration')
                  : t('create_tax_configuration', 'Create Tax Configuration')}
              </h2>
              <button onClick={() => setIsFormOpen(false)} className={styles.closeButton}>
                ×
              </button>
            </div>

            <form onSubmit={handleSubmit} className={styles.form}>
              <div className={styles.formGroup}>
                <label htmlFor="name">{t('name', 'Name')}</label>
                <input
                  id="name"
                  type="text"
                  value={formData.name}
                  onChange={(e) => setFormData({ ...formData, name: e.target.value })}
                  placeholder={t('tax_name_placeholder', 'e.g., VAT, Sales Tax')}
                  required
                />
              </div>

              <div className={styles.formGroup}>
                <label htmlFor="rate">{t('rate_percent', 'Rate (%)')}</label>
                <input
                  id="rate"
                  type="text"
                  inputMode="decimal"
                  value={rateInput}
                  onChange={(e) => handleRateChange(e.target.value)}
                  placeholder={t('tax_rate_placeholder', 'e.g., 8.00')}
                  className={!isRateValid ? styles.inputError : ''}
                  required
                />
                {!isRateValid && (
                  <small className={styles.errorText}>
                    {t('rate_must_be_valid_number', 'Rate must be a valid number between 0 and 100')}
                  </small>
                )}
              </div>

              <div className={styles.formGroup}>
                <label htmlFor="description">{t('description', 'Description')}</label>
                <textarea
                  id="description"
                  value={formData.description}
                  onChange={(e) => setFormData({ ...formData, description: e.target.value })}
                  placeholder={t('tax_description_placeholder', 'e.g., Standard VAT rate for Switzerland')}
                  rows={3}
                />
              </div>

              <div className={styles.formGroup}>
                <label>{t('applicable_order_types', 'Applicable Order Types')}</label>
                <div className={styles.checkboxGroup}>
                  <label className={styles.checkboxLabel}>
                    <input
                      type="checkbox"
                      checked={formData.applicableOrderTypes.includes(OrderType.DineIn)}
                      onChange={(e) => {
                        const types = e.target.checked
                          ? [...formData.applicableOrderTypes, OrderType.DineIn]
                          : formData.applicableOrderTypes.filter((t) => t !== OrderType.DineIn);
                        setFormData({ ...formData, applicableOrderTypes: types });
                      }}
                    />
                    <span>{t('dine_in_restaurant', 'Dine-In (Restaurant)')}</span>
                  </label>
                  <label className={styles.checkboxLabel}>
                    <input
                      type="checkbox"
                      checked={formData.applicableOrderTypes.includes(OrderType.Takeaway)}
                      onChange={(e) => {
                        const types = e.target.checked
                          ? [...formData.applicableOrderTypes, OrderType.Takeaway]
                          : formData.applicableOrderTypes.filter((t) => t !== OrderType.Takeaway);
                        setFormData({ ...formData, applicableOrderTypes: types });
                      }}
                    />
                    <span>{t('takeaway_to_go', 'Takeaway (To Go)')}</span>
                  </label>
                  <label className={styles.checkboxLabel}>
                    <input
                      type="checkbox"
                      checked={formData.applicableOrderTypes.includes(OrderType.Delivery)}
                      onChange={(e) => {
                        const types = e.target.checked
                          ? [...formData.applicableOrderTypes, OrderType.Delivery]
                          : formData.applicableOrderTypes.filter((t) => t !== OrderType.Delivery);
                        setFormData({ ...formData, applicableOrderTypes: types });
                      }}
                    />
                    <span>{t('delivery', 'Delivery')}</span>
                  </label>
                </div>
              </div>

              <div className={styles.formGroup}>
                <label className={styles.checkboxLabel}>
                  <input
                    type="checkbox"
                    checked={formData.isEnabled}
                    onChange={(e) => setFormData({ ...formData, isEnabled: e.target.checked })}
                  />
                  <span>{t('enable_this_tax_configuration', 'Enable this tax configuration')}</span>
                </label>
              </div>

              <div className={styles.formActions}>
                <button type="button" onClick={() => setIsFormOpen(false)} className={styles.cancelButton}>
                  {t('cancel', 'Cancel')}
                </button>
                <button type="submit" className={styles.submitButton} disabled={!isRateValid || !rateInput}>
                  {editingConfig ? t('update', 'Update') : t('create', 'Create')}
                </button>
              </div>
            </form>
          </div>
        </div>
      )}

      <ConfirmationModal
        isOpen={isDeleteModalOpen}
        onClose={() => {
          setIsDeleteModalOpen(false);
          setDeletingTaxId(null);
        }}
        onConfirm={confirmDelete}
        message={t('tax_delete_confirm', 'Are you sure you want to delete this tax configuration?')}
      />
    </div>
  );
}
