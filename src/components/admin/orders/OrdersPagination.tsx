import React from 'react';
import { useTranslation } from 'react-i18next';
import { ChevronLeft, ChevronRight } from 'lucide-react';
import styles from './OrdersPagination.module.css';

interface OrdersPaginationProps {
  currentPage: number;
  totalPages: number;
  onPageChange: (page: number) => void;
}

export const OrdersPagination: React.FC<OrdersPaginationProps> = ({
  currentPage,
  totalPages,
  onPageChange,
}) => {
  const { t } = useTranslation();

  if (totalPages <= 1) {
    return null;
  }

  return (
    <div className={styles.pagination}>
      <button
        onClick={() => onPageChange(Math.max(1, currentPage - 1))}
        disabled={currentPage === 1}
        className={styles.paginationButton}
      >
        <ChevronLeft size={18} />
        {t('previous', 'Previous')}
      </button>
      <div className={styles.pageInfo}>
        {t('page_info', `Page ${currentPage} of ${totalPages}`)}
      </div>
      <button
        onClick={() => onPageChange(Math.min(totalPages, currentPage + 1))}
        disabled={currentPage === totalPages}
        className={styles.paginationButton}
      >
        {t('next', 'Next')}
        <ChevronRight size={18} />
      </button>
    </div>
  );
};
