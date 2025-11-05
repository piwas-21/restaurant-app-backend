"use client";

import React, { useState, useEffect } from 'react';
import { useTranslation } from 'react-i18next';
import { adminFidelityService } from '@/services/adminFidelityService';
import type { PointEarningRule } from '@/types/fidelity';
import { Plus, Edit, Trash2, CheckCircle, XCircle, Loader2 } from 'lucide-react';
import { useSnackbar } from 'notistack';
import PointRuleForm from '@/components/admin/PointRuleForm';
import { AdminAuthGuard } from '@/components/admin/AdminAuthGuard';
import styles from './point-rules.module.css';

export default function PointRulesPage() {
  const { t } = useTranslation();
  const { enqueueSnackbar } = useSnackbar();
  const [rules, setRules] = useState<PointEarningRule[]>([]);
  const [loading, setLoading] = useState(true);
  const [showForm, setShowForm] = useState(false);
  const [editingRule, setEditingRule] = useState<PointEarningRule | null>(null);
  const [activeOnly, setActiveOnly] = useState(false);

  const loadRules = async () => {
    try {
      setLoading(true);
      const data = await adminFidelityService.getPointRules(activeOnly);
      setRules(data);
    } catch {
      enqueueSnackbar(t('error_loading_rules', 'Failed to load point rules'), {
        variant: 'error',
      });
    } finally {
      setLoading(false);
    }
  };

  useEffect(() => {
    loadRules();
    // eslint-disable-next-line react-hooks/exhaustive-deps
  }, [activeOnly]);

  const handleCreate = () => {
    setEditingRule(null);
    setShowForm(true);
  };

  const handleEdit = (rule: PointEarningRule) => {
    setEditingRule(rule);
    setShowForm(true);
  };

  const handleDelete = async (id: string) => {
    if (!confirm(t('confirm_delete_rule', 'Are you sure you want to delete this rule?'))) {
      return;
    }

    try {
      await adminFidelityService.deletePointRule(id);
      enqueueSnackbar(t('rule_deleted', 'Point rule deleted successfully'), {
        variant: 'success',
      });
      loadRules();
    } catch {
      enqueueSnackbar(t('error_deleting_rule', 'Failed to delete rule'), {
        variant: 'error',
      });
    }
  };

  const handleFormSuccess = () => {
    setShowForm(false);
    setEditingRule(null);
    loadRules();
  };

  const handleFormCancel = () => {
    setShowForm(false);
    setEditingRule(null);
  };

  return (
    <AdminAuthGuard>
      <main className={styles.container}>
      <div className={styles.header}>
        <div>
          <h1 className={styles.title}>{t('point_earning_rules', 'Point Earning Rules')}</h1>
          <p className={styles.subtitle}>
            {t('point_rules_desc', 'Manage point earning rules based on order amounts')}
          </p>
        </div>
        <button onClick={handleCreate} className={styles.createButton}>
          <Plus size={20} />
          {t('create_rule', 'Create Rule')}
        </button>
      </div>

      {/* Filter */}
      <div className={styles.filters}>
        <label className={styles.filterLabel}>
          <input
            type="checkbox"
            checked={activeOnly}
            onChange={(e) => setActiveOnly(e.target.checked)}
            className={styles.checkbox}
          />
          {t('show_active_only', 'Show active only')}
        </label>
      </div>

      {/* Table */}
      {loading ? (
        <div className={styles.loadingContainer}>
          <Loader2 size={48} className={styles.spinner} />
          <p>{t('loading', 'Loading...')}</p>
        </div>
      ) : rules.length === 0 ? (
        <div className={styles.emptyState}>
          <p>{t('no_rules_found', 'No point earning rules found')}</p>
          <button onClick={handleCreate} className={styles.createButtonSecondary}>
            <Plus size={20} />
            {t('create_first_rule', 'Create your first rule')}
          </button>
        </div>
      ) : (
        <div className={styles.tableContainer}>
          <table className={styles.table}>
            <thead>
              <tr>
                <th>{t('name', 'Name')}</th>
                <th>{t('order_amount_range', 'Order Amount Range')}</th>
                <th>{t('points_awarded', 'Points Awarded')}</th>
                <th>{t('priority', 'Priority')}</th>
                <th>{t('status', 'Status')}</th>
                <th>{t('actions', 'Actions')}</th>
              </tr>
            </thead>
            <tbody>
              {rules.map((rule) => (
                <tr key={rule.id}>
                  <td className={styles.nameCell}>{rule.name}</td>
                  <td>
                    CHF {rule.minOrderAmount.toFixed(2)} -{' '}
                    {rule.maxOrderAmount ? `CHF ${rule.maxOrderAmount.toFixed(2)}` : '∞'}
                  </td>
                  <td className={styles.pointsCell}>
                    <span className={styles.pointsBadge}>{rule.pointsAwarded} pts</span>
                  </td>
                  <td>{rule.priority}</td>
                  <td>
                    {rule.isActive ? (
                      <span className={styles.statusActive}>
                        <CheckCircle size={16} />
                        {t('active', 'Active')}
                      </span>
                    ) : (
                      <span className={styles.statusInactive}>
                        <XCircle size={16} />
                        {t('inactive', 'Inactive')}
                      </span>
                    )}
                  </td>
                  <td>
                    <div className={styles.actions}>
                      <button
                        onClick={() => handleEdit(rule)}
                        className={styles.editButton}
                        title={t('edit', 'Edit')}
                      >
                        <Edit size={18} />
                      </button>
                      <button
                        onClick={() => handleDelete(rule.id)}
                        className={styles.deleteButton}
                        title={t('delete', 'Delete')}
                      >
                        <Trash2 size={18} />
                      </button>
                    </div>
                  </td>
                </tr>
              ))}
            </tbody>
          </table>
        </div>
      )}

      {/* Form Modal */}
      {showForm && (
        <PointRuleForm
          rule={editingRule}
          onSuccess={handleFormSuccess}
          onCancel={handleFormCancel}
        />
      )}
      </main>
    </AdminAuthGuard>
  );
}
