import React from 'react';
import { useTranslation } from 'react-i18next';
import { GroupDiscountDto, DiscountType } from '@/types/userGroupTypes';
import { Edit, Trash2 } from 'lucide-react';
import styles from '@/app/styles/AdminPage.module.css';

interface DiscountsTableProps {
  discounts: GroupDiscountDto[];
  isLoading: boolean;
  onEdit: (discount: GroupDiscountDto) => void;
  onDelete: (discount: GroupDiscountDto) => void;
}

const DiscountsTable: React.FC<DiscountsTableProps> = ({
  discounts,
  isLoading,
  onEdit,
  onDelete
}) => {
  const { t } = useTranslation();

  if (isLoading) {
    return <div className={styles.loading}>{t('loading')}</div>;
  }

  if (discounts.length === 0) {
    return <div className={styles.noData}>{t('no_customer_discounts_found')}</div>;
  }

  return (
    <div className={styles.tableContainer}>
      <table className={styles.adminTable}>
        <thead>
          <tr>
            <th>{t('discount_name')}</th>
            <th>{t('discount_type')}</th>
            <th>{t('discount_value')}</th>
            <th>{t('min_order_amount')}</th>
            <th>{t('max_discount_amount')}</th>
            <th>{t('status')}</th>
            <th>{t('actions_header')}</th>
          </tr>
        </thead>
        <tbody>
          {discounts.map((discount) => (
            <tr key={discount.id}>
              <td>{discount.name}</td>
              <td>{discount.type === DiscountType.Percentage ? t('percentage') : t('fixed_amount')}</td>
              <td>
                {discount.type === DiscountType.Percentage
                  ? `${discount.value}%`
                  : `CHF ${discount.value.toFixed(2)}`}
              </td>
              <td>{discount.minimumOrderAmount ? `CHF ${discount.minimumOrderAmount.toFixed(2)}` : '-'}</td>
              <td>{discount.maximumDiscountAmount ? `CHF ${discount.maximumDiscountAmount.toFixed(2)}` : '-'}</td>
              <td>
                <span className={discount.isActive ? styles.statusActive : styles.statusInactive}>
                  {discount.isActive ? t('active') : t('inactive')}
                </span>
              </td>
              <td className={styles.actionsCell}>
                <button
                  className={styles.actionButton}
                  onClick={() => onEdit(discount)}
                  title={t('edit')}
                >
                  <Edit size={18} />
                </button>
                <button
                  className={`${styles.actionButton} ${styles.deleteButton}`}
                  onClick={() => onDelete(discount)}
                  title={t('delete')}
                >
                  <Trash2 size={18} />
                </button>
              </td>
            </tr>
          ))}
        </tbody>
      </table>
    </div>
  );
};

export default DiscountsTable;
