'use client';

import React, { useState, useEffect } from 'react';
import { useSnackbar } from 'notistack';
import {
  Trash2,
  Edit,
  Plus,
  DollarSign,
  ToggleLeft,
  ToggleRight
} from 'lucide-react';
import { AdminAuthGuard } from '@/components/admin/AdminAuthGuard';
import { adminTaxConfigurationService } from '@/services/adminTaxConfigurationService';
import type { TaxConfiguration } from '@/services/adminTaxConfigurationService';
import styles from './tax-configuration.module.css';

interface TaxFormData {
  name: string;
  rate: number;
  isEnabled: boolean;
  description: string;
}

export default function TaxConfigurationPage() {
  const { enqueueSnackbar } = useSnackbar();
  const [taxConfigs, setTaxConfigs] = useState<TaxConfiguration[]>([]);
  const [loading, setLoading] = useState(true);
  const [isFormOpen, setIsFormOpen] = useState(false);
  const [editingConfig, setEditingConfig] = useState<TaxConfiguration | null>(null);
  const [formData, setFormData] = useState<TaxFormData>({
    name: '',
    rate: 0,
    isEnabled: false,
    description: ''
  });

  useEffect(() => {
    fetchTaxConfigs();
    // eslint-disable-next-line react-hooks/exhaustive-deps
  }, []);

  const fetchTaxConfigs = async () => {
    try {
      setLoading(true);
      const data = await adminTaxConfigurationService.getAllTaxConfigurations();
      setTaxConfigs(data);
    } catch {
      enqueueSnackbar('Failed to load tax configurations', { variant: 'error' });
    } finally {
      setLoading(false);
    }
  };

  const handleCreate = () => {
    setEditingConfig(null);
    setFormData({
      name: '',
      rate: 0,
      isEnabled: false,
      description: ''
    });
    setIsFormOpen(true);
  };

  const handleEdit = (config: TaxConfiguration) => {
    setEditingConfig(config);
    setFormData({
      name: config.name,
      rate: config.rate,
      isEnabled: config.isEnabled,
      description: config.description
    });
    setIsFormOpen(true);
  };

  const handleDelete = async (id: string) => {
    if (!confirm('Are you sure you want to delete this tax configuration?')) return;

    try {
      await adminTaxConfigurationService.deleteTaxConfiguration(id);
      enqueueSnackbar('Tax configuration deleted successfully', { variant: 'success' });
      fetchTaxConfigs();
    } catch {
      enqueueSnackbar('Failed to delete tax configuration', { variant: 'error' });
    }
  };

  const handleSubmit = async (e: React.FormEvent) => {
    e.preventDefault();

    try {
      if (editingConfig) {
        await adminTaxConfigurationService.updateTaxConfiguration({
          ...formData,
          id: editingConfig.id
        });
      } else {
        await adminTaxConfigurationService.createTaxConfiguration(formData);
      }

      enqueueSnackbar(
        `Tax configuration ${editingConfig ? 'updated' : 'created'} successfully`,
        { variant: 'success' }
      );

      setIsFormOpen(false);
      fetchTaxConfigs();
    } catch {
      enqueueSnackbar('Failed to save tax configuration', { variant: 'error' });
    }
  };

  const handleToggle = async (config: TaxConfiguration) => {
    try {
      await adminTaxConfigurationService.updateTaxConfiguration({
        id: config.id,
        name: config.name,
        rate: config.rate,
        isEnabled: !config.isEnabled,
        description: config.description
      });

      enqueueSnackbar(
        `Tax ${!config.isEnabled ? 'enabled' : 'disabled'} successfully`,
        { variant: 'success' }
      );

      fetchTaxConfigs();
    } catch {
      enqueueSnackbar('Failed to toggle tax configuration', { variant: 'error' });
    }
  };

  return (
    <AdminAuthGuard>
      <div className={styles.container}>
        <div className={styles.header}>
          <div className={styles.headerContent}>
            <DollarSign className={styles.headerIcon} />
            <div>
              <h1 className={styles.title}>Tax Configuration</h1>
              <p className={styles.subtitle}>
                Manage tax rates and enable/disable tax calculations
              </p>
            </div>
          </div>
          <button onClick={handleCreate} className={styles.createButton}>
            <Plus size={20} />
            Add Tax Configuration
          </button>
        </div>

        {loading ? (
          <div className={styles.loading}>Loading...</div>
        ) : (
          <div className={styles.configList}>
            {taxConfigs.length === 0 ? (
              <div className={styles.emptyState}>
                <DollarSign size={48} />
                <p>No tax configurations found</p>
                <button onClick={handleCreate} className={styles.emptyButton}>
                  Create First Tax Configuration
                </button>
              </div>
            ) : (
              taxConfigs.map((config) => (
                <div
                  key={config.id}
                  className={`${styles.configCard} ${
                    config.isEnabled ? styles.enabled : styles.disabled
                  }`}
                >
                  <div className={styles.configHeader}>
                    <div className={styles.configInfo}>
                      <h3 className={styles.configName}>{config.name}</h3>
                      <p className={styles.configDescription}>{config.description}</p>
                    </div>
                    <div className={styles.configStatus}>
                      {config.isEnabled ? (
                        <span className={styles.statusBadge}>Active</span>
                      ) : (
                        <span className={`${styles.statusBadge} ${styles.inactive}`}>
                          Inactive
                        </span>
                      )}
                    </div>
                  </div>

                  <div className={styles.configDetails}>
                    <div className={styles.rateDisplay}>
                      <span className={styles.rateLabel}>Rate:</span>
                      <span className={styles.rateValue}>
                        {(config.rate * 100).toFixed(2)}%
                      </span>
                    </div>
                  </div>

                  <div className={styles.configActions}>
                    <button
                      onClick={() => handleToggle(config)}
                      className={styles.toggleButton}
                      title={config.isEnabled ? 'Disable' : 'Enable'}
                    >
                      {config.isEnabled ? (
                        <>
                          <ToggleRight size={20} />
                          Disable
                        </>
                      ) : (
                        <>
                          <ToggleLeft size={20} />
                          Enable
                        </>
                      )}
                    </button>
                    <button
                      onClick={() => handleEdit(config)}
                      className={styles.editButton}
                      title="Edit"
                    >
                      <Edit size={18} />
                    </button>
                    <button
                      onClick={() => handleDelete(config.id)}
                      className={styles.deleteButton}
                      title="Delete"
                    >
                      <Trash2 size={18} />
                    </button>
                  </div>
                </div>
              ))
            )}
          </div>
        )}

        {isFormOpen && (
          <div className={styles.modal}>
            <div className={styles.modalContent}>
              <div className={styles.modalHeader}>
                <h2>{editingConfig ? 'Edit' : 'Create'} Tax Configuration</h2>
                <button
                  onClick={() => setIsFormOpen(false)}
                  className={styles.closeButton}
                >
                  ×
                </button>
              </div>

              <form onSubmit={handleSubmit} className={styles.form}>
                <div className={styles.formGroup}>
                  <label htmlFor="name">Name</label>
                  <input
                    id="name"
                    type="text"
                    value={formData.name}
                    onChange={(e) => setFormData({ ...formData, name: e.target.value })}
                    placeholder="e.g., VAT, Sales Tax"
                    required
                  />
                </div>

                <div className={styles.formGroup}>
                  <label htmlFor="rate">Rate (%)</label>
                  <input
                    id="rate"
                    type="number"
                    step="0.01"
                    min="0"
                    max="100"
                    value={formData.rate * 100}
                    onChange={(e) =>
                      setFormData({ ...formData, rate: parseFloat(e.target.value) / 100 })
                    }
                    placeholder="e.g., 8.00"
                    required
                  />
                  <small>Current rate: {(formData.rate * 100).toFixed(2)}%</small>
                </div>

                <div className={styles.formGroup}>
                  <label htmlFor="description">Description</label>
                  <textarea
                    id="description"
                    value={formData.description}
                    onChange={(e) =>
                      setFormData({ ...formData, description: e.target.value })
                    }
                    placeholder="e.g., Standard VAT rate for Switzerland"
                    rows={3}
                  />
                </div>

                <div className={styles.formGroup}>
                  <label className={styles.checkboxLabel}>
                    <input
                      type="checkbox"
                      checked={formData.isEnabled}
                      onChange={(e) =>
                        setFormData({ ...formData, isEnabled: e.target.checked })
                      }
                    />
                    <span>Enable this tax configuration</span>
                  </label>
                  <small>
                    Note: Enabling this will disable any other active tax configuration
                  </small>
                </div>

                <div className={styles.formActions}>
                  <button
                    type="button"
                    onClick={() => setIsFormOpen(false)}
                    className={styles.cancelButton}
                  >
                    Cancel
                  </button>
                  <button type="submit" className={styles.submitButton}>
                    {editingConfig ? 'Update' : 'Create'}
                  </button>
                </div>
              </form>
            </div>
          </div>
        )}
      </div>
    </AdminAuthGuard>
  );
}
