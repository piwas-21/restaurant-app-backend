/**
 * Kitchen receipt template
 * Used for printing order items to kitchen printers
 * Simple list layout - no tables
 * Includes pricing for customer-facing 'All' prints
 */
import { OrderDto, OrderItemDto } from '@/types/order';
import { THERMAL_BASE_STYLES } from './baseStyles';

type TranslationFunction = (key: string, fallback: string) => string;

// Currency formatter
const formatCurrency = (amount: number): string => {
  return new Intl.NumberFormat('de-CH', {
    style: 'currency',
    currency: 'CHF',
  }).format(amount);
};

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

// Build kitchen item HTML - with optional pricing
const buildKitchenItemHtml = (item: OrderItemDto, translate: TranslationFunction, showPrices: boolean): string => {
  const itemName = item.productName || item.menuName || translate('item', 'Item');

  // Calculate unit price with fallback
  const unitPriceValue = item.unitPrice || (item.quantity > 0 ? item.itemTotal / item.quantity : 0);
  const totalPrice = formatCurrency(item.itemTotal);
  const unitPrice = formatCurrency(unitPriceValue);

  let html = `
    <div style="margin-bottom: 12px; padding-bottom: 8px; border-bottom: 1px dashed #ccc;">
      <div style="display: flex; justify-content: space-between; font-size: 13pt; font-weight: bold;">
        <span>${item.quantity}x ${escapeHtml(itemName)}</span>
        ${showPrices ? `<span>${totalPrice}</span>` : ''}
      </div>`;

  // Show unit price breakdown if prices enabled
  if (showPrices && item.quantity > 0) {
    html += `<div style="font-size: 10pt; color: #555;">${item.quantity} @ ${unitPrice}</div>`;
  }

  // Variation
  if (item.variationName) {
    html += `<div style="margin-left: 16px; font-size: 11pt;">Size: ${escapeHtml(item.variationName)}</div>`;
  }

  // Only show removed and extra ingredients
  const customizedIngredients = item.ingredientCustomizations?.filter(ing =>
    ing.isRemoved || ing.quantity > 1
  ) || [];

  if (customizedIngredients.length > 0) {
    customizedIngredients.forEach(ing => {
      if (ing.isRemoved) {
        html += `<div style="margin-left: 16px; font-size: 11pt; text-decoration: line-through;">✘ NO ${escapeHtml(ing.ingredientName)}</div>`;
      } else if (ing.quantity > 1) {
        html += `<div style="margin-left: 16px; font-size: 11pt;">+ EXTRA ${escapeHtml(ing.ingredientName)}</div>`;
      }
    });
  }

  // Side items
  if (item.sideItems && item.sideItems.length > 0) {
    html += `<div style="margin-left: 16px; font-size: 11pt; margin-top: 4px;"><strong>Additionals:</strong></div>`;
    item.sideItems.forEach(side => {
      const sidePrice = showPrices ? ` (${formatCurrency(side.itemTotal)})` : '';
      html += `<div style="margin-left: 24px; font-size: 11pt;">+ ${escapeHtml(side.productName || 'Item')}${side.quantity > 1 ? ` x${side.quantity}` : ''}${sidePrice}</div>`;
    });
  }

  // Special instructions - prominent styling
  if (item.specialInstructions) {
    html += `
      <div style="margin: 8px 0 0 16px; padding: 6px 8px; background: #f5f5f5; border-left: 4px solid #000; font-size: 11pt;">
        <strong>NOTE:</strong> ${escapeHtml(item.specialInstructions)}
      </div>`;
  }

  html += `</div>`;
  return html;
};

/**
 * Generate HTML for kitchen receipt - simple list layout
 * Shows prices for 'All' type (customer-facing), hides for specific kitchens
 */
export const generateKitchenReceiptHtml = (
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

  // Show prices only for 'All' (customer-facing receipt)
  const showPrices = kitchenType === 'All';

  // Kitchen type label
  const kitchenLabel = kitchenType === 'FrontKitchen'
    ? translate('kitchen_type_frontkitchen', 'Front Kitchen')
    : kitchenType === 'BackKitchen'
    ? translate('kitchen_type_backkitchen', 'Back Kitchen')
    : translate('order_details', 'Order Details');

  // Build items with or without prices
  const itemsHtml = filteredItems.map(item => buildKitchenItemHtml(item, translate, showPrices)).join('');

  // Totals section only for customer-facing 'All' type
  const totalsHtml = showPrices ? `
    <div class="double-separator"></div>
    <div style="margin: 8px 0;">
      <div style="display: flex; justify-content: space-between; margin: 4px 0;">
        <span>Subtotal:</span>
        <span>${formatCurrency(order.subTotal)}</span>
      </div>
      ${order.tax > 0 ? `
        <div style="display: flex; justify-content: space-between; margin: 4px 0;">
          <span>Tax:</span>
          <span>${formatCurrency(order.tax)}</span>
        </div>
      ` : ''}
      ${order.deliveryFee && order.deliveryFee > 0 ? `
        <div style="display: flex; justify-content: space-between; margin: 4px 0;">
          <span>Delivery:</span>
          <span>${formatCurrency(order.deliveryFee)}</span>
        </div>
      ` : ''}
      ${order.discount && order.discount > 0 ? `
        <div style="display: flex; justify-content: space-between; margin: 4px 0;">
          <span>Discount:</span>
          <span>-${formatCurrency(order.discount)}</span>
        </div>
      ` : ''}
    </div>
    <div class="separator"></div>
    <div style="display: flex; justify-content: space-between; margin: 8px 0; font-size: 14pt; font-weight: bold;">
      <span>TOTAL:</span>
      <span>${formatCurrency(order.total)}</span>
    </div>
    ${order.payments && order.payments.length > 0 ? `
      <div style="margin-top: 8px;">
        <strong>Payment:</strong>
        ${order.payments.map(p => `<div>${p.paymentMethod}: ${formatCurrency(p.amount)}</div>`).join('')}
      </div>
    ` : ''}
  ` : '';

  return `
    <!DOCTYPE html>
    <html>
      <head>
        <meta charset="UTF-8">
        <title>${kitchenLabel} - ${escapeHtml(order.orderNumber)}</title>
        <style>${THERMAL_BASE_STYLES}</style>
      </head>
      <body>
        <div class="header">
          <h1>${kitchenLabel}</h1>
        </div>

        <div class="separator"></div>

        <div style="margin: 8px 0;">
          <div style="font-size: 11pt; font-weight: bold; margin-bottom: 4px; white-space: nowrap;">
            ${escapeHtml(order.orderNumber)} - ${new Date(order.orderDate).toLocaleDateString()} ${new Date(order.orderDate).toLocaleTimeString([], { hour: '2-digit', minute: '2-digit' })}
          </div>
          <div>
            <strong>Type:</strong> ${escapeHtml(getOrderTypeLabel(order.type, t))}${order.type === 'DineIn' && order.tableNumber ? ` - Table ${order.tableNumber}` : ''}
          </div>
        </div>

        ${order.customerName ? `
          <div style="margin: 8px 0; padding: 6px; background: #f5f5f5;">
            <strong>Customer:</strong> ${escapeHtml(order.customerName)}
          </div>
        ` : ''}

        <div class="double-separator"></div>

        <div style="margin: 8px 0;">
          ${itemsHtml}
        </div>

        ${totalsHtml}

        <div class="separator"></div>

        <div style="text-align: center; font-size: 9pt; color: #666;">
          Printed: ${new Date().toLocaleString()}
        </div>
      </body>
    </html>
  `;
};
