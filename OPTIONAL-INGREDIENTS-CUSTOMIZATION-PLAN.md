# Optional Ingredients & Product Customization Feature - Implementation Plan

## Overview
This feature allows users to customize products when adding them to the cart by:
1. Selecting/unselecting optional ingredients (with pricing)
2. Viewing and adding suggested side items
3. Adding special requests/instructions (e.g., "extra spicy")

## Feature Requirements

### User Experience
- When clicking "Add to Order" on a product with optional ingredients or suggested sides, a modal opens
- Modal displays:
  - Product name, image, and base price
  - Optional ingredients (checkboxes) with individual prices
  - Suggested side items with prices
  - Special request text area
  - Total price calculation (base + selected options)
  - Quantity selector
  - Add to Cart button

### Admin Experience
- Ability to mark ingredients as "optional" in product details page
- Set pricing for optional ingredients
- Ingredients are multilingual (already supported)
- Suggested side items already exist in the system

### Technical Requirements
- Fully responsive design
- Dark/light theme support
- Multilingual support (en, tr, de, fr, it, ar, es)
- Integration with existing cart/basket system
- Backend API integration (if ingredient model changes are needed)

---

## Task Breakdown

### Phase 1: Backend & Data Model (If Required)

#### Task 1.1: Investigate Backend Ingredient Model
- **Priority**: HIGH
- **Files to check**: `backend/RestaurantSystem.Domain/`, `backend/RestaurantSystem.Infrastructure/`
- **Action items**:
  - Check if Product entity has ingredient entities or just string arrays
  - Determine if we need to create a new `ProductIngredient` entity
  - Fields needed: `id`, `productId`, `name`, `isOptional`, `price`, `isActive`, `displayOrder`
  - May need multilingual content support for ingredient names
- **Decision**: 
  - If ingredients are just strings → Need new entity and migration
  - If ingredient entity exists → Add `isOptional` and `price` fields

#### Task 1.2: Create/Update Backend API Endpoints
- **If new model needed**:
  - Create `ProductIngredient` entity
  - Add EF Core migration
  - Create endpoints: `GET/PUT /api/Products/{id}/ingredients`
- **Update existing endpoints**:
  - Ensure `AddItemToBasket` accepts `selectedIngredients` and `specialInstructions`
  - Update `BasketItem` to store customization data

---

### Phase 2: Frontend Type Definitions

#### Task 2.1: Update menu.ts Types
**File**: `src/types/menu.ts`

Add new interfaces:
```typescript
export interface ProductIngredient {
  id: string;
  name: string;
  isOptional: boolean;
  price: number;
  isActive: boolean;
  displayOrder: number;
  // Multilingual support
  content?: Record<string, {
    name: string;
    description?: string;
  }>;
}

export interface MenuItem {
  // ... existing fields
  ingredients?: string[]; // Keep for backward compatibility
  detailedIngredients?: ProductIngredient[]; // New field
  suggestedSideItems?: string[]; // Already exists, but ensure it's used
}

export interface DetailedProduct {
  // ... existing fields
  detailedIngredients?: ProductIngredient[];
  suggestedSideItems: SuggestedSideItem[];
}
```

#### Task 2.2: Update basket.ts Types
**File**: `src/types/basket.ts`

Add customization fields:
```typescript
export interface BasketItemDto {
  // ... existing fields
  selectedIngredients?: string[]; // IDs of selected optional ingredients
  excludedIngredients?: string[]; // IDs of ingredients to exclude
  addedIngredients?: string[]; // IDs of optional ingredients added
  specialInstructions?: string; // Already exists, ensure it's used properly
  customizationPrice?: number; // Additional price from customizations
}
```

---

### Phase 3: Component Development

#### Task 3.1: Create CustomizationModal Component
**File**: `src/components/menu/CustomizationModal.tsx`

**Props**:
```typescript
interface CustomizationModalProps {
  isOpen: boolean;
  onClose: () => void;
  product: MenuItem | DetailedProduct;
  onAddToCart: (customization: ProductCustomization) => void;
}

interface ProductCustomization {
  productId: string;
  quantity: number;
  selectedIngredients?: string[];
  excludedIngredients?: string[];
  addedIngredients?: string[];
  selectedSideItems?: string[];
  specialInstructions?: string;
  totalPrice: number;
}
```

**Features**:
- Modal overlay with click-outside-to-close
- Scrollable content area
- Sticky header (product info + close button)
- Sticky footer (total price + add to cart button)
- Price calculation in real-time

#### Task 3.2: Create OptionalIngredientsSection Component
**File**: `src/components/menu/customization/OptionalIngredientsSection.tsx`

**Features**:
- List of optional ingredients with checkboxes
- Display price next to each ingredient
- Show ingredient name in current language
- Group by included/excluded if needed
- Visual indicator for selected items

#### Task 3.3: Create SuggestedSideItemsSection Component
**File**: `src/components/menu/customization/SuggestedSideItemsSection.tsx`

**Features**:
- Display suggested side items with images (if available)
- Show name, description, and price
- Add/remove buttons
- Quantity selector for each side item

#### Task 3.4: Create SpecialRequestSection Component
**File**: `src/components/menu/customization/SpecialRequestSection.tsx`

**Features**:
- Textarea for special instructions
- Character limit (e.g., 200 characters)
- Character count display
- Placeholder with examples (e.g., "Extra spicy, no onions")

#### Task 3.5: Create PriceCalculator Component
**File**: `src/components/menu/customization/PriceCalculator.tsx`

**Features**:
- Display base price
- List of price modifications (ingredients, side items)
- Subtotal
- Quantity selector
- Final total

---

### Phase 4: Cart Integration

#### Task 4.1: Update CartContext
**File**: `src/components/cart/CartContext.tsx`

**Changes**:
- Modify `addItem` to accept customization data:
```typescript
const addItem = async (payload: AddItemPayload & {
  selectedIngredients?: string[];
  excludedIngredients?: string[];
  addedIngredients?: string[];
  specialInstructions?: string;
}) => {
  // ... existing logic
}
```

#### Task 4.2: Update MenuItem Component
**File**: `src/components/menu/MenuItem.tsx`

**Changes**:
- Replace direct `addItem` call with modal trigger
- Check if product has optional ingredients or suggested sides
- If yes → open CustomizationModal
- If no → add directly to cart (existing behavior)

```typescript
const handleAddItemToCart = useCallback(() => {
  const hasCustomizableOptions = 
    (item.detailedIngredients?.some(ing => ing.isOptional)) ||
    (item.suggestedSideItems?.length > 0);

  if (hasCustomizableOptions) {
    setShowCustomizationModal(true);
  } else {
    // Direct add to cart (existing logic)
    addItem({ productId: item.id, quantity: 1 });
  }
}, [item, addItem]);
```

#### Task 4.3: Update Cart Display
**File**: `src/components/cart/Cart.tsx`

**Changes**:
- Display customization details for cart items
- Show selected/excluded ingredients
- Show special instructions
- Show added side items

---

### Phase 5: Admin Dashboard Updates

#### Task 5.1: Create IngredientsManager Component
**File**: `src/components/admin/product-details/IngredientsManager.tsx`

**Features**:
- Table/list of all ingredients for a product
- Toggle "optional" checkbox for each ingredient
- Price input for optional ingredients
- Add/remove ingredient buttons
- Drag-and-drop for reordering (displayOrder)
- Multilingual name input

#### Task 5.2: Update Product Details Page
**File**: `src/app/admin/menu-management/[productId]/page.tsx`

**Changes**:
- Add new section: "Detailed Ingredients"
- Replace simple ingredients string input with IngredientsManager
- Keep backward compatibility with simple string array

---

### Phase 6: Styling

#### Task 6.1: Create CustomizationModal Styles
**File**: `src/components/menu/CustomizationModal.module.css`

**Requirements**:
- Modal overlay: semi-transparent dark background
- Modal content: centered, max-width 600px, rounded corners
- Responsive: full-width on mobile
- Dark theme support: darker backgrounds, lighter text
- Light theme support: clean white/light gray
- Smooth animations (fade-in, slide-up)

**Key classes**:
```css
.modalOverlay { /* overlay */ }
.modalContent { /* main modal container */ }
.modalHeader { /* sticky header */ }
.modalBody { /* scrollable content */ }
.modalFooter { /* sticky footer */ }
.closeButton { /* X button */ }
```

#### Task 6.2: Create Section Styles
**Files**:
- `src/components/menu/customization/OptionalIngredientsSection.module.css`
- `src/components/menu/customization/SuggestedSideItemsSection.module.css`
- `src/components/menu/customization/SpecialRequestSection.module.css`
- `src/components/menu/customization/PriceCalculator.module.css`

**Dark theme considerations**:
```css
[data-theme="dark"] .ingredientItem {
  background-color: var(--secondary-color, #2c2c2c);
  border-color: var(--border-color, #444);
  color: var(--text-color, #e0e0e0);
}
```

---

### Phase 7: Translations

#### Task 7.1: Add Translation Keys
Add to all language files (`en.json`, `tr.json`, `de.json`, `fr.json`, `it.json`, `ar.json`, `es.json`):

**English (en.json)**:
```json
{
  "customize_product": "Customize Your Order",
  "optional_ingredients": "Optional Ingredients",
  "suggested_sides": "Suggested Side Items",
  "special_requests": "Special Requests",
  "special_requests_placeholder": "Any special instructions? (e.g., extra spicy, no onions)",
  "add_ingredient": "Add",
  "remove_ingredient": "Remove",
  "ingredient_included": "Included",
  "ingredient_optional": "Optional",
  "base_price": "Base Price",
  "customization_cost": "Customizations",
  "quantity": "Quantity",
  "total_price": "Total",
  "add_to_cart_customized": "Add {{quantity}} to Cart",
  "character_limit": "{{current}}/{{max}} characters",
  "no_optional_ingredients": "No optional ingredients available",
  "no_suggested_sides": "No suggested sides available",
  "ingredient_price": "+CHF {{price}}",
  "side_item_price": "CHF {{price}}",
  "select_options": "Select options",
  "make_it_yours": "Make it yours!"
}
```

**Repeat for other languages** (Use appropriate translations)

---

### Phase 8: Testing & Refinement

#### Task 8.1: Manual Testing
- [ ] Test modal open/close on menu page
- [ ] Test ingredient selection/deselection
- [ ] Test side item selection
- [ ] Test special request input
- [ ] Test price calculation accuracy
- [ ] Test quantity changes
- [ ] Test add to cart with customizations
- [ ] Test cart display of customized items
- [ ] Test responsive design (mobile, tablet, desktop)
- [ ] Test dark/light theme switching
- [ ] Test all languages
- [ ] Test keyboard navigation (accessibility)
- [ ] Test screen reader compatibility

#### Task 8.2: Admin Testing
- [ ] Test ingredient management in admin
- [ ] Test marking ingredients as optional
- [ ] Test setting ingredient prices
- [ ] Test multilingual ingredient names
- [ ] Test suggested side items management

#### Task 8.3: Edge Cases
- [ ] Product with no optional ingredients
- [ ] Product with no suggested sides
- [ ] Product with both
- [ ] Product with many ingredients (scroll behavior)
- [ ] Very long product name
- [ ] Very long special request
- [ ] Zero quantity handling
- [ ] Negative prices (validation)

---

## File Structure Summary

### New Files to Create
```
src/
├── components/
│   └── menu/
│       ├── CustomizationModal.tsx
│       ├── CustomizationModal.module.css
│       └── customization/
│           ├── OptionalIngredientsSection.tsx
│           ├── OptionalIngredientsSection.module.css
│           ├── SuggestedSideItemsSection.tsx
│           ├── SuggestedSideItemsSection.module.css
│           ├── SpecialRequestSection.tsx
│           ├── SpecialRequestSection.module.css
│           ├── PriceCalculator.tsx
│           └── PriceCalculator.module.css
└── components/
    └── admin/
        └── product-details/
            ├── IngredientsManager.tsx
            └── IngredientsManager.module.css
```

### Files to Modify
```
src/
├── types/
│   ├── menu.ts (add ProductIngredient interface)
│   └── basket.ts (add customization fields)
├── components/
│   ├── menu/
│   │   └── MenuItem.tsx (add modal trigger logic)
│   └── cart/
│       ├── CartContext.tsx (update addItem function)
│       └── Cart.tsx (display customizations)
├── app/
│   └── admin/
│       └── menu-management/
│           └── [productId]/
│               └── page.tsx (add ingredients manager)
└── locales/
    ├── en.json
    ├── tr.json
    ├── de.json
    ├── fr.json
    ├── it.json
    ├── ar.json
    └── es.json
```

---

## Implementation Order (Recommended)

1. **Start with Types** (Phase 2) - Foundation for everything else
2. **Create Basic Modal Structure** (Task 3.1) - Empty modal with open/close
3. **Add Section Components** (Tasks 3.2-3.4) - Build each section independently
4. **Connect to MenuItem** (Task 4.2) - Trigger modal on click
5. **Add Price Calculator** (Task 3.5) - Real-time calculation
6. **Integrate with Cart** (Tasks 4.1, 4.3) - Full cart integration
7. **Add Translations** (Phase 7) - Multilingual support
8. **Style Everything** (Phase 6) - Polish UI with dark/light themes
9. **Admin Features** (Phase 5) - Management interface
10. **Backend Updates** (Phase 1) - If needed based on API testing
11. **Testing** (Phase 8) - Comprehensive testing

---

## Design Mockup (Text Description)

### Modal Layout
```
┌──────────────────────────────────────────────┐
│  [Product Image]  Product Name          [X]  │  ← Header (sticky)
│                   CHF 15.90                   │
├──────────────────────────────────────────────┤
│                                              │  ↓
│  Optional Ingredients                        │  |
│  ☑ Tomatoes (+CHF 0.00)                     │  |
│  ☑ Onions (+CHF 0.00)                       │  |  Body
│  ☐ Extra Cheese (+CHF 2.50)                │  |  (scrollable)
│                                              │  |
│  Suggested Side Items                        │  |
│  [+] French Fries    CHF 4.50               │  |
│  [-] Cola            CHF 3.50               │  ↑
│                                              │
│  Special Requests                            │
│  [Text area: Any special instructions?]      │
│                                              │
├──────────────────────────────────────────────┤
│  Base Price:          CHF 15.90             │  ← Footer (sticky)
│  Extra Cheese:        CHF 2.50              │
│  Cola:                CHF 3.50              │
│  ────────────────────────────────           │
│  Quantity: [-] 1 [+]  Total: CHF 21.90     │
│                                              │
│  [Add 1 to Cart - CHF 21.90]               │
└──────────────────────────────────────────────┘
```

---

## Accessibility Considerations

- **Keyboard Navigation**: 
  - Tab through all interactive elements
  - Enter/Space to toggle checkboxes
  - Escape to close modal
- **Screen Readers**: 
  - Proper ARIA labels on all inputs
  - Announce price changes
  - Announce when items are added/removed
- **Focus Management**: 
  - Focus trap within modal when open
  - Return focus to trigger button on close
- **Color Contrast**: 
  - Ensure sufficient contrast in both themes
  - Don't rely solely on color to convey information

---

## Performance Considerations

- **Lazy Load Modal**: Only render when opened
- **Debounce Price Calculations**: Avoid excessive recalculations
- **Optimize Images**: Use next/image for side item images
- **Memoize Components**: Use React.memo for section components
- **Virtual Scrolling**: If ingredient list is very long (unlikely but good practice)

---

## Future Enhancements (Out of Scope)

- Save favorite customizations
- Quick reorder with same customizations
- Ingredient allergen warnings with icons
- Image zoom for side items
- Recipe/ingredient info popup
- Nutritional information calculator
- "Popular combinations" suggestions

---

## Success Metrics

- Users can successfully customize products
- Cart correctly displays customizations
- Admin can manage ingredients efficiently
- UI is intuitive and accessible
- Performance remains smooth (< 100ms interaction response)
- Zero critical bugs in production
- Positive user feedback

---

## Risk Mitigation

**Risk**: Backend doesn't support ingredient entities
- **Mitigation**: Start with frontend using mock data, add backend later

**Risk**: Performance issues with many ingredients
- **Mitigation**: Implement virtual scrolling, optimize renders

**Risk**: UI is too complex for mobile users
- **Mitigation**: Simplify mobile layout, use accordions to collapse sections

**Risk**: Translation delays
- **Mitigation**: Use English first, add translations incrementally

---

## Timeline Estimate

- **Phase 1 (Backend)**: 2-3 days (if changes needed)
- **Phase 2 (Types)**: 0.5 day
- **Phase 3 (Components)**: 3-4 days
- **Phase 4 (Cart Integration)**: 1-2 days
- **Phase 5 (Admin)**: 2-3 days
- **Phase 6 (Styling)**: 2 days
- **Phase 7 (Translations)**: 1 day
- **Phase 8 (Testing)**: 2-3 days

**Total**: ~14-19 days for full implementation

---

## Notes

- Keep backward compatibility with products that don't have optional ingredients
- Ensure existing "Add to Order" button behavior remains for simple products
- Cache ingredient data to avoid repeated API calls
- Consider adding analytics to track which customizations are most popular
