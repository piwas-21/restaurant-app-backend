# Specials Management Implementation Plan

## Overview
Implement a comprehensive "Specials Management" feature in the admin dashboard that allows administrators to:
1. View all menu items marked as "special" (`isSpecial: true`)
2. Select one special item to be featured as "Today's Special" or "Chef's Special"
3. Display the selected special prominently on the public `/menu` page for customers

## Current State Analysis

### Existing Infrastructure
- ✅ `Product.IsSpecial` field exists in backend entity (`RestaurantSystem.Domain.Entities.Product`)
- ✅ `isSpecial` field exists in frontend TypeScript interfaces
- ✅ Basic `/admin/specials-management` page exists (mock data only)
- ✅ Admin sidebar navigation includes Specials Management link
- ✅ ProductsController with full CRUD operations exists

### Missing Components
- ❌ Backend API endpoint to fetch products where `IsSpecial = true`
- ❌ Backend API endpoint to set/unset featured special (needs new table/field)
- ❌ Frontend service to interact with specials API
- ❌ Frontend components for specials table and selection UI
- ❌ Public menu page integration to display featured special
- ❌ Database migration for featured special tracking

---

## Implementation Tasks

### Phase 1: Database & Backend API

#### Task 1.1: Database Schema Update
**File**: `backend/RestaurantSystem.Domain/Entities/`
- [ ] Create new entity `FeaturedSpecial` or add `IsFeaturedSpecial` + `FeaturedDate` fields to Product
- [ ] Decision: Use separate table or extend Product entity?
  - **Recommendation**: Add fields to Product entity for simplicity
  - Add: `IsFeaturedSpecial` (bool, default false)
  - Add: `FeaturedDate` (DateTime?, nullable)
  - Add logic to ensure only ONE product can be featured at a time
- [ ] Create EF Core migration

#### Task 1.2: Backend DTOs
**Directory**: `backend/RestaurantSystem.Api/Features/Products/Dtos/`
- [ ] Create `SpecialProductDto.cs` - DTO for special products listing
- [ ] Create `FeaturedSpecialDto.cs` - DTO for featured special response
- [ ] Add properties: id, name, description, basePrice, imageUrl, isFeaturedSpecial, featuredDate

#### Task 1.3: Backend Queries
**Directory**: `backend/RestaurantSystem.Api/Features/Products/Queries/`
- [ ] Create `GetSpecialProductsQuery/` folder
  - [ ] `GetSpecialProductsQuery.cs` - Query to fetch all products where IsSpecial = true
  - [ ] `GetSpecialProductsQueryHandler.cs` - Handler with pagination support
- [ ] Create `GetFeaturedSpecialQuery/` folder
  - [ ] `GetFeaturedSpecialQuery.cs` - Query to fetch the current featured special
  - [ ] `GetFeaturedSpecialQueryHandler.cs` - Handler returning single featured item

#### Task 1.4: Backend Commands
**Directory**: `backend/RestaurantSystem.Api/Features/Products/Commands/`
- [ ] Create `SetFeaturedSpecialCommand/` folder
  - [ ] `SetFeaturedSpecialCommand.cs` - Command with ProductId
  - [ ] `SetFeaturedSpecialCommandHandler.cs`
    - Unset any existing featured special (set IsFeaturedSpecial = false)
    - Set new product as featured (set IsFeaturedSpecial = true, FeaturedDate = now)
    - Validate product exists and IsSpecial = true
- [ ] Create `UnsetFeaturedSpecialCommand/` folder
  - [ ] `UnsetFeaturedSpecialCommand.cs` - Command to clear featured special
  - [ ] `UnsetFeaturedSpecialCommandHandler.cs`

#### Task 1.5: Update ProductsController
**File**: `backend/RestaurantSystem.Api/Features/Products/ProductsController.cs`
- [ ] Add endpoint: `GET /api/Products/specials` - List all special products
- [ ] Add endpoint: `GET /api/Products/featured-special` - Get current featured special (public)
- [ ] Add endpoint: `POST /api/Products/{id}/set-featured` - Set product as featured (admin)
- [ ] Add endpoint: `DELETE /api/Products/featured-special` - Unset featured special (admin)

---

### Phase 2: Frontend Services

#### Task 2.1: Update Product Service
**File**: `rumi-restaurant-web/src/services/productService.ts`
- [ ] Add function: `getSpecialProducts(pageNumber?, pageSize?)` - Fetch special products
- [ ] Add function: `setFeaturedSpecial(productId: string)` - Set featured special
- [ ] Add function: `unsetFeaturedSpecial()` - Clear featured special
- [ ] Handle API errors and fallback gracefully

#### Task 2.2: Create Menu Service Extension
**File**: `rumi-restaurant-web/src/services/menuService.ts`
- [ ] Add function: `getFeaturedSpecial()` - Fetch current featured special for public menu
- [ ] Add error handling and caching strategy

#### Task 2.3: Update Mock API Client
**File**: `rumi-restaurant-web/src/services/mockApiClient.ts`
- [ ] Add mock data for special products
- [ ] Add mock implementation for featured special operations
- [ ] Ensure fallback works when backend is unavailable

---

### Phase 3: Frontend Admin Components

#### Task 3.1: Create Specials Hook
**File**: `rumi-restaurant-web/src/hooks/useSpecialsManagement.ts`
- [ ] Create custom hook with:
  - `specialProducts` state
  - `featuredSpecial` state
  - `isLoading` state
  - `error` state
  - `fetchSpecialProducts()` function
  - `setFeaturedSpecial(productId)` function
  - `unsetFeaturedSpecial()` function
  - Auto-fetch on mount

#### Task 3.2: Update Specials Management Page
**File**: `rumi-restaurant-web/src/app/admin/specials-management/page.tsx`
- [ ] Remove mock data
- [ ] Integrate `useSpecialsManagement` hook
- [ ] Add loading and error states
- [ ] Add empty state when no specials exist

#### Task 3.3: Create Specials Table Component
**File**: `rumi-restaurant-web/src/components/admin/specials-management/SpecialsTable.tsx`
- [ ] Display table with columns:
  - Product Name
  - Description (truncated)
  - Price
  - Status (Featured / Not Featured)
  - Actions
- [ ] Add "Set as Featured" button for each product
- [ ] Highlight currently featured special with badge/icon
- [ ] Add confirmation dialog before setting featured
- [ ] Show loading state during API calls

#### Task 3.4: Create Featured Special Card Component
**File**: `rumi-restaurant-web/src/components/admin/specials-management/FeaturedSpecialCard.tsx`
- [ ] Display current featured special prominently
- [ ] Show product image, name, description, price
- [ ] Add "Remove Featured" button
- [ ] Show "No featured special" state when none selected

#### Task 3.5: Add Confirmation Modals
**Reuse**: Existing `ConfirmationModal` and `ResultModal` components
- [ ] Integrate confirmation for setting featured special
- [ ] Integrate confirmation for removing featured special
- [ ] Show success/error messages

---

### Phase 4: Frontend Public Menu Integration

#### Task 4.1: Update Menu Page
**File**: `rumi-restaurant-web/src/app/menu/page.tsx`
- [ ] Add state for `featuredSpecial`
- [ ] Fetch featured special on component mount
- [ ] Add conditional section at top of menu (before category nav)
- [ ] Handle loading and error states

#### Task 4.2: Create Featured Special Component
**File**: `rumi-restaurant-web/src/components/menu/FeaturedSpecial.tsx`
- [ ] Create attractive card/banner design
- [ ] Display:
  - Section title: "Chef's Special" or "Today's Special"
  - Product image (large, prominent)
  - Product name
  - Product description
  - Price with emphasis
  - "Order Now" / "Add to Cart" button
- [ ] Make component responsive (mobile-friendly)
- [ ] Add animations/transitions for visual appeal

#### Task 4.3: Update Menu Styles
**File**: `rumi-restaurant-web/src/app/styles/MenuPage.module.css`
- [ ] Add styles for `.featuredSpecialSection`
- [ ] Add styles for `.featuredSpecialCard`
- [ ] Add styles for `.featuredSpecialBadge`
- [ ] Ensure responsive design (mobile, tablet, desktop)
- [ ] Add hover effects and transitions

---

### Phase 5: Internationalization

#### Task 5.1: Add Translation Keys
**Files**: Translation JSON files (en, de, fr, etc.)
- [ ] Add keys:
  - `admin_specials_management_title`: "Specials Management"
  - `specials_table_header`: "Special Menu Items"
  - `featured_special_label`: "Featured Special"
  - `set_as_featured`: "Set as Featured"
  - `remove_featured`: "Remove Featured"
  - `confirm_set_featured`: "Are you sure you want to set this as the featured special?"
  - `confirm_remove_featured`: "Are you sure you want to remove the featured special?"
  - `no_specials_found`: "No special items found. Mark items as special in Menu Management."
  - `no_featured_special`: "No featured special selected"
  - `chefs_special`: "Chef's Special"
  - `todays_special`: "Today's Special"
  - `special_of_the_day`: "Special of the Day"
  - `order_special_now`: "Order Now"
  - `featured_special_updated`: "Featured special updated successfully"
  - `error_updating_featured`: "Error updating featured special"

---

### Phase 6: Testing & Quality Assurance

#### Task 6.1: Backend Tests
**Directory**: `backend/RestaurantSystem.IntegrationTests/`
- [ ] Write integration tests for GetSpecialProductsQuery
- [ ] Write integration tests for GetFeaturedSpecialQuery
- [ ] Write integration tests for SetFeaturedSpecialCommand
- [ ] Write integration tests for UnsetFeaturedSpecialCommand
- [ ] Test business rule: only one featured special at a time
- [ ] Test validation: can only feature products with IsSpecial = true

#### Task 6.2: Frontend Unit Tests
**Directory**: `rumi-restaurant-web/src/`
- [ ] Write tests for `useSpecialsManagement` hook
- [ ] Write tests for `SpecialsTable` component
- [ ] Write tests for `FeaturedSpecial` component
- [ ] Mock API calls and test error handling

#### Task 6.3: E2E Tests
**File**: `rumi-restaurant-web/e2e/admin-specials.e2e.ts`
- [ ] Test: Admin can view special products
- [ ] Test: Admin can set featured special
- [ ] Test: Admin can remove featured special
- [ ] Test: Only one special can be featured at a time
- [ ] Test: Featured special appears on public menu page
- [ ] Test: Non-admin users cannot access specials management

#### Task 6.4: Manual Testing Checklist
- [ ] Verify special products list loads correctly
- [ ] Verify featured special badge appears on correct item
- [ ] Verify featured special appears on public menu
- [ ] Verify "Set as Featured" works
- [ ] Verify "Remove Featured" works
- [ ] Verify only one item can be featured at a time
- [ ] Verify responsive design on mobile
- [ ] Verify translations work in all languages
- [ ] Verify error handling when API fails
- [ ] Verify loading states display correctly

---

### Phase 7: Documentation & Deployment

#### Task 7.1: Update Documentation
- [ ] Update README with specials management feature
- [ ] Document API endpoints in OpenAPI/Swagger
- [ ] Add comments to complex business logic
- [ ] Update admin user guide

#### Task 7.2: Database Migration
- [ ] Create and test migration script
- [ ] Prepare rollback strategy
- [ ] Document migration steps

#### Task 7.3: Deployment
- [ ] Deploy backend changes
- [ ] Run database migration
- [ ] Deploy frontend changes
- [ ] Verify in staging environment
- [ ] Deploy to production
- [ ] Monitor for errors

---

## Design Decisions

### Database Approach
**Decision**: Extend Product entity with `IsFeaturedSpecial` and `FeaturedDate` fields
**Rationale**:
- Simpler than separate table
- Maintains data integrity
- Easy to query
- Supports audit trail with FeaturedDate

### Business Rules
1. Only ONE product can be featured at a time
2. Only products with `IsSpecial = true` can be featured
3. Featured special is public (no auth required to view)
4. Only admins can set/unset featured special

### UI/UX Considerations
1. Featured special should be visually prominent on menu page
2. Should appear above category navigation
3. Mobile-first responsive design
4. Clear call-to-action ("Order Now")
5. Admin UI should clearly indicate current featured status

### Naming Convention
**Options for public display**:
- "Chef's Special" (✅ Recommended - personal, prestigious)
- "Today's Special"
- "Special of the Day"
- "Featured Dish"

### Performance Optimization
- Cache featured special on frontend (5-minute TTL)
- Use indexed query for IsSpecial and IsFeaturedSpecial fields
- Lazy load images on admin page
- Optimize product image sizes

---

## Timeline Estimate

- **Phase 1** (Backend): 4-6 hours
- **Phase 2** (Services): 2-3 hours
- **Phase 3** (Admin UI): 4-6 hours
- **Phase 4** (Public Menu): 3-4 hours
- **Phase 5** (i18n): 1-2 hours
- **Phase 6** (Testing): 4-6 hours
- **Phase 7** (Deployment): 2-3 hours

**Total Estimate**: 20-30 hours

---

## Dependencies

- Backend: .NET 8, Entity Framework Core, PostgreSQL
- Frontend: Next.js, React, TypeScript
- Styling: CSS Modules
- State Management: React Hooks
- API Communication: Fetch API
- Authentication: Existing auth system

---

## Success Criteria

- ✅ Admin can view all special menu items
- ✅ Admin can set one item as featured special
- ✅ Only one item is featured at a time
- ✅ Featured special appears prominently on public menu
- ✅ All operations have proper error handling
- ✅ UI is responsive and accessible
- ✅ Translations work in all supported languages
- ✅ Tests achieve >80% coverage
- ✅ No performance degradation on menu page

---

## Future Enhancements (Out of Scope)

- Schedule featured specials in advance
- Rotate specials automatically (daily, weekly)
- Multiple featured items (carousel)
- Special pricing for featured items
- Analytics: track featured special views/orders
- Customer notifications when new special is featured
- Featured special history/archive
