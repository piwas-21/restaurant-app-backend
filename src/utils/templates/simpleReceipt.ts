/**
 * Simple thermal receipt template (80mm width)
 * Used for cashier receipts - simplified layout without tables
 */
import { OrderDto, OrderItemDto } from '@/types/order';
import { THERMAL_BASE_STYLES } from './baseStyles';
import { formatCurrency } from '../currency';

type TranslationFunction = (key: string, fallback: string) => string;

// Escape HTML
const escapeHtml = (text: string): string => {
  const map: Record<string, string> = {
    '&': '&amp;',
    '<': '&lt;',
    '>': '&gt;',
    '"': '&quot;',
    "'": '&#039;',
  };
  return text.replace(/[&<>"']/g, (char) => map[char]);
};

// Get order type label
const getOrderTypeLabel = (type: string | undefined, t?: TranslationFunction): string => {
  const translate = t || ((key: string, fallback: string) => fallback);
  switch (type) {
    case 'DineIn': return translate('order_type.dinein', 'Dine In');
    case 'Takeaway': return translate('order_type.takeaway', 'Takeaway');
    case 'Delivery': return translate('order_type.delivery', 'Delivery');
    default: return type || 'Unknown';
  }
};

// Get payment method label
const getPaymentMethodLabel = (method: string): string => {
  const labels: Record<string, string> = {
    cash: 'Cash',
    card: 'Card',
    twint: 'TWINT',
  };
  return labels[method?.toLowerCase()] || method || 'N/A';
};

// Build item HTML - simple format (name, qty, total only - no unit price breakdown)
const buildItemHtml = (item: OrderItemDto): string => {
  const itemName = item.productName || item.menuName || 'Item';
  const variation = item.variationName ? ` (${item.variationName})` : '';
  const totalPrice = formatCurrency(item.itemTotal);
  
  return `
    <div style="margin-bottom: 8px; padding-bottom: 6px; border-bottom: 1px dashed #aaa;">
      <div style="display: flex; justify-content: space-between;">
        <span><strong>${item.quantity}x</strong> ${escapeHtml(itemName)}${escapeHtml(variation)}</span>
        <span><strong>${totalPrice}</strong></span>
      </div>
    </div>`;
};

/**
 * Generate HTML for simple thermal receipt
 */
export const generateSimpleReceiptHtml = (order: OrderDto, t?: TranslationFunction): string => {
  const translate = t || ((key: string, fallback: string) => fallback);

  // Build items HTML
  const itemsHtml = order.items.map(buildItemHtml).join('');

  // Format date
  const orderDate = new Date(order.orderDate);
  const dateStr = orderDate.toLocaleDateString();
  const timeStr = orderDate.toLocaleTimeString([], { hour: '2-digit', minute: '2-digit' });

  // Delivery address
  const deliveryAddress = order.type === 'Delivery' && order.deliveryAddress 
    ? `
      <div style="margin: 10px 0; padding: 8px; border: 1px dashed #000;">
        <strong>${translate('delivery_to', 'DELIVERY TO')}:</strong><br/>
        ${escapeHtml(order.deliveryAddress.addressLine1 || '')}
        ${order.deliveryAddress.addressLine2 ? '<br/>' + escapeHtml(order.deliveryAddress.addressLine2) : ''}
        <br/>${escapeHtml(order.deliveryAddress.postalCode || '')} ${escapeHtml(order.deliveryAddress.city || '')}
        ${order.deliveryAddress.phone ? '<br/>Tel: ' + escapeHtml(order.deliveryAddress.phone) : ''}
      </div>
    ` : '';

  // Payments
  const paymentsHtml = order.payments && order.payments.length > 0 
    ? `
      <div style="margin-top: 8px;">
        <strong>${translate('payment', 'PAYMENT')}:</strong>
        ${order.payments.map(p => `<div>${getPaymentMethodLabel(p.paymentMethod)}: ${formatCurrency(p.amount)}</div>`).join('')}
      </div>
    ` : '';

  return `
    <!DOCTYPE html>
    <html>
      <head>
        <meta charset="UTF-8">
        <title>Receipt - ${escapeHtml(order.orderNumber)}</title>
        <style>${THERMAL_BASE_STYLES}</style>
      </head>
      <body>
        <div class="header">
          <h1>Rumi Restaurant</h1>
          <div>${translate('online_order', 'ONLINE ORDER')}</div>
        </div>

        <div class="separator"></div>

        <div style="margin: 8px 0;">
          <div style="font-size: 11pt; font-weight: bold; margin-bottom: 4px; white-space: nowrap;">
            ${escapeHtml(order.orderNumber)} - ${dateStr} ${timeStr}
          </div>
          <div>
            <strong>${translate('type', 'Type')}:</strong> ${escapeHtml(getOrderTypeLabel(order.type, t))}${order.type === 'DineIn' && order.tableNumber ? ` - Table ${order.tableNumber}` : ''}
          </div>
        </div>

        ${order.customerName ? `
          <div style="margin: 8px 0;">
            <div><strong>${translate('customer', 'Customer')}:</strong> ${escapeHtml(order.customerName)}</div>
          </div>
        ` : ''}

        ${deliveryAddress}

        <div class="separator"></div>

        <div style="margin: 8px 0;">
          ${itemsHtml}
        </div>

        <div class="double-separator"></div>

        <div style="margin: 8px 0;">
          <div class="flex-row">
            <span>${translate('subtotal', 'Subtotal')}:</span>
            <span>${formatCurrency(order.subTotal)}</span>
          </div>
          ${order.tax > 0 ? `
            <div class="flex-row">
              <span>${translate('tax', 'Tax')}:</span>
              <span>${formatCurrency(order.tax)}</span>
            </div>
          ` : ''}
          ${order.deliveryFee && order.deliveryFee > 0 ? `
            <div class="flex-row">
              <span>${translate('delivery_fee', 'Delivery')}:</span>
              <span>${formatCurrency(order.deliveryFee)}</span>
            </div>
          ` : ''}
          ${order.discount && order.discount > 0 ? `
            <div class="flex-row">
              <span>${translate('discount', 'Discount')}:</span>
              <span>-${formatCurrency(order.discount)}</span>
            </div>
          ` : ''}
          ${order.tip && order.tip > 0 ? `
            <div class="flex-row">
              <span>${translate('tip', 'Tip')}:</span>
              <span>${formatCurrency(order.tip)}</span>
            </div>
          ` : ''}
        </div>

        <div class="separator"></div>

        <div class="total-line flex-row" style="margin: 8px 0;">
          <span>${translate('total', 'TOTAL')}:</span>
          <span>${formatCurrency(order.total)}</span>
        </div>

        ${paymentsHtml}

        ${order.notes ? `
          <div class="separator"></div>
          <div style="margin: 8px 0;">
            <strong>${translate('notes', 'Notes')}:</strong> ${escapeHtml(order.notes)}
          </div>
        ` : ''}

        <div class="double-separator"></div>

        <div style="text-align: center; margin-top: 10px; font-size: 10pt;">
          ${translate('thank_you', 'Thank you for your visit!')}
        </div>
      </body>
    </html>
  `;
};
