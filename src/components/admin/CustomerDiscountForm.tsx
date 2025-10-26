'use client';

import React, { useState, useEffect, useRef, useCallback } from 'react';
import { useSnackbar } from 'notistack';
import { X, Save, Loader2, Search, UserCheck } from 'lucide-react';
import { adminFidelityService, CreateCustomerDiscountDto, UpdateCustomerDiscountDto } from '@/services/adminFidelityService';
import { fetchUsers, UserDto } from '@/services/userService';
import type { CustomerDiscountRule } from '@/types/fidelity';
import styles from './CustomerDiscountForm.module.css';

interface CustomerDiscountFormProps {
  discount: CustomerDiscountRule | null;
  onClose: () => void;
  onSuccess: () => void;
}

export default function CustomerDiscountForm({
  discount,
  onClose,
  onSuccess,
}: CustomerDiscountFormProps) {
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
    discountValue: 0,
    minOrderAmount: 0,
    maxOrderAmount: 0,
    hasMaxOrderAmount: false,
    maxUsageCount: 0,
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
        discountValue: discount.discountValue,
        minOrderAmount: discount.minOrderAmount || 0,
        maxOrderAmount: discount.maxOrderAmount || 0,
        hasMaxOrderAmount: !!discount.maxOrderAmount,
        maxUsageCount: discount.maxUsageCount || 0,
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
        email: '',
        firstName: '',
        lastName: '',
        fullName: `User ${discount.userId}`,
        role: '',
        isEmailConfirmed: false,
        createdAt: '',
        metadata: {},
        orderLimitAmount: 0,
        discountPercentage: 0,
        isDiscountActive: false,
      });
    }
  }, [discount]);

  // Debounced user search
  const handleSearchChange = useCallback((query: string) => {
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
        const response = await fetchUsers('', false, query, 1, 10);
        if (response.success && response.data?.items) {
          setSearchResults(response.data.items);
        } else {
          setSearchResults([]);
        }
      } catch (error) {
        // eslint-disable-next-line no-console
        console.error('Error searching users:', error);
        setSearchResults([]);
        enqueueSnackbar('Failed to search users', { variant: 'error' });
      } finally {
        setSearchLoading(false);
      }
    }, 500); // 500ms debounce
  }, [enqueueSnackbar]);

  const handleUserSelect = useCallback((user: UserDto) => {
    setSelectedUser(user);
    setFormData(prev => ({ ...prev, userId: user.id }));
    setSearchQuery('');
    setSearchResults([]);
    setShowSearchResults(false);
  }, []);

  const handleClearUser = useCallback(() => {
    setSelectedUser(null);
    setFormData(prev => ({ ...prev, userId: '' }));
  }, []);

  const handleChange = (
    e: React.ChangeEvent<HTMLInputElement | HTMLSelectElement>
  ) => {
    const { name, value, type } = e.target;
    const checked = (e.target as HTMLInputElement).checked;

    setFormData((prev) => ({
      ...prev,
      [name]: type === 'checkbox' ? checked : type === 'number' ? parseFloat(value) || 0 : value,
    }));
  };

  const validateForm = (): string | null => {
    if (!formData.userId.trim()) return 'User ID is required';
    if (!formData.name.trim()) return 'Name is required';
    if (formData.discountValue <= 0) return 'Discount value must be greater than 0';
    if (formData.discountType === 'Percentage' && formData.discountValue > 100) {
      return 'Percentage discount cannot exceed 100%';
    }
    if (formData.minOrderAmount < 0) return 'Minimum order amount cannot be negative';
    if (formData.hasMaxOrderAmount && formData.maxOrderAmount <= formData.minOrderAmount) {
      return 'Maximum order amount must be greater than minimum';
    }
    if (formData.hasMaxUsageCount && formData.maxUsageCount <= 0) {
      return 'Max usage count must be greater than 0';
    }
    if (formData.hasValidFrom && formData.hasValidUntil) {
      const from = new Date(formData.validFrom);
      const until = new Date(formData.validUntil);
      if (until <= from) {
        return 'Valid until date must be after valid from date';
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
        discountValue: formData.discountValue,
        minOrderAmount: formData.minOrderAmount || undefined,
        maxOrderAmount: formData.hasMaxOrderAmount ? formData.maxOrderAmount : undefined,
        maxUsageCount: formData.hasMaxUsageCount ? formData.maxUsageCount : undefined,
        isActive: formData.isActive,
        validFrom: formData.hasValidFrom ? new Date(formData.validFrom).toISOString() : undefined,
        validUntil: formData.hasValidUntil ? new Date(formData.validUntil).toISOString() : undefined,
      };

      if (discount) {
        await adminFidelityService.updateCustomerDiscount(discount.id, dto as UpdateCustomerDiscountDto);
        enqueueSnackbar('Discount updated successfully', { variant: 'success' });
      } else {
        await adminFidelityService.createCustomerDiscount(dto);
        enqueueSnackbar('Discount created successfully', { variant: 'success' });
      }

      onSuccess();
    } catch (error: any) {
      // Parse error message for better user feedback
      let errorMessage = `Failed to ${discount ? 'update' : 'create'} discount`;

      if (error?.response?.data) {
        const errorData = error.response.data;

        // Check if errors array exists (our API format)
        if (Array.isArray(errorData.errors) && errorData.errors.length > 0) {
          const firstError = errorData.errors[0];

          // Check for user not found error
          if (firstError.toLowerCase().includes('user') && firstError.toLowerCase().includes('not found')) {
            errorMessage = `User with ID "${formData.userId}" was not found. Please verify the user ID and try again.`;
          }
          // Check for duplicate discount error
          else if (firstError.toLowerCase().includes('already exists')) {
            errorMessage = `A discount already exists for this user. Please edit the existing discount instead of creating a new one.`;
          }
          // Check for validation errors
          else if (firstError.toLowerCase().includes('invalid')) {
            errorMessage = firstError;
          }
          else {
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
          <h2>{discount ? 'Edit Discount' : 'Create New Discount'}</h2>
          <button
            onClick={onClose}
            className={styles.closeButton}
            disabled={loading}
          >
            <X size={24} />
          </button>
        </div>

        <form onSubmit={handleSubmit} className={styles.form}>
          <div className={styles.formGrid}>
            <div className={styles.formGroup}>
              <label htmlFor="userSearch">
                Customer <span className={styles.required}>*</span>
              </label>

              {selectedUser ? (
                <div className={styles.selectedUserCard}>
                  <div className={styles.selectedUserInfo}>
                    <UserCheck size={20} />
                    <div>
                      <div className={styles.selectedUserName}>{selectedUser.fullName || `${selectedUser.firstName} ${selectedUser.lastName}`}</div>
                      <div className={styles.selectedUserEmail}>{selectedUser.email || `ID: ${selectedUser.id}`}</div>
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
                      placeholder="Search by name or email..."
                      autoComplete="off"
                    />
                    {searchLoading && (
                      <Loader2 size={18} className={`${styles.searchIcon} ${styles.spinner}`} />
                    )}
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
                            <div className={styles.searchResultName}>{user.fullName || `${user.firstName} ${user.lastName}`}</div>
                            <div className={styles.searchResultEmail}>{user.email}</div>
                          </div>
                        </button>
                      ))}
                    </div>
                  )}

                  {showSearchResults && searchResults.length === 0 && !searchLoading && (
                    <div className={styles.searchNoResults}>
                      No users found matching &ldquo;{searchQuery}&rdquo;
                    </div>
                  )}
                </div>
              )}

              <small className={styles.help}>
                {selectedUser ? 'Selected customer for this discount' : 'Search and select a customer'}
              </small>
            </div>

            <div className={styles.formGroup}>
              <label htmlFor="name">
                Name <span className={styles.required}>*</span>
              </label>
              <input
                type="text"
                id="name"
                name="name"
                value={formData.name}
                onChange={handleChange}
                disabled={loading}
                className={styles.input}
                placeholder="e.g., VIP 10% Discount"
              />
            </div>
          </div>

          <div className={styles.formGrid}>
            <div className={styles.formGroup}>
              <label htmlFor="discountType">
                Discount Type <span className={styles.required}>*</span>
              </label>
              <select
                id="discountType"
                name="discountType"
                value={formData.discountType}
                onChange={handleChange}
                disabled={loading}
                className={styles.select}
              >
                <option value="Percentage">Percentage</option>
                <option value="FixedAmount">Fixed Amount</option>
              </select>
            </div>

            <div className={styles.formGroup}>
              <label htmlFor="discountValue">
                Discount Value <span className={styles.required}>*</span>
              </label>
              <input
                type="number"
                id="discountValue"
                name="discountValue"
                value={formData.discountValue}
                onChange={handleChange}
                disabled={loading}
                className={styles.input}
                min="0"
                step={formData.discountType === 'Percentage' ? '1' : '0.01'}
                max={formData.discountType === 'Percentage' ? '100' : undefined}
                placeholder={formData.discountType === 'Percentage' ? '10' : '5.00'}
              />
              <small className={styles.help}>
                {formData.discountType === 'Percentage' ? 'Enter percentage (0-100)' : 'Enter dollar amount'}
              </small>
            </div>
          </div>

          <div className={styles.formGrid}>
            <div className={styles.formGroup}>
              <label htmlFor="minOrderAmount">Minimum Order Amount</label>
              <input
                type="number"
                id="minOrderAmount"
                name="minOrderAmount"
                value={formData.minOrderAmount}
                onChange={handleChange}
                disabled={loading}
                className={styles.input}
                min="0"
                step="0.01"
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
                Set Maximum Order Amount
              </label>
              {formData.hasMaxOrderAmount && (
                <input
                  type="number"
                  name="maxOrderAmount"
                  value={formData.maxOrderAmount}
                  onChange={handleChange}
                  disabled={loading}
                  className={styles.input}
                  min={formData.minOrderAmount}
                  step="0.01"
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
                Set Valid From Date
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
                Set Valid Until Date
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
                Limit Usage Count
              </label>
              {formData.hasMaxUsageCount && (
                <input
                  type="number"
                  name="maxUsageCount"
                  value={formData.maxUsageCount}
                  onChange={handleChange}
                  disabled={loading}
                  className={styles.input}
                  min="1"
                  step="1"
                  placeholder="10"
                />
              )}
              <small className={styles.help}>
                Maximum number of times this discount can be used
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
                Active
              </label>
              <small className={styles.help}>
                Only active discounts can be applied to orders
              </small>
            </div>
          </div>

          <div className={styles.actions}>
            <button
              type="button"
              onClick={onClose}
              className={styles.cancelButton}
              disabled={loading}
            >
              Cancel
            </button>
            <button
              type="submit"
              className={styles.submitButton}
              disabled={loading}
            >
              {loading ? (
                <>
                  <Loader2 size={18} className={styles.spinner} />
                  Saving...
                </>
              ) : (
                <>
                  <Save size={18} />
                  {discount ? 'Update' : 'Create'} Discount
                </>
              )}
            </button>
          </div>
        </form>
      </div>
    </div>
  );
}
