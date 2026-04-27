/**
 * Export Utilities
 * Helper functions for exporting data in various formats
 */

import { OrderDto } from '@/types/order';

// Translation function type
type TranslationFunction = (key: string, fallback: string) => string;

/**
 * Convert an order to CSV row data
 */
export function orderToCSVRow(order: OrderDto, t?: TranslationFunction): string[] {
  const translate = t || ((key: string, fallback: string) => fallback);

  const getOrderTypeLabel = (type: string): string => {
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

  return [
    order.orderNumber,
    order.customerName || translate('n_a', 'N/A'),
    order.customerEmail || translate('n_a', 'N/A'),
    order.customerPhone || translate('n_a', 'N/A'),
    getOrderTypeLabel(order.type),
    order.status ? translate(`order_status_${order.status.toLowerCase()}`, order.status) : translate('n_a', 'N/A'),
    order.paymentStatus
      ? translate(`payment_status_${order.paymentStatus.toLowerCase()}`, order.paymentStatus)
      : translate('n_a', 'N/A'),
    order.subTotal.toFixed(2),
    order.tax.toFixed(2),
    order.discount.toFixed(2),
    order.deliveryFee.toFixed(2),
    order.total.toFixed(2),
    order.isFullyPaid ? translate('yes', 'Yes') : translate('no', 'No'),
    new Date(order.orderDate).toLocaleString('de-CH'),
    order.notes || '',
    order.items.length.toString(),
    order.items.map((item) => `${item.productName} (${item.quantity}x)`).join('; '),
  ];
}

/**
 * Convert orders to CSV format
 */
export function ordersToCSV(orders: OrderDto[], t?: TranslationFunction): string {
  const translate = t || ((key: string, fallback: string) => fallback);

  const headers = [
    translate('order_number', 'Order Number'),
    translate('customer_name', 'Customer Name'),
    translate('customer_email', 'Customer Email'),
    translate('customer_phone', 'Customer Phone'),
    translate('order_type', 'Order Type'),
    translate('status', 'Status'),
    translate('payment_status', 'Payment Status'),
    translate('subtotal', 'Subtotal') + ' (CHF)',
    translate('tax', 'Tax') + ' (CHF)',
    translate('discount', 'Discount') + ' (CHF)',
    translate('delivery_fee', 'Delivery Fee') + ' (CHF)',
    translate('total', 'Total') + ' (CHF)',
    translate('fully_paid', 'Fully Paid'),
    translate('order_date', 'Order Date'),
    translate('notes', 'Notes'),
    translate('item_count', 'Item Count'),
    translate('items', 'Items'),
  ];

  const rows = orders.map((order) => orderToCSVRow(order, t));

  // Escape CSV values
  const escapeCsvValue = (value: string): string => {
    if (value.includes(',') || value.includes('"') || value.includes('\n')) {
      return `"${value.replace(/"/g, '""')}"`;
    }
    return value;
  };

  const csvContent = [
    headers.map(escapeCsvValue).join(','),
    ...rows.map((row) => row.map(escapeCsvValue).join(',')),
  ].join('\n');

  return csvContent;
}

/**
 * Download CSV file
 */
export function downloadCSV(csvContent: string, filename: string): void {
  const blob = new Blob([csvContent], { type: 'text/csv;charset=utf-8;' });
  const link = document.createElement('a');
  const url = URL.createObjectURL(blob);

  link.setAttribute('href', url);
  link.setAttribute('download', filename);
  link.style.visibility = 'hidden';

  document.body.appendChild(link);
  link.click();
  document.body.removeChild(link);

  URL.revokeObjectURL(url);
}

/**
 * Export single order to CSV
 */
export function exportOrderToCSV(order: OrderDto, t?: TranslationFunction): void {
  const csv = ordersToCSV([order], t);
  const filename = `order-${order.orderNumber}-${new Date().toISOString().split('T')[0]}.csv`;
  downloadCSV(csv, filename);
}

/**
 * Export multiple orders to CSV
 */
export function exportOrdersToCSV(orders: OrderDto[], t?: TranslationFunction): void {
  const csv = ordersToCSV(orders, t);
  const filename = `orders-export-${new Date().toISOString().split('T')[0]}.csv`;
  downloadCSV(csv, filename);
}

/**
 * Generate order receipt text (for print or PDF)
 */
export function generateOrderReceipt(order: OrderDto, t?: TranslationFunction): string {
  const _translate = t || ((key: string, fallback: string) => fallback);
  const lines: string[] = [];

  lines.push('RUMI RESTAURANT');
  lines.push('Order Receipt');
  lines.push('='.repeat(50));
  lines.push('');
  lines.push(`Order Number: ${order.orderNumber}`);
  lines.push(`Order Date: ${new Date(order.orderDate).toLocaleString('de-CH')}`);
  lines.push(`Order Type: ${order.type}`);
  lines.push(`Status: ${order.status}`);
  lines.push('');

  if (order.customerName) {
    lines.push('CUSTOMER INFORMATION:');
    lines.push(`Name: ${order.customerName}`);
    if (order.customerEmail) lines.push(`Email: ${order.customerEmail}`);
    if (order.customerPhone) lines.push(`Phone: ${order.customerPhone}`);
    lines.push('');
  }

  if (order.deliveryAddress && order.type === 'Delivery') {
    lines.push('DELIVERY ADDRESS:');
    lines.push(order.deliveryAddress.addressLine1);
    if (order.deliveryAddress.addressLine2) lines.push(order.deliveryAddress.addressLine2);
    lines.push(`${order.deliveryAddress.postalCode} ${order.deliveryAddress.city}`);
    lines.push('');
  }

  lines.push('ORDER ITEMS:');
  lines.push('-'.repeat(50));
  order.items.forEach((item) => {
    const itemLine = `${item.quantity}x ${item.productName}`;
    const price = `CHF ${item.itemTotal.toFixed(2)}`;
    lines.push(`${itemLine.padEnd(40)} ${price.padStart(10)}`);
    if (item.variationName) {
      lines.push(`   (${item.variationName})`);
    }
    if (item.specialInstructions) {
      lines.push(`   Note: ${item.specialInstructions}`);
    }
  });
  lines.push('-'.repeat(50));
  lines.push('');

  lines.push('ORDER SUMMARY:');
  lines.push(`Subtotal: ${'CHF ' + order.subTotal.toFixed(2).padStart(10)}`);
  if (order.discount > 0) {
    lines.push(`Discount: -CHF ${order.discount.toFixed(2).padStart(10)}`);
  }
  if (order.deliveryFee > 0) {
    lines.push(`Delivery Fee: CHF ${order.deliveryFee.toFixed(2).padStart(10)}`);
  }
  lines.push(`Tax: CHF ${order.tax.toFixed(2).padStart(10)}`);
  lines.push('-'.repeat(50));
  lines.push(`TOTAL: CHF ${order.total.toFixed(2).padStart(10)}`);
  lines.push('='.repeat(50));
  lines.push('');

  lines.push(`Payment Status: ${order.isFullyPaid ? 'PAID' : 'PENDING'}`);

  if (order.notes) {
    lines.push('');
    lines.push('NOTES:');
    lines.push(order.notes);
  }

  lines.push('');
  lines.push('Thank you for your order!');
  lines.push('');

  return lines.join('\n');
}
