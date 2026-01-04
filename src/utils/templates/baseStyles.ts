/**
 * Base styles for PDF/print templates
 */

// Common CSS for thermal printer style (80mm width)
export const THERMAL_BASE_STYLES = `
  @page { size: 80mm auto; margin: 5mm; }
  @media print { body { margin: 0; padding: 0; } }
  * { margin: 0; padding: 0; box-sizing: border-box; }
  body {
    font-family: 'Courier New', monospace;
    font-size: 11pt;
    line-height: 1.4;
    color: #000;
    background: white;
    padding: 10px;
    max-width: 300px;
  }
  .header { text-align: center; margin-bottom: 10px; }
  .header h1 { font-size: 16pt; margin-bottom: 2px; }
  .separator { border-top: 1px dashed #000; margin: 8px 0; }
  .double-separator { border-top: 2px solid #000; margin: 8px 0; }
  .total-line { font-size: 14pt; font-weight: bold; }
  .flex-row { display: flex; justify-content: space-between; margin: 4px 0; }
  .indent { margin-left: 20px; font-size: 10pt; }
  .strikethrough { text-decoration: line-through; }
  .italic { font-style: italic; }
`;

// Common CSS for A4 page style
export const A4_BASE_STYLES = `
  @page { size: A4 portrait; margin: 20mm; }
  @media print { body { margin: 0; padding: 0; } }
  * { margin: 0; padding: 0; box-sizing: border-box; }
  body {
    font-family: -apple-system, BlinkMacSystemFont, 'Segoe UI', Roboto, 'Helvetica Neue', Arial, sans-serif;
    font-size: 11pt;
    line-height: 1.5;
    color: #1a1a1a;
    background: white;
    padding: 20px;
  }
  .header { text-align: center; margin-bottom: 20px; border-bottom: 2px solid #c0392b; padding-bottom: 10px; }
  .header h1 { font-size: 24pt; color: #c0392b; margin-bottom: 5px; }
  .header h2 { font-size: 16pt; color: #555; }
  .section { margin: 20px 0; page-break-inside: avoid; }
  .section h3 { font-size: 13pt; color: #c0392b; margin-bottom: 10px; border-bottom: 1px solid #e5e7eb; padding-bottom: 5px; }
  table { width: 100%; border-collapse: collapse; margin: 10px 0; }
  table.items-table { border: 1px solid #e5e7eb; }
  table.items-table thead { background: #c0392b; color: white; }
  table.items-table th, table.items-table td { padding: 10px; text-align: left; border: 1px solid #e5e7eb; }
`;

// Kitchen receipt specific styles
export const KITCHEN_STYLES = `
  ${THERMAL_BASE_STYLES}
  .order-info { margin: 12px 0; padding: 8px; border: 1px solid #000; }
  .order-info .order-number { font-size: 16pt; font-weight: bold; margin-bottom: 4px; }
  .order-info p { margin: 2px 0; font-size: 11pt; }
  .customer-info { margin-top: 12px; padding: 8px; border: 1px solid #000; font-size: 11pt; }
  .timestamp { text-align: center; font-size: 9pt; margin-top: 15px; border-top: 1px solid #000; padding-top: 8px; }
`;
