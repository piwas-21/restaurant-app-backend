/**
 * PDF Export Utilities
 * 
 * This file contains orchestration logic for printing/exporting orders.
 * HTML templates are in ./templates/ directory.
 */
import { OrderDto } from '@/types/order';

// Import templates
import { 
  generateSimpleReceiptHtml, 
  generateKitchenReceiptHtml 
} from './templates';

// Re-export templates for backward compatibility
export { generateSimpleReceiptHtml, generateKitchenReceiptHtml };

// Translation function type
type TranslationFunction = (key: string, fallback: string) => string;

// =============================================================================
// PRINT UTILITY
// =============================================================================

/**
 * Helper to print HTML content using a hidden iframe
 * This avoids opening new tabs/windows for each print job
 */
export const printHtmlContent = (htmlContent: string): void => {
  // Create a hidden iframe
  const iframe = document.createElement('iframe');
  iframe.style.position = 'fixed';
  iframe.style.right = '0';
  iframe.style.bottom = '0';
  iframe.style.width = '0';
  iframe.style.height = '0';
  iframe.style.border = '0';
  
  document.body.appendChild(iframe);
  
  const doc = iframe.contentWindow?.document;
  if (!doc) {
    document.body.removeChild(iframe);
    throw new Error('Could not access iframe document');
  }

  doc.open();
  doc.write(htmlContent);
  doc.close();

  // Wait for content to load then print
  iframe.onload = () => {
    try {
      iframe.contentWindow?.focus();
      iframe.contentWindow?.print();
    } catch (e) {
      console.error('Print failed', e);
    } finally {
      // Clean up after a delay to allow print dialog to initialize
      setTimeout(() => {
        if (document.body.contains(iframe)) {
          document.body.removeChild(iframe);
        }
      }, 60000);
    }
  };
};

// =============================================================================
// EXPORT FUNCTIONS - Using new templates
// =============================================================================

/**
 * Export a single order to PDF using simplified thermal receipt style (80mm)
 * This is the preferred format for thermal printers
 */
export const exportOrderToPDF = (order: OrderDto, t?: TranslationFunction): void => {
  const html = generateSimpleReceiptHtml(order, t);
  printHtmlContent(html);
};

/**
 * Alias for exportOrderToPDF - explicit thermal receipt export
 */
export const exportSimpleReceiptToPDF = (order: OrderDto, t?: TranslationFunction): void => {
  const html = generateSimpleReceiptHtml(order, t);
  printHtmlContent(html);
};

/**
 * Export order items by kitchen type for kitchen staff
 * Uses the new kitchen receipt template with ingredient filtering
 */
export const exportKitchenItemsToPDF = (
  order: OrderDto,
  kitchenType: 'FrontKitchen' | 'BackKitchen' | 'All',
  t?: TranslationFunction
): void => {
  const translate = t || ((key: string, fallback: string) => fallback);
  const html = generateKitchenReceiptHtml(order, kitchenType, t);

  if (!html) {
    alert(translate('cashier.no_items_for_kitchen', 'No items for this kitchen type'));
    return;
  }

  printHtmlContent(html);
};

// =============================================================================
// MULTI-ORDER EXPORT (for reports)
// =============================================================================

// Helper functions
const formatDate = (dateString: string): string => {
  const date = new Date(dateString);
  return date.toLocaleString('en-GB', {
    day: '2-digit',
    month: '2-digit',
    year: 'numeric',
    hour: '2-digit',
    minute: '2-digit',
  });
};

const formatCurrency = (amount: number): string => {
  return `CHF ${amount.toFixed(2)}`;
};

const escapeHtml = (text: string): string => {
  const map: { [key: string]: string } = {
    '&': '&amp;',
    '<': '&lt;',
    '>': '&gt;',
    '"': '&quot;',
    "'": '&#039;',
  };
  return text.replace(/[&<>"']/g, (char) => map[char]);
};

const getOrderStatusLabel = (status: string, t?: TranslationFunction): string => {
  const translate = t || ((key: string, fallback: string) => fallback);
  switch (status) {
    case 'Pending': return translate('order_status_pending', 'Pending');
    case 'Confirmed': return translate('order_status_confirmed', 'Confirmed');
    case 'Preparing': return translate('order_status_preparing', 'Preparing');
    case 'Ready': return translate('order_status_ready', 'Ready');
    case 'Completed': return translate('order_status_completed', 'Completed');
    case 'Cancelled': return translate('order_status_cancelled', 'Cancelled');
    default: return status;
  }
};

/**
 * Export multiple orders to PDF using browser's print functionality
 * Used for generating reports
 */
export const exportOrdersToPDF = (orders: OrderDto[], t?: TranslationFunction): void => {
  const translate = t || ((key: string, fallback: string) => fallback);

  const ordersRows = orders.map(order => `
    <tr>
      <td>${escapeHtml(order.orderNumber)}</td>
      <td>${formatDate(order.orderDate)}</td>
      <td>${escapeHtml(order.customerName || translate('guest', 'Guest'))}</td>
      <td style="text-align: center;">${order.items.length}</td>
      <td style="text-align: right;">${formatCurrency(order.total)}</td>
      <td>${escapeHtml(getOrderStatusLabel(order.status, t))}</td>
    </tr>
  `).join('');

  const totalAmount = orders.reduce((sum, order) => sum + order.total, 0);
  const completedOrders = orders.filter(o => o.status === 'Completed').length;
  const cancelledOrders = orders.filter(o => o.status === 'Cancelled').length;

  const html = `
    <!DOCTYPE html>
    <html>
      <head>
        <meta charset="UTF-8">
        <title>${translate('orders_report', 'Orders Report')}</title>
        <style>
          @page { size: A4 landscape; margin: 15mm; }
          @media print { body { margin: 0; padding: 0; } }
          * { margin: 0; padding: 0; box-sizing: border-box; }
          body { font-family: Arial, sans-serif; font-size: 11pt; line-height: 1.5; padding: 20px; }
          .header { text-align: center; margin-bottom: 20px; border-bottom: 2px solid #c0392b; padding-bottom: 10px; }
          .header h1 { font-size: 20pt; color: #c0392b; }
          .summary { display: flex; gap: 20px; margin-bottom: 20px; }
          .summary-card { flex: 1; padding: 15px; background: #f8f9fa; border-radius: 8px; text-align: center; }
          .summary-card h3 { font-size: 10pt; color: #666; margin-bottom: 5px; }
          .summary-card .value { font-size: 18pt; font-weight: bold; color: #333; }
          table { width: 100%; border-collapse: collapse; }
          th, td { padding: 10px; border: 1px solid #e5e7eb; }
          thead { background: #c0392b; color: white; }
          tbody tr:nth-child(even) { background: #f8f9fa; }
          .footer { text-align: center; margin-top: 20px; font-size: 9pt; color: #666; }
        </style>
      </head>
      <body>
        <div class="header">
          <h1>Rumi Restaurant</h1>
          <p>${translate('orders_report', 'Orders Report')} - ${new Date().toLocaleDateString()}</p>
        </div>

        <div class="summary">
          <div class="summary-card">
            <h3>${translate('total_orders', 'Total Orders')}</h3>
            <div class="value">${orders.length}</div>
          </div>
          <div class="summary-card">
            <h3>${translate('completed', 'Completed')}</h3>
            <div class="value" style="color: #27ae60;">${completedOrders}</div>
          </div>
          <div class="summary-card">
            <h3>${translate('cancelled', 'Cancelled')}</h3>
            <div class="value" style="color: #e74c3c;">${cancelledOrders}</div>
          </div>
          <div class="summary-card">
            <h3>${translate('total_revenue', 'Total Revenue')}</h3>
            <div class="value" style="color: #c0392b;">${formatCurrency(totalAmount)}</div>
          </div>
        </div>

        <table>
          <thead>
            <tr>
              <th>${translate('order_number', 'Order #')}</th>
              <th>${translate('date_time', 'Date/Time')}</th>
              <th>${translate('customer', 'Customer')}</th>
              <th style="text-align: center;">${translate('items', 'Items')}</th>
              <th style="text-align: right;">${translate('total', 'Total')}</th>
              <th>${translate('status', 'Status')}</th>
            </tr>
          </thead>
          <tbody>
            ${ordersRows}
          </tbody>
        </table>

        <div class="footer">
          ${translate('printed_at', 'Printed at')}: ${new Date().toLocaleString()}
        </div>
      </body>
    </html>
  `;

  printHtmlContent(html);
};
