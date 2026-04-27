import { ReservationDto, ReservationStatus } from '@/types/reservation';
import { reservationService } from '@/services/reservationService';

/**
 * Export reservations to CSV format
 */
export function exportReservationsToCSV(reservations: ReservationDto[], filename = 'reservations'): void {
  if (reservations.length === 0) {
    alert('No reservations to export');
    return;
  }

  // Define CSV headers
  const headers = [
    'Reservation ID',
    'Customer Name',
    'Customer Email',
    'Customer Phone',
    'Table Number',
    'Reservation Date',
    'Start Time',
    'End Time',
    'Number of Guests',
    'Status',
    'Special Requests',
    'Notes',
    'Created At',
  ];

  // Convert reservations to CSV rows
  const rows = reservations.map((reservation) => [
    reservation.id,
    reservation.customerName,
    reservation.customerEmail,
    reservation.customerPhone,
    reservation.tableNumber,
    new Date(reservation.reservationDate).toLocaleDateString(),
    reservation.startTime,
    reservation.endTime,
    reservation.numberOfGuests.toString(),
    reservationService.getStatusLabel(reservation.status),
    reservation.specialRequests || '',
    reservation.notes || '',
    new Date(reservation.createdAt).toLocaleString(),
  ]);

  // Combine headers and rows
  const csvContent = [headers.join(','), ...rows.map((row) => row.map((cell) => `"${cell}"`).join(','))].join('\n');

  // Create blob and download
  const blob = new Blob([csvContent], { type: 'text/csv;charset=utf-8;' });
  const link = document.createElement('a');
  const url = URL.createObjectURL(blob);

  link.setAttribute('href', url);
  link.setAttribute('download', `${filename}_${new Date().toISOString().split('T')[0]}.csv`);
  link.style.visibility = 'hidden';

  document.body.appendChild(link);
  link.click();
  document.body.removeChild(link);
}

/**
 * Export reservations to PDF format
 */
export function exportReservationsToPDF(reservations: ReservationDto[], _filename = 'reservations'): void {
  if (reservations.length === 0) {
    alert('No reservations to export');
    return;
  }

  // Create HTML content for PDF
  const htmlContent = `
    <!DOCTYPE html>
    <html>
      <head>
        <meta charset="utf-8">
        <title>Reservations Report</title>
        <style>
          body {
            font-family: Arial, sans-serif;
            padding: 20px;
            color: #333;
          }
          h1 {
            color: #4f46e5;
            border-bottom: 3px solid #4f46e5;
            padding-bottom: 10px;
            margin-bottom: 30px;
          }
          .meta {
            margin-bottom: 30px;
            color: #666;
          }
          table {
            width: 100%;
            border-collapse: collapse;
            margin-top: 20px;
          }
          th, td {
            border: 1px solid #ddd;
            padding: 12px;
            text-align: left;
          }
          th {
            background-color: #4f46e5;
            color: white;
            font-weight: 600;
          }
          tr:nth-child(even) {
            background-color: #f9fafb;
          }
          .status {
            padding: 4px 12px;
            border-radius: 12px;
            font-size: 12px;
            font-weight: 600;
            display: inline-block;
          }
          .status-pending {
            background: #fef3c7;
            color: #92400e;
          }
          .status-confirmed {
            background: #d1fae5;
            color: #065f46;
          }
          .status-cancelled {
            background: #fee2e2;
            color: #991b1b;
          }
          .status-completed {
            background: #e5e7eb;
            color: #374151;
          }
          .status-noshow {
            background: #fecaca;
            color: #7f1d1d;
          }
          @media print {
            body {
              padding: 0;
            }
          }
        </style>
      </head>
      <body>
        <h1>Reservations Report</h1>
        <div class="meta">
          <p><strong>Generated:</strong> ${new Date().toLocaleString()}</p>
          <p><strong>Total Reservations:</strong> ${reservations.length}</p>
        </div>
        <table>
          <thead>
            <tr>
              <th>Customer</th>
              <th>Contact</th>
              <th>Table</th>
              <th>Date</th>
              <th>Time</th>
              <th>Guests</th>
              <th>Status</th>
            </tr>
          </thead>
          <tbody>
            ${reservations
              .map((reservation) => {
                const statusClass = `status-${reservationService.getStatusLabel(reservation.status).toLowerCase().replace(' ', '')}`;
                return `
                <tr>
                  <td><strong>${reservation.customerName}</strong></td>
                  <td>
                    ${reservation.customerEmail}<br/>
                    ${reservation.customerPhone}
                  </td>
                  <td>${reservation.tableNumber}</td>
                  <td>${new Date(reservation.reservationDate).toLocaleDateString()}</td>
                  <td>${reservation.startTime} - ${reservation.endTime}</td>
                  <td>${reservation.numberOfGuests}</td>
                  <td>
                    <span class="status ${statusClass}">
                      ${reservationService.getStatusLabel(reservation.status)}
                    </span>
                  </td>
                </tr>
                ${
                  reservation.specialRequests
                    ? `
                  <tr>
                    <td colspan="7" style="background: #f9fafb; font-size: 12px; color: #666;">
                      <strong>Special Requests:</strong> ${reservation.specialRequests}
                    </td>
                  </tr>
                `
                    : ''
                }
              `;
              })
              .join('')}
          </tbody>
        </table>
      </body>
    </html>
  `;

  // Create a new window and print
  const printWindow = window.open('', '_blank');
  if (printWindow) {
    printWindow.document.write(htmlContent);
    printWindow.document.close();

    // Wait for content to load then print
    printWindow.onload = () => {
      printWindow.print();
    };
  }
}

/**
 * Get statistics about reservations
 */
export function getReservationStatistics(reservations: ReservationDto[]) {
  const total = reservations.length;
  const pending = reservations.filter((r) => r.status === ReservationStatus.Pending).length;
  const confirmed = reservations.filter((r) => r.status === ReservationStatus.Confirmed).length;
  const cancelled = reservations.filter((r) => r.status === ReservationStatus.Cancelled).length;
  const completed = reservations.filter((r) => r.status === ReservationStatus.Completed).length;
  const noShow = reservations.filter((r) => r.status === ReservationStatus.NoShow).length;

  const totalGuests = reservations.reduce((sum, r) => sum + r.numberOfGuests, 0);
  const avgGuests = total > 0 ? (totalGuests / total).toFixed(1) : '0';

  return {
    total,
    pending,
    confirmed,
    cancelled,
    completed,
    noShow,
    totalGuests,
    avgGuests,
  };
}
