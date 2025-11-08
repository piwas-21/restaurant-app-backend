'use client';

import { useState, useEffect } from 'react';
import { useTranslation } from 'react-i18next';
import styles from './AddressManagement.module.css';
import {
  getMyAddresses,
  createAddress,
  updateAddress,
  deleteAddress,
  setDefaultAddress,
  type AddressDto,
  type CreateAddressCommand,
  type UpdateAddressCommand,
} from '@/services/addressService';

export default function AddressManagement() {
  const { t } = useTranslation();
  const [addresses, setAddresses] = useState<AddressDto[]>([]);
  const [loading, setLoading] = useState(true);
  const [showAddressForm, setShowAddressForm] = useState(false);
  const [editingAddress, setEditingAddress] = useState<AddressDto | null>(null);
  const [error, setError] = useState<string>('');
  const [deleteConfirmModal, setDeleteConfirmModal] = useState<{ show: boolean; addressId: string | null }>({ show: false, addressId: null });

  const loadAddresses = async () => {
    try {
      setLoading(true);
      const data = await getMyAddresses();
      setAddresses(data);
      setError('');
    } catch (err: any) {
      // eslint-disable-next-line no-console
      console.error('Failed to load addresses:', err);
      setError(t('address_load_error', 'Failed to load addresses. Please try again.'));
    } finally {
      setLoading(false);
    }
  };

  // Load addresses
  useEffect(() => {
    loadAddresses();
    // eslint-disable-next-line react-hooks/exhaustive-deps
  }, []);

  const handleAddNew = () => {
    setEditingAddress(null);
    setShowAddressForm(true);
  };

  const handleEdit = (address: AddressDto) => {
    setEditingAddress(address);
    setShowAddressForm(true);
  };

  const handleDelete = async (addressId: string) => {
    setDeleteConfirmModal({ show: true, addressId });
  };

  const confirmDelete = async () => {
    if (!deleteConfirmModal.addressId) return;

    try {
      await deleteAddress(deleteConfirmModal.addressId);
      await loadAddresses();
      setDeleteConfirmModal({ show: false, addressId: null });
    } catch (err: any) {
      // eslint-disable-next-line no-console
      console.error('Failed to delete address:', err);
      setError(t('address_delete_error', 'Failed to delete address. Please try again.'));
      setDeleteConfirmModal({ show: false, addressId: null });
    }
  };

  const handleSetDefault = async (addressId: string) => {
    try {
      await setDefaultAddress(addressId);
      await loadAddresses();
    } catch (err: any) {
      // eslint-disable-next-line no-console
      console.error('Failed to set default address:', err);
      setError(t('address_set_default_error', 'Failed to set default address. Please try again.'));
    }
  };

  const handleSaveAddress = async (formData: CreateAddressCommand | UpdateAddressCommand) => {
    try {
      if (editingAddress) {
        await updateAddress(editingAddress.id, formData as UpdateAddressCommand);
      } else {
        await createAddress(formData as CreateAddressCommand);
      }
      await loadAddresses();
      setShowAddressForm(false);
      setEditingAddress(null);
      setError('');
    } catch (err: any) {
      // eslint-disable-next-line no-console
      console.error('Failed to save address:', err);
      throw err; // Let the form handle the error
    }
  };

  if (loading) {
    return (
      <div className={styles.container}>
        <div className={styles.loading}>{t('loading', 'Loading...')}</div>
      </div>
    );
  }

  return (
    <div className={styles.container}>
      <div className={styles.header}>
        <h2>{t('delivery_addresses_title', 'Delivery Addresses')}</h2>
        <button onClick={handleAddNew} className={styles.addButton}>
          + {t('add_address_button', 'Add New Address')}
        </button>
      </div>

      {error && <div className={styles.error}>{error}</div>}

      {addresses.length === 0 ? (
        <div className={styles.emptyState}>
          <p>{t('no_addresses_message', 'You haven\'t added any delivery addresses yet.')}</p>
          <button onClick={handleAddNew} className={styles.addButton}>
            {t('add_first_address_button', 'Add Your First Address')}
          </button>
        </div>
      ) : (
        <div className={styles.addressGrid}>
          {addresses.map((address) => (
            <AddressCard
              key={address.id}
              address={address}
              onEdit={() => handleEdit(address)}
              onDelete={() => handleDelete(address.id)}
              onSetDefault={() => handleSetDefault(address.id)}
            />
          ))}
        </div>
      )}

      {showAddressForm && (
        <AddressFormModal
          address={editingAddress}
          onSave={handleSaveAddress}
          onCancel={() => {
            setShowAddressForm(false);
            setEditingAddress(null);
          }}
        />
      )}

      {deleteConfirmModal.show && (
        <div className={styles.modalOverlay} onClick={() => setDeleteConfirmModal({ show: false, addressId: null })}>
          <div className={styles.modalContent} onClick={(e) => e.stopPropagation()}>
            <h2>{t('confirm_delete_title', 'Delete Address')}</h2>
            <p>{t('address_delete_confirm', 'Are you sure you want to delete this address?')}</p>
            <div className={styles.formActions}>
              <button
                type="button"
                onClick={() => setDeleteConfirmModal({ show: false, addressId: null })}
                className={styles.cancelButton}
              >
                {t('cancel_button', 'Cancel')}
              </button>
              <button
                type="button"
                onClick={confirmDelete}
                className={styles.deleteButton}
              >
                {t('delete_button', 'Delete')}
              </button>
            </div>
          </div>
        </div>
      )}
    </div>
  );
}

// Address Card Component
interface AddressCardProps {
  address: AddressDto;
  onEdit: () => void;
  onDelete: () => void;
  onSetDefault: () => void;
}

function AddressCard({ address, onEdit, onDelete, onSetDefault }: AddressCardProps) {
  const { t } = useTranslation();

  return (
    <div className={`${styles.addressCard} ${address.isDefault ? styles.defaultCard : ''}`}>
      {address.isDefault && (
        <div className={styles.defaultBadge}>
          {t('default_address_badge', 'Default')}
        </div>
      )}

      <div className={styles.addressContent}>
        <h3 className={styles.addressLabel}>{address.label}</h3>
        <p className={styles.addressText}>
          {address.addressLine1}
          {address.addressLine2 && <><br />{address.addressLine2}</>}
          <br />
          {address.postalCode} {address.city}
          {address.state && `, ${address.state}`}
          <br />
          {address.country}
        </p>
        {address.phone && (
          <p className={styles.addressPhone}>
            📞 {address.phone}
          </p>
        )}
        {address.deliveryInstructions && (
          <p className={styles.addressInstructions}>
            📝 {address.deliveryInstructions}
          </p>
        )}
      </div>

      <div className={styles.addressActions}>
        <button onClick={onEdit} className={styles.editButton}>
          {t('edit_button', 'Edit')}
        </button>
        {!address.isDefault && (
          <button onClick={onSetDefault} className={styles.setDefaultButton}>
            {t('set_default_button', 'Set as Default')}
          </button>
        )}
        <button onClick={onDelete} className={styles.deleteButton}>
          {t('delete_button', 'Delete')}
        </button>
      </div>
    </div>
  );
}

// Address Form Modal Component
interface AddressFormModalProps {
  address: AddressDto | null;
  onSave: (data: CreateAddressCommand | UpdateAddressCommand) => Promise<void>;
  onCancel: () => void;
}

function AddressFormModal({ address, onSave, onCancel }: AddressFormModalProps) {
  const { t } = useTranslation();
  const [formData, setFormData] = useState({
    label: address?.label || '',
    addressLine1: address?.addressLine1 || '',
    addressLine2: address?.addressLine2 || '',
    city: address?.city || '',
    state: address?.state || '',
    postalCode: address?.postalCode || '',
    country: address?.country || 'Switzerland',
    phone: address?.phone || '',
    deliveryInstructions: address?.deliveryInstructions || '',
  });
  const [errors, setErrors] = useState<Record<string, string>>({});
  const [saving, setSaving] = useState(false);

  const validate = (): boolean => {
    const newErrors: Record<string, string> = {};

    if (!formData.label.trim()) {
      newErrors.label = t('field_required_error', { fieldName: t('address_label', 'Label') });
    }
    if (!formData.addressLine1.trim()) {
      newErrors.addressLine1 = t('field_required_error', { fieldName: t('address_line1', 'Address Line 1') });
    }
    if (!formData.city.trim()) {
      newErrors.city = t('field_required_error', { fieldName: t('city', 'City') });
    }
    if (!formData.postalCode.trim()) {
      newErrors.postalCode = t('field_required_error', { fieldName: t('postal_code', 'Postal Code') });
    }
    if (!formData.country.trim()) {
      newErrors.country = t('field_required_error', { fieldName: t('country', 'Country') });
    }

    setErrors(newErrors);
    return Object.keys(newErrors).length === 0;
  };

  const handleSubmit = async (e: React.FormEvent) => {
    e.preventDefault();

    if (!validate()) return;

    try {
      setSaving(true);
      const command = address
        ? { ...formData, id: address.id }
        : formData;
      await onSave(command);
    } catch (err: any) {
      // eslint-disable-next-line no-console
      console.error('Failed to save:', err);
      setErrors({ form: err.message || t('address_save_error', 'Failed to save address. Please try again.') });
    } finally {
      setSaving(false);
    }
  };

  const handleChange = (e: React.ChangeEvent<HTMLInputElement | HTMLTextAreaElement>) => {
    const { name, value } = e.target;
    setFormData((prev) => ({ ...prev, [name]: value }));
    if (errors[name]) {
      setErrors((prev) => ({ ...prev, [name]: '' }));
    }
  };

  return (
    <div className={styles.modalOverlay} onClick={onCancel}>
      <div className={styles.modalContent} onClick={(e) => e.stopPropagation()}>
        <h2>{address ? t('edit_address_title', 'Edit Address') : t('add_address_title', 'Add New Address')}</h2>

        <form onSubmit={handleSubmit} className={styles.addressForm}>
          {errors.form && <div className={styles.formError}>{errors.form}</div>}

          <div className={styles.formGroup}>
            <label htmlFor="label">{t('address_label', 'Label')} *</label>
            <input
              type="text"
              id="label"
              name="label"
              value={formData.label}
              onChange={handleChange}
              placeholder={t('address_label_placeholder', 'e.g., Home, Work, Office')}
              className={errors.label ? styles.inputError : ''}
            />
            {errors.label && <span className={styles.errorText}>{errors.label}</span>}
          </div>

          <div className={styles.formGroup}>
            <label htmlFor="addressLine1">{t('address_line1', 'Address Line 1')} *</label>
            <input
              type="text"
              id="addressLine1"
              name="addressLine1"
              value={formData.addressLine1}
              onChange={handleChange}
              placeholder={t('address_line1_placeholder', 'Street address')}
              className={errors.addressLine1 ? styles.inputError : ''}
            />
            {errors.addressLine1 && <span className={styles.errorText}>{errors.addressLine1}</span>}
          </div>

          <div className={styles.formGroup}>
            <label htmlFor="addressLine2">{t('address_line2', 'Address Line 2')}</label>
            <input
              type="text"
              id="addressLine2"
              name="addressLine2"
              value={formData.addressLine2}
              onChange={handleChange}
              placeholder={t('address_line2_placeholder', 'Apartment, suite, etc. (optional)')}
            />
          </div>

          <div className={styles.formRow}>
            <div className={styles.formGroup}>
              <label htmlFor="city">{t('city', 'City')} *</label>
              <input
                type="text"
                id="city"
                name="city"
                value={formData.city}
                onChange={handleChange}
                className={errors.city ? styles.inputError : ''}
              />
              {errors.city && <span className={styles.errorText}>{errors.city}</span>}
            </div>

            <div className={styles.formGroup}>
              <label htmlFor="postalCode">{t('postal_code', 'Postal Code')} *</label>
              <input
                type="text"
                id="postalCode"
                name="postalCode"
                value={formData.postalCode}
                onChange={handleChange}
                className={errors.postalCode ? styles.inputError : ''}
              />
              {errors.postalCode && <span className={styles.errorText}>{errors.postalCode}</span>}
            </div>
          </div>

          <div className={styles.formRow}>
            <div className={styles.formGroup}>
              <label htmlFor="state">{t('state', 'State/Canton')}</label>
              <input
                type="text"
                id="state"
                name="state"
                value={formData.state}
                onChange={handleChange}
              />
            </div>

            <div className={styles.formGroup}>
              <label htmlFor="country">{t('country', 'Country')} *</label>
              <input
                type="text"
                id="country"
                name="country"
                value={formData.country}
                onChange={handleChange}
                className={errors.country ? styles.inputError : ''}
              />
              {errors.country && <span className={styles.errorText}>{errors.country}</span>}
            </div>
          </div>

          <div className={styles.formGroup}>
            <label htmlFor="phone">{t('phone', 'Phone Number')}</label>
            <input
              type="tel"
              id="phone"
              name="phone"
              value={formData.phone}
              onChange={handleChange}
              placeholder="+41 79 123 45 67"
            />
          </div>

          <div className={styles.formGroup}>
            <label htmlFor="deliveryInstructions">{t('delivery_instructions', 'Delivery Instructions')}</label>
            <textarea
              id="deliveryInstructions"
              name="deliveryInstructions"
              value={formData.deliveryInstructions}
              onChange={handleChange}
              placeholder={t('delivery_instructions_placeholder', 'e.g., Ring the bell, Leave at door')}
              rows={3}
            />
          </div>

          <div className={styles.formActions}>
            <button type="button" onClick={onCancel} className={styles.cancelButton} disabled={saving}>
              {t('cancel_button', 'Cancel')}
            </button>
            <button type="submit" className={styles.saveButton} disabled={saving}>
              {saving ? t('saving', 'Saving...') : t('save_button', 'Save Address')}
            </button>
          </div>
        </form>
      </div>
    </div>
  );
}
