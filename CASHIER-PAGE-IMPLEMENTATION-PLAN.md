# Cashier Page Implementation Plan

## Overview
Complete cashier interface for managing orders in real-time, updating order details, processing payments, and managing order lifecycle.

## Features & Capabilities

### Core Features
1. **Real-time Order Management**
   - Display all active orders fetched from backend
   - Real-time order updates via Server-Sent Events (SSE)
   - Auto-refresh order list and details
   - Handle connection loss gracefully with fallback polling

2. **Order Viewing & Filtering**
   - View all orders with pagination
   - Filter by order status (Pending, Confirmed, Preparing, Ready, etc.)
   - Filter by payment status (Pending, Completed, PartiallyPaid, etc.)
   - Filter by order type (DineIn, Takeaway, Delivery)
   - Search by order number or customer name
   - Sort by order date, priority

3. **Order Details Panel**
   - Customer information (name, email, phone)
   - Order items with quantities and prices
   - Order type and table number (if dine-in)
   - Delivery address (if delivery)
   - Special instructions/notes
   - Order totals (subtotal, tax, delivery fee, discount, total)
   - Payment information and remaining amount

4. **Order Status Management**
   - Update order status with validation
   - Status workflow: Pending → Confirmed → Preparing → Ready → Completed
   - Alternative: Cancel order with confirmation
   - Refund capability for cancelled orders
   - Mark orders as focus/priority with custom reason

5. **Item Management** ⚠️ **LIMITATION**
   - View all items in order
   - **Note:** Backend doesn't provide item add/remove/update endpoints
   - Recommendation: Create new backend commands for item management, OR create a note system for order modifications

6. **Payment Management**
   - View all payments on order
   - Add new payment with method selection
   - View payment details (amount, method, status, date)
   - Process refunds (full or partial)
   - Update payment status
   - Calculate remaining balance

7. **Order Type Changes** ⚠️ **CRITICAL LIMITATION**
   - **CANNOT change order type after creation** (backend limitation)
   - Tax is calculated once at order creation and cannot be recalculated
   - If customer requests change: Document in notes, or suggest creating new order
   - **Decision needed:** Should we create backend endpoint for order type changes with tax recalculation?

8. **Design & UX**
   - Match app's design system (RUMI Red, CSS variables, dark/light mode)
   - Responsive layout for desktop (cashier uses desktop)
   - Multi-column layout: order list + order details
   - Action buttons with clear icons and labels
   - Status badges with appropriate colors
   - Real-time visual updates

9. **Multi-language Support**
   - All UI text translated using i18next
   - Support: English, German, Turkish, Italian, Arabic, French, Spanish
   - Dynamic language switching

10. **Additional Considerations**
    - Confirm destructive actions (cancel, refund, status changes)
    - Show success/error notifications for all actions
    - Log all cashier actions (created orders, payments, refunds, status changes)
    - Role-based access control (Cashier role only)
    - Handle concurrent updates (multiple cashiers)
    - Error handling and fallback states

## Backend Limitations & Workarounds

| Limitation | Workaround |
|-----------|-----------|
| Cannot change order type after creation | Add note about requested change, suggest new order, OR create new backend endpoint |
| Cannot add/remove/update items after creation | Create item management backend commands OR use order notes system |
| Tax is immutable after creation | Inform cashier during order placement or note system |
| No generic order field updates | Use specific endpoints (status, payments, focus) |
| SSE in-memory (lost on server restart) | Implement polling fallback every 10 seconds |
| No payment gateway integration | Manual payment status updates by cashier |

## Implementation Tasks

### Phase 1: Foundation (5 tasks)
- [ ] **1.1** Create Cashier page layout with sidebar and main area
- [ ] **1.2** Create real-time order list component with SSE integration
- [ ] **1.3** Create order details panel component
- [ ] **1.4** Implement order filtering and search
- [ ] **1.5** Set up error handling and loading states

### Phase 2: Order Management (4 tasks)
- [ ] **2.1** Implement order status update UI and API calls
- [ ] **2.2** Implement focus order toggle and priority management
- [ ] **2.3** Implement order cancellation with confirmation
- [ ] **2.4** Add notes/comments system for order modifications

### Phase 3: Payment Management (4 tasks)
- [ ] **3.1** Create payment details view component
- [ ] **3.2** Implement add payment dialog and form
- [ ] **3.3** Implement payment refund dialog (full/partial)
- [ ] **3.4** Add payment status tracking and validation

### Phase 4: Item Management (2 tasks)
- [ ] **4.1** Create items list view in order details
- [ ] **4.2** Add note-based item modification system (workaround for missing backend)

### Phase 5: UI/UX & Styling (3 tasks)
- [ ] **5.1** Apply design system colors and CSS variables
- [ ] **5.2** Implement dark/light mode support
- [ ] **5.3** Add mobile responsiveness (optional, primarily desktop)

### Phase 6: Localization & Polish (3 tasks)
- [ ] **6.1** Extract all text strings and add translations
- [ ] **6.2** Test all language switching
- [ ] **6.3** Add success/error notifications and confirmations

### Phase 7: Testing & QA (2 tasks)
- [ ] **7.1** Test real-time SSE updates and fallback polling
- [ ] **7.2** Test all order workflows (create, update, payment, cancel)

## File Structure

```
src/
├── app/
│   ├── cashier/
│   │   ├── page.tsx                    # Main cashier page
│   │   └── layout.tsx                  # Cashier layout with auth
│   └── styles/
│       └── CashierPage.module.css      # Cashier styles
├── components/
│   ├── cashier/
│   │   ├── OrderList.tsx               # Real-time order list
│   │   ├── OrderDetails.tsx            # Order detail panel
│   │   ├── PaymentPanel.tsx            # Payment management
│   │   ├── ItemsList.tsx               # Order items view
│   │   ├── StatusUpdateDialog.tsx      # Status update modal
│   │   ├── PaymentDialog.tsx           # Add payment modal
│   │   ├── RefundDialog.tsx            # Refund modal
│   │   ├── CancelOrderDialog.tsx       # Cancel confirmation modal
│   │   └── OrderNotes.tsx              # Notes/modifications
│   └── cashier/styles/
│       ├── OrderList.module.css
│       ├── OrderDetails.module.css
│       ├── PaymentPanel.module.css
│       └── [...other styles]
├── hooks/
│   ├── useCashierOrders.ts             # Order management hook with SSE
│   └── useOrderActions.ts              # Order action handlers
└── services/
    ├── cashierService.ts               # Cashier-specific API calls
    └── orderEventService.ts            # SSE connection management
```

## API Integration Points

### Real-time (SSE)
- `GET /api/events/kitchen` or `/api/events/service` - Order event stream

### Order Queries
- `GET /api/orders` - Get orders with filters
- `GET /api/orders/{id}` - Get order details
- `GET /api/orders/focus` - Get focus orders (if needed)

### Order Updates
- `PUT /api/orders/{orderId}/status` - Update status
- `PUT /api/orders/{orderId}/focus` - Toggle focus
- `POST /api/orders/{orderId}/cancel` - Cancel order
- `POST /api/orders/{orderId}/payments` - Add payment
- `POST /api/orders/{orderId}/payments/{paymentId}/refund` - Refund payment

### Tax Info
- `GET /api/taxconfiguration/by-order-type/{orderType}` - Tax rates

## UI Components Breakdown

### Main Layout (CashierPage.tsx)
- Header with title, filters, and refresh button
- Two-column layout: sidebar (order list) + main (order details)
- Real-time status indicator (connected/disconnected)

### Order List Component
- Sortable, filterable list of orders
- Status badges with color coding
- Customer name, order number, total
- Click to select and view details
- Highlight pending/focus orders

### Order Details Panel
- Customer info section
- Items list with quantities and prices
- Order summary (totals, tax, delivery fee)
- Payment information
- Order notes/history
- Action buttons (status, payment, cancel)

### Payment Panel
- Current payment status
- List of all payments on order
- Remaining balance
- Add payment button
- Refund buttons for completed payments

### Status Update Dialog
- Dropdown or button group with valid next statuses
- Confirmation before updating
- Success notification

### Payment Dialog
- Amount input
- Payment method selector (Cash, Credit Card, Debit Card, etc.)
- Optional transaction ID/reference
- Confirmation button

### Refund Dialog
- Show original payment amount
- Refund amount input
- Confirm refund action
- Update order balance

## Translation Keys Needed

```
cashier.title
cashier.orders
cashier.order_details
cashier.customer_info
cashier.items
cashier.payment
cashier.payments
cashier.status
cashier.payment_status
cashier.total
cashier.subtotal
cashier.tax
cashier.delivery_fee
cashier.discount
cashier.remaining
cashier.notes
cashier.actions
cashier.update_status
cashier.add_payment
cashier.refund
cashier.cancel_order
cashier.focus_order
cashier.search_placeholder
cashier.filter_status
cashier.filter_payment_status
cashier.filter_type
cashier.no_orders
cashier.loading
cashier.error_loading_orders
cashier.confirm_cancel
cashier.confirm_refund
cashier.confirm_status_change
cashier.status_updated
cashier.payment_added
cashier.refund_completed
cashier.order_cancelled
cashier.order_type_change_not_supported
cashier.connection_lost
cashier.reconnecting
```

## Success Criteria

- ✅ Real-time order updates via SSE (with polling fallback)
- ✅ View all active orders with filtering and search
- ✅ Update order status through UI
- ✅ Add and refund payments
- ✅ Cancel orders with confirmation
- ✅ Mark orders as focus/priority
- ✅ Full multi-language support
- ✅ Matches app design system
- ✅ Responsive design
- ✅ Error handling and notifications
- ✅ Role-based access (Cashier only)
- ✅ No console errors or warnings

## Estimated Effort
- Phase 1: 4-5 hours
- Phase 2: 3-4 hours
- Phase 3: 3-4 hours
- Phase 4: 1-2 hours
- Phase 5: 2-3 hours
- Phase 6: 2 hours
- Phase 7: 2-3 hours
- **Total: ~20-25 hours**

## Notes

1. **Order Type Changes**: Backend limitation means order type cannot be changed after creation. Consider adding this feature to backend if needed.

2. **Item Modifications**: Backend doesn't support adding/removing/updating items. Consider either:
   - Creating backend endpoints for item management
   - Using order notes system for modifications
   - Suggesting new order for changes

3. **SSE Connection**: Implement heartbeat detection and auto-reconnect on disconnect

4. **Concurrent Updates**: Multiple cashiers may modify same order - implement optimistic locking or version checks

5. **Performance**: With many orders, use virtual scrolling in order list

6. **Mobile**: Cashier primarily uses desktop, but design should be tablet-friendly as fallback
