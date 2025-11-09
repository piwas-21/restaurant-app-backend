# Fidelity Points & Discount System Implementation Plan

## Overview
Implement a comprehensive loyalty program with fidelity points, automatic discounts, and admin-controlled rules for both point accumulation and exclusive customer discounts.

---

## Feature Requirements

### 1. Fidelity Points System
- Users earn points by placing orders
- Points are based on configurable rules (e.g., order amount ranges)
- Points can be used for discounts on future orders
- Points balance displayed in user account page
- Points history tracking (earned/spent)

### 2. Point Earning Rules (Admin Configurable)
- Rule-based point allocation based on order total
- Example: "Earn 10 points for orders between $20-$50"
- Multiple rules can exist with different ranges and point values
- Rules are applied automatically when order is completed

### 3. Point Redemption
- Users can redeem points for discounts
- Configurable conversion rate (e.g., 100 points = $1 discount)
- Display available discount based on current points balance
- Show potential discount during order review

### 4. Exclusive Customer Discounts (Admin Managed)
- Admin can set customer-specific discount rules
- Rule-based discounts (e.g., "10% off orders above $50 for this customer")
- Multiple discount rules per customer
- Automatic application during checkout
- Display as "Special Discount" in order review

---

## Database Schema Design

### Tables to Create/Modify

#### 1. **FidelityPointsTransaction** (New)
```
- Id (Guid, PK)
- UserId (Guid, FK to ApplicationUser)
- OrderId (Guid?, FK to Order) - nullable for admin adjustments
- TransactionType (enum: Earned, Redeemed, AdminAdjustment, Expired)
- Points (int) - positive for earning, negative for spending
- OrderTotal (decimal?) - for reference
- Description (string)
- CreatedAt (DateTime)
- ExpiresAt (DateTime?) - for point expiration feature
```

#### 2. **FidelityPointBalance** (New)
```
- Id (Guid, PK)
- UserId (Guid, FK to ApplicationUser, unique)
- CurrentPoints (int)
- TotalEarnedPoints (int)
- TotalRedeemedPoints (int)
- LastUpdated (DateTime)
```

#### 3. **PointEarningRule** (New)
```
- Id (Guid, PK)
- Name (string) - e.g., "Bronze Tier"
- MinOrderAmount (decimal)
- MaxOrderAmount (decimal?)
- PointsAwarded (int)
- IsActive (bool)
- Priority (int) - for rule ordering
- CreatedAt (DateTime)
- UpdatedAt (DateTime?)
```

#### 4. **CustomerDiscountRule** (New)
```
- Id (Guid, PK)
- UserId (Guid, FK to ApplicationUser)
- Name (string) - e.g., "VIP 10% Discount"
- DiscountType (enum: Percentage, FixedAmount)
- DiscountValue (decimal)
- MinOrderAmount (decimal?)
- MaxOrderAmount (decimal?)
- MaxUsageCount (int?) - limit how many times it can be used
- UsageCount (int) - track usage
- IsActive (bool)
- ValidFrom (DateTime?)
- ValidUntil (DateTime?)
- CreatedBy (string) - admin who created it
- CreatedAt (DateTime)
- UpdatedAt (DateTime?)
```

#### 5. **Order** (Extend existing)
```
Add new fields:
- FidelityPointsEarned (int)
- FidelityPointsRedeemed (int)
- FidelityPointsDiscount (decimal)
- CustomerDiscountAmount (decimal)
- CustomerDiscountRuleId (Guid?)
```

---

## Implementation Tasks

### Phase 1: Database & Domain Layer (Backend)

#### Task 1.1: Create Domain Entities
- [ ] Create `FidelityPointsTransaction` entity
- [ ] Create `FidelityPointBalance` entity
- [ ] Create `PointEarningRule` entity
- [ ] Create `CustomerDiscountRule` entity
- [ ] Create enums: `TransactionType`, `DiscountType`
- [ ] Add fidelity fields to `Order` entity

#### Task 1.2: Database Configuration
- [ ] Create EF Core configurations for new entities
- [ ] Add DbSets to ApplicationDbContext
- [ ] Create and apply database migration
- [ ] Add seed data for initial point earning rules

### Phase 2: Backend Services & Business Logic

#### Task 2.1: Fidelity Points Service
- [ ] Create `IFidelityPointsService` interface
- [ ] Implement `FidelityPointsService`:
  - Calculate points for order based on active rules
  - Award points to user (create transaction & update balance)
  - Redeem points (deduct from balance, create transaction)
  - Get user's point balance
  - Get user's points history (transactions)
  - Check if points are sufficient for redemption

#### Task 2.2: Point Earning Rules Service
- [ ] Create `IPointEarningRuleService` interface
- [ ] Implement `PointEarningRuleService`:
  - Get all active rules
  - Find applicable rule for order amount
  - CRUD operations for rules (admin)
  - Validate rule conflicts (overlapping ranges)

#### Task 2.3: Customer Discount Service
- [ ] Create `ICustomerDiscountService` interface
- [ ] Implement `CustomerDiscountService`:
  - Get active discounts for user
  - Find applicable discount for order
  - Calculate discount amount
  - Apply discount and track usage
  - CRUD operations for discount rules (admin)
  - Validate discount rules

#### Task 2.4: Order Processing Integration
- [ ] Modify `CreateOrderCommandHandler`:
  - Calculate fidelity points for new order
  - Calculate and apply customer discounts
  - Calculate point redemption discount if requested
  - Store all discount/point info in order
- [ ] Create background job to award points when order is completed
- [ ] Handle point expiration (if implementing)

### Phase 3: Backend API Endpoints

#### Task 3.1: Fidelity Points Endpoints
- [ ] `GET /api/FidelityPoints/balance` - Get user's point balance
- [ ] `GET /api/FidelityPoints/history` - Get user's points transactions
- [ ] `GET /api/FidelityPoints/available-discount` - Calculate available discount from points
- [ ] `POST /api/FidelityPoints/calculate-for-order` - Preview points for order (before placing)

#### Task 3.2: Point Earning Rules Endpoints (Admin)
- [ ] `GET /api/Admin/PointRules` - List all rules
- [ ] `GET /api/Admin/PointRules/{id}` - Get rule details
- [ ] `POST /api/Admin/PointRules` - Create new rule
- [ ] `PUT /api/Admin/PointRules/{id}` - Update rule
- [ ] `DELETE /api/Admin/PointRules/{id}` - Delete rule
- [ ] `PUT /api/Admin/PointRules/{id}/activate` - Activate/deactivate rule

#### Task 3.3: Customer Discount Endpoints (Admin)
- [ ] `GET /api/Admin/CustomerDiscounts` - List all customer discounts
- [ ] `GET /api/Admin/CustomerDiscounts/user/{userId}` - Get discounts for user
- [ ] `POST /api/Admin/CustomerDiscounts` - Create discount rule
- [ ] `PUT /api/Admin/CustomerDiscounts/{id}` - Update discount rule
- [ ] `DELETE /api/Admin/CustomerDiscounts/{id}` - Delete discount rule

#### Task 3.4: Order Review Enhancement
- [ ] Modify order preview/calculation endpoint to include:
  - Points that will be earned
  - Available customer discounts
  - Available points redemption discount
  - Applied discounts breakdown

### Phase 4: Frontend - User Features

#### Task 4.1: Fidelity Points Display (Account Page)
- [ ] Update `FidelityPointsSection` component:
  - Display current points balance
  - Display total earned/redeemed
  - Show available discount from points
  - Display points history/transactions
  - Add "Learn More" about point system

#### Task 4.2: Order Review Enhancement
- [ ] Create `DiscountSummary` component showing:
  - Points to be earned from this order
  - Available customer special discount
  - Option to redeem points for discount
  - Discount breakdown (subtotal, discounts, final total)
- [ ] Add discount calculation to order review page
- [ ] Update order summary to show all discount types

#### Task 4.3: Points Redemption UI
- [ ] Create checkbox/toggle to use points for discount
- [ ] Show slider or input for partial point redemption
- [ ] Live calculation of discount as points are adjusted
- [ ] Display final price after point redemption

#### Task 4.4: TypeScript Types
- [ ] Create types for `FidelityPointBalance`
- [ ] Create types for `FidelityPointsTransaction`
- [ ] Create types for `PointEarningRule`
- [ ] Create types for `CustomerDiscountRule`
- [ ] Update `OrderDto` type with new fields

#### Task 4.5: Frontend Services
- [ ] Create `fidelityPointsService.ts`:
  - getBalance()
  - getHistory()
  - getAvailableDiscount()
  - calculatePointsForOrder()
- [ ] Update `orderService.ts`:
  - Include point redemption in order creation
  - Fetch order preview with discounts

### Phase 5: Frontend - Admin Features

#### Task 5.1: Point Earning Rules Management
- [ ] Create admin page: `/admin/point-rules`
- [ ] Create `PointRulesTable` component
- [ ] Create `PointRuleForm` modal for add/edit
- [ ] Implement rule validation (no overlapping ranges)
- [ ] Add activate/deactivate toggle

#### Task 5.2: Customer Discount Management
- [ ] Create admin page: `/admin/customer-discounts`
- [ ] Create `CustomerDiscountTable` component
- [ ] Create `CustomerDiscountForm` modal for add/edit
- [ ] Add user search/select component
- [ ] Display usage statistics
- [ ] Add filters (active, expired, by customer)

#### Task 5.3: Admin Dashboard Enhancement
- [ ] Add points analytics card (total points issued, redeemed)
- [ ] Add discount analytics (total discount given, by type)
- [ ] Add popular customers by points earned

### Phase 6: Testing & Validation

#### Task 6.1: Backend Tests
- [ ] Unit tests for FidelityPointsService
- [ ] Unit tests for PointEarningRuleService
- [ ] Unit tests for CustomerDiscountService
- [ ] Integration tests for order with points/discounts
- [ ] Test edge cases (insufficient points, expired discounts, etc.)

#### Task 6.2: Frontend Tests
- [ ] Test FidelityPointsSection rendering
- [ ] Test discount calculations
- [ ] Test point redemption flow
- [ ] Test admin rule creation/editing

#### Task 6.3: End-to-End Testing
- [ ] Test complete flow: place order → earn points → use points
- [ ] Test admin creating rules
- [ ] Test customer discount application
- [ ] Test order review with all discounts

### Phase 7: Documentation & Deployment

#### Task 7.1: Documentation
- [ ] Document API endpoints (Swagger)
- [ ] Create user guide for fidelity points
- [ ] Create admin guide for managing rules
- [ ] Update database schema documentation

#### Task 7.2: Deployment
- [ ] Run database migrations
- [ ] Deploy backend changes
- [ ] Deploy frontend changes
- [ ] Monitor for errors

---

## Technical Considerations

### 1. **Concurrency & Race Conditions**
- Use database transactions when updating point balances
- Implement optimistic concurrency for balance updates
- Lock user balance during point transactions

### 2. **Performance**
- Index foreign keys (UserId, OrderId)
- Cache active rules (rarely change)
- Consider materializing point balance instead of calculating

### 3. **Business Rules**
- Decide when points are awarded (order placed vs completed)
- Handle order cancellations (reverse points if already awarded)
- Handle refunds (reverse points)
- Point expiration policy (optional)

### 4. **Security**
- Validate point redemption amounts
- Ensure users can only redeem their own points
- Admin-only access for rule management
- Audit trail for admin adjustments

### 5. **User Experience**
- Clear messaging about how points work
- Show points progress (e.g., "10 more points to next reward")
- Highlight special discounts prominently
- Make point redemption optional and clear

---

## Success Metrics

1. **User Engagement**
   - % of users with points balance > 0
   - Average points per user
   - Points redemption rate

2. **Business Impact**
   - Increased order frequency from loyalty members
   - Average order value increase
   - Customer retention rate

3. **System Performance**
   - Point calculation time < 100ms
   - Discount application accuracy 100%
   - No point balance inconsistencies

---

## Future Enhancements

1. **Tiered Loyalty Program**
   - Bronze, Silver, Gold tiers based on points
   - Different earning rates per tier
   - Exclusive benefits per tier

2. **Point Expiration**
   - Points expire after X months
   - Notifications before expiration
   - Grace period extensions

3. **Referral Program**
   - Earn points for referring friends
   - Bonus points for referee's first order

4. **Special Promotions**
   - Double points days
   - Bonus points for specific menu items
   - Birthday bonuses

5. **Point Transfer**
   - Gift points to other users
   - Family/group accounts

---

## Estimated Timeline

- **Phase 1-2 (Backend Core)**: 3-4 days
- **Phase 3 (API)**: 2 days
- **Phase 4 (Frontend User)**: 3 days
- **Phase 5 (Frontend Admin)**: 2-3 days
- **Phase 6 (Testing)**: 2 days
- **Phase 7 (Documentation & Deploy)**: 1 day

**Total: ~13-15 days** (for a single developer working full-time)

---

## Priority for MVP

### Must Have (P0)
1. Basic point earning on order completion
2. Point balance display in account
3. Simple point redemption (fixed conversion rate)
4. Basic admin rule for point earning (one global rule)

### Should Have (P1)
1. Multiple point earning rules (different ranges)
2. Customer-specific discount rules
3. Admin UI for managing rules
4. Points history for users

### Nice to Have (P2)
1. Advanced analytics
2. Point expiration
3. Tiered loyalty program
4. Notifications
