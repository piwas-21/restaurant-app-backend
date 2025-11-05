import React, { useState } from 'react';
import { Download, X, Package, ChevronDown, FileText } from 'lucide-react';
import { useTranslation } from 'react-i18next';
import styles from './BulkActionsBar.module.css';

interface BulkActionsBarProps {
  selectedCount: number;
  onExportCSV: () => void;
  onExportPDF: () => void;
  onUpdateStatus: () => void;
  onClearSelection: () => void;
}

export const BulkActionsBar: React.FC<BulkActionsBarProps> = ({
  selectedCount,
  onExportCSV,
  onExportPDF,
  onUpdateStatus,
  onClearSelection,
}) => {
  const { t } = useTranslation();
  const [showExportMenu, setShowExportMenu] = useState(false);

  return (
    <div className={styles.bulkActions}>
      <div className={styles.bulkInfo}>
        <span className={styles.bulkCount}>
          {selectedCount} {t('orders_selected', 'order(s) selected')}
        </span>
      </div>
      <div className={styles.bulkButtons}>
        <button onClick={onUpdateStatus} className={styles.bulkStatusButton}>
          <Package size={16} />
          {t('update_status_bulk', 'Update Status')}
        </button>
        <div className={styles.exportDropdown}>
          <button
            onClick={() => setShowExportMenu(!showExportMenu)}
            className={styles.bulkExportButton}
          >
            <Download size={16} />
            {t('export_selected', 'Export Selected')}
            <ChevronDown size={14} />
          </button>
          {showExportMenu && (
            <div className={styles.exportMenu}>
              <button
                onClick={() => {
                  onExportCSV();
                  setShowExportMenu(false);
                }}
                className={styles.exportMenuItem}
              >
                <FileText size={16} />
                {t('export_as_csv', 'Export as CSV')}
              </button>
              <button
                onClick={() => {
                  onExportPDF();
                  setShowExportMenu(false);
                }}
                className={styles.exportMenuItem}
              >
                <FileText size={16} />
                {t('export_as_pdf', 'Export as PDF')}
              </button>
            </div>
          )}
        </div>
        <button onClick={onClearSelection} className={styles.bulkClearButton}>
          <X size={16} />
          {t('clear_selection', 'Clear Selection')}
        </button>
      </div>
    </div>
  );
};
