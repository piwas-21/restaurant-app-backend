import { OrderDto } from '@/types/order';
import { getPaymentMethodLabel } from './paymentMethodDisplay';

// Translation function type
type TranslationFunction = (key: string, fallback: string) => string;

/**
 * Format date for PDF display
 */
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

/**
 * Format currency for PDF display
 */
const formatCurrency = (amount: number): string => {
  return `CHF ${amount.toFixed(2)}`;
};

/**
 * Get order type label
 */
const getOrderTypeLabel = (type: string, t?: TranslationFunction): string => {
  const translate = t || ((key: string, fallback: string) => fallback);

  switch (type) {
    case 'DineIn':
      return translate('order_type_dine_in', 'Dine In');
    case 'Takeaway':
      return translate('order_type_takeaway', 'Takeaway');
    case 'Delivery':
      return translate('order_type_delivery', 'Delivery');
    default:
      return type;
  }
};

/**
 * Escape HTML special characters
 */
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
      // Note: In some browsers, removing iframe too early can cancel print.
      // 1 minute timeout should be safe enough while preventing DOM clutter
      setTimeout(() => {
        if (document.body.contains(iframe)) {
          document.body.removeChild(iframe);
        }
      }, 60000);
    }
  };
};

/**
 * Generate HTML for a single order receipt
 */
export const generateOrderReceiptHtml = (order: OrderDto, t?: TranslationFunction): string => {
  const translate = t || ((key: string, fallback: string) => fallback);

  // Build order items HTML
  const itemsRows = order.items.map(item => `
    <tr>
      <td>${escapeHtml(item.productName || item.menuName || translate('item', 'Item'))}</td>
      <td style="text-align: center;">${item.quantity}</td>
      <td style="text-align: right;">${formatCurrency(item.unitPrice)}</td>
      <td style="text-align: right;">${formatCurrency(item.itemTotal)}</td>
    </tr>
  `).join('');

  // Build delivery address section if applicable
  const deliverySection = order.type === 'Delivery' && order.deliveryAddress ? `
    <div class="section">
      <h3>${translate('delivery_address', 'Delivery Address')}</h3>
      <div class="info-grid">
        ${order.deliveryAddress.addressLine1 ? `<p>${escapeHtml(order.deliveryAddress.addressLine1)}</p>` : ''}
        ${order.deliveryAddress.addressLine2 ? `<p>${escapeHtml(order.deliveryAddress.addressLine2)}</p>` : ''}
        <p>${escapeHtml(order.deliveryAddress.postalCode || '')} ${escapeHtml(order.deliveryAddress.city || '')}</p>
      </div>
    </div>
  ` : '';

  // Build payment details section if applicable
  const paymentsSection = order.payments && order.payments.length > 0 ? `
    <div class="section">
      <h3>${translate('payment_details', 'Payment Details')}</h3>
      <table class="payment-table">
        ${order.payments.map(payment => {
          const paymentMethod = getPaymentMethodLabel(payment.paymentMethod) || translate('n_a', 'N/A');
          const paymentStatus = payment.status
            ? translate(`payment_status_${payment.status.toLowerCase()}`, payment.status)
            : translate('n_a', 'N/A');

          return `
            <tr>
              <td>${escapeHtml(paymentMethod)}</td>
              <td style="text-align: right;">${formatCurrency(payment.amount)}</td>
              <td>${escapeHtml(paymentStatus)}</td>
            </tr>
          `;
        }).join('')}
      </table>
    </div>
  ` : '';

  // Build notes section if applicable
  const notesSection = order.notes ? `
    <div class="section">
      <h3>${translate('notes', 'Notes')}</h3>
      <p>${escapeHtml(order.notes)}</p>
    </div>
  ` : '';

  return `
    <!DOCTYPE html>
    <html>
      <head>
        <meta charset="UTF-8">
        <title>${translate('order_details', 'Order Details')} - ${escapeHtml(order.orderNumber)}</title>
        <style>
          @page {
            size: A4 portrait;
            margin: 20mm;
          }
          @media print {
            body {
              margin: 0;
              padding: 0;
            }
          }
          * {
            margin: 0;
            padding: 0;
            box-sizing: border-box;
          }
          body {
            font-family: -apple-system, BlinkMacSystemFont, 'Segoe UI', Roboto, 'Helvetica Neue', Arial, sans-serif;
            font-size: 11pt;
            line-height: 1.5;
            color: #1a1a1a;
            background: white;
            margin: 0; /* Changed from 50px for iframe compatibility */
            padding: 20px;
          }
          .header {
            text-align: center;
            margin-bottom: 20px;
            border-bottom: 2px solid #c0392b;
            padding-bottom: 10px;
          }
          .header h1 {
            font-size: 24pt;
            color: #c0392b;
            margin-bottom: 5px;
          }
          .header h2 {
            font-size: 16pt;
            color: #555;
          }
          .section {
            margin: 20px 0;
            page-break-inside: avoid;
          }
          .section h3 {
            font-size: 13pt;
            color: #c0392b;
            margin-bottom: 10px;
            border-bottom: 1px solid #e5e7eb;
            padding-bottom: 5px;
          }
          .info-grid {
            margin: 10px 0;
          }
          .info-grid-row {
            display: grid;
            grid-template-columns: 150px 1fr;
            gap: 8px;
            margin: 5px 0;
          }
          .info-grid-row .label {
            font-weight: 600;
            color: #555;
          }
          .info-grid-row .value {
            color: #1a1a1a;
          }
          table {
            width: 100%;
            border-collapse: collapse;
            margin: 10px 0;
          }
          table.items-table {
            border: 1px solid #e5e7eb;
          }
          table.items-table thead {
            background: #c0392b;
            color: white;
          }
          table.items-table th,
          table.items-table td {
            padding: 10px;
            text-align: left;
            border: 1px solid #e5e7eb;
          }
          table.payment-table td {
            padding: 5px 10px;
            border-bottom: 1px solid #e5e7eb;
          }
          .summary {
            margin-top: 20px;
            border-top: 2px solid #333;
            padding-top: 10px;
            page-break-inside: avoid;
          }
          .summary-row {
            display: flex;
            justify-content: space-between;
            padding: 5px 0;
            margin-left: 60%;
          }
          .summary-row.total {
            font-weight: bold;
            font-size: 13pt;
            padding-top: 10px;
          }
          .summary-row .label {
            font-weight: 600;
          }
        </style>
      </head>
      <body>
        <div class="header">
          <h1>Rumi Restaurant</h1>
          <h2>${translate('order_details', 'Order Details')}</h2>
        </div>

        <div class="section">
          <h3>${translate('order_information', 'Order Information')}</h3>
          <div class="info-grid">
            <div class="info-grid-row">
              <div class="label">${translate('order_number', 'Order Number')}:</div>
              <div class="value">${escapeHtml(order.orderNumber)}</div>
            </div>

            <div class="info-grid-row">
              <div class="label">${translate('order_date', 'Order Date')}:</div>
              <div class="value">${formatDate(order.orderDate)}</div>
            </div>

            <div class="info-grid-row">
              <div class="label">${translate('status', 'Status')}:</div>
              <div class="value">${escapeHtml(order.status ? translate('order_status_' + order.status.toLowerCase(), order.status) : translate('n_a', 'N/A'))}</div>
            </div>

            <div class="info-grid-row">
              <div class="label">${translate('order_type', 'Order Type')}:</div>
              <div class="value">${escapeHtml(getOrderTypeLabel(order.type, t))}</div>
            </div>

            <div class="info-grid-row">
              <div class="label">${translate('payment_status', 'Payment Status')}:</div>
              <div class="value">${escapeHtml(order.paymentStatus ? translate('payment_status_' + order.paymentStatus.toLowerCase(), order.paymentStatus) : translate('n_a', 'N/A'))}</div>
            </div>

            ${order.type === 'DineIn' && order.tableNumber ? `
              <div class="info-grid-row">
                <div class="label">${translate('table_number', 'Table Number')}:</div>
                <div class="value">${escapeHtml(order.tableNumber.toString())}</div>
              </div>
            ` : ''}
          </div>
        </div>

        <div class="section">
          <h3>${translate('customer_information', 'Customer Information')}</h3>
          <div class="info-grid">
            <div class="info-grid-row">
              <div class="label">${translate('name', 'Name')}:</div>
              <div class="value">${escapeHtml(order.customerName || translate('n_a', 'N/A'))}</div>
            </div>

            <div class="info-grid-row">
              <div class="label">${translate('email', 'Email')}:</div>
              <div class="value">${escapeHtml(order.customerEmail || translate('n_a', 'N/A'))}</div>
            </div>

            <div class="info-grid-row">
              <div class="label">${translate('phone', 'Phone')}:</div>
              <div class="value">${escapeHtml(order.customerPhone || translate('n_a', 'N/A'))}</div>
            </div>
          </div>
        </div>

        ${deliverySection}

        <div class="section">
          <h3>${translate('order_items', 'Order Items')}</h3>
          <table class="items-table">
            <thead>
              <tr>
                <th>${translate('item', 'Item')}</th>
                <th style="text-align: center;">${translate('qty', 'Qty')}</th>
                <th style="text-align: right;">${translate('price', 'Price')}</th>
                <th style="text-align: right;">${translate('total', 'Total')}</th>
              </tr>
            </thead>
            <tbody>
              ${itemsRows}
            </tbody>
          </table>
        </div>

        <div class="summary">
          <div class="summary-row">
            <span class="label">${translate('subtotal', 'Subtotal')}:</span>
            <span>${formatCurrency(order.subTotal)}</span>
          </div>
          ${order.tax > 0 ? `
            <div class="summary-row">
              <span class="label">${translate('tax', 'Tax')}:</span>
              <span>${formatCurrency(order.tax)}</span>
            </div>
          ` : ''}
          ${order.deliveryFee && order.deliveryFee > 0 ? `
            <div class="summary-row">
              <span class="label">${translate('delivery_fee', 'Delivery Fee')}:</span>
              <span>${formatCurrency(order.deliveryFee)}</span>
            </div>
          ` : ''}
          ${order.discount && order.discount > 0 ? `
            <div class="summary-row">
              <span class="label">${translate('discount', 'Discount')}:</span>
              <span>-${formatCurrency(order.discount)}</span>
            </div>
          ` : ''}
          ${order.tip && order.tip > 0 ? `
            <div class="summary-row">
              <span class="label">${translate('tip', 'Tip')}:</span>
              <span>${formatCurrency(order.tip)}</span>
            </div>
          ` : ''}
          <div class="summary-row total">
            <span class="label">${translate('total', 'Total')}:</span>
            <span>${formatCurrency(order.total)}</span>
          </div>
        </div>

        ${paymentsSection}
        ${notesSection}
      </body>
    </html>
  `;
};

/**
 * Generate HTML for kitchen items
 */
export const generateKitchenItemsHtml = (
  order: OrderDto,
  kitchenType: 'FrontKitchen' | 'BackKitchen' | 'All',
  t?: TranslationFunction
): string | null => {
  const translate = t || ((key: string, fallback: string) => fallback);

  // Filter items by kitchen type
  const filteredItems = order.items.filter(item => {
    if (kitchenType === 'All') return true;
    return item.kitchenType === kitchenType;
  });

  if (filteredItems.length === 0) {
    return null;
  }

  // Get kitchen type label
  const getKitchenTypeLabel = (): string => {
    switch (kitchenType) {
      case 'FrontKitchen':
        return translate('kitchen_type_frontkitchen', 'Front Kitchen');
      case 'BackKitchen':
        return translate('kitchen_type_backkitchen', 'Back Kitchen');
      default:
        return translate('order_details', 'Order Details');
    }
  };

  // Build order items HTML
  const itemsRows = filteredItems.map(item => `
    <tr>
      <td>${escapeHtml(item.productName || item.menuName || translate('item', 'Item'))}</td>
      <td style="text-align: center;">${item.quantity}</td>
      ${kitchenType === 'All' ? `
        <td style="text-align: right;">${formatCurrency(item.unitPrice)}</td>
        <td style="text-align: right;">${formatCurrency(item.itemTotal)}</td>
      ` : ''}
    </tr>
  `).join('');

  return `
    <!DOCTYPE html>
    <html>
      <head>
        <meta charset="UTF-8">
        <title>${getKitchenTypeLabel()} - ${escapeHtml(order.orderNumber)}</title>
        <style>
          @page {
            size: A4 portrait;
            margin: 20mm;
          }
          @media print {
            body {
              margin: 0;
              padding: 0;
            }
          }
          * {
            margin: 0;
            padding: 0;
            box-sizing: border-box;
          }
          body {
            font-family: -apple-system, BlinkMacSystemFont, 'Segoe UI', Roboto, 'Helvetica Neue', Arial, sans-serif;
            font-size: 12pt;
            line-height: 1.6;
            color: #1a1a1a;
            background: white;
            margin: 0; /* Changed from 40px for iframe compatibility */
            padding: 20px;
          }
          .header {
            text-align: center;
            margin-bottom: 20px;
            border-bottom: 2px solid #c0392b;
            padding-bottom: 10px;
          }
          .header h1 {
            font-size: 20pt;
            color: #c0392b;
            margin-bottom: 5px;
          }
          .header h2 {
            font-size: 14pt;
            color: #555;
          }
          .order-info {
            margin: 15px 0;
            padding: 10px;
            background: #f3f4f6;
            border-left: 4px solid #c0392b;
          }
          .order-info p {
            margin: 3px 0;
            font-size: 11pt;
          }
          .order-info .order-number {
            font-size: 18pt;
            font-weight: bold;
            color: #c0392b;
            margin-bottom: 5px;
          }
          table {
            width: 100%;
            border-collapse: collapse;
            margin: 15px 0;
          }
          table.items-table {
            border: 1px solid #e5e7eb;
          }
          table.items-table thead {
            background: #c0392b;
            color: white;
          }
          table.items-table th,
          table.items-table td {
            padding: 8px 10px;
            text-align: left;
            border: 1px solid #e5e7eb;
          }
          table.items-table th {
            font-weight: bold;
          }
          .item-instructions {
            font-size: 10pt;
            color: #666;
            font-style: italic;
            margin-top: 3px;
          }
          .customer-info {
            margin-top: 15px;
            padding: 10px;
            background: #f3f4f6;
            border-left: 4px solid #3b82f6;
            font-size: 11pt;
          }
          .timestamp {
            text-align: center;
            font-size: 10pt;
            color: #999;
            margin-top: 20px;
            border-top: 1px solid #e5e7eb;
            padding-top: 10px;
          }
        </style>
      </head>
      <body>
        <div class="header">
          <h1>Rumi Restaurant</h1>
          <h2>${getKitchenTypeLabel()}</h2>
        </div>

        <div class="order-info">
          <div class="order-number">${escapeHtml(order.orderNumber)}</div>
          <p><strong>${translate('order_type', 'Order Type')}:</strong> ${escapeHtml(order.type ? translate('order_type.' + order.type.toLowerCase(), order.type) : 'Unknown')}</p>
          ${order.type === 'DineIn' && order.tableNumber ? `<p><strong>${translate('table_number', 'Table')}:</strong> ${escapeHtml(order.tableNumber.toString())}</p>` : ''}
          <p><strong>${translate('order_date', 'Order Time')}:</strong> ${new Date(order.orderDate).toLocaleTimeString()}</p>
        </div>

        ${order.customerName ? `
          <div class="customer-info">
            <strong>${translate('customer_name', 'Customer')}:</strong> ${escapeHtml(order.customerName)}<br/>
            ${order.customerPhone ? `<strong>${translate('phone', 'Phone')}:</strong> ${escapeHtml(order.customerPhone)}<br/>` : ''}
          </div>
        ` : ''}

        <div>
          <h3 style="font-size: 13pt; color: #c0392b; margin: 15px 0 10px 0;">${translate('order_items', 'Items')}</h3>
          <table class="items-table">
            <thead>
              <tr>
                <th>${translate('item', 'Item')}</th>
                <th style="text-align: center;">${translate('qty', 'Qty')}</th>
                ${kitchenType === 'All' ? `
                  <th style="text-align: right;">${translate('price', 'Price')}</th>
                  <th style="text-align: right;">${translate('total', 'Total')}</th>
                ` : ''}
              </tr>
            </thead>
            <tbody>
              ${itemsRows}
            </tbody>
          </table>
        </div>

        ${kitchenType === 'All' ? `
          <div style="margin-top: 20px; border-top: 2px solid #333; padding-top: 10px;">
            <div style="display: flex; justify-content: space-between; margin: 5px 0; margin-left: 60%;">
              <span><strong>${translate('subtotal', 'Subtotal')}:</strong></span>
              <span>${formatCurrency(order.subTotal)}</span>
            </div>
            ${order.tax > 0 ? `
              <div style="display: flex; justify-content: space-between; margin: 5px 0; margin-left: 60%;">
                <span><strong>${translate('tax', 'Tax')}:</strong></span>
                <span>${formatCurrency(order.tax)}</span>
              </div>
            ` : ''}
            <div style="display: flex; justify-content: space-between; margin: 10px 0; margin-left: 60%; font-weight: bold; font-size: 13pt;">
              <span>${translate('total', 'Total')}:</span>
              <span>${formatCurrency(order.total)}</span>
            </div>
          </div>
        ` : ''}

        <div class="timestamp">
          ${translate('printed_at', 'Printed at')}: ${new Date().toLocaleString()}
        </div>
      </body>
    </html>
  `;
};

/**
 * Export a single order to PDF using browser's print functionality
 */
export const exportOrderToPDF = (order: OrderDto, t?: TranslationFunction): void => {
  const html = generateOrderReceiptHtml(order, t);
  printHtmlContent(html);
};

/**
 * Export order items by kitchen type for kitchen staff
 */
export const exportKitchenItemsToPDF = (
  order: OrderDto,
  kitchenType: 'FrontKitchen' | 'BackKitchen' | 'All',
  t?: TranslationFunction
): void => {
  const translate = t || ((key: string, fallback: string) => fallback);
  const html = generateKitchenItemsHtml(order, kitchenType, t);

  if (!html) {
    alert(translate('cashier.no_items_for_kitchen', 'No items for this kitchen type'));
    return;
  }

  printHtmlContent(html);
};

/**
 * Export multiple orders to PDF using browser's print functionality
 */
export const exportOrdersToPDF = (orders: OrderDto[], t?: TranslationFunction): void => {
  const translate = t || ((key: string, fallback: string) => fallback);

  // Build orders table rows
  const ordersRows = orders.map(order => `
    <tr>
      <td>${escapeHtml(order.orderNumber)}</td>
      <td>${formatDate(order.orderDate)}</td>
      <td>${escapeHtml(getOrderTypeLabel(order.type, t))}</td>
      <td>${escapeHtml(order.status ? translate('order_status_' + order.status.toLowerCase(), order.status) : translate('n_a', 'N/A'))}</td>
      <td>${escapeHtml(order.customerName || translate('n_a', 'N/A'))}</td>
      <td style="text-align: right;">${formatCurrency(order.total)}</td>
      <td>${escapeHtml(order.paymentStatus ? translate('payment_status_' + order.paymentStatus.toLowerCase(), order.paymentStatus) : translate('n_a', 'N/A'))}</td>
    </tr>
  `).join('');

  // Calculate summary
  const totalRevenue = orders.reduce((sum, order) => sum + order.total, 0);
  const paidOrders = orders.filter(o => o.paymentStatus === 'Paid').length;

  const html = `
    <!DOCTYPE html>
    <html>
      <head>
        <meta charset="UTF-8">
        <title>${translate('orders_export', 'Orders Export')}</title>
        <style>
          @page {
            size: A4 landscape;
            margin: 20mm;
          }
          @media print {
            body {
              margin: 0;
              padding: 0;
            }
          }
          * {
            margin: 0;
            padding: 0;
            box-sizing: border-box;
          }
          body {
            font-family: -apple-system, BlinkMacSystemFont, 'Segoe UI', Roboto, 'Helvetica Neue', Arial, sans-serif;
            font-size: 10pt;
            line-height: 1.5;
            color: #1a1a1a;
            background: white;
            margin: 0;
            padding: 20px;
          }
          .header {
            text-align: center;
            margin-bottom: 20px;
            border-bottom: 2px solid #c0392b;
            padding-bottom: 10px;
          }
          .header h1 {
            font-size: 24pt;
            color: #c0392b;
            margin-bottom: 5px;
          }
          .header-info {
            font-size: 10pt;
            color: #555;
            margin: 5px 0;
          }
          table {
            width: 100%;
            border-collapse: collapse;
            margin: 15px 0;
          }
          table.orders-table {
            border: 1px solid #e5e7eb;
          }
          table.orders-table thead {
            background: #c0392b;
            color: white;
          }
          table.orders-table th,
          table.orders-table td {
            padding: 10px;
            text-align: left;
            border: 1px solid #e5e7eb;
          }
          table.summary-table td {
            padding: 8px 0;
            border-bottom: 1px solid #e5e7eb;
          }
          table.summary-table .label {
            font-weight: 600;
            width: 200px;
          }
          table.summary-table .value {
            text-align: right;
            width: 150px;
          }
          .summary-section {
            margin-top: 30px;
            page-break-inside: avoid;
          }
          .summary-section h3 {
            font-size: 12pt;
            color: #c0392b;
            margin-bottom: 10px;
            border-bottom: 2px solid #c0392b;
            padding-bottom: 5px;
          }
        </style>
      </head>
      <body>
        <div class="header">
          <h1>Rumi Restaurant</h1>
          <div class="header-info">${translate('orders_export', 'Orders Export')}</div>
          <div class="header-info">${translate('total_orders', 'Total Orders')}: ${orders.length}</div>
          <div class="header-info">${translate('export_date', 'Export Date')}: ${formatDate(new Date().toISOString())}</div>
        </div>

        <table class="orders-table">
          <thead>
            <tr>
              <th>${translate('order_number_short', 'Order #')}</th>
              <th>${translate('date', 'Date')}</th>
              <th>${translate('type', 'Type')}</th>
              <th>${translate('status', 'Status')}</th>
              <th>${translate('customer', 'Customer')}</th>
              <th>${translate('amount', 'Amount')}</th>
              <th>${translate('payment', 'Payment')}</th>
            </tr>
          </thead>
          <tbody>
            ${ordersRows}
          </tbody>
        </table>

        <div class="summary-section">
          <h3>${translate('summary', 'Summary')}</h3>
          <table class="summary-table">
            <tr>
              <td class="label">${translate('total_revenue', 'Total Revenue')}:</td>
              <td class="value">${formatCurrency(totalRevenue)}</td>
            </tr>
            <tr>
              <td class="label">${translate('paid_orders', 'Paid Orders')}:</td>
              <td class="value">${paidOrders} ${translate('of', 'of')} ${orders.length}</td>
            </tr>
          </table>
        </div>
      </body>
    </html>
  `;
  
  printHtmlContent(html);
};

