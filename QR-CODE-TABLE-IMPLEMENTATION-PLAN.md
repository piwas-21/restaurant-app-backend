# QR Code Table Implementation Plan

## Overview
Implement QR code functionality for restaurant tables to allow customers to scan and order directly from their table, with admin features to generate and manage table QR codes.

## Goals
1. Customer scans QR code → redirected to menu with table pre-selected
2. Order automatically set to "Dine-in" with specific table number
3. Admin can view/generate/print QR codes for each table
4. Seamless integration with existing order flow

---

## Phase 1: Backend - QR Code Support for Tables

### Task 1.1: Add QR Code Field to Table Entity
- [ ] Add `QRCodeData` string field to RestaurantTable entity (store QR code identifier)
- [ ] Add `QRCodeGeneratedAt` DateTime field to track generation
- [ ] Create database migration

### Task 1.2: Create QR Code Generation Endpoint
- [ ] Create `GenerateTableQRCodeCommand` with tableId parameter
- [ ] Create handler that generates unique QR code identifier (e.g., `table_{tableId}_{uuid}`)
- [ ] Update table entity with QR code data
- [ ] Return QR code data URL for frontend to generate actual QR image
- [ ] Endpoint: `POST /api/Table/generate-qr/{tableId}`

### Task 1.3: Create QR Code Validation Endpoint
- [ ] Create `ValidateTableQRCodeQuery` with qrCodeData parameter
- [ ] Return table information if QR code is valid
- [ ] Endpoint: `GET /api/Table/validate-qr/{qrCodeData}`

---

## Phase 2: Frontend - QR Code Scanning & Redirection

### Task 2.1: Create QR Code Scanner Page
- [ ] Create `/scan` route that accepts `?qr={qrCodeData}` parameter
- [ ] Validate QR code with backend API
- [ ] Extract table information
- [ ] Store table info in session/localStorage
- [ ] Redirect to `/menu` with table context

### Task 2.2: Update Menu Page for QR Flow
- [ ] Check for table context on menu page load
- [ ] Display "Ordering for Table X" banner/indicator
- [ ] Auto-select "Dine-in" order type
- [ ] Auto-populate table number in checkout

### Task 2.3: Update Checkout Flow
- [ ] Pre-fill order type as "Dine-in" when coming from QR scan
- [ ] Pre-fill table number (read-only field)
- [ ] Add visual indicator showing table-based order
- [ ] Ensure table context persists through checkout

---

## Phase 3: Admin - QR Code Management UI

### Task 3.1: Create QR Code Display Component
**File:** `src/components/admin/table-management/TableQRCodeDisplay.tsx`
- [ ] Create modal component to display QR code
- [ ] Use `qrcode.react` library to generate QR code image
- [ ] Show table information (number, location, capacity)
- [ ] Add "Download QR Code" button (PNG format)
- [ ] Add "Print QR Code" button
- [ ] Style for both light/dark themes

### Task 3.2: Create QR Code Generator Component
**File:** `src/components/admin/table-management/QRCodeGenerator.tsx`
- [ ] Component to trigger QR generation for tables without QR codes
- [ ] Show loading state during generation
- [ ] Display success/error messages
- [ ] Automatically show generated QR code after creation

### Task 3.3: Update Admin Dashboard Tables Section
- [ ] Add "QR Code" column/action to tables list
- [ ] Add "View QR" button for tables with existing QR codes
- [ ] Add "Generate QR" button for tables without QR codes
- [ ] Show QR code generation date/time
- [ ] Add bulk "Generate All QR Codes" button

### Task 3.4: Create Table QR Management Page (Optional)
**File:** `src/app/admin/table-qr-codes/page.tsx`
- [ ] Dedicated page showing all tables with QR code status
- [ ] Grid/card view of all table QR codes
- [ ] Bulk download option (PDF with all QR codes)
- [ ] Bulk print option
- [ ] Filter by location (indoor/outdoor)

---

## Phase 4: Utilities & Helpers

### Task 4.1: Create QR Code Utility Functions
**File:** `src/utils/qrCode.ts`
- [ ] `generateQRCodeURL(tableId: string): string` - Generate full URL with QR data
- [ ] `extractTableFromQR(qrData: string): {tableId: string, tableNumber: string}`
- [ ] `downloadQRCode(canvas: HTMLCanvasElement, fileName: string): void`
- [ ] `printQRCode(canvas: HTMLCanvasElement): void`

### Task 4.2: Create Table Context Provider
**File:** `src/contexts/TableContext.tsx`
- [ ] Create context to store current table information
- [ ] Provide table data across app (tableId, tableNumber, qrScanned)
- [ ] Methods to set/clear table context
- [ ] Persist to session storage

### Task 4.3: Create Table Banner Component
**File:** `src/components/TableBanner.tsx`
- [ ] Small banner showing "Ordering for Table X"
- [ ] Display on menu and cart pages
- [ ] Clear button to remove table context
- [ ] Responsive design for mobile/desktop
- [ ] Theme-aware styling

---

## Phase 5: Styling

### Task 5.1: Create QR Code Display Styles
**File:** `src/components/admin/table-management/TableQRCodeDisplay.module.css`
- [ ] Modal overlay and content
- [ ] QR code container with padding
- [ ] Table information card
- [ ] Action buttons (download, print, close)
- [ ] Dark/light theme variables

### Task 5.2: Create Table Banner Styles
**File:** `src/components/TableBanner.module.css`
- [ ] Banner container (top of page or floating)
- [ ] Table info text styling
- [ ] Close/clear button
- [ ] Responsive breakpoints
- [ ] Theme transitions

### Task 5.3: Update Admin Table Management Styles
- [ ] Add QR code icon/button styles
- [ ] QR code status indicators (generated/not generated)
- [ ] Hover states for QR actions
- [ ] Grid view styles for QR code page

---

## Phase 6: Localization

### Task 6.1: Add QR Code Translations to All Locale Files
Add to: `en.json`, `tr.json`, `de.json`, `es.json`, `fr.json`, `it.json`, `ar.json`

```json
{
  "qr_code": "QR Code",
  "generate_qr_code": "Generate QR Code",
  "view_qr_code": "View QR Code",
  "download_qr_code": "Download QR Code",
  "print_qr_code": "Print QR Code",
  "qr_code_generated": "QR Code generated successfully",
  "qr_code_generation_failed": "Failed to generate QR Code",
  "scan_qr_to_order": "Scan QR code to order from this table",
  "ordering_for_table": "Ordering for Table {{tableNumber}}",
  "clear_table_selection": "Clear Table Selection",
  "qr_not_generated": "QR Code Not Generated",
  "qr_generated_on": "QR Code generated on {{date}}",
  "invalid_qr_code": "Invalid or expired QR code",
  "table_not_found": "Table not found",
  "scanning_qr_code": "Scanning QR code...",
  "redirecting_to_menu": "Redirecting to menu...",
  "generate_all_qr_codes": "Generate All QR Codes",
  "bulk_qr_generation": "Bulk QR Code Generation",
  "download_all_qr_codes": "Download All QR Codes (PDF)",
  "print_all_qr_codes": "Print All QR Codes"
}
```

---

## Phase 7: Testing & Refinement

### Task 7.1: Test QR Code Flow
- [ ] Test QR scanning with real device
- [ ] Test table context persistence
- [ ] Test checkout with pre-filled table
- [ ] Test clearing table context

### Task 7.2: Test Admin QR Management
- [ ] Test QR code generation for single table
- [ ] Test QR code viewing/display
- [ ] Test download functionality
- [ ] Test print functionality
- [ ] Test bulk operations

### Task 7.3: Cross-browser Testing
- [ ] Test QR scanning on iOS Safari
- [ ] Test QR scanning on Android Chrome
- [ ] Test admin features on desktop browsers
- [ ] Test responsive layouts

---

## Technical Dependencies

### NPM Packages to Install
```bash
npm install qrcode.react
npm install @types/qrcode.react --save-dev
```

### Backend Dependencies (.NET)
- No additional packages needed (use built-in Guid generation)

---

## Implementation Order

1. **Start with Backend** (Phase 1) - Ensure API is ready
2. **Core Frontend Flow** (Phase 2) - Get basic QR → Menu flow working
3. **Admin UI** (Phase 3) - Build admin management features
4. **Polish** (Phases 4-6) - Add utilities, styling, translations
5. **Test** (Phase 7) - Comprehensive testing

---

## Notes

- QR code data format: `https://yourdomain.com/scan?qr={qrCodeIdentifier}`
- QR code identifier format: `table_{tableId}_{uuid}`
- Use session storage for table context (clears on browser close)
- Ensure QR codes are large enough to scan from printed materials
- Consider adding QR code regeneration feature for security
- Add expiry date for QR codes if needed for security

---

## File Structure Preview

```
backend/
  RestaurantSystem.Api/Features/Table/
    Commands/
      GenerateTableQRCodeCommand/
        GenerateTableQRCodeCommand.cs
    Queries/
      ValidateTableQRCodeQuery/
        ValidateTableQRCodeQuery.cs

frontend/
  src/
    app/
      scan/
        page.tsx (QR scanner/validator page)
      admin/
        table-qr-codes/
          page.tsx (QR management page)
    components/
      TableBanner.tsx
      TableBanner.module.css
      admin/
        table-management/
          TableQRCodeDisplay.tsx
          TableQRCodeDisplay.module.css
          QRCodeGenerator.tsx
    contexts/
      TableContext.tsx
    utils/
      qrCode.ts
```

---

## Estimated Timeline

- Phase 1 (Backend): 2-3 hours
- Phase 2 (Frontend Flow): 3-4 hours
- Phase 3 (Admin UI): 4-5 hours
- Phase 4 (Utilities): 2 hours
- Phase 5 (Styling): 2-3 hours
- Phase 6 (Localization): 1 hour
- Phase 7 (Testing): 2-3 hours

**Total: ~16-23 hours**

---

## Success Criteria

✅ Customer scans QR code and is redirected to menu
✅ Table number is automatically pre-filled in checkout
✅ Order is automatically set to "Dine-in"
✅ Admin can generate QR codes for tables
✅ Admin can view, download, and print QR codes
✅ QR codes are styled appropriately for printing
✅ All features work in dark/light theme
✅ All text is translated in 7 languages
✅ Mobile-friendly QR scanning experience
