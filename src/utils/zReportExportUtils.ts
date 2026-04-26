import { ZReportDto } from '@/types/order';
import { printHtmlContent } from './pdfExportUtils';
import { formatCurrency } from './currency';

const formatDate = (dateStr: string): string => {
  return new Date(dateStr).toLocaleDateString('de-CH', {
    weekday: 'long',
    year: 'numeric',
    month: 'long',
    day: 'numeric',
  });
};

const formatTimestamp = (dateStr: string): string => {
  return new Date(dateStr).toLocaleString('de-CH');
};

export const exportZReportToPDF = (report: ZReportDto): void => {
  const html = `<!DOCTYPE html>
<html>
<head>
  <meta charset="UTF-8">
  <title>Z-Report - ${formatDate(report.reportDate)}</title>
  <style>
    @page { size: A4 portrait; margin: 15mm; }
    * { margin: 0; padding: 0; box-sizing: border-box; }
    body { font-family: 'Helvetica Neue', Arial, sans-serif; font-size: 12px; color: #333; line-height: 1.4; }

    .header { text-align: center; margin-bottom: 20px; padding-bottom: 12px; border-bottom: 3px solid #c0392b; }
    .header h1 { font-size: 22px; color: #c0392b; margin-bottom: 4px; }
    .header .subtitle { font-size: 16px; font-weight: 600; color: #555; }
    .header .date { font-size: 13px; color: #777; margin-top: 4px; }

    .summary-grid { display: grid; grid-template-columns: repeat(3, 1fr); gap: 10px; margin-bottom: 20px; }
    .summary-card { border: 1px solid #ddd; border-radius: 6px; padding: 10px; text-align: center; }
    .summary-card .label { font-size: 10px; color: #888; text-transform: uppercase; letter-spacing: 0.5px; }
    .summary-card .value { font-size: 18px; font-weight: 700; color: #222; margin-top: 2px; }

    .two-col { display: grid; grid-template-columns: 1fr 1fr; gap: 20px; margin-bottom: 20px; }

    .section { margin-bottom: 16px; }
    .section-title { font-size: 13px; font-weight: 700; color: #c0392b; border-bottom: 2px solid #c0392b; padding-bottom: 4px; margin-bottom: 8px; }

    table { width: 100%; border-collapse: collapse; font-size: 11px; }
    th { text-align: left; padding: 4px 8px; font-weight: 600; color: #777; border-bottom: 1px solid #ddd; font-size: 10px; text-transform: uppercase; }
    th:last-child, td:last-child { text-align: right; }
    td { padding: 4px 8px; border-bottom: 1px solid #f0f0f0; }
    .total-row td { font-weight: 700; border-top: 2px solid #ddd; border-bottom: none; }

    .footer { margin-top: 20px; padding-top: 10px; border-top: 1px solid #ddd; text-align: center; font-size: 10px; color: #999; }

    @media print { body { -webkit-print-color-adjust: exact; print-color-adjust: exact; } }
  </style>
</head>
<body>
  <div class="header">
    <h1>Rumi Restaurant</h1>
    <div class="subtitle">Z-Report / End of Day Summary</div>
    <div class="date">${formatDate(report.reportDate)}</div>
  </div>

  <div class="summary-grid">
    <div class="summary-card">
      <div class="label">Transactions</div>
      <div class="value">${report.totalTransactions}</div>
    </div>
    <div class="summary-card">
      <div class="label">Gross Sales</div>
      <div class="value">${formatCurrency(report.grossSales)}</div>
    </div>
    <div class="summary-card">
      <div class="label">Net Sales</div>
      <div class="value">${formatCurrency(report.netSales)}</div>
    </div>
    <div class="summary-card">
      <div class="label">Tax</div>
      <div class="value">${formatCurrency(report.totalTax)}</div>
    </div>
    <div class="summary-card">
      <div class="label">Tips</div>
      <div class="value">${formatCurrency(report.totalTips)}</div>
    </div>
    <div class="summary-card">
      <div class="label">Delivery Fees</div>
      <div class="value">${formatCurrency(report.totalDeliveryFees)}</div>
    </div>
  </div>

  <div class="two-col">
    <div>
      <div class="section">
        <div class="section-title">Sales by Payment Method</div>
        ${report.paymentsByMethod.length > 0 ? `
        <table>
          <thead><tr><th>Method</th><th>Txns</th><th>Amount</th></tr></thead>
          <tbody>
            ${report.paymentsByMethod.map(pm => `<tr><td>${pm.paymentMethod}</td><td>${pm.transactionCount}</td><td>${formatCurrency(pm.totalAmount)}</td></tr>`).join('')}
          </tbody>
        </table>` : '<p style="color:#999;font-size:11px;">No payments</p>'}
      </div>

      <div class="section">
        <div class="section-title">Sales by Order Type</div>
        ${report.salesByOrderType.length > 0 ? `
        <table>
          <thead><tr><th>Type</th><th>Orders</th><th>Amount</th></tr></thead>
          <tbody>
            ${report.salesByOrderType.map(ot => `<tr><td>${ot.orderType}</td><td>${ot.orderCount}</td><td>${formatCurrency(ot.totalAmount)}</td></tr>`).join('')}
          </tbody>
        </table>` : '<p style="color:#999;font-size:11px;">No orders</p>'}
      </div>
    </div>

    <div>
      <div class="section">
        <div class="section-title">Discounts</div>
        <table>
          <tbody>
            <tr><td>Promo Code</td><td>${formatCurrency(report.discounts.promoCodeDiscounts)}</td></tr>
            <tr><td>Customer Discounts</td><td>${formatCurrency(report.discounts.customerDiscounts)}</td></tr>
            <tr><td>Fidelity Points</td><td>${formatCurrency(report.discounts.fidelityPointsDiscounts)}</td></tr>
            <tr class="total-row"><td>Total Discounts</td><td>${formatCurrency(report.discounts.totalDiscounts)}</td></tr>
          </tbody>
        </table>
      </div>

      <div class="section">
        <div class="section-title">Refunds & Cancellations</div>
        <table>
          <tbody>
            <tr><td>Refund Count</td><td>${report.refunds.refundCount}</td></tr>
            <tr><td>Total Refunded</td><td>${formatCurrency(report.refunds.totalRefundedAmount)}</td></tr>
            <tr><td>Cancelled Orders</td><td>${report.cancelledOrdersCount}</td></tr>
            <tr><td>Cancelled Total</td><td>${formatCurrency(report.cancelledOrdersTotal)}</td></tr>
          </tbody>
        </table>
      </div>

      ${report.salesByProductType.length > 0 ? `
      <div class="section">
        <div class="section-title">Sales by Product Type</div>
        <table>
          <thead><tr><th>Type</th><th>Items</th><th>Amount</th></tr></thead>
          <tbody>
            ${report.salesByProductType.map(pt => `<tr><td>${pt.productType}</td><td>${pt.itemCount}</td><td>${formatCurrency(pt.totalAmount)}</td></tr>`).join('')}
          </tbody>
        </table>
      </div>` : ''}
    </div>
  </div>

  ${report.topSellingItems.length > 0 ? `
  <div class="section">
    <div class="section-title">Top Selling Items</div>
    <table>
      <thead><tr><th>#</th><th>Product</th><th>Qty Sold</th><th>Revenue</th></tr></thead>
      <tbody>
        ${report.topSellingItems.map((item, i) => `<tr><td style="color:#999">${i + 1}</td><td>${item.productName}</td><td>${item.quantitySold}</td><td>${formatCurrency(item.totalRevenue)}</td></tr>`).join('')}
      </tbody>
    </table>
  </div>` : ''}

  <div class="footer">
    Generated: ${formatTimestamp(report.generatedAt)} | Rumi Restaurant Z-Report
  </div>
</body>
</html>`;

  printHtmlContent(html);
};
