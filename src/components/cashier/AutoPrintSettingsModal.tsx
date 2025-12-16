'use client';

import React, { useState } from 'react';
import { useTranslation } from 'react-i18next';
import { AutoPrintSettings } from '@/types/cashier';
import styles from './AutoPrintSettingsModal.module.css';

interface AutoPrintSettingsModalProps {
  isOpen: boolean;
  onClose: () => void;
  settings: AutoPrintSettings;
  onSave: (settings: AutoPrintSettings) => void;
}

export default function AutoPrintSettingsModal({
  isOpen,
  onClose,
  settings,
  onSave,
}: AutoPrintSettingsModalProps) {
  const { t } = useTranslation();
  const [localSettings, setLocalSettings] = useState<AutoPrintSettings>(settings);

  if (!isOpen) return null;

  const handleSave = () => {
    // Validation: at least one order type, status, and print content must be selected
    const hasOrderType = Object.values(localSettings.orderTypes).some(v => v);
    const hasStatus = Object.values(localSettings.orderStatuses).some(v => v);
    const hasPrintContent = Object.values(localSettings.printContent).some(v => v);

    if (!hasOrderType || !hasStatus || !hasPrintContent) {
      alert(t('cashier.at_least_one_option') || 'Please select at least one order type, status, and print content');
      return;
    }

    onSave(localSettings);
    onClose();
  };

  const handleOrderTypeChange = (type: keyof AutoPrintSettings['orderTypes']) => {
    setLocalSettings({
      ...localSettings,
      orderTypes: {
        ...localSettings.orderTypes,
        [type]: !localSettings.orderTypes[type],
      },
    });
  };

  const handleStatusChange = (status: keyof AutoPrintSettings['orderStatuses']) => {
    setLocalSettings({
      ...localSettings,
      orderStatuses: {
        ...localSettings.orderStatuses,
        [status]: !localSettings.orderStatuses[status],
      },
    });
  };

  const handlePrintContentChange = (type: keyof AutoPrintSettings['printContent']) => {
    setLocalSettings({
      ...localSettings,
      printContent: {
        ...localSettings.printContent,
        [type]: !localSettings.printContent[type],
      },
    });
  };

  return (
    <div className={styles.settingsModal} onClick={onClose}>
      <div className={styles.settingsContent} onClick={(e) => e.stopPropagation()}>
        <div className={styles.settingsHeader}>
          <h2>{t('cashier.auto_print_settings') || 'Auto Print Settings'}</h2>
          <button className={styles.closeButton} onClick={onClose}>
            ×
          </button>
        </div>

        <div className={styles.settingsBody}>
          {/* Order Types Section */}
          <div className={styles.settingsSection}>
            <h3 className={styles.sectionTitle}>
              {t('cashier.order_types_to_print') || 'Order Types to Print'}
            </h3>
            <div className={styles.optionsGroup}>
              <label className={styles.optionLabel}>
                <input
                  type="checkbox"
                  checked={localSettings.orderTypes.dineIn}
                  onChange={() => handleOrderTypeChange('dineIn')}
                />
                {t('order_type_dine_in') || 'Dine-in'}
              </label>
              <label className={styles.optionLabel}>
                <input
                  type="checkbox"
                  checked={localSettings.orderTypes.takeaway}
                  onChange={() => handleOrderTypeChange('takeaway')}
                />
                {t('order_type_takeaway') || 'Takeaway'}
              </label>
              <label className={styles.optionLabel}>
                <input
                  type="checkbox"
                  checked={localSettings.orderTypes.delivery}
                  onChange={() => handleOrderTypeChange('delivery')}
                />
                {t('order_type_delivery') || 'Delivery'}
              </label>
            </div>
          </div>

          {/* Order Statuses Section */}
          <div className={styles.settingsSection}>
            <h3 className={styles.sectionTitle}>
              {t('cashier.order_statuses_to_print') || 'Order Statuses to Print'}
            </h3>
            <div className={styles.optionsGroup}>
              <label className={styles.optionLabel}>
                <input
                  type="checkbox"
                  checked={localSettings.orderStatuses.pending}
                  onChange={() => handleStatusChange('pending')}
                />
                {t('order_status_pending') || 'Pending'}
              </label>
              <label className={styles.optionLabel}>
                <input
                  type="checkbox"
                  checked={localSettings.orderStatuses.confirmed}
                  onChange={() => handleStatusChange('confirmed')}
                />
                {t('order_status_confirmed') || 'Confirmed'}
              </label>
              <label className={styles.optionLabel}>
                <input
                  type="checkbox"
                  checked={localSettings.orderStatuses.preparing}
                  onChange={() => handleStatusChange('preparing')}
                />
                {t('order_status_preparing') || 'Preparing'}
              </label>
              <label className={styles.optionLabel}>
                <input
                  type="checkbox"
                  checked={localSettings.orderStatuses.ready}
                  onChange={() => handleStatusChange('ready')}
                />
                {t('order_status_ready') || 'Ready'}
              </label>
            </div>
          </div>

          {/* Print Content Section */}
          <div className={styles.settingsSection}>
            <h3 className={styles.sectionTitle}>
              {t('cashier.what_to_print') || 'What to Print'}
            </h3>
            <div className={styles.optionsGroup}>
              <label className={styles.optionLabel}>
                <input
                  type="checkbox"
                  checked={localSettings.printContent.all}
                  onChange={() => handlePrintContentChange('all')}
                />
                {t('cashier.print_all_kitchen') || 'Kitchen Receipt (All Items)'}
              </label>
              <label className={styles.optionLabel}>
                <input
                  type="checkbox"
                  checked={localSettings.printContent.frontKitchen}
                  onChange={() => handlePrintContentChange('frontKitchen')}
                />
                {t('cashier.print_front_kitchen') || 'Front Kitchen Only'}
              </label>
              <label className={styles.optionLabel}>
                <input
                  type="checkbox"
                  checked={localSettings.printContent.backKitchen}
                  onChange={() => handlePrintContentChange('backKitchen')}
                />
                {t('cashier.print_back_kitchen') || 'Back Kitchen Only'}
              </label>
              <label className={styles.optionLabel}>
                <input
                  type="checkbox"
                  checked={localSettings.printContent.bill}
                  onChange={() => handlePrintContentChange('bill')}
                />
                {t('cashier.print_customer_bill') || 'Customer Bill'}
              </label>
            </div>
          </div>
        </div>

        <div className={styles.settingsFooter}>
          <button className={styles.cancelButton} onClick={onClose}>
            {t('cancel') || 'Cancel'}
          </button>
          <button className={styles.saveButton} onClick={handleSave}>
            {t('save_changes') || 'Save Settings'}
          </button>
        </div>
      </div>
    </div>
  );
}
