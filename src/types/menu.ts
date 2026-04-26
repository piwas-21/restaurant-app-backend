export interface MenuItemContent {
  name: string;
  description: string;
  ingredient: string;
}

export type DietaryTag = "vegan" | "halal" | "gluten-free" | "vegetarian" | string;

export interface MenuItemImage {
  url: string;
  alt: string;
}

/**
 * Detailed ingredient with optional/pricing information
 */
export interface ProductIngredient {
  id: string;
  name: string;
  isOptional: boolean;
  maxQuantity?: number; // Maximum quantity allowed for this ingredient (default 1)
  price: number;
  isIncludedInBasePrice?: boolean; // If true, price is included in base and deducted when deselected
  isActive: boolean;
  displayOrder: number;
  // Multilingual support
  content?: Record<string, {
    name: string;
    description?: string;
  }>;
  globalIngredientId?: string;
}

/**
 * Product customization selected by user
 */
export interface ProductCustomization {
  productId: string;
  quantity: number;
  selectedVariationId?: string | null; // ID or name of selected variation
  selectedIngredients?: string[]; // IDs of optional ingredients to include
  ingredientQuantities?: Record<string, number>; // Quantity for each optional ingredient (default 1)
  excludedIngredients?: string[]; // IDs of default ingredients to exclude
  addedIngredients?: string[]; // IDs of optional ingredients added
  selectedSideItems?: Array<{
    id: string;
    quantity: number;
  }>;
  selectedMenuOptions?: SelectedMenuOption[]; // For menu bundle customization
  specialInstructions?: string;
  totalPrice: number;
}

export interface MenuItem {
  id: string;
  name: string; // Base name from API for fallback
  description?: string; // Base description from API for description fallback
  ingredients?: string[]; // Base ingredients from API for ingredients fallback (simple strings)
  detailedIngredients?: ProductIngredient[]; // Detailed ingredients with optional/pricing info
  content: Partial<Record<string, MenuItemContent>> & {
    en?: MenuItemContent;
  };
  price: number;
  image: string;
  dietaryTags: DietaryTag[];
  allergens?: string[];
  preparationTimeMinutes?: number;
  variations?: Array<{
    name: string;
    isActive: boolean;
    priceModifier: number;
    displayOrder: number;
    description?: string;
    content?: Record<string, {
      name: string;
      description?: string;
    }>;
  }>;
  suggestedSideItems?: SuggestedSideItem[]; // Full side item objects
  categoryKey?: string;
  isSpecial?: boolean;
  isActive?: boolean;
  isAvailable?: boolean;
  images?: MenuItemImage[];
  longDescription?: string;
  kitchenType?: KitchenType;
}

export type ApiCategory = { id: string; name: string };

export type ProductType = 'mainItem' | 'sideItem' | 'beverage' | 'dessert' | 'sauce' | 'addOn' | 'menu';

/**
 * Kitchen type enum for product kitchen designation
 */
export type KitchenType = 'None' | 'FrontKitchen' | 'BackKitchen';

export const KITCHEN_TYPES: Record<KitchenType, { label: string; value: KitchenType }> = {
  'None': { label: 'Not Assigned', value: 'None' },
  'FrontKitchen': { label: 'Front Kitchen', value: 'FrontKitchen' },
  'BackKitchen': { label: 'Back Kitchen', value: 'BackKitchen' },
};

export type ContentData = Record<string, {
  name: string;
  description?: string;
  ingredient?: string;
}>;

export interface CreateProductData {
  name: string;
  basePrice: number;
  type: ProductType;
  isActive: boolean;
  isAvailable: boolean;
  isSpecial: boolean;
  categoryIds: string[];
  primaryCategoryId: string;
  description?: string;
  ingredients?: string[];
  allergens?: string[];
  variations: Array<{
    name: string;
    isActive: boolean;
    priceModifier: number;
    displayOrder: number;
    description?: string;
  }>;
  content: ContentData;
}

export interface ProductResponse {
  success: boolean;
  message?: string;
  errors?: string[];
  data: {
    id: string;
  };
}

export interface DetailedProductVariation {
  id: string;
  name: string;
  description?: string;
  priceModifier: number;
  finalPrice: number;
  isActive: boolean;
  displayOrder: number;
  content?: Record<string, {
    name: string;
    description?: string;
  }>;
}

export interface SuggestedSideItem {
  id: string;
  name: string;
  description?: string;
  price: number;
  imageUrl?: string;
  isRequired: boolean;
  displayOrder: number;
  images?: MenuItemImage[];
}

export interface DetailedProduct {
  id: string;
  name: string;
  description?: string;
  basePrice: number;
  imageUrl?: string;
  isActive: boolean;
  isAvailable: boolean;
  isSpecial: boolean;
  preparationTimeMinutes?: number;
  type: ProductType;
  ingredients: string[]; // Simple ingredient strings for backward compatibility
  detailedIngredients?: ProductIngredient[]; // Detailed ingredients with optional/pricing info
  allergens: string[];
  displayOrder: number;
  content: ContentData;
  images: MenuItemImage[];
  categories: Array<{
    categoryId: string;
    categoryName: string;
    isPrimary: boolean;
    displayOrder: number;
  }>;
  primaryCategory?: {
    id: string;
    name: string;
    description?: string;
    imageUrl?: string;
    isActive: boolean;
    displayOrder: number;
    productCount: number;
    createdAt: string;
    updatedAt: string;
  };
  variations: DetailedProductVariation[];
  suggestedSideItems: SuggestedSideItem[];
  kitchenType?: KitchenType;
  menuDefinition?: MenuDefinition; // For menu bundle products
}

export interface DetailedProductResponse {
  success: boolean;
  message?: string;
  data: DetailedProduct;
  errors?: string[];
}

export interface FeaturedSpecial {
  id: string;
  name: string;
  description?: string;
  basePrice: number;
  imageUrl?: string;
  featuredDate: string;
  preparationTimeMinutes: number;
  ingredients?: string[];
  allergens?: string[];
  images?: MenuItemImage[];
  variations: DetailedProductVariation[];
  suggestedSideItems: SuggestedSideItem[];
  detailedIngredients: ProductIngredient[];
  content?: ContentData;
  kitchenType?: KitchenType;
}

export interface FeaturedSpecialResponse {
  success: boolean;
  message?: string;
  data: FeaturedSpecial | null;
  errors?: string[];
}

// ============================================
// Menu Bundle Interfaces
// ============================================

/**
 * Menu bundle definition with sections and scheduling
 */
export interface MenuDefinition {
  id: string;
  isAlwaysAvailable: boolean;
  startTime?: string; // HH:mm format
  endTime?: string; // HH:mm format
  availableMonday: boolean;
  availableTuesday: boolean;
  availableWednesday: boolean;
  availableThursday: boolean;
  availableFriday: boolean;
  availableSaturday: boolean;
  availableSunday: boolean;
  sections: MenuSection[];
}

/**
 * Section within a menu (e.g., "Choose Drink", "Select Side")
 */
export interface MenuSection {
  id: string;
  name: string;
  description?: string;
  displayOrder: number;
  isRequired: boolean;
  minSelection: number;
  maxSelection: number;
  items: MenuSectionItem[];
}

/**
 * Individual item choice within a menu section
 */
export interface MenuSectionItem {
  id: string;
  productId: string;
  productName?: string;
  additionalPrice: number;
  displayOrder: number;
  isDefault: boolean;
  ingredients?: string[];
  allergens?: string[];
  detailedIngredients?: DetailedIngredient[];
  suggestedSideItems?: SuggestedSideItem[];
}

export interface DetailedIngredient {
  id: string;
  name: string;
  isOptional: boolean;
  price: number;
  isIncludedInBasePrice: boolean;
  isActive: boolean;
  displayOrder: number;
  maxQuantity: number;
  content?: Record<string, { name: string; description?: string }>;
}

export interface SuggestedSideItem {
  id: string;
  sideItemProductId: string;
  sideItemProductName?: string;
  sideItemBasePrice: number;
  isRequired: boolean;
  displayOrder: number;
}

/**
 * Selected menu option by customer
 */
export interface SelectedMenuOption {
  sectionId: string;
  itemId: string;
  quantity: number;
  // Nested customization for this item
  specialInstructions?: string;
  selectedIngredients?: string[];
  excludedIngredients?: string[];
  ingredientQuantities?: Record<string, number>;
  selectedSideItems?: Array<{ id: string; quantity: number }>;
}

/**
 * Menu bundle for customer display
 */
export interface MenuBundleItem {
  id: string;
  name: string;
  description?: string;
  basePrice: number;
  content?: Record<string, { name: string; description: string }>;
  menuDefinition: MenuDefinition;
  images?: MenuItemImage[];
  isActive: boolean;
  isAvailable: boolean;
  isSpecial: boolean;
  preparationTimeMinutes?: number;
  displayOrder: number;
}
