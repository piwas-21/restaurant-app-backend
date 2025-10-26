/**
 * Export Utilities
 * Helper functions for exporting data in various formats
 */

import { OrderDto } from '@/types/order';

/**
 * Convert an order to CSV row data
 */
export function orderToCSVRow(order: OrderDto): string[] {
  return [
    order.orderNumber,
    order.customerName || 'N/A',
    order.customerEmail || 'N/A',
    order.customerPhone || 'N/A',
    order.type,
    order.status,
    order.paymentStatus,
    order.subTotal.toFixed(2),
    order.tax.toFixed(2),
    order.discount.toFixed(2),
    order.deliveryFee.toFixed(2),
    order.total.toFixed(2),
    order.isFullyPaid ? 'Yes' : 'No',
    new Date(order.orderDate).toLocaleString('de-CH'),
    order.notes || '',
    order.items.length.toString(),
    order.items.map(item => `${item.productName} (${item.quantity}x)`).join('; '),
  ];
}

/**
 * Convert orders to CSV format
 */
export function ordersToCSV(orders: OrderDto[]): string {
  const headers = [
    'Order Number',
    'Customer Name',
    'Customer Email',
    'Customer Phone',
    'Order Type',
    'Status',
    'Payment Status',
    'Subtotal (CHF)',
    'Tax (CHF)',
    'Discount (CHF)',
    'Delivery Fee (CHF)',
    'Total (CHF)',
    'Fully Paid',
    'Order Date',
    'Notes',
    'Item Count',
    'Items',
  ];

  const rows = orders.map(order => orderToCSVRow(order));

  // Escape CSV values
  const escapeCsvValue = (value: string): string => {
    if (value.includes(',') || value.includes('"') || value.includes('\n')) {
      return `"${value.replace(/"/g, '""')}"`;
    }
    return value;
  };

  const csvContent = [
    headers.map(escapeCsvValue).join(','),
    ...rows.map(row => row.map(escapeCsvValue).join(',')),
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
export function exportOrderToCSV(order: OrderDto): void {
  const csv = ordersToCSV([order]);
  const filename = `order-${order.orderNumber}-${new Date().toISOString().split('T')[0]}.csv`;
  downloadCSV(csv, filename);
}

/**
 * Export multiple orders to CSV
 */
export function exportOrdersToCSV(orders: OrderDto[]): void {
  const csv = ordersToCSV(orders);
  const filename = `orders-export-${new Date().toISOString().split('T')[0]}.csv`;
  downloadCSV(csv, filename);
}

/**
 * Generate order receipt text (for print or PDF)
 */
export function generateOrderReceipt(order: OrderDto): string {
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
  order.items.forEach(item => {
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
