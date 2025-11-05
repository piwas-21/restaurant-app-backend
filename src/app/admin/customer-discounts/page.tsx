'use client';

import React, { useState, useEffect } from 'react';
import { useSnackbar } from 'notistack';
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
import { AdminAuthGuard } from '@/components/admin/AdminAuthGuard';
import styles from './customer-discounts.module.css';

export default function CustomerDiscountsPage() {
  const { enqueueSnackbar } = useSnackbar();
  const [discounts, setDiscounts] = useState<CustomerDiscountRule[]>([]);
  const [loading, setLoading] = useState(true);
  const [isFormOpen, setIsFormOpen] = useState(false);
  const [editingDiscount, setEditingDiscount] = useState<CustomerDiscountRule | null>(null);
  const [filterUserId, setFilterUserId] = useState<string>('');
  const [showActiveOnly, setShowActiveOnly] = useState(false);

  useEffect(() => {
    fetchDiscounts();
    // eslint-disable-next-line react-hooks/exhaustive-deps
  }, [filterUserId, showActiveOnly]);

  const fetchDiscounts = async () => {
    try {
      setLoading(true);
      const data = await adminFidelityService.getCustomerDiscounts(
        filterUserId || undefined,
        showActiveOnly
      );
      setDiscounts(data);
    } catch {
      enqueueSnackbar('Failed to load customer discounts', { variant: 'error' });
    } finally {
      setLoading(false);
    }
  };

  const handleCreate = () => {
    setEditingDiscount(null);
    setIsFormOpen(true);
  };

  const handleEdit = (discount: CustomerDiscountRule) => {
    setEditingDiscount(discount);
    setIsFormOpen(true);
  };

  const handleDelete = async (id: string) => {
    if (!confirm('Are you sure you want to delete this discount?')) return;

    try {
      await adminFidelityService.deleteCustomerDiscount(id);
      enqueueSnackbar('Discount deleted successfully', { variant: 'success' });
      fetchDiscounts();
    } catch {
      enqueueSnackbar('Failed to delete discount', { variant: 'error' });
    }
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
      return <span className={`${styles.badge} ${styles.badgeInactive}`}>Inactive</span>;
    }
    if (isExpired(discount)) {
      return <span className={`${styles.badge} ${styles.badgeExpired}`}>Expired</span>;
    }
    if (isNotYetValid(discount)) {
      return <span className={`${styles.badge} ${styles.badgePending}`}>Pending</span>;
    }
    return <span className={`${styles.badge} ${styles.badgeActive}`}>Active</span>;
  };

  const getUsageStatus = (discount: CustomerDiscountRule) => {
    if (!discount.maxUsageCount) return null;
    const percentage = (discount.usageCount / discount.maxUsageCount) * 100;
    const isNearLimit = percentage >= 80;
    return (
      <span className={`${styles.usageBadge} ${isNearLimit ? styles.usageWarning : ''}`}>
        {discount.usageCount}/{discount.maxUsageCount} uses
      </span>
    );
  };

  return (
    <AdminAuthGuard>
      <div className={styles.container}>
      <div className={styles.header}>
        <div className={styles.headerLeft}>
          <h1 className={styles.title}>Customer Discounts</h1>
          <p className={styles.subtitle}>Manage exclusive customer discount rules</p>
        </div>
        <button onClick={handleCreate} className={styles.createButton}>
          <Plus size={20} />
          Create Discount
        </button>
      </div>

      <div className={styles.filters}>
        <div className={styles.filterGroup}>
          <Filter size={18} />
          <input
            type="text"
            placeholder="Filter by User ID..."
            value={filterUserId}
            onChange={(e) => setFilterUserId(e.target.value)}
            className={styles.filterInput}
          />
          {filterUserId && (
            <button
              onClick={() => setFilterUserId('')}
              className={styles.clearFilter}
            >
              Clear
            </button>
          )}
        </div>
        <label className={styles.checkboxLabel}>
          <input
            type="checkbox"
            checked={showActiveOnly}
            onChange={(e) => setShowActiveOnly(e.target.checked)}
          />
          Active only
        </label>
      </div>

      {loading ? (
        <div className={styles.loading}>
          <div className={styles.spinner}></div>
          <p>Loading discounts...</p>
        </div>
      ) : discounts.length === 0 ? (
        <div className={styles.empty}>
          <TrendingUp size={48} />
          <h3>No customer discounts found</h3>
          <p>Create your first customer discount to get started</p>
          <button onClick={handleCreate} className={styles.emptyButton}>
            <Plus size={20} />
            Create First Discount
          </button>
        </div>
      ) : (
        <div className={styles.tableWrapper}>
          <table className={styles.table}>
            <thead>
              <tr>
                <th>Name</th>
                <th>Customer</th>
                <th>Type</th>
                <th>Value</th>
                <th>Order Range</th>
                <th>Valid Period</th>
                <th>Usage</th>
                <th>Status</th>
                <th>Actions</th>
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
                        <span className={styles.userName}>{discount.userName || 'Unknown User'}</span>
                        {discount.userEmail && (
                          <span className={styles.userEmail}>{discount.userEmail}</span>
                        )}
                      </div>
                    </div>
                  </td>
                  <td>
                    <span className={`${styles.badge} ${styles.badgeType}`}>
                      {adminFidelityService.getDiscountTypeLabel(discount.discountType)}
                    </span>
                  </td>
                  <td className={styles.valueCell}>
                    {discount.discountType === 'Percentage'
                      ? `${discount.discountValue}%`
                      : adminFidelityService.formatCurrency(discount.discountValue)}
                  </td>
                  <td className={styles.rangeCell}>
                    {discount.minOrderAmount ? adminFidelityService.formatCurrency(discount.minOrderAmount) : 'CHF 0'}
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
                              <span className={styles.dateTo}>to {adminFidelityService.formatDate(discount.validUntil)}</span>
                            </>
                          )}
                        </>
                      ) : (
                        <span className={styles.dateUnlimited}>No restrictions</span>
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
                        title="Edit"
                      >
                        <Edit size={18} />
                      </button>
                      <button
                        onClick={() => handleDelete(discount.id)}
                        className={`${styles.actionButton} ${styles.deleteButton}`}
                        title="Delete"
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
      </div>
    </AdminAuthGuard>
  );
}
