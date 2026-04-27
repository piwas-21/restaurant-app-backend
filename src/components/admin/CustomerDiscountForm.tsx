'use client';

import React, { useState, useEffect, useRef, useCallback } from 'react';
import { useSnackbar } from 'notistack';
import { useTranslation } from 'react-i18next';
import { X, Save, Loader2, Search, UserCheck } from 'lucide-react';
import {
  adminFidelityService,
  CreateCustomerDiscountDto,
  UpdateCustomerDiscountDto,
} from '@/services/adminFidelityService';
import { fetchUsers, UserDto } from '@/services/userService';
import { UserRole } from '@/types/user';
import type { CustomerDiscountRule } from '@/types/fidelity';
import styles from './CustomerDiscountForm.module.css';

interface CustomerDiscountFormProps {
  discount: CustomerDiscountRule | null;
  onClose: () => void;
  onSuccess: () => void;
}

export default function CustomerDiscountForm({ discount, onClose, onSuccess }: CustomerDiscountFormProps) {
  const { t } = useTranslation();
  const { enqueueSnackbar } = useSnackbar();
  const [loading, setLoading] = useState(false);
  const [searchLoading, setSearchLoading] = useState(false);
  const [searchQuery, setSearchQuery] = useState('');
  const [searchResults, setSearchResults] = useState<UserDto[]>([]);
  const [selectedUser, setSelectedUser] = useState<UserDto | null>(null);
  const [showSearchResults, setShowSearchResults] = useState(false);
  const searchTimeoutRef = useRef<NodeJS.Timeout | null>(null);
  const [formData, setFormData] = useState({
    userId: '',
    name: '',
    discountType: 'Percentage' as 'Percentage' | 'FixedAmount',
    discountValue: '',
    minOrderAmount: '',
    maxOrderAmount: '',
    hasMaxOrderAmount: false,
    maxUsageCount: '',
    hasMaxUsageCount: false,
    isActive: true,
    validFrom: '',
    validUntil: '',
    hasValidFrom: false,
    hasValidUntil: false,
  });

  useEffect(() => {
    if (discount) {
      setFormData({
        userId: discount.userId,
        name: discount.name,
        discountType: discount.discountType,
        discountValue: discount.discountValue.toString(),
        minOrderAmount: discount.minOrderAmount ? discount.minOrderAmount.toString() : '',
        maxOrderAmount: discount.maxOrderAmount ? discount.maxOrderAmount.toString() : '',
        hasMaxOrderAmount: !!discount.maxOrderAmount,
        maxUsageCount: discount.maxUsageCount ? discount.maxUsageCount.toString() : '',
        hasMaxUsageCount: !!discount.maxUsageCount,
        isActive: discount.isActive,
        validFrom: discount.validFrom ? new Date(discount.validFrom).toISOString().slice(0, 16) : '',
        validUntil: discount.validUntil ? new Date(discount.validUntil).toISOString().slice(0, 16) : '',
        hasValidFrom: !!discount.validFrom,
        hasValidUntil: !!discount.validUntil,
      });
      // Set selected user for display in edit mode
      setSelectedUser({
        id: discount.userId,
        email: discount.userEmail || '',
        firstName: '',
        lastName: '',
        fullName: discount.userName || discount.userEmail || `User ${discount.userId}`,
        role: UserRole.Customer,
        isEmailConfirmed: false,
        createdAt: '',
        metadata: {},
        orderLimitAmount: 0,
        discountPercentage: 0,
        isDiscountActive: false,
        isDeleted: false,
      });
    }
  }, [discount]);

  // Debounced user search
  const handleSearchChange = useCallback(
    (query: string) => {
      setSearchQuery(query);

      if (searchTimeoutRef.current) {
        clearTimeout(searchTimeoutRef.current);
      }

      if (query.trim().length < 2) {
        setSearchResults([]);
        setShowSearchResults(false);
        return;
      }

      setSearchLoading(true);
      setShowSearchResults(true);

      searchTimeoutRef.current = setTimeout(async () => {
        try {
          const response = (await fetchUsers('', false, query, 1, 10)) as { success: boolean; data?: { items: any[] } };
          if (response.success && response.data?.items) {
            setSearchResults(response.data.items);
          } else {
            setSearchResults([]);
          }
        } catch (error) {
          console.error('Error searching users:', error);
          setSearchResults([]);
          enqueueSnackbar(t('failed_search_users', 'Failed to search users'), { variant: 'error' });
        } finally {
          setSearchLoading(false);
        }
      }, 500); // 500ms debounce
    },
    [enqueueSnackbar, t],
  );

  const handleUserSelect = useCallback((user: UserDto) => {
    setSelectedUser(user);
    setFormData((prev) => ({ ...prev, userId: user.id }));
    setSearchQuery('');
    setSearchResults([]);
    setShowSearchResults(false);
  }, []);

  const handleClearUser = useCallback(() => {
    setSelectedUser(null);
    setFormData((prev) => ({ ...prev, userId: '' }));
  }, []);

  const handleChange = (e: React.ChangeEvent<HTMLInputElement | HTMLSelectElement>) => {
    const { name, value, type } = e.target;
    const checked = (e.target as HTMLInputElement).checked;

    setFormData((prev) => ({
      ...prev,
      [name]: type === 'checkbox' ? checked : value,
    }));
  };

  const validateForm = (): string | null => {
    if (!formData.userId.trim()) return t('user_id_required', 'User ID is required');
    if (!formData.name.trim()) return t('name_required', 'Name is required');

    const discountValue = parseFloat(formData.discountValue);
    if (!formData.discountValue || isNaN(discountValue) || discountValue <= 0) {
      return t('discount_value_must_be_greater_than_zero', 'Discount value must be greater than 0');
    }
    if (formData.discountType === 'Percentage' && discountValue > 100) {
      return t('percentage_discount_cannot_exceed_100', 'Percentage discount cannot exceed 100%');
    }

    const minOrderAmount = parseFloat(formData.minOrderAmount || '0');
    if (isNaN(minOrderAmount) || minOrderAmount < 0) {
      return t('min_order_amount_cannot_be_negative', 'Minimum order amount cannot be negative');
    }

    if (formData.hasMaxOrderAmount) {
      const maxOrderAmount = parseFloat(formData.maxOrderAmount);
      if (!formData.maxOrderAmount || isNaN(maxOrderAmount) || maxOrderAmount <= minOrderAmount) {
        return t('max_order_amount_must_be_greater_than_min', 'Maximum order amount must be greater than minimum');
      }
    }

    if (formData.hasMaxUsageCount) {
      const maxUsageCount = parseInt(formData.maxUsageCount);
      if (!formData.maxUsageCount || isNaN(maxUsageCount) || maxUsageCount <= 0) {
        return t('max_usage_count_must_be_greater_than_zero', 'Max usage count must be greater than 0');
      }
    }

    if (formData.hasValidFrom && formData.hasValidUntil) {
      const from = new Date(formData.validFrom);
      const until = new Date(formData.validUntil);
      if (until <= from) {
        return t('valid_until_must_be_after_valid_from', 'Valid until date must be after valid from date');
      }
    }
    return null;
  };

  const handleSubmit = async (e: React.FormEvent) => {
    e.preventDefault();

    const validationError = validateForm();
    if (validationError) {
      enqueueSnackbar(validationError, { variant: 'warning' });
      return;
    }

    setLoading(true);

    try {
      const dto: CreateCustomerDiscountDto | UpdateCustomerDiscountDto = {
        userId: formData.userId,
        name: formData.name,
        discountType: formData.discountType,
        discountValue: parseFloat(formData.discountValue),
        minOrderAmount: formData.minOrderAmount ? parseFloat(formData.minOrderAmount) : undefined,
        maxOrderAmount:
          formData.hasMaxOrderAmount && formData.maxOrderAmount ? parseFloat(formData.maxOrderAmount) : undefined,
        maxUsageCount:
          formData.hasMaxUsageCount && formData.maxUsageCount ? parseInt(formData.maxUsageCount) : undefined,
        isActive: formData.isActive,
        validFrom: formData.hasValidFrom ? new Date(formData.validFrom).toISOString() : undefined,
        validUntil: formData.hasValidUntil ? new Date(formData.validUntil).toISOString() : undefined,
      };

      if (discount) {
        await adminFidelityService.updateCustomerDiscount(discount.id, dto as UpdateCustomerDiscountDto);
        enqueueSnackbar(t('discount_updated_successfully', 'Discount updated successfully'), { variant: 'success' });
      } else {
        await adminFidelityService.createCustomerDiscount(dto);
        enqueueSnackbar(t('discount_created_successfully', 'Discount created successfully'), { variant: 'success' });
      }

      onSuccess();
    } catch (error: any) {
      // Parse error message for better user feedback
      let errorMessage = t(
        discount ? 'failed_update_discount' : 'failed_create_discount',
        `Failed to ${discount ? 'update' : 'create'} discount`,
      );

      if (error?.response?.data) {
        const errorData = error.response.data;

        // Check if errors array exists (our API format)
        if (Array.isArray(errorData.errors) && errorData.errors.length > 0) {
          const firstError = errorData.errors[0];

          // Check for user not found error
          if (firstError.toLowerCase().includes('user') && firstError.toLowerCase().includes('not found')) {
            errorMessage = t(
              'user_not_found_error',
              'User with ID "{{userId}}" was not found. Please verify the user ID and try again.',
              { userId: formData.userId },
            );
          }
          // Check for duplicate discount error
          else if (firstError.toLowerCase().includes('already exists')) {
            errorMessage = t(
              'discount_already_exists_error',
              'A discount already exists for this user. Please edit the existing discount instead of creating a new one.',
            );
          }
          // Check for validation errors
          else if (firstError.toLowerCase().includes('invalid')) {
            errorMessage = firstError;
          } else {
            // Use the first error message directly
            errorMessage = firstError;
          }
        }
        // Check if errorData is a string (fallback)
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

      enqueueSnackbar(errorMessage, {
        variant: 'error',
        autoHideDuration: 8000, // Show longer for complex messages
      });
    } finally {
      setLoading(false);
    }
  };

  return (
    <div className={styles.overlay} onClick={onClose}>
      <div className={styles.modal} onClick={(e) => e.stopPropagation()}>
        <div className={styles.header}>
          <h2>{discount ? t('edit_discount', 'Edit Discount') : t('create_new_discount', 'Create New Discount')}</h2>
          <button onClick={onClose} className={styles.closeButton} disabled={loading}>
            <X size={24} />
          </button>
        </div>

        <form onSubmit={handleSubmit} className={styles.form}>
          <div className={styles.formGrid}>
            <div className={styles.formGroup}>
              <label htmlFor="userSearch">
                {t('customer', 'Customer')} <span className={styles.required}>*</span>
              </label>

              {selectedUser ? (
                <div className={styles.selectedUserCard}>
                  <div className={styles.selectedUserInfo}>
                    <UserCheck size={20} />
                    <div>
                      <div className={styles.selectedUserName}>
                        {selectedUser.fullName || `${selectedUser.firstName} ${selectedUser.lastName}`}
                      </div>
                      <div className={styles.selectedUserEmail}>
                        {selectedUser.email || `${t('id', 'ID')}: ${selectedUser.id}`}
                      </div>
                    </div>
                  </div>
                  {!discount && (
                    <button
                      type="button"
                      onClick={handleClearUser}
                      className={styles.clearUserButton}
                      disabled={loading}
                    >
                      <X size={16} />
                    </button>
                  )}
                </div>
              ) : (
                <div className={styles.searchContainer}>
                  <div className={styles.searchInputWrapper}>
                    <Search size={18} className={styles.searchIcon} />
                    <input
                      type="text"
                      id="userSearch"
                      value={searchQuery}
                      onChange={(e) => handleSearchChange(e.target.value)}
                      disabled={loading}
                      className={styles.searchInput}
                      placeholder={t('search_by_name_or_email', 'Search by name or email...')}
                      autoComplete="off"
                    />
                    {searchLoading && <Loader2 size={18} className={`${styles.searchIcon} ${styles.spinner}`} />}
                  </div>

                  {showSearchResults && searchResults.length > 0 && (
                    <div className={styles.searchResults}>
                      {searchResults.map((user) => (
                        <button
                          key={user.id}
                          type="button"
                          onClick={() => handleUserSelect(user)}
                          className={styles.searchResultItem}
                        >
                          <div className={styles.searchResultInfo}>
                            <div className={styles.searchResultName}>
                              {user.fullName || `${user.firstName} ${user.lastName}`}
                            </div>
                            <div className={styles.searchResultEmail}>{user.email}</div>
                          </div>
                        </button>
                      ))}
                    </div>
                  )}

                  {showSearchResults && searchResults.length === 0 && !searchLoading && (
                    <div className={styles.searchNoResults}>
                      {t('no_users_found_matching', 'No users found matching "{{query}}"', { query: searchQuery })}
                    </div>
                  )}
                </div>
              )}

              <small className={styles.help}>
                {selectedUser
                  ? t('selected_customer_for_discount', 'Selected customer for this discount')
                  : t('search_and_select_customer', 'Search and select a customer')}
              </small>
            </div>

            <div className={styles.formGroup}>
              <label htmlFor="name">
                {t('name', 'Name')} <span className={styles.required}>*</span>
              </label>
              <input
                type="text"
                id="name"
                name="name"
                value={formData.name}
                onChange={handleChange}
                disabled={loading}
                className={styles.input}
                placeholder={t('discount_name_placeholder', 'e.g., VIP 10% Discount')}
              />
            </div>
          </div>

          <div className={styles.formGrid}>
            <div className={styles.formGroup}>
              <label htmlFor="discountType">
                {t('discount_type', 'Discount Type')} <span className={styles.required}>*</span>
              </label>
              <select
                id="discountType"
                name="discountType"
                value={formData.discountType}
                onChange={handleChange}
                disabled={loading}
                className={styles.select}
              >
                <option value="Percentage">{t('percentage', 'Percentage')}</option>
                <option value="FixedAmount">{t('fixed_amount', 'Fixed Amount')}</option>
              </select>
            </div>

            <div className={styles.formGroup}>
              <label htmlFor="discountValue">
                {t('discount_value', 'Discount Value')} <span className={styles.required}>*</span>
              </label>
              <input
                type="text"
                inputMode="decimal"
                id="discountValue"
                name="discountValue"
                value={formData.discountValue}
                onChange={handleChange}
                disabled={loading}
                className={styles.input}
                placeholder={formData.discountType === 'Percentage' ? '10' : '5.00'}
              />
              <small className={styles.help}>
                {formData.discountType === 'Percentage'
                  ? t('enter_percentage_0_100', 'Enter percentage (0-100)')
                  : t('enter_chf_amount', 'Enter CHF amount')}
              </small>
            </div>
          </div>

          <div className={styles.formGrid}>
            <div className={styles.formGroup}>
              <label htmlFor="minOrderAmount">{t('minimum_order_amount', 'Minimum Order Amount')}</label>
              <input
                type="text"
                inputMode="decimal"
                id="minOrderAmount"
                name="minOrderAmount"
                value={formData.minOrderAmount}
                onChange={handleChange}
                disabled={loading}
                className={styles.input}
                placeholder="0.00"
              />
            </div>

            <div className={styles.formGroup}>
              <label>
                <input
                  type="checkbox"
                  name="hasMaxOrderAmount"
                  checked={formData.hasMaxOrderAmount}
                  onChange={handleChange}
                  disabled={loading}
                  className={styles.checkbox}
                />
                {t('set_maximum_order_amount', 'Set Maximum Order Amount')}
              </label>
              {formData.hasMaxOrderAmount && (
                <input
                  type="text"
                  inputMode="decimal"
                  name="maxOrderAmount"
                  value={formData.maxOrderAmount}
                  onChange={handleChange}
                  disabled={loading}
                  className={styles.input}
                  placeholder="100.00"
                />
              )}
            </div>
          </div>

          <div className={styles.formGrid}>
            <div className={styles.formGroup}>
              <label>
                <input
                  type="checkbox"
                  name="hasValidFrom"
                  checked={formData.hasValidFrom}
                  onChange={handleChange}
                  disabled={loading}
                  className={styles.checkbox}
                />
                {t('set_valid_from_date', 'Set Valid From Date')}
              </label>
              {formData.hasValidFrom && (
                <input
                  type="datetime-local"
                  name="validFrom"
                  value={formData.validFrom}
                  onChange={handleChange}
                  disabled={loading}
                  className={styles.input}
                />
              )}
            </div>

            <div className={styles.formGroup}>
              <label>
                <input
                  type="checkbox"
                  name="hasValidUntil"
                  checked={formData.hasValidUntil}
                  onChange={handleChange}
                  disabled={loading}
                  className={styles.checkbox}
                />
                {t('set_valid_until_date', 'Set Valid Until Date')}
              </label>
              {formData.hasValidUntil && (
                <input
                  type="datetime-local"
                  name="validUntil"
                  value={formData.validUntil}
                  onChange={handleChange}
                  disabled={loading}
                  className={styles.input}
                />
              )}
            </div>
          </div>

          <div className={styles.formGrid}>
            <div className={styles.formGroup}>
              <label>
                <input
                  type="checkbox"
                  name="hasMaxUsageCount"
                  checked={formData.hasMaxUsageCount}
                  onChange={handleChange}
                  disabled={loading}
                  className={styles.checkbox}
                />
                {t('limit_usage_count', 'Limit Usage Count')}
              </label>
              {formData.hasMaxUsageCount && (
                <input
                  type="text"
                  inputMode="numeric"
                  name="maxUsageCount"
                  value={formData.maxUsageCount}
                  onChange={handleChange}
                  disabled={loading}
                  className={styles.input}
                  placeholder="10"
                />
              )}
              <small className={styles.help}>
                {t('max_usage_count_help', 'Maximum number of times this discount can be used')}
              </small>
            </div>

            <div className={styles.formGroup}>
              <label>
                <input
                  type="checkbox"
                  name="isActive"
                  checked={formData.isActive}
                  onChange={handleChange}
                  disabled={loading}
                  className={styles.checkbox}
                />
                {t('active', 'Active')}
              </label>
              <small className={styles.help}>
                {t('active_discounts_help', 'Only active discounts can be applied to orders')}
              </small>
            </div>
          </div>

          <div className={styles.actions}>
            <button type="button" onClick={onClose} className={styles.cancelButton} disabled={loading}>
              {t('cancel', 'Cancel')}
            </button>
            <button type="submit" className={styles.submitButton} disabled={loading}>
              {loading ? (
                <>
                  <Loader2 size={18} className={styles.spinner} />
                  {t('saving', 'Saving...')}
                </>
              ) : (
                <>
                  <Save size={18} />
                  {discount ? t('update_discount', 'Update Discount') : t('create_discount', 'Create Discount')}
                </>
              )}
            </button>
          </div>
        </form>
      </div>
    </div>
  );
}
