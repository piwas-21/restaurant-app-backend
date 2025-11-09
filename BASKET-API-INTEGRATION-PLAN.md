# Basket & Orders API Integration Plan

## Overview
This document outlines the comprehensive plan to integrate the backend Basket and Orders APIs into the frontend application, enabling a full e-commerce checkout flow.

**Backend API Base URL:** `http://localhost:5221/`  
**Frontend Base URL:** `http://localhost:3000/`  
**Initial Payment Method:** Pay on Cashier (Cash)  
**Status:** Ready for Implementation

---

## Available Backend APIs

### Basket Endpoints
| Method | Endpoint | Purpose | Auth Required |
|--------|----------|---------|---------------|
| GET | `/api/Basket` | Get user's basket | Session ID or Auth |
| DELETE | `/api/Basket` | Clear entire basket | Session ID or Auth |
| GET | `/api/Basket/summary` | Get basket item count & total | Session ID or Auth |
| POST | `/api/Basket/items` | Add item to basket | Session ID or Auth |
| PUT | `/api/Basket/items/{basketItemId}` | Update item quantity/instructions | Session ID or Auth |
| DELETE | `/api/Basket/items/{basketItemId}` | Remove item from basket | Session ID or Auth |
| POST | `/api/Basket/promo-code` | Apply promo code | Session ID or Auth |
| DELETE | `/api/Basket/promo-code` | Remove promo code | Session ID or Auth |

**Authentication:** All basket endpoints accept either:
- Authenticated user (Bearer token)
- Anonymous user with `X-Session-Id` header (UUID)

### Order Endpoints
| Method | Endpoint | Purpose | Auth Required |
|--------|----------|---------|---------------|
| POST | `/api/Orders` | Create new order | Optional |
| GET | `/api/Orders` | Get orders list (with filters) | Auth (Admin/Staff) |
| GET | `/api/Orders/{id}` | Get order details | Auth (Owner/Admin) |
| POST | `/api/Orders/{orderId}/payments` | Add payment to order | Auth |
| PUT | `/api/Orders/{orderId}/status` | Update order status | Auth (Admin/Staff) |
| POST | `/api/Orders/{orderId}/cancel` | Cancel order | Auth (Owner/Admin) |
| PUT | `/api/Orders/{orderId}/focus` | Toggle focus order | Auth (Admin/Staff) |
| GET | `/api/Orders/focus` | Get focus orders | Auth (Admin/Staff) |

### Key Data Models

#### BasketDto
```typescript
interface BasketDto {
  id: string;
  userId?: string;
  sessionId?: string;
  subTotal: number;
  tax: number;
  deliveryFee: number;
  discount: number;
  total: number;
  promoCode?: string;
  totalItems: number;
  expiresAt?: string;
  notes?: string;
  items: BasketItemDto[];
}
```

#### BasketItemDto
```typescript
interface BasketItemDto {
  productId?: string;
  productName?: string;
  productDescription?: string;
  productImageUrl?: string;
  productVariationId?: string;
  variationName?: string;
  menuId?: string;
  menuName?: string;
  menuDate?: string;
  menuItems?: MenuItemSummaryDto[];
  quantity: number;
  unitPrice: number;
  itemTotal: number;
  specialInstructions?: string;
}
```

#### AddToBasketDto (Request)
```typescript
interface AddToBasketDto {
  productId: string;
  productVariationId?: string;
  menuId?: string;
  quantity: number;
  specialInstructions?: string;
}
```

#### CreateOrderCommand (Request)
```typescript
interface CreateOrderCommand {
  userId?: string;
  customerName?: string;
  customerEmail?: string;
  customerPhone?: string;
  type: 'DineIn' | 'Takeaway' | 'Delivery';
  tableNumber?: number; // Required for DineIn
  promoCode?: string;
  hasUserLimitDiscount: boolean;
  userLimitAmount: number;
  isFocusOrder: boolean;
  priority?: number;
  focusReason?: string;
  notes?: string;
  deliveryAddress?: CreateOrderDeliveryAddressDto; // Required for Delivery
  items: CreateOrderItemDto[];
  payments?: CreateOrderPaymentDto[];
}
```

#### OrderDto (Response)
```typescript
interface OrderDto {
  id: string;
  orderNumber: string;
  userId?: string;
  customerName?: string;
  customerEmail?: string;
  customerPhone?: string;
  type: string; // 'DineIn' | 'Takeaway' | 'Delivery'
  tableNumber?: number;
  subTotal: number;
  tax: number;
  deliveryFee: number;
  discount: number;
  discountPercentage: number;
  tip: number;
  total: number;
  totalPaid: number;
  remainingAmount: number;
  isFullyPaid: boolean;
  status: string; // Order status
  paymentStatus: string;
  isFocusOrder: boolean;
  priority?: number;
  focusReason?: string;
  orderDate: string;
  estimatedDeliveryTime?: string;
  actualDeliveryTime?: string;
  notes?: string;
  deliveryAddress?: DeliveryAddressDto;
  cancellationReason?: string;
  promoCode?: string;
  items: OrderItemDto[];
  payments: OrderPaymentDto[];
  statusHistory: OrderStatusHistoryDto[];
}
```

#### Payment Methods (Enum)
```typescript
type PaymentMethod = 
  | 'Cash' 
  | 'CreditCard' 
  | 'DebitCard' 
  | 'OnlinePayment' 
  | 'MobilePayment' 
  | 'BankTransfer';
```

#### Order Types (Enum)
```typescript
type OrderType = 'DineIn' | 'Takeaway' | 'Delivery';
```

---

## Implementation Phases

### Phase 1: API Service Layer Setup
**Files to Create:**
- `src/services/basketService.ts`
- `src/services/orderService.ts`
- `src/types/basket.ts`
- `src/types/order.ts`
- `src/utils/apiClient.ts`

**User Stories:**
1. **As a developer**, I need a centralized HTTP client with error handling so that all API calls are consistent
2. **As a developer**, I need TypeScript interfaces matching backend DTOs so that data is type-safe
3. **As a developer**, I need basket service functions so that I can interact with basket endpoints
4. **As a developer**, I need order service functions so that I can interact with order endpoints

**Tasks:**
- [ ] Create `apiClient.ts` with axios configuration, error handling, and auth token management
- [ ] Define all TypeScript interfaces in `basket.ts` and `order.ts`
- [ ] Implement basket service functions: `getBasket()`, `addItem()`, `updateItem()`, `removeItem()`, `clearBasket()`, `applyPromoCode()`, `removePromoCode()`
- [ ] Implement order service functions: `createOrder()`, `getOrders()`, `getOrderById()`, `updateOrderStatus()`, `cancelOrder()`
- [ ] Add request/response interceptors for session ID and authentication

**Acceptance Criteria:**
- All service functions return properly typed responses
- Error handling provides user-friendly messages
- API client automatically includes session ID or auth token

---

### Phase 2: Session Management Implementation
**Files to Create:**
- `src/services/sessionService.ts`
- `src/hooks/useSession.ts`

**User Stories:**
1. **As an anonymous user**, I want my cart to persist across page refreshes so that I don't lose my items
2. **As an anonymous user**, I want a unique session ID generated automatically so that the backend can track my basket

**Tasks:**
- [ ] Create `sessionService.ts` with UUID generation (v4)
- [ ] Implement localStorage persistence for session ID
- [ ] Create `useSession` hook to manage session lifecycle
- [ ] Add session ID to all basket API requests via `X-Session-Id` header
- [ ] Handle session expiration and renewal

**Acceptance Criteria:**
- Session ID is generated on first visit
- Session ID persists in localStorage
- Session ID is included in all basket requests
- Expired sessions trigger new session creation

---

### Phase 3: Cart Context Backend Sync
**Files to Modify:**
- `src/components/cart/CartContext.tsx`

**User Stories:**
1. **As a user**, when I add an item to cart, it should sync with the backend immediately so that my cart is saved
2. **As a user**, when I refresh the page, my cart should load from the backend so that I don't lose items
3. **As a user**, when I update item quantities, changes should sync to the backend so that prices are recalculated

**Tasks:**
- [ ] Add new actions: `SYNC_FROM_BACKEND`, `SET_LOADING`, `SET_ERROR`
- [ ] Implement `syncCartWithBackend()` function to fetch basket on mount
- [ ] Update `ADD_ITEM` action to call `basketService.addItem()`
- [ ] Update `REMOVE_ITEM` action to call `basketService.removeItem()`
- [ ] Update `UPDATE_QUANTITY` action to call `basketService.updateItem()`
- [ ] Add optimistic updates with rollback on API failure
- [ ] Store `basketItemId` from backend in cart items for updates/deletes

**Acceptance Criteria:**
- Cart syncs with backend on app load
- All cart mutations call backend APIs
- UI updates optimistically, rolls back on failure
- Loading states are shown during API calls
- Error messages are displayed on API failures

---

### Phase 4: Cart Page UI Enhancement
**Files to Modify:**
- `src/app/cart/page.tsx` (or create if doesn't exist)
- `src/components/cart/CartItem.tsx`
- `src/components/cart/CartSummary.tsx`

**User Stories:**
1. **As a user**, I want to see accurate prices including tax and fees so that I know the total cost
2. **As a user**, I want to apply promo codes to get discounts so that I can save money
3. **As a user**, I want to add special instructions to items so that the restaurant knows my preferences

**Tasks:**
- [ ] Display basket data from backend: `subTotal`, `tax`, `deliveryFee`, `discount`, `total`
- [ ] Add promo code input field with apply/remove buttons
- [ ] Show applied promo code and discount amount
- [ ] Add special instructions textarea to each cart item
- [ ] Display loading skeleton while fetching basket
- [ ] Show empty cart message when no items
- [ ] Add "Continue Shopping" and "Proceed to Checkout" buttons

**Acceptance Criteria:**
- All prices match backend calculations
- Promo code can be applied and removed
- Special instructions are saved to backend
- Loading states are smooth
- Empty cart has meaningful message

---

### Phase 5: Checkout Flow - Order Type Selection
**Files to Create:**
- `src/app/checkout/page.tsx`
- `src/components/checkout/OrderTypeSelector.tsx`

**User Stories:**
1. **As a customer**, I want to choose between Dine-In, Takeaway, or Delivery so that I can get my order how I prefer
2. **As a customer ordering Dine-In**, I want to enter my table number so that staff know where to serve me
3. **As a customer ordering Delivery**, I want to enter my delivery address so that my order arrives at the right location

**Tasks:**
- [ ] Create checkout page with stepper/wizard UI (Type → Info → Summary → Confirmation)
- [ ] Add order type selection: DineIn, Takeaway, Delivery (radio buttons or cards)
- [ ] Conditionally show table number input for DineIn
- [ ] Conditionally show delivery address form for Delivery (street, city, postal code, etc.)
- [ ] Add form validation for required fields
- [ ] Store order type selection in local state

**Acceptance Criteria:**
- User can select one order type
- Table number is required for DineIn
- Delivery address is required for Delivery
- Validation errors are shown clearly
- User can navigate back to cart

---

### Phase 6: Checkout Flow - Customer Information
**Files to Create:**
- `src/components/checkout/CustomerInfoForm.tsx`

**User Stories:**
1. **As a customer**, I want to enter my name, email, and phone so that the restaurant can contact me
2. **As a logged-in user**, I want my information pre-filled so that I don't have to type it again
3. **As an anonymous user**, I want my info remembered for next time so that checkout is faster

**Tasks:**
- [ ] Create customer info form with fields: name (required), email (required), phone (required)
- [ ] Add email format validation
- [ ] Add phone number validation
- [ ] Pre-fill from user profile if authenticated
- [ ] Store in localStorage for anonymous users
- [ ] Add optional notes field for order

**Acceptance Criteria:**
- All fields are validated before proceeding
- Email format is validated
- Phone format is validated
- Logged-in users see pre-filled data
- Anonymous users' data persists in localStorage

---

### Phase 7: Order Summary & Payment
**Files to Create:**
- `src/components/checkout/OrderSummary.tsx`
- `src/components/checkout/PaymentSection.tsx`

**User Stories:**
1. **As a customer**, I want to review my entire order before placing it so that I can verify everything is correct
2. **As a customer**, I want to pay at the cashier so that I can pay when I arrive
3. **As a customer**, when I place an order, I want confirmation that it was successful so that I know it went through

**Tasks:**
- [ ] Display order summary: items, quantities, prices, fees, total
- [ ] Show selected order type and customer info
- [ ] Add payment method selection (initially only "Pay on Cashier" / Cash)
- [ ] Implement "Place Order" button with loading state
- [ ] Call `orderService.createOrder()` with all data
- [ ] Handle success: redirect to order confirmation page
- [ ] Handle errors: show error message, allow retry
- [ ] Clear basket after successful order placement

**Acceptance Criteria:**
- All order details are visible and correct
- Place order button is disabled during submission
- Success redirects to confirmation page with order ID
- Errors are shown with retry option
- Basket is cleared after successful order

---

### Phase 8: Order Confirmation Page
**Files to Create:**
- `src/app/order-confirmation/[orderId]/page.tsx`
- `src/components/order/OrderDetails.tsx`

**User Stories:**
1. **As a customer**, after placing an order, I want to see my order number so that I can reference it
2. **As a customer**, I want to see order details so that I can verify what I ordered
3. **As a customer**, I want to see estimated delivery/pickup time so that I know when to expect my order

**Tasks:**
- [ ] Create dynamic route for order confirmation
- [ ] Fetch order details using `orderService.getOrderById(orderId)`
- [ ] Display order number prominently
- [ ] Show order status and payment status
- [ ] Display estimated delivery/pickup time
- [ ] List all order items with quantities and prices
- [ ] Show total amount and payment method
- [ ] Add "View My Orders" button to order history
- [ ] Add "Back to Home" button

**Acceptance Criteria:**
- Order details are fetched and displayed correctly
- Order number is prominent and copyable
- All items, prices, and totals are shown
- Navigation buttons work correctly
- Page handles invalid order IDs gracefully

---

### Phase 9: Order History - Customer View
**Files to Create:**
- `src/app/my-orders/page.tsx`
- `src/components/order/OrderCard.tsx`

**User Stories:**
1. **As a customer**, I want to see all my past orders so that I can track my order history
2. **As a customer**, I want to filter orders by status so that I can find specific orders
3. **As a customer**, I want to view order details so that I can see what I ordered
4. **As a customer**, I want to re-order previous orders so that I can quickly order my favorites again

**Tasks:**
- [ ] Create "My Orders" page (protected route - requires auth)
- [ ] Fetch orders using `orderService.getOrders()` with UserId filter
- [ ] Display orders in card format with key info (number, dat, status, total)
- [ ] Add status filter dropdown (All, Pending, Preparing, Ready, Completed, Cancelled)
- [ ] Implement pagination if needed
- [ ] Add "View Details" button to expand order
- [ ] Add "Re-order" button to add all items to cart
- [ ] Show empty state when no orders

**Acceptance Criteria:**
- Only authenticated users can access
- Orders are listed with correct information
- Filters work correctly
- View details shows full order information
- Re-order adds all items to cart
- Empty state is shown when appropriate

---

### Phase 10: Admin Order Management
**Files to Create:**
- `src/app/admin/orders-management/page.tsx`
- `src/components/admin/OrdersTable.tsx`
- `src/components/admin/UpdateOrderStatusModal.tsx`

**User Stories:**
1. **As an admin**, I want to see all orders so that I can manage them
2. **As an admin**, I want to filter orders by status, date, and payment status so that I can find specific orders
3. **As an admin**, I want to update order status so that customers know their order progress
4. **As an admin**, I want to mark orders as focus orders so that they get priority
5. **As a staff member**, I want to view focus orders so that I can prioritize them

**Tasks:**
- [ ] Create admin orders page (protected - admin/staff only)
- [ ] Fetch all orders with filters: status, payment status, order type, date range
- [ ] Display orders in table with columns: number, customer, type, status, payment, total, date
- [ ] Add filter controls: status dropdown, payment status dropdown, date pickers, search
- [ ] Implement pagination with page size selection
- [ ] Add "Update Status" button with modal for status change
- [ ] Add "Focus Order" toggle button
- [ ] Add "View Details" to open order detail modal
- [ ] Add real-time updates (optional - using Events API)
- [ ] Add export functionality (CSV/PDF) (optional)
- [ ] Add navigation link to admin sidebar

**Acceptance Criteria:**
- Admin and staff can access page
- All filters work correctly
- Pagination works smoothly
- Status updates are reflected immediately
- Focus orders are highlighted
- Navigation link is added to admin sidebar

---

### Phase 11: Translation Keys Addition
**Files to Modify:**
- `src/locales/en/translation.json`
- `src/locales/tr/translation.json`
- `src/locales/es/translation.json`
- `src/locales/ar/translation.json`
- `src/locales/de/translation.json`
- `src/locales/fr/translation.json`
- `src/locales/it/translation.json`

**User Stories:**
1. **As a user**, I want the checkout flow in my language so that I can understand everything
2. **As an international user**, I want order statuses in my language so that I know what's happening

**Translation Keys to Add:**
```json
{
  "checkout_title": "Checkout",
  "order_type_label": "Order Type",
  "order_type_dine_in": "Dine In",
  "order_type_takeaway": "Takeaway",
  "order_type_delivery": "Delivery",
  "table_number_label": "Table Number",
  "delivery_address_label": "Delivery Address",
  "customer_info_title": "Customer Information",
  "customer_name_label": "Full Name",
  "customer_email_label": "Email Address",
  "customer_phone_label": "Phone Number",
  "order_notes_label": "Order Notes (Optional)",
  "payment_method_label": "Payment Method",
  "payment_cash": "Pay on Cashier (Cash)",
  "payment_credit_card": "Credit Card",
  "payment_debit_card": "Debit Card",
  "place_order_button": "Place Order",
  "order_summary_title": "Order Summary",
  "promo_code_label": "Promo Code",
  "apply_promo_button": "Apply",
  "remove_promo_button": "Remove",
  "special_instructions_label": "Special Instructions",
  "order_confirmation_title": "Order Confirmed!",
  "order_number_label": "Order Number",
  "order_status_label": "Order Status",
  "payment_status_label": "Payment Status",
  "estimated_time_label": "Estimated Time",
  "order_status_pending": "Pending",
  "order_status_preparing": "Preparing",
  "order_status_ready": "Ready",
  "order_status_completed": "Completed",
  "order_status_cancelled": "Cancelled",
  "payment_status_pending": "Pending",
  "payment_status_paid": "Paid",
  "payment_status_partially_paid": "Partially Paid",
  "payment_status_refunded": "Refunded",
  "my_orders_title": "My Orders",
  "view_order_details": "View Details",
  "reorder_button": "Re-order",
  "admin_orders_management_title": "Orders Management",
  "update_status_button": "Update Status",
  "focus_order_button": "Focus Order",
  "filter_by_status": "Filter by Status",
  "filter_by_payment": "Filter by Payment",
  "error_placing_order": "Error placing order. Please try again.",
  "error_loading_basket": "Error loading cart. Please refresh.",
  "error_invalid_promo_code": "Invalid promo code.",
  "success_order_placed": "Order placed successfully!",
  "validation_required_field": "This field is required",
  "validation_invalid_email": "Please enter a valid email",
  "validation_invalid_phone": "Please enter a valid phone number"
}
```

**Tasks:**
- [ ] Add all keys to English translation file
- [ ] Translate all keys to Turkish
- [ ] Translate all keys to Spanish
- [ ] Translate all keys to Arabic
- [ ] Translate all keys to German
- [ ] Translate all keys to French
- [ ] Translate all keys to Italian
- [ ] Test all translations on checkout flow
- [ ] Test all translations on order management

**Acceptance Criteria:**
- All translation keys are added to all 7 language files
- Translations are accurate and natural
- No hardcoded strings remain in code
- All UI text switches correctly when language changes

---

### Phase 12: Integration Testing & Bug Report
**Files to Create:**
- `BACKEND-ISSUES.md`

**User Stories:**
1. **As a developer**, I want to test the complete checkout flow so that I can verify it works correctly
2. **As a backend developer**, I want a list of bugs and missing features so that I can improve the API

**Tasks:**
- [ ] Test anonymous user flow: add to cart → checkout → place order
- [ ] Test authenticated user flow: login → add to cart → checkout → place order
- [ ] Test all order types: DineIn, Takeaway, Delivery
- [ ] Test promo code application and removal
- [ ] Test special instructions on items
- [ ] Test customer order history page
- [ ] Test admin order management: view, filter, update status
- [ ] Test focus order functionality
- [ ] Test error scenarios: invalid promo, network errors, validation errors
- [ ] Document all backend bugs in BACKEND-ISSUES.md
- [ ] Document missing features in BACKEND-ISSUES.md
- [ ] Document API inconsistencies in BACKEND-ISSUES.md

**Backend Issues to Check:**
- [ ] Does basket API return proper error messages?
- [ ] Are validation errors clear and helpful?
- [ ] Can orders be created without authentication?
- [ ] Is session ID properly handled for anonymous users?
- [ ] Are basket item IDs returned for updates/deletes?
- [ ] Is order number format user-friendly?
- [ ] Are estimated delivery times calculated?
- [ ] Is there a way to get order updates via WebSocket/SSE?
- [ ] Are prices rounded correctly?
- [ ] Is tax calculation accurate?

**Acceptance Criteria:**
- All user flows work end-to-end
- All order types can be placed successfully
- Error handling is robust
- BACKEND-ISSUES.md contains detailed bug reports
- All issues are categorized (Bug, Missing Feature, Improvement)

---

## Technical Considerations

### Session Management
- Use UUID v4 for session IDs
- Store in localStorage with key `rumi_session_id`
- Include in all basket API requests via `X-Session-Id` header
- Handle session expiration (backend returns expiry time)
- Migrate basket on user login (merge session basket with user basket)

### Error Handling
- Network errors: Show "Check your internet connection"
- 401 Unauthorized: Redirect to login
- 400 Bad Request: Show validation errors from backend
- 404 Not Found: Show "Item not found" message
- 500 Server Error: Show "Something went wrong, please try again"
- Use toast notifications for non-blocking errors
- Use modal dialogs for critical errors

### Loading States
- Show skeleton loaders for basket items
- Disable buttons during API calls
- Show spinner on "Place Order" button
- Use optimistic updates for immediate feedback

### Data Validation
- Client-side validation before API calls
- Email format validation (regex)
- Phone number validation (international format)
- Quantity validation (min: 1, max: 99)
- Table number validation (positive integer)
- Required field validation with helpful messages

### Performance Optimization
- Debounce quantity updates (500ms)
- Cache basket data for 30 seconds
- Use SWR or React Query for data fetching (optional)
- Implement request cancellation for rapid changes
- Lazy load order history pages

### Accessibility
- All forms have proper labels
- Focus management in modals
- Keyboard navigation support
- Screen reader announcements for updates
- Proper ARIA labels on interactive elements

---

## File Structure

```
src/
├── app/
│   ├── cart/
│   │   └── page.tsx (Enhanced with backend sync)
│   ├── checkout/
│   │   └── page.tsx (New - Checkout flow)
│   ├── order-confirmation/
│   │   └── [orderId]/
│   │       └── page.tsx (New - Order confirmation)
│   ├── my-orders/
│   │   └── page.tsx (New - Customer order history)
│   └── admin/
│       └── orders-management/
│           └── page.tsx (New - Admin orders)
├── components/
│   ├── cart/
│   │   ├── CartContext.tsx (Modified - Backend sync)
│   │   ├── CartItem.tsx (Enhanced with instructions)
│   │   └── CartSummary.tsx (Enhanced with promo code)
│   ├── checkout/
│   │   ├── OrderTypeSelector.tsx (New)
│   │   ├── CustomerInfoForm.tsx (New)
│   │   ├── OrderSummary.tsx (New)
│   │   └── PaymentSection.tsx (New)
│   ├── order/
│   │   ├── OrderDetails.tsx (New)
│   │   └── OrderCard.tsx (New)
│   └── admin/
│       ├── OrdersTable.tsx (New)
│       └── UpdateOrderStatusModal.tsx (New)
├── services/
│   ├── basketService.ts (New)
│   ├── orderService.ts (New)
│   └── sessionService.ts (New)
├── types/
│   ├── basket.ts (New)
│   └── order.ts (New)
├── hooks/
│   └── useSession.ts (New)
└── utils/
    └── apiClient.ts (New)
```

---

## Definition of Done

A phase is considered complete when:
1. All tasks are implemented
2. Code is properly typed with TypeScript
3. All components have loading and error states
4. User stories are fulfilled
5. Acceptance criteria are met
6. Code is tested manually
7. No console errors or warnings
8. Translation keys are added (if UI changes)
9. Code follows existing project patterns
10. Git commit is made with descriptive message

---

## Next Steps

1. **Review this plan** with the team
2. **Start with Phase 1** - Set up API service layer
3. **Work incrementally** - Complete one phase at a time
4. **Test frequently** - Test after each phase completion
5. **Document issues** - Keep BACKEND-ISSUES.md updated
6. **Communicate** - Report blockers immediately

---

## Notes

- Payment integration (credit card, online payment) is **out of scope** for initial implementation
- Real-time updates via Events API are **optional** for initial implementation
- Admin functionality beyond order management is **out of scope**
- Mobile app integration is **out of scope**
- This plan focuses on **web frontend only**

---

**Prepared by:** GitHub Copilot  
**Date:** 2025  
**Status:** Ready for Implementation ✅
