/**
 * QR Code Utility Functions
 * Helpers for QR code generation, download, and print functionality
 */

/**
 * Generate full URL with QR data for table ordering
 */
export function generateQRCodeURL(qrData: string): string {
  const baseUrl = process.env.NEXT_PUBLIC_APP_URL || window.location.origin;
  return `${baseUrl}/scan?qr=${encodeURIComponent(qrData)}`;
}

/**
 * Extract table information from QR code data
 */
export function extractTableFromQR(qrData: string): { tableId: string; tableNumber: string } | null {
  try {
    // QR data format: "table_{tableId}_{guid}"
    const parts = qrData.split('_');
    if (parts.length >= 2 && parts[0] === 'table') {
      return {
        tableId: parts[1],
        tableNumber: '', // Will be filled by API validation
      };
    }
    return null;
  } catch {
    return null;
  }
}

/**
 * Download QR code as PNG image
 */
export function downloadQRCode(canvas: HTMLCanvasElement, fileName: string): void {
  try {
    // Convert canvas to blob
    canvas.toBlob((blob) => {
      if (!blob) {
        throw new Error('Failed to create blob from canvas');
      }

      // Create download link
      const url = URL.createObjectURL(blob);
      const link = document.createElement('a');
      link.href = url;
      link.download = `${fileName}.png`;

      // Trigger download
      document.body.appendChild(link);
      link.click();

      // Cleanup
      document.body.removeChild(link);
      URL.revokeObjectURL(url);
    }, 'image/png');
  } catch {
    throw new Error('Failed to download QR code');
  }
}

/**
 * Print QR code
 */
export function printQRCode(canvas: HTMLCanvasElement, tableNumber: string): void {
  try {
    // Convert canvas to data URL
    const dataUrl = canvas.toDataURL('image/png');

    // Create print window
    const printWindow = window.open('', '_blank');
    if (!printWindow) {
      throw new Error('Failed to open print window. Please check popup settings.');
    }

    // Generate print HTML
    printWindow.document.write(`
      <!DOCTYPE html>
      <html>
        <head>
          <title>QR Code - Table ${tableNumber}</title>
          <style>
            @media print {
              @page {
                size: A4;
                margin: 2cm;
              }
            }
            body {
              font-family: -apple-system, BlinkMacSystemFont, 'Segoe UI', Roboto, sans-serif;
              display: flex;
              flex-direction: column;
              align-items: center;
              justify-content: center;
              min-height: 100vh;
              margin: 0;
              padding: 2rem;
            }
            .container {
              text-align: center;
              max-width: 500px;
            }
            h1 {
              font-size: 2rem;
              margin-bottom: 1rem;
              color: #333;
            }
            .table-number {
              font-size: 3rem;
              font-weight: bold;
              color: #2563eb;
              margin-bottom: 2rem;
            }
            img {
              max-width: 100%;
              height: auto;
              border: 2px solid #e5e7eb;
              border-radius: 8px;
              padding: 1rem;
              background: white;
            }
            .instructions {
              margin-top: 2rem;
              font-size: 1.125rem;
              color: #666;
              line-height: 1.6;
            }
            .footer {
              margin-top: 2rem;
              font-size: 0.875rem;
              color: #999;
            }
          </style>
        </head>
        <body>
          <div class="container">
            <h1>Scan to Order</h1>
            <div class="table-number">Table ${tableNumber}</div>
            <img src="${dataUrl}" alt="QR Code for Table ${tableNumber}" />
            <div class="instructions">
              Scan this QR code with your phone camera to view our menu and place your order directly from your table.
            </div>
            <div class="footer">
              Rumi Restaurant - Digital Ordering System
            </div>
          </div>
          <script>
            // Auto-print when page loads
            window.onload = function() {
              window.print();
              // Close window after printing (with delay for user to cancel if needed)
              setTimeout(function() {
                window.close();
              }, 100);
            };
          </script>
        </body>
      </html>
    `);

    printWindow.document.close();
  } catch {
    throw new Error('Failed to print QR code');
  }
}

/**
 * Download multiple QR codes as a PDF (future enhancement)
 */
export function downloadAllQRCodesAsPDF(
  qrCodes: Array<{ canvas: HTMLCanvasElement; tableNumber: string }>
): void {
  // TODO: Implement PDF generation using library like jsPDF
  throw new Error(`Bulk PDF download not yet implemented. ${qrCodes.length} QR codes pending.`);
}

/**
 * Validate QR code format
 */
export function isValidQRCodeFormat(qrData: string): boolean {
  if (!qrData || typeof qrData !== 'string') {
    return false;
  }

  // Check format: "table_{tableId}_{guid}"
  const parts = qrData.split('_');
  return parts.length >= 3 && parts[0] === 'table';
}

/**
 * Format QR code generation date
 */
export function formatQRGeneratedDate(date: string | Date | null | undefined): string {
  if (!date) {
    return 'Not generated';
  }

  try {
    const dateObj = typeof date === 'string' ? new Date(date) : date;
    return dateObj.toLocaleDateString('en-US', {
      year: 'numeric',
      month: 'long',
      day: 'numeric',
      hour: '2-digit',
      minute: '2-digit',
    });
  } catch {
    return 'Invalid date';
  }
}
