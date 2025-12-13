"use client";

import React, { useState, useEffect } from 'react';
import { useTranslation } from 'react-i18next';
import { 
  orderTypeConfigurationService,
  OrderTypeConfigurationDto 
} from '@/services/orderTypeConfigurationService';
import { OrderType } from '@/types/order';
import { Utensils, Store, Truck } from 'lucide-react';
import ConfirmationModal from '@/components/common/ConfirmationModal';
import { enqueueSnackbar } from 'notistack';
import styles from './OrderTypeManager.module.css';

export default function OrderTypeManager() {
  const { t } = useTranslation();
  const [configurations, setConfigurations] = useState<OrderTypeConfigurationDto[]>([]);
  const [loading, setLoading] = useState(true);
  const [saving, setSaving] = useState(false);

  // Confirmation modal state
  const [showConfirmModal, setShowConfirmModal] = useState(false);
  const [pendingOrderType, setPendingOrderType] = useState<OrderType | null>(null);

  useEffect(() => {
    fetchConfigurations();
  }, []);

  const fetchConfigurations = async () => {
    try {
      setLoading(true);
      const data = await orderTypeConfigurationService.getAll();
      setConfigurations(data);
    } catch (err) {
      enqueueSnackbar(t('failed_to_load_configurations', 'Failed to load order type configurations'), {
        variant: 'error'
      });
    } finally {
      setLoading(false);
    }
  };

  const handleToggle = async (orderType: OrderType, currentlyEnabled: boolean) => {
    const newEnabledState = !currentlyEnabled;
    
    // Show confirmation modal when disabling
    if (!newEnabledState) {
      setPendingOrderType(orderType);
      setShowConfirmModal(true);
      return;
    }

    // If enabling, proceed directly
    await updateOrderType(orderType, newEnabledState);
  };

  const handleConfirmDisable = async () => {
    if (!pendingOrderType) return;

    setShowConfirmModal(false);
    await updateOrderType(pendingOrderType, false);
    setPendingOrderType(null);
  };

  const handleCancelDisable = () => {
    setShowConfirmModal(false);
    setPendingOrderType(null);
  };

  const updateOrderType = async (orderType: OrderType, isEnabled: boolean) => {
    try {
      setSaving(true);

      await orderTypeConfigurationService.update({
        orderType,
        isEnabled,
      });

      // Update local state
      setConfigurations(prev =>
        prev.map(config =>
          config.orderType === orderType
            ? { ...config, isEnabled }
            : config
        )
      );

      enqueueSnackbar(t('order_type_updated_successfully', 'Order type updated successfully'), {
        variant: 'success'
      });
    } catch (err) {
      enqueueSnackbar(t('failed_to_update_order_type', 'Failed to update order type'), {
        variant: 'error'
      });
    } finally {
      setSaving(false);
    }
  };

  const getOrderTypeName = (orderType: OrderType): string => {
    switch (orderType) {
      case OrderType.DineIn:
        return t('order_type_dine_in', 'Dine In');
      case OrderType.Takeaway:
        return t('order_type_takeaway', 'Takeaway');
      case OrderType.Delivery:
        return t('order_type_delivery', 'Delivery');
      default:
        return orderType;
    }
  };

  const getOrderTypeIcon = (orderType: OrderType) => {
    switch (orderType) {
      case OrderType.DineIn:
        return <Utensils size={28} />;
      case OrderType.Takeaway:
        return <Store size={28} />;
      case OrderType.Delivery:
        return <Truck size={28} />;
      default:
        return null;
    }
  };

  const getOrderTypeDescription = (orderType: OrderType): string => {
    switch (orderType) {
      case OrderType.DineIn:
        return t('order_type_dine_in_desc', 'Enjoy your meal at our restaurant');
      case OrderType.Takeaway:
        return t('order_type_takeaway_desc', 'Pick up your order');
      case OrderType.Delivery:
        return t('order_type_delivery_desc', 'We deliver to your address');
      default:
        return '';
    }
  };

  if (loading) {
    return (
      <div className={styles.loading}>
        <p>{t('common.loading', 'Loading...')}</p>
      </div>
    );
  }

  return (
    <div className={styles.container}>
      <div className={styles.configurationsGrid}>
        {configurations.map((config) => (
          <div
            key={config.orderType}
            className={`${styles.configCard} ${config.isEnabled ? styles.enabled : styles.disabled}`}
          >
            <div className={styles.cardHeader}>
              <div className={styles.iconWrapper}>
                {getOrderTypeIcon(config.orderType)}
              </div>
              <div className={styles.cardInfo}>
                <h3 className={styles.cardTitle}>
                  {getOrderTypeName(config.orderType)}
                </h3>
                <p className={styles.cardDescription}>
                  {getOrderTypeDescription(config.orderType)}
                </p>
              </div>
            </div>

            <div className={styles.cardActions}>
              <div className={`${styles.statusBadge} ${config.isEnabled ? styles.statusEnabled : styles.statusDisabled}`}>
                {config.isEnabled
                  ? t('order_type_enabled', 'Enabled')
                  : t('order_type_disabled', 'Disabled')}
              </div>
              <label className={styles.toggleSwitch}>
                <input
                  type="checkbox"
                  checked={config.isEnabled}
                  onChange={() => handleToggle(config.orderType, config.isEnabled)}
                  disabled={saving}
                />
                <span className={styles.toggleSlider}></span>
              </label>
            </div>
          </div>
        ))}
      </div>

      {/* Confirmation Modal */}
      <ConfirmationModal
        isOpen={showConfirmModal}
        onClose={handleCancelDisable}
        onConfirm={handleConfirmDisable}
        message={pendingOrderType ? t('confirm_disable_order_type', 
          `Are you sure you want to disable {{orderType}}? Customers will not be able to select this option.`,
          { orderType: getOrderTypeName(pendingOrderType) }) : ''}
      />
    </div>
  );
}
