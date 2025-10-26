"use client";

import React, { useState, useEffect } from 'react';
import { useTranslation } from 'react-i18next';
import { adminFidelityService, CreatePointRuleDto } from '@/services/adminFidelityService';
import type { PointEarningRule } from '@/types/fidelity';
import { X, Loader2 } from 'lucide-react';
import { useSnackbar } from 'notistack';
import styles from './PointRuleForm.module.css';

interface PointRuleFormProps {
  rule: PointEarningRule | null;
  onSuccess: () => void;
  onCancel: () => void;
}

export default function PointRuleForm({ rule, onSuccess, onCancel }: PointRuleFormProps) {
  const { t } = useTranslation();
  const { enqueueSnackbar } = useSnackbar();
  const [submitting, setSubmitting] = useState(false);

  const [formData, setFormData] = useState<CreatePointRuleDto>({
    name: '',
    minOrderAmount: 0,
    maxOrderAmount: undefined,
    pointsAwarded: 0,
    isActive: true,
    priority: 0,
  });

  useEffect(() => {
    if (rule) {
      setFormData({
        name: rule.name,
        minOrderAmount: rule.minOrderAmount,
        maxOrderAmount: rule.maxOrderAmount,
        pointsAwarded: rule.pointsAwarded,
        isActive: rule.isActive,
        priority: rule.priority,
      });
    }
  }, [rule]);

  const handleChange = (e: React.ChangeEvent<HTMLInputElement>) => {
    const { name, value, type, checked } = e.target;

    if (type === 'checkbox') {
      setFormData(prev => ({ ...prev, [name]: checked }));
    } else if (type === 'number') {
      const numValue = value === '' ? undefined : parseFloat(value);
      setFormData(prev => ({ ...prev, [name]: numValue }));
    } else {
      setFormData(prev => ({ ...prev, [name]: value }));
    }
  };

  const handleSubmit = async (e: React.FormEvent) => {
    e.preventDefault();

    // Validation
    if (!formData.name.trim()) {
      enqueueSnackbar(t('name_required', 'Name is required'), { variant: 'error' });
      return;
    }

    if (formData.minOrderAmount < 0) {
      enqueueSnackbar(t('min_amount_invalid', 'Minimum order amount must be >= 0'), {
        variant: 'error',
      });
      return;
    }

    if (
      formData.maxOrderAmount !== undefined &&
      formData.maxOrderAmount <= formData.minOrderAmount
    ) {
      enqueueSnackbar(
        t('max_amount_invalid', 'Maximum order amount must be greater than minimum'),
        { variant: 'error' }
      );
      return;
    }

    if (formData.pointsAwarded <= 0) {
      enqueueSnackbar(t('points_invalid', 'Points awarded must be greater than 0'), {
        variant: 'error',
      });
      return;
    }

    try {
      setSubmitting(true);

      if (rule) {
        // Update existing rule
        await adminFidelityService.updatePointRule(rule.id, {
          ...formData,
          id: rule.id,
        });
        enqueueSnackbar(t('rule_updated', 'Point rule updated successfully'), {
          variant: 'success',
        });
      } else {
        // Create new rule
        await adminFidelityService.createPointRule(formData);
        enqueueSnackbar(t('rule_created', 'Point rule created successfully'), {
          variant: 'success',
        });
      }

      onSuccess();
    } catch (error: any) {
      // Parse error message for better user feedback
      let errorMessage = t('error_saving_rule', 'Failed to save point rule');

      if (error?.response?.data) {
        const errorData = error.response.data;

        // Check if errors array exists (our API format)
        if (Array.isArray(errorData.errors) && errorData.errors.length > 0) {
          const firstError = errorData.errors[0];

          // Check for overlap error
          if (firstError.toLowerCase().includes('overlap')) {
            // Extract range information: "Rule overlaps with existing rule. Range: $0 - $11"
            const rangeMatch = firstError.match(/Range:\s*\$?([\d.]+)\s*-\s*\$?([\d.]+|unlimited)/i);

            if (rangeMatch) {
              const minAmount = rangeMatch[1];
              const maxAmount = rangeMatch[2];
              errorMessage = `This rule overlaps with an existing rule covering $${minAmount} - $${maxAmount === 'unlimited' ? 'unlimited' : '$' + maxAmount}. Please adjust your order amount range to avoid conflicts with existing rules.`;
            } else {
              errorMessage = `This rule overlaps with an existing rule. Please adjust the order amount range to avoid conflicts.`;
            }
          } else {
            // Use the first error message directly
            errorMessage = firstError;
          }
        }
        // Check if errorData is a string
        else if (typeof errorData === 'string') {
          errorMessage = errorData;
        }
        // Check for message property
        else if (errorData.message) {
          errorMessage = errorData.message;
        }
        // Check if errors is an object (validation errors)
        else if (errorData.errors && typeof errorData.errors === 'object') {
          const errorMessages = Object.values(errorData.errors).flat();
          errorMessage = errorMessages.join(', ');
        }
      }
      // Check if error has a message property directly
      else if (error?.message) {
        errorMessage = error.message;
      }

      enqueueSnackbar(errorMessage, {
        variant: 'error',
        autoHideDuration: 8000, // Show longer for complex messages
      });
    } finally {
      setSubmitting(false);
    }
  };

  return (
    <div className={styles.modalOverlay} onClick={onCancel}>
      <div className={styles.modal} onClick={(e) => e.stopPropagation()}>
        <div className={styles.modalHeader}>
          <h2 className={styles.modalTitle}>
            {rule
              ? t('edit_point_rule', 'Edit Point Rule')
              : t('create_point_rule', 'Create Point Rule')}
          </h2>
          <button onClick={onCancel} className={styles.closeButton} aria-label={t('close', 'Close')}>
            <X size={24} />
          </button>
        </div>

        <form onSubmit={handleSubmit} className={styles.form}>
          <div className={styles.formGroup}>
            <label htmlFor="name" className={styles.label}>
              {t('rule_name', 'Rule Name')} *
            </label>
            <input
              type="text"
              id="name"
              name="name"
              value={formData.name}
              onChange={handleChange}
              placeholder={t('rule_name_placeholder', 'e.g., Bronze Tier')}
              className={styles.input}
              required
            />
          </div>

          <div className={styles.formRow}>
            <div className={styles.formGroup}>
              <label htmlFor="minOrderAmount" className={styles.label}>
                {t('min_order_amount', 'Min Order Amount')} * ($)
              </label>
              <input
                type="number"
                id="minOrderAmount"
                name="minOrderAmount"
                value={formData.minOrderAmount}
                onChange={handleChange}
                min="0"
                step="0.01"
                className={styles.input}
                required
              />
            </div>

            <div className={styles.formGroup}>
              <label htmlFor="maxOrderAmount" className={styles.label}>
                {t('max_order_amount', 'Max Order Amount')} ($)
              </label>
              <input
                type="number"
                id="maxOrderAmount"
                name="maxOrderAmount"
                value={formData.maxOrderAmount ?? ''}
                onChange={handleChange}
                min="0"
                step="0.01"
                placeholder={t('unlimited', 'Unlimited')}
                className={styles.input}
              />
            </div>
          </div>

          <div className={styles.formRow}>
            <div className={styles.formGroup}>
              <label htmlFor="pointsAwarded" className={styles.label}>
                {t('points_awarded', 'Points Awarded')} *
              </label>
              <input
                type="number"
                id="pointsAwarded"
                name="pointsAwarded"
                value={formData.pointsAwarded}
                onChange={handleChange}
                min="1"
                step="1"
                className={styles.input}
                required
              />
            </div>

            <div className={styles.formGroup}>
              <label htmlFor="priority" className={styles.label}>
                {t('priority', 'Priority')} *
              </label>
              <input
                type="number"
                id="priority"
                name="priority"
                value={formData.priority}
                onChange={handleChange}
                min="0"
                step="1"
                className={styles.input}
                required
              />
              <small className={styles.helpText}>
                {t('priority_help', 'Lower numbers have higher priority')}
              </small>
            </div>
          </div>

          <div className={styles.formGroup}>
            <label className={styles.checkboxLabel}>
              <input
                type="checkbox"
                name="isActive"
                checked={formData.isActive}
                onChange={handleChange}
                className={styles.checkbox}
              />
              {t('active', 'Active')}
            </label>
          </div>

          <div className={styles.formActions}>
            <button type="button" onClick={onCancel} className={styles.cancelButton} disabled={submitting}>
              {t('cancel', 'Cancel')}
            </button>
            <button type="submit" className={styles.submitButton} disabled={submitting}>
              {submitting ? (
                <>
                  <Loader2 size={18} className={styles.spinner} />
                  {t('saving', 'Saving...')}
                </>
              ) : (
                t('save', 'Save')
              )}
            </button>
          </div>
        </form>
      </div>
    </div>
  );
}
