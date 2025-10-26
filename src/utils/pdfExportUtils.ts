import jsPDF from 'jspdf';
import autoTable from 'jspdf-autotable';
import { OrderDto } from '@/types/order';

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
const getOrderTypeLabel = (type: string): string => {
  const typeMap: { [key: string]: string } = {
    DineIn: 'Dine In',
    Takeaway: 'Takeaway',
    Delivery: 'Delivery',
  };
  return typeMap[type] || type;
};

/**
 * Export a single order to PDF
 */
export const exportOrderToPDF = (order: OrderDto): void => {
  const doc = new jsPDF();

  // Add company header
  doc.setFontSize(20);
  doc.setFont('helvetica', 'bold');
  doc.text('Rumi Restaurant', 14, 20);

  // Add order details header
  doc.setFontSize(12);
  doc.setFont('helvetica', 'normal');
  doc.text('Order Details', 14, 30);

  // Order information
  doc.setFontSize(10);
  const orderInfo = [
    ['Order Number:', order.orderNumber],
    ['Order Date:', formatDate(order.orderDate)],
    ['Status:', order.status],
    ['Order Type:', getOrderTypeLabel(order.type)],
    ['Payment Status:', order.paymentStatus],
  ];

  if (order.type === 'DineIn' && order.tableNumber) {
    orderInfo.push(['Table Number:', order.tableNumber.toString()]);
  }

  let yPos = 40;
  orderInfo.forEach(([label, value]) => {
    doc.setFont('helvetica', 'bold');
    doc.text(label, 14, yPos);
    doc.setFont('helvetica', 'normal');
    doc.text(value, 60, yPos);
    yPos += 7;
  });

  // Customer information
  yPos += 5;
  doc.setFont('helvetica', 'bold');
  doc.text('Customer Information', 14, yPos);
  yPos += 7;

  const customerInfo = [
    ['Name:', order.customerName || 'N/A'],
    ['Email:', order.customerEmail || 'N/A'],
    ['Phone:', order.customerPhone || 'N/A'],
  ];

  customerInfo.forEach(([label, value]) => {
    doc.setFont('helvetica', 'bold');
    doc.text(label, 14, yPos);
    doc.setFont('helvetica', 'normal');
    doc.text(value, 60, yPos);
    yPos += 7;
  });

  // Delivery address (if applicable)
  if (order.type === 'Delivery' && order.deliveryAddress) {
    yPos += 5;
    doc.setFont('helvetica', 'bold');
    doc.text('Delivery Address', 14, yPos);
    yPos += 7;
    doc.setFont('helvetica', 'normal');
    doc.text(order.deliveryAddress.addressLine1 || '', 14, yPos);
    yPos += 7;
    if (order.deliveryAddress.addressLine2) {
      doc.text(order.deliveryAddress.addressLine2, 14, yPos);
      yPos += 7;
    }
    doc.text(
      `${order.deliveryAddress.postalCode || ''} ${order.deliveryAddress.city || ''}`,
      14,
      yPos
    );
    yPos += 7;
  }

  // Order items table
  yPos += 5;
  const itemsData = order.items.map(item => [
    item.productName || item.menuName || 'Item',
    item.quantity.toString(),
    formatCurrency(item.unitPrice),
    formatCurrency(item.itemTotal),
  ]);

  autoTable(doc, {
    startY: yPos,
    head: [['Item', 'Qty', 'Price', 'Total']],
    body: itemsData,
    theme: 'grid',
    headStyles: { fillColor: [192, 0, 0], textColor: 255 },
    margin: { left: 14, right: 14 },
  });

  // Order summary
  const finalY = (doc as any).lastAutoTable.finalY || yPos + 10;
  yPos = finalY + 10;

  doc.setFont('helvetica', 'bold');
  doc.text('Order Summary', 14, yPos);
  yPos += 7;

  const summaryData: [string, string][] = [
    ['Subtotal:', formatCurrency(order.subTotal)],
  ];

  if (order.tax > 0) {
    summaryData.push(['Tax:', formatCurrency(order.tax)]);
  }

  if (order.deliveryFee && order.deliveryFee > 0) {
    summaryData.push(['Delivery Fee:', formatCurrency(order.deliveryFee)]);
  }

  if (order.discount && order.discount > 0) {
    summaryData.push(['Discount:', `-${formatCurrency(order.discount)}`]);
  }

  if (order.tip && order.tip > 0) {
    summaryData.push(['Tip:', formatCurrency(order.tip)]);
  }

  summaryData.push(['Total:', formatCurrency(order.total)]);

  summaryData.forEach(([label, value]) => {
    doc.setFont('helvetica', 'normal');
    doc.text(label, 120, yPos);
    doc.setFont('helvetica', 'bold');
    doc.text(value, 170, yPos, { align: 'right' });
    yPos += 7;
  });

  // Payment details
  if (order.payments && order.payments.length > 0) {
    yPos += 5;
    doc.setFont('helvetica', 'bold');
    doc.text('Payment Details', 14, yPos);
    yPos += 7;

    order.payments.forEach(payment => {
      doc.setFont('helvetica', 'normal');
      doc.text(
        `${payment.paymentMethod}: ${formatCurrency(payment.amount)} (${payment.status})`,
        14,
        yPos
      );
      yPos += 7;
    });
  }

  // Notes
  if (order.notes) {
    yPos += 5;
    doc.setFont('helvetica', 'bold');
    doc.text('Notes', 14, yPos);
    yPos += 7;
    doc.setFont('helvetica', 'normal');
    const splitText = doc.splitTextToSize(order.notes, 180);
    doc.text(splitText, 14, yPos);
  }

  // Save the PDF
  doc.save(`order-${order.orderNumber}.pdf`);
};

/**
 * Export multiple orders to PDF (summary table)
 */
export const exportOrdersToPDF = (orders: OrderDto[]): void => {
  const doc = new jsPDF();

  // Add header
  doc.setFontSize(20);
  doc.setFont('helvetica', 'bold');
  doc.text('Rumi Restaurant', 14, 20);

  doc.setFontSize(12);
  doc.setFont('helvetica', 'normal');
  doc.text('Orders Export', 14, 30);

  doc.setFontSize(10);
  doc.text(`Total Orders: ${orders.length}`, 14, 38);
  doc.text(`Export Date: ${formatDate(new Date().toISOString())}`, 14, 45);

  // Prepare table data
  const tableData = orders.map(order => [
    order.orderNumber,
    formatDate(order.orderDate),
    getOrderTypeLabel(order.type),
    order.status,
    order.customerName || 'N/A',
    formatCurrency(order.total),
    order.paymentStatus,
  ]);

  // Create table
  autoTable(doc, {
    startY: 52,
    head: [['Order #', 'Date', 'Type', 'Status', 'Customer', 'Amount', 'Payment']],
    body: tableData,
    theme: 'grid',
    headStyles: { fillColor: [192, 0, 0], textColor: 255 },
    styles: { fontSize: 8 },
    margin: { left: 14, right: 14 },
  });

  // Add summary
  const finalY = (doc as any).lastAutoTable.finalY || 52;
  const totalRevenue = orders.reduce((sum, order) => sum + order.total, 0);
  const paidOrders = orders.filter(o => o.paymentStatus === 'Paid').length;

  doc.setFont('helvetica', 'bold');
  doc.text('Summary', 14, finalY + 10);
  doc.setFont('helvetica', 'normal');
  doc.text(`Total Revenue: ${formatCurrency(totalRevenue)}`, 14, finalY + 17);
  doc.text(`Paid Orders: ${paidOrders} of ${orders.length}`, 14, finalY + 24);

  // Save the PDF
  const timestamp = new Date().toISOString().split('T')[0];
  doc.save(`orders-export-${timestamp}.pdf`);
};
