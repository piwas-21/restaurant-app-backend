'use client';

import React, { useState, useEffect } from 'react';
import { useSnackbar } from 'notistack';
import { useTranslation } from 'react-i18next';
import {
  Trash2,
  Edit,
  Plus,
  User,
  Calendar,
  TrendingUp,
  Filter
} from 'lucide-react';
import { adminFidelityService } from '@/services/adminFidelityService';
import type { CustomerDiscountRule } from '@/types/fidelity';
import CustomerDiscountForm from '@/components/admin/CustomerDiscountForm';
import DeleteDiscountModal from '@/components/admin/DeleteDiscountModal';
import { AdminAuthGuard } from '@/components/admin/AdminAuthGuard';
import styles from './customer-discounts.module.css';

export default function CustomerDiscountsPage() {
  const { t } = useTranslation();
  const { enqueueSnackbar } = useSnackbar();
  const [allDiscounts, setAllDiscounts] = useState<CustomerDiscountRule[]>([]);
  const [discounts, setDiscounts] = useState<CustomerDiscountRule[]>([]);
  const [loading, setLoading] = useState(true);
  const [isFormOpen, setIsFormOpen] = useState(false);
  const [editingDiscount, setEditingDiscount] = useState<CustomerDiscountRule | null>(null);
  const [filterUserId, setFilterUserId] = useState<string>('');
  const [showActiveOnly, setShowActiveOnly] = useState(false);
  const [isDeleteModalOpen, setIsDeleteModalOpen] = useState(false);
  const [discountToDelete, setDiscountToDelete] = useState<CustomerDiscountRule | null>(null);
  const [isDeleting, setIsDeleting] = useState(false);

  useEffect(() => {
    fetchDiscounts();
    // eslint-disable-next-line react-hooks/exhaustive-deps
  }, [showActiveOnly]);

  useEffect(() => {
    if (allDiscounts.length > 0) {
      applyFilters(allDiscounts, filterUserId, showActiveOnly);
    }
    // eslint-disable-next-line react-hooks/exhaustive-deps
  }, [filterUserId]);

  const fetchDiscounts = async () => {
    try {
      setLoading(true);
      const data = await adminFidelityService.getCustomerDiscounts(
        undefined,
        showActiveOnly
      );
      setAllDiscounts(data);
      applyFilters(data, filterUserId, showActiveOnly);
    } catch {
      enqueueSnackbar(t('failed_load_customer_discounts', 'Failed to load customer discounts'), { variant: 'error' });
    } finally {
      setLoading(false);
    }
  };

  const applyFilters = (data: CustomerDiscountRule[], search: string, activeOnly: boolean) => {
    let filtered = data;

    // Filter by search term (userId, userName, or email)
    if (search.trim()) {
      const searchLower = search.toLowerCase().trim();
      filtered = filtered.filter(discount => {
        const userId = discount.userId?.toLowerCase() || '';
        const userName = discount.userName?.toLowerCase() || '';
        const userEmail = discount.userEmail?.toLowerCase() || '';
        return userId.includes(searchLower) || userName.includes(searchLower) || userEmail.includes(searchLower);
      });
    }

    // Filter by active status
    if (activeOnly) {
      filtered = filtered.filter(discount => {
        if (!discount.isActive) return false;
        const now = new Date();
        if (discount.validFrom && new Date(discount.validFrom) > now) return false;
        if (discount.validUntil && new Date(discount.validUntil) < now) return false;
        return true;
      });
    }

    setDiscounts(filtered);
  };

  const handleCreate = () => {
    setEditingDiscount(null);
    setIsFormOpen(true);
  };

  const handleEdit = (discount: CustomerDiscountRule) => {
    setEditingDiscount(discount);
    setIsFormOpen(true);
  };

  const handleDelete = async (discount: CustomerDiscountRule) => {
    setDiscountToDelete(discount);
    setIsDeleteModalOpen(true);
  };

  const handleConfirmDelete = async () => {
    if (!discountToDelete) return;

    try {
      setIsDeleting(true);
      await adminFidelityService.deleteCustomerDiscount(discountToDelete.id);
      enqueueSnackbar(t('discount_deleted_successfully', 'Discount deleted successfully'), { variant: 'success' });
      setIsDeleteModalOpen(false);
      setDiscountToDelete(null);
      fetchDiscounts();
    } catch {
      enqueueSnackbar(t('failed_delete_discount', 'Failed to delete discount'), { variant: 'error' });
    } finally {
      setIsDeleting(false);
    }
  };

  const handleCancelDelete = () => {
    setIsDeleteModalOpen(false);
    setDiscountToDelete(null);
  };

  const handleFormSuccess = () => {
    setIsFormOpen(false);
    setEditingDiscount(null);
    fetchDiscounts();
  };

  const isExpired = (discount: CustomerDiscountRule): boolean => {
    if (!discount.validUntil) return false;
    return new Date(discount.validUntil) < new Date();
  };

  const isNotYetValid = (discount: CustomerDiscountRule): boolean => {
    if (!discount.validFrom) return false;
    return new Date(discount.validFrom) > new Date();
  };

  const getStatusBadge = (discount: CustomerDiscountRule) => {
    if (!discount.isActive) {
      return <span className={`${styles.badge} ${styles.badgeInactive}`}>{t('inactive', 'Inactive')}</span>;
    }
    if (isExpired(discount)) {
      return <span className={`${styles.badge} ${styles.badgeExpired}`}>{t('expired', 'Expired')}</span>;
    }
    if (isNotYetValid(discount)) {
      return <span className={`${styles.badge} ${styles.badgePending}`}>{t('pending', 'Pending')}</span>;
    }
    return <span className={`${styles.badge} ${styles.badgeActive}`}>{t('active', 'Active')}</span>;
  };

  const getUsageStatus = (discount: CustomerDiscountRule) => {
    if (!discount.maxUsageCount) return null;
    const percentage = (discount.usageCount / discount.maxUsageCount) * 100;
    const isNearLimit = percentage >= 80;
    return (
      <span className={`${styles.usageBadge} ${isNearLimit ? styles.usageWarning : ''}`}>
        {t('usage_count', '{{used}}/{{max}} uses', { used: discount.usageCount, max: discount.maxUsageCount })}
      </span>
    );
  };

  const getDiscountTypeTranslation = (discountType: string) => {
    const typeMap: Record<string, string> = {
      'Percentage': 'discount_type_percentage',
      'FixedAmount': 'discount_type_fixed_amount',
    };
    const key = typeMap[discountType] || `discount_type_${discountType}`;
    return t(key, discountType);
  };

  return (
    <AdminAuthGuard>
      <div className={styles.container}>
      <div className={styles.header}>
        <div className={styles.headerLeft}>
          <h1 className={styles.title}>{t('customer_discounts', 'Customer Discounts')}</h1>
          <p className={styles.subtitle}>{t('manage_exclusive_customer_discount_rules', 'Manage exclusive customer discount rules')}</p>
        </div>
        <button onClick={handleCreate} className={styles.createButton}>
          <Plus size={20} />
          {t('create_discount', 'Create Discount')}
        </button>
      </div>

      <div className={styles.filters}>
        <div className={styles.filterGroup}>
          <Filter size={18} />
          <input
            type="text"
            placeholder={t('filter_by_user', 'Filter by User ID, Name, or Email...')}
            value={filterUserId}
            onChange={(e) => setFilterUserId(e.target.value)}
            className={styles.filterInput}
          />
          {filterUserId && (
            <button
              onClick={() => setFilterUserId('')}
              className={styles.clearFilter}
            >
              {t('clear', 'Clear')}
            </button>
          )}
        </div>
        <label className={styles.checkboxLabel}>
          <input
            type="checkbox"
            checked={showActiveOnly}
            onChange={(e) => setShowActiveOnly(e.target.checked)}
          />
          {t('active_only', 'Active only')}
        </label>
      </div>

      {loading ? (
        <div className={styles.loading}>
          <div className={styles.spinner}></div>
          <p>{t('loading_discounts', 'Loading discounts...')}</p>
        </div>
      ) : discounts.length === 0 ? (
        <div className={styles.empty}>
          <TrendingUp size={48} />
          <h3>{t('no_customer_discounts_found', 'No customer discounts found')}</h3>
          <p>{t('create_first_customer_discount', 'Create your first customer discount to get started')}</p>
          <button onClick={handleCreate} className={styles.emptyButton}>
            <Plus size={20} />
            {t('create_first_discount', 'Create First Discount')}
          </button>
        </div>
      ) : (
        <div className={styles.tableWrapper}>
          <table className={styles.table}>
            <thead>
              <tr>
                <th>{t('name', 'Name')}</th>
                <th>{t('customer', 'Customer')}</th>
                <th>{t('type', 'Type')}</th>
                <th>{t('value', 'Value')}</th>
                <th>{t('order_range', 'Order Range')}</th>
                <th>{t('valid_period', 'Valid Period')}</th>
                <th>{t('usage', 'Usage')}</th>
                <th>{t('status', 'Status')}</th>
                <th>{t('actions', 'Actions')}</th>
              </tr>
            </thead>
            <tbody>
              {discounts.map((discount) => (
                <tr key={discount.id}>
                  <td className={styles.nameCell}>{discount.name}</td>
                  <td>
                    <div className={styles.userCell}>
                      <User size={16} />
                      <div className={styles.userInfo}>
                        <span className={styles.userName}>{discount.userName || t('unknown_user', 'Unknown User')}</span>
                        {discount.userEmail && (
                          <span className={styles.userEmail}>{discount.userEmail}</span>
                        )}
                      </div>
                    </div>
                  </td>
                  <td>
                    <span className={`${styles.badge} ${styles.badgeType}`}>
                      {getDiscountTypeTranslation(discount.discountType)}
                    </span>
                  </td>
                  <td className={styles.valueCell}>
                    {discount.discountType === 'Percentage'
                      ? `${discount.discountValue}%`
                      : adminFidelityService.formatCurrency(discount.discountValue)}
                  </td>
                  <td className={styles.rangeCell}>
                    {discount.minOrderAmount ? adminFidelityService.formatCurrency(discount.minOrderAmount) : t('currency_zero', 'CHF 0')}
                    {' → '}
                    {discount.maxOrderAmount ? adminFidelityService.formatCurrency(discount.maxOrderAmount) : '∞'}
                  </td>
                  <td>
                    <div className={styles.dateCell}>
                      <Calendar size={14} />
                      {discount.validFrom ? (
                        <>
                          {adminFidelityService.formatDate(discount.validFrom)}
                          {discount.validUntil && (
                            <>
                              <br />
                              <span className={styles.dateTo}>{t('to', 'to')} {adminFidelityService.formatDate(discount.validUntil)}</span>
                            </>
                          )}
                        </>
                      ) : (
                        <span className={styles.dateUnlimited}>{t('no_restrictions', 'No restrictions')}</span>
                      )}
                    </div>
                  </td>
                  <td>{getUsageStatus(discount)}</td>
                  <td>{getStatusBadge(discount)}</td>
                  <td>
                    <div className={styles.actions}>
                      <button
                        onClick={() => handleEdit(discount)}
                        className={styles.actionButton}
                        title={t('edit', 'Edit')}
                      >
                        <Edit size={18} />
                      </button>
                      <button
                        onClick={() => handleDelete(discount)}
                        className={`${styles.actionButton} ${styles.deleteButton}`}
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

      {isFormOpen && (
        <CustomerDiscountForm
          discount={editingDiscount}
          onClose={() => {
            setIsFormOpen(false);
            setEditingDiscount(null);
          }}
          onSuccess={handleFormSuccess}
        />
      )}

      <DeleteDiscountModal
        isOpen={isDeleteModalOpen}
        discountName={discountToDelete?.name || ''}
        onConfirm={handleConfirmDelete}
        onCancel={handleCancelDelete}
        isLoading={isDeleting}
      />
      </div>
    </AdminAuthGuard>
  );
}
