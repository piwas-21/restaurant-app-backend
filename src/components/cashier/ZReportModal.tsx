'use client';

import { useState, useEffect, useCallback } from 'react';
import { useTranslation } from 'react-i18next';
import { X, FileBarChart, Printer, Loader2 } from 'lucide-react';
import { ZReportDto } from '@/types/order';
import { getZReport } from '@/services/orderService';
import { exportZReportToPDF } from '@/utils/zReportExportUtils';
import { formatCurrency } from '@/utils/currency';
import styles from './ZReportModal.module.css';

interface ZReportModalProps {
  isOpen: boolean;
  onClose: () => void;
}

const getTodayISO = (): string => {
  const now = new Date();
  return now.toISOString().split('T')[0];
};

export default function ZReportModal({ isOpen, onClose }: ZReportModalProps) {
  const { t } = useTranslation();
  const [reportDate, setReportDate] = useState<string>(getTodayISO());
  const [reportData, setReportData] = useState<ZReportDto | null>(null);
  const [isLoading, setIsLoading] = useState(false);
  const [error, setError] = useState<string | null>(null);

  const fetchReport = useCallback(async (date: string) => {
    setIsLoading(true);
    setError(null);
    try {
      const data = await getZReport(date);
      setReportData(data);
    } catch {
      setError(t('cashier.zreport.error') || 'Failed to load Z-Report');
      setReportData(null);
    } finally {
      setIsLoading(false);
    }
  }, [t]);

  // Reset date to today and fetch when modal opens
  useEffect(() => {
    if (isOpen) {
      const today = getTodayISO();
      setReportDate(today);
      fetchReport(today);
    }
  }, [isOpen, fetchReport]);

  // Re-fetch when date changes (user picks a different date)
  const handleDateChange = (newDate: string) => {
    setReportDate(newDate);
    fetchReport(newDate);
  };

  if (!isOpen) return null;

  const handleExportPDF = () => {
    if (reportData) {
      exportZReportToPDF(reportData);
    }
  };

  return (
    <div className="modal-overlay" onClick={onClose}>
      <div className={`modal-content ${styles.modalWide}`} onClick={(e) => e.stopPropagation()}>
        {/* Header */}
        <div className="modal-header">
          <div className={styles.headerControls}>
            <FileBarChart size={22} />
            <h2>{t('cashier.zreport.title') || 'Z-Report'}</h2>
            <input
              type="date"
              className={styles.dateInput}
              value={reportDate}
              onChange={(e) => handleDateChange(e.target.value)}
              max={getTodayISO()}
            />
            {reportData && (
              <button className={styles.exportButton} onClick={handleExportPDF}>
                <Printer size={16} />
                {t('cashier.zreport.export_pdf') || 'Print / PDF'}
              </button>
            )}
          </div>
          <button className="modal-close" onClick={onClose} aria-label="Close">
            <X size={20} />
          </button>
        </div>

        {/* Body */}
        <div className="modal-body">
          {isLoading && (
            <div className={styles.loading}>
              <Loader2 size={32} className={styles.spinner} />
              <span>{t('cashier.zreport.loading') || 'Loading report...'}</span>
            </div>
          )}

          {error && !isLoading && (
            <div className={styles.error}>
              <span>{error}</span>
              <button className={styles.retryButton} onClick={() => fetchReport(reportDate)}>
                {t('cashier.zreport.retry') || 'Retry'}
              </button>
            </div>
          )}

          {!isLoading && !error && reportData && (
            <>
              {/* Summary Cards */}
              <div className={styles.summaryCards}>
                <div className={styles.card}>
                  <div className={styles.cardLabel}>{t('cashier.zreport.total_transactions') || 'Transactions'}</div>
                  <div className={styles.cardValue}>{reportData.totalTransactions}</div>
                </div>
                <div className={styles.card}>
                  <div className={styles.cardLabel}>{t('cashier.zreport.gross_sales') || 'Gross Sales'}</div>
                  <div className={styles.cardValue}>{formatCurrency(reportData.grossSales)}</div>
                </div>
                <div className={styles.card}>
                  <div className={styles.cardLabel}>{t('cashier.zreport.net_sales') || 'Net Sales'}</div>
                  <div className={styles.cardValue}>{formatCurrency(reportData.netSales)}</div>
                </div>
                <div className={styles.card}>
                  <div className={styles.cardLabel}>{t('cashier.zreport.total_tax') || 'Tax'}</div>
                  <div className={styles.cardValue}>{formatCurrency(reportData.totalTax)}</div>
                </div>
                <div className={styles.card}>
                  <div className={styles.cardLabel}>{t('cashier.zreport.total_tips') || 'Tips'}</div>
                  <div className={styles.cardValue}>{formatCurrency(reportData.totalTips)}</div>
                </div>
                <div className={styles.card}>
                  <div className={styles.cardLabel}>{t('cashier.zreport.delivery_fees') || 'Delivery Fees'}</div>
                  <div className={styles.cardValue}>{formatCurrency(reportData.totalDeliveryFees)}</div>
                </div>
              </div>

              <div className={styles.twoColumns}>
                {/* Left column */}
                <div>
                  {/* Payment Methods */}
                  <div className={styles.section}>
                    <div className={styles.sectionTitle}>{t('cashier.zreport.payment_methods') || 'Sales by Payment Method'}</div>
                    {reportData.paymentsByMethod.length > 0 ? (
                      <table className={styles.table}>
                        <thead>
                          <tr>
                            <th>{t('cashier.zreport.payment_method') || 'Method'}</th>
                            <th>{t('cashier.zreport.transactions') || 'Txns'}</th>
                            <th>{t('cashier.zreport.amount') || 'Amount'}</th>
                          </tr>
                        </thead>
                        <tbody>
                          {reportData.paymentsByMethod.map((pm) => (
                            <tr key={pm.paymentMethod}>
                              <td>{pm.paymentMethod}</td>
                              <td>{pm.transactionCount}</td>
                              <td>{formatCurrency(pm.totalAmount)}</td>
                            </tr>
                          ))}
                        </tbody>
                      </table>
                    ) : (
                      <div className={styles.empty}>{t('cashier.zreport.no_data') || 'No data'}</div>
                    )}
                  </div>

                  {/* Order Types */}
                  <div className={styles.section}>
                    <div className={styles.sectionTitle}>{t('cashier.zreport.order_types') || 'Sales by Order Type'}</div>
                    {reportData.salesByOrderType.length > 0 ? (
                      <table className={styles.table}>
                        <thead>
                          <tr>
                            <th>{t('cashier.zreport.order_type') || 'Type'}</th>
                            <th>{t('cashier.zreport.orders') || 'Orders'}</th>
                            <th>{t('cashier.zreport.amount') || 'Amount'}</th>
                          </tr>
                        </thead>
                        <tbody>
                          {reportData.salesByOrderType.map((ot) => (
                            <tr key={ot.orderType}>
                              <td>{ot.orderType}</td>
                              <td>{ot.orderCount}</td>
                              <td>{formatCurrency(ot.totalAmount)}</td>
                            </tr>
                          ))}
                        </tbody>
                      </table>
                    ) : (
                      <div className={styles.empty}>{t('cashier.zreport.no_data') || 'No data'}</div>
                    )}
                  </div>
                </div>

                {/* Right column */}
                <div>
                  {/* Discounts */}
                  <div className={styles.section}>
                    <div className={styles.sectionTitle}>{t('cashier.zreport.discounts') || 'Discounts'}</div>
                    <table className={styles.table}>
                      <tbody>
                        <tr>
                          <td>{t('cashier.zreport.promo_code_discounts') || 'Promo Code'}</td>
                          <td>{formatCurrency(reportData.discounts.promoCodeDiscounts)}</td>
                        </tr>
                        <tr>
                          <td>{t('cashier.zreport.customer_discounts') || 'Customer Discounts'}</td>
                          <td>{formatCurrency(reportData.discounts.customerDiscounts)}</td>
                        </tr>
                        <tr>
                          <td>{t('cashier.zreport.fidelity_discounts') || 'Fidelity Points'}</td>
                          <td>{formatCurrency(reportData.discounts.fidelityPointsDiscounts)}</td>
                        </tr>
                        <tr className={styles.totalRow}>
                          <td>{t('cashier.zreport.total_discounts') || 'Total Discounts'}</td>
                          <td>{formatCurrency(reportData.discounts.totalDiscounts)}</td>
                        </tr>
                      </tbody>
                    </table>
                  </div>

                  {/* Refunds & Cancellations */}
                  <div className={styles.section}>
                    <div className={styles.sectionTitle}>{t('cashier.zreport.refunds') || 'Refunds'} & {t('cashier.zreport.cancellations') || 'Cancellations'}</div>
                    <table className={styles.table}>
                      <tbody>
                        <tr>
                          <td>{t('cashier.zreport.refund_count') || 'Refund Count'}</td>
                          <td>{reportData.refunds.refundCount}</td>
                        </tr>
                        <tr>
                          <td>{t('cashier.zreport.total_refunded') || 'Total Refunded'}</td>
                          <td>{formatCurrency(reportData.refunds.totalRefundedAmount)}</td>
                        </tr>
                        <tr>
                          <td>{t('cashier.zreport.cancelled_orders') || 'Cancelled Orders'}</td>
                          <td>{reportData.cancelledOrdersCount}</td>
                        </tr>
                        <tr>
                          <td>{t('cashier.zreport.cancelled_total') || 'Cancelled Total'}</td>
                          <td>{formatCurrency(reportData.cancelledOrdersTotal)}</td>
                        </tr>
                      </tbody>
                    </table>
                  </div>

                  {/* Product Types */}
                  {reportData.salesByProductType.length > 0 && (
                    <div className={styles.section}>
                      <div className={styles.sectionTitle}>{t('cashier.zreport.product_types') || 'Sales by Product Type'}</div>
                      <table className={styles.table}>
                        <thead>
                          <tr>
                            <th>{t('cashier.zreport.product_type') || 'Type'}</th>
                            <th>{t('cashier.zreport.items_sold') || 'Items'}</th>
                            <th>{t('cashier.zreport.amount') || 'Amount'}</th>
                          </tr>
                        </thead>
                        <tbody>
                          {reportData.salesByProductType.map((pt) => (
                            <tr key={pt.productType}>
                              <td>{pt.productType}</td>
                              <td>{pt.itemCount}</td>
                              <td>{formatCurrency(pt.totalAmount)}</td>
                            </tr>
                          ))}
                        </tbody>
                      </table>
                    </div>
                  )}
                </div>
              </div>

              {/* Top Selling Items */}
              {reportData.topSellingItems.length > 0 && (
                <div className={styles.section}>
                  <div className={styles.sectionTitle}>{t('cashier.zreport.top_items') || 'Top Selling Items'}</div>
                  <table className={styles.table}>
                    <thead>
                      <tr>
                        <th>#</th>
                        <th>{t('cashier.zreport.product_name') || 'Product'}</th>
                        <th>{t('cashier.zreport.quantity_sold') || 'Qty Sold'}</th>
                        <th>{t('cashier.zreport.revenue') || 'Revenue'}</th>
                      </tr>
                    </thead>
                    <tbody>
                      {reportData.topSellingItems.map((item, idx) => (
                        <tr key={item.productName}>
                          <td className={styles.rank}>{idx + 1}</td>
                          <td>{item.productName}</td>
                          <td>{item.quantitySold}</td>
                          <td>{formatCurrency(item.totalRevenue)}</td>
                        </tr>
                      ))}
                    </tbody>
                  </table>
                </div>
              )}

              {reportData.totalTransactions === 0 && (
                <div className={styles.empty}>
                  {t('cashier.zreport.no_data') || 'No data available for this date'}
                </div>
              )}
            </>
          )}
        </div>
      </div>
    </div>
  );
}
