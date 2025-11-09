# Menu Page Refactoring Documentation

## Overview
The menu page has been refactored to improve maintainability, testability, and code organization by separating concerns into smaller, focused components, hooks, and utility functions.

## Structure

### 📁 Utilities (`/src/utils/`)

#### `categoryNameMapper.ts`
**Purpose**: Handles category name mapping and translation logic

**Functions**:
- `mapCategoryNameToTranslationKey(apiCategoryName: string): string`
  - Maps API category names to translation keys
  - Returns lowercase category name if no mapping exists
  
- `getCategoryDisplayName(categoryName: string, translationFunction: (key: string) => string): string`
  - Gets the display name for a category
  - Returns translated name if available, otherwise returns API name

**Usage**:
```typescript
import { getCategoryDisplayName } from '@/utils/categoryNameMapper';

const displayName = getCategoryDisplayName(category.name, t);
```

#### `imageHelpers.ts`
**Purpose**: Handles image fallback and gallery logic

**Constants**:
- `FALLBACK_IMAGE`: Default placeholder image path

**Functions**:
- `setFallbackImage(menuItem: MenuItem): void`
  - Sets fallback image for menu items
  
- `getMenuItemImages(menuItem: MenuItem | null, currentLanguage: LanguageCode): Array<{ url: string; alt: string }>`
  - Gets image gallery array for a menu item
  - Handles multilingual alt text

**Usage**:
```typescript
import { setFallbackImage, getMenuItemImages } from '@/utils/imageHelpers';

setFallbackImage(menuItem);
const images = getMenuItemImages(menuItem, currentLanguage);
```

---

### 🎣 Custom Hooks (`/src/hooks/`)

#### `useImageGallery.ts`
**Purpose**: Manages image modal state and navigation

**Returns**:
- `enlargedImageItem`: Currently displayed item
- `currentImageIndex`: Current image index in gallery
- `currentEnlargedGalleryImages`: Array of images for current item
- `handleImageClick`: Opens image modal
- `handleCloseEnlargedImage`: Closes image modal
- `showNextImage`: Navigate to next image
- `showPrevImage`: Navigate to previous image

**Features**:
- Keyboard navigation support (Arrow keys, Escape)
- Automatic image index management
- Multi-language support for alt text

**Usage**:
```typescript
import { useImageGallery } from '@/hooks/useImageGallery';

const {
  enlargedImageItem,
  handleImageClick,
  handleCloseEnlargedImage,
  // ... other values
} = useImageGallery(currentLanguage);
```

#### `useFeaturedSpecial.ts`
**Purpose**: Manages featured special state and cart operations

**Returns**:
- `featuredSpecial`: Featured special data
- `showFeaturedDetails`: Details modal visibility state
- `showFeaturedCustomization`: Customization modal visibility state
- `handleAddFeaturedToCart`: Add featured special to cart
- `handleFeaturedCustomizationConfirm`: Confirm customization and add to cart
- `handleViewFeaturedDetails`: Show details modal
- `handleCloseFeaturedDetails`: Hide details modal
- `setShowFeaturedCustomization`: Set customization modal state

**Features**:
- Automatic loading of featured special on mount
- Cart integration
- Customization detection
- Toast notifications
- Error handling

**Usage**:
```typescript
import { useFeaturedSpecial } from '@/hooks/useFeaturedSpecial';

const {
  featuredSpecial,
  handleAddFeaturedToCart,
  handleViewFeaturedDetails,
  // ... other values
} = useFeaturedSpecial();
```

---

### 🧩 Components (`/src/components/menu/`)

#### `MenuPageHeader.tsx`
**Purpose**: Renders the menu page title with icon

**Props**: None (uses translation internally)

**Renders**:
- Page heading with utensils icon
- Accessible title with `aria-label`

**Usage**:
```typescript
import MenuPageHeader from '@/components/menu/MenuPageHeader';

<MenuPageHeader />
```

#### `MenuContent.tsx`
**Purpose**: Renders the main menu content section

**Props**:
```typescript
interface MenuContentProps {
  categoriesForNav: ApiCategory[];
  selectedView: string | typeof ALL_ITEMS_KEY;
  onSelectView: (view: string | typeof ALL_ITEMS_KEY) => void;
  categoryDisplayName: string;
  isLoadingItems: boolean;
  errorLoadingItems: string | null;
  currentMenuItems: MenuItem[];
  currentPage: number;
  totalPages: number;
  totalCount: number;
  onPageChange: (page: number) => void;
  onImageClick: (item: MenuItem, imageIndex?: number) => void;
  getFallbackImage: (menuItem: MenuItem) => void;
}
```

**Features**:
- Category navigation
- Menu items grid
- Pagination
- Loading/error/empty states
- Pagination info display

**Usage**:
```typescript
import MenuContent from '@/components/menu/MenuContent';

<MenuContent
  categoriesForNav={categories}
  selectedView={selectedView}
  onSelectView={setSelectedView}
  // ... other props
/>
```

#### `MenuModals.tsx`
**Purpose**: Centralized modal management for menu page

**Props**:
```typescript
interface MenuModalsProps {
  // Image Modal
  enlargedImageItem: MenuItem | null;
  currentImageIndex: number;
  currentEnlargedGalleryImages: Array<{ url: string; alt: string }>;
  onCloseEnlargedImage: () => void;
  onNextImage: () => void;
  onPrevImage: () => void;
  currentLanguage: LanguageCode;

  // Featured Special Modals
  featuredSpecial: FeaturedSpecial | null;
  showFeaturedDetails: boolean;
  showFeaturedCustomization: boolean;
  onCloseFeaturedDetails: () => void;
  onCloseFeaturedCustomization: () => void;
  onFeaturedCustomizationConfirm: (customization: ProductCustomization) => Promise<void>;
}
```

**Manages**:
- Image gallery modal
- Featured special details modal
- Featured special customization modal

**Usage**:
```typescript
import MenuModals from '@/components/menu/MenuModals';

<MenuModals
  enlargedImageItem={enlargedImageItem}
  currentImageIndex={currentImageIndex}
  // ... other props
/>
```

---

## Main Page Structure

### `src/app/menu/page.tsx`

The refactored main page is now much cleaner:

```typescript
export default function MenuPage() {
  // 1. Basic setup
  const { t, i18n } = useTranslation();
  const currentLanguage = ...;

  // 2. Custom hooks (encapsulated logic)
  const { ... } = usePublicMenu();
  const { ... } = useImageGallery(currentLanguage);
  const { ... } = useFeaturedSpecial();

  // 3. Simple rendering logic
  return (
    <main>
      <MenuPageHeader />
      <TableBanner />
      {featuredSpecial && <FeaturedSpecialComponent />}
      <MenuContent {...props} />
      <MenuModals {...props} />
      <Link to cart />
    </main>
  );
}
```

---

## Benefits of Refactoring

### 1. **Improved Maintainability**
- Each component/hook has a single responsibility
- Easier to locate and fix bugs
- Changes to one feature don't affect others

### 2. **Better Testability**
- Utilities can be unit tested independently
- Hooks can be tested with React Testing Library
- Components can be tested in isolation

### 3. **Code Reusability**
- Image gallery logic can be reused elsewhere
- Category mapping can be used in admin pages
- Featured special logic is portable

### 4. **Reduced Complexity**
- Main page reduced from ~400 lines to ~120 lines
- Each file has a clear, focused purpose
- Easier for new developers to understand

### 5. **Better Type Safety**
- Clear interfaces for all components
- Explicit prop types
- Better IDE autocomplete support

---

## File Organization

```
src/
├── app/menu/
│   └── page.tsx                 (Main menu page - 120 lines)
├── components/menu/
│   ├── MenuPageHeader.tsx       (Page header - 15 lines)
│   ├── MenuContent.tsx          (Content section - 110 lines)
│   ├── MenuModals.tsx           (Modal management - 130 lines)
│   ├── CategoryNav.tsx          (Existing)
│   ├── MenuList.tsx             (Existing)
│   ├── FeaturedSpecial.tsx      (Existing)
│   └── ...other existing components
├── hooks/
│   ├── useImageGallery.ts       (Image gallery logic - 60 lines)
│   ├── useFeaturedSpecial.ts    (Featured special logic - 110 lines)
│   └── usePublicMenu.ts         (Existing)
└── utils/
    ├── categoryNameMapper.ts    (Category utilities - 40 lines)
    └── imageHelpers.ts          (Image utilities - 35 lines)
```

---

## Migration Notes

### Breaking Changes
**None** - This refactoring maintains the same API and functionality

### Added Dependencies
**None** - Only uses existing dependencies

### Testing Checklist
- [ ] Featured special displays correctly
- [ ] Category navigation works
- [ ] Menu items load and display
- [ ] Image gallery opens and navigates
- [ ] Add to cart functionality works
- [ ] Customization modal opens when needed
- [ ] Details modal shows product information
- [ ] Pagination works correctly
- [ ] Error states display properly
- [ ] Loading states work
- [ ] Mobile responsive layout maintained

---

## Future Improvements

1. **Add Unit Tests**
   - Test utilities independently
   - Test hooks with React Testing Library
   - Test components in isolation

2. **Add Error Boundaries**
   - Wrap major sections in error boundaries
   - Provide fallback UI for errors

3. **Add Loading Skeletons**
   - Replace "Loading..." text with skeleton screens
   - Improve perceived performance

4. **Memoization**
   - Add `React.memo` to pure components
   - Use `useMemo` for expensive computations

5. **Performance Monitoring**
   - Add performance tracking
   - Monitor component render times

---

## Related Documentation

- [Component Guidelines](./COMPONENT_GUIDELINES.md)
- [Hook Patterns](./HOOK_PATTERNS.md)
- [Testing Strategy](./TESTING_STRATEGY.md)
- [Mobile Responsiveness](./MOBILE_RESPONSIVENESS.md)
