'use client';

import React, { useState, useEffect } from 'react';
import { useTranslation } from 'react-i18next';
import { X, Check, DollarSign } from 'lucide-react';
import { OrderType } from '@/types/order';
import { adminTaxConfigurationService, TaxConfiguration } from '@/services/adminTaxConfigurationService';
import styles from './TaxSelectionModal.module.css';

interface TaxSelectionModalProps {
  isOpen: boolean;
  onClose: () => void;
  onSelectTax: (tax: TaxConfiguration | null) => void;
  currentOrderType: OrderType;
  currentTaxId?: string | null;
}

export const TaxSelectionModal: React.FC<TaxSelectionModalProps> = ({
  isOpen,
  onClose,
  onSelectTax,
  currentOrderType,
  currentTaxId
}) => {
  const { t } = useTranslation();
  const [taxConfigurations, setTaxConfigurations] = useState<TaxConfiguration[]>([]);
  const [selectedTaxId, setSelectedTaxId] = useState<string | null>(currentTaxId || null);
  const [loading, setLoading] = useState(false);

  const fetchTaxConfigurations = async () => {
    try {
      setLoading(true);
      const allTaxes = await adminTaxConfigurationService.getAllTaxConfigurations();

      // Filter taxes that apply to the current order type and are enabled
      const applicableTaxes = allTaxes.filter(
        (tax) =>
          tax.isEnabled &&
          tax.applicableOrderTypes &&
          tax.applicableOrderTypes.includes(currentOrderType)
      );

      setTaxConfigurations(applicableTaxes);
    } catch {
      // Error handled silently or could show a toast notification
      setTaxConfigurations([]);
    } finally {
      setLoading(false);
    }
  };

  useEffect(() => {
    if (isOpen) {
      fetchTaxConfigurations();
    }
    // eslint-disable-next-line react-hooks/exhaustive-deps
  }, [isOpen]);

  const handleSelectTax = (taxId: string) => {
    setSelectedTaxId(taxId);
  };

  const handleConfirm = () => {
    const selectedTax = taxConfigurations.find((tax) => tax.id === selectedTaxId);
    onSelectTax(selectedTax || null);
    onClose();
  };

  const handleNoTax = () => {
    setSelectedTaxId(null);
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

  if (!isOpen) return null;

  return (
    <div className={styles.overlay} onClick={onClose}>
      <div className={styles.modal} onClick={(e) => e.stopPropagation()}>
        <div className={styles.header}>
          <div className={styles.headerContent}>
            <DollarSign className={styles.icon} />
            <div>
              <h2 className={styles.title}>{t('select_tax_rate', 'Select Tax Rate')}</h2>
              <p className={styles.subtitle}>
                {t('order_type', 'Order Type')}: <strong>{getOrderTypeLabel(currentOrderType)}</strong>
              </p>
            </div>
          </div>
          <button onClick={onClose} className={styles.closeButton} aria-label="Close">
            <X size={24} />
          </button>
        </div>

        <div className={styles.content}>
          {loading ? (
            <div className={styles.loading}>{t('loading_tax_configurations', 'Loading tax configurations...')}</div>
          ) : taxConfigurations.length === 0 ? (
            <div className={styles.emptyState}>
              <DollarSign size={48} />
              <p>{t('no_tax_configurations_for_order_type', 'No tax configurations available for {{orderType}}', { orderType: getOrderTypeLabel(currentOrderType) })}</p>
              <small>{t('create_tax_configuration_admin_panel', 'Please create a tax configuration in the admin panel')}</small>
            </div>
          ) : (
            <>
              <div className={styles.taxList}>
                {/* Option for no tax */}
                <div
                  className={`${styles.taxCard} ${selectedTaxId === null ? styles.selected : ''}`}
                  onClick={handleNoTax}
                >
                  <div className={styles.taxInfo}>
                    <h3 className={styles.taxName}>{t('no_tax', 'No Tax')}</h3>
                    <p className={styles.taxDescription}>{t('no_tax_description', 'Do not apply any tax to this order')}</p>
                  </div>
                  <div className={styles.taxRate}>0.00%</div>
                  {selectedTaxId === null && (
                    <div className={styles.checkmark}>
                      <Check size={20} />
                    </div>
                  )}
                </div>

                {taxConfigurations.map((tax) => (
                  <div
                    key={tax.id}
                    className={`${styles.taxCard} ${selectedTaxId === tax.id ? styles.selected : ''}`}
                    onClick={() => handleSelectTax(tax.id)}
                  >
                    <div className={styles.taxInfo}>
                      <h3 className={styles.taxName}>{tax.name}</h3>
                      <p className={styles.taxDescription}>{tax.description}</p>
                      <div className={styles.applicableTypes}>
                        {tax.applicableOrderTypes.map((type) => (
                          <span key={type} className={styles.typeBadge}>
                            {getOrderTypeLabel(type)}
                          </span>
                        ))}
                      </div>
                    </div>
                    <div className={styles.taxRate}>{(tax.rate * 100).toFixed(2)}%</div>
                    {selectedTaxId === tax.id && (
                      <div className={styles.checkmark}>
                        <Check size={20} />
                      </div>
                    )}
                  </div>
                ))}
              </div>
            </>
          )}
        </div>

        <div className={styles.footer}>
          <button onClick={onClose} className={styles.cancelButton}>
            {t('cancel', 'Cancel')}
          </button>
          <button
            onClick={handleConfirm}
            className={styles.confirmButton}
            disabled={loading}
          >
            {t('confirm_selection', 'Confirm Selection')}
          </button>
        </div>
      </div>
    </div>
  );
};
