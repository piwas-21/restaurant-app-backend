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
  price: number;
  isActive: boolean;
  displayOrder: number;
  // Multilingual support
  content?: Record<string, {
    name: string;
    description?: string;
  }>;
}

/**
 * Product customization selected by user
 */
export interface ProductCustomization {
  productId: string;
  quantity: number;
  selectedVariationId?: string | null; // ID or name of selected variation
  selectedIngredients?: string[]; // IDs of optional ingredients to include
  excludedIngredients?: string[]; // IDs of default ingredients to exclude
  addedIngredients?: string[]; // IDs of optional ingredients added
  selectedSideItems?: Array<{
    id: string;
    quantity: number;
  }>;
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
  }>;
  suggestedSideItems?: SuggestedSideItem[]; // Full side item objects
  categoryKey?: string;
  isSpecial?: boolean;
  isActive?: boolean;
  isAvailable?: boolean;
  images?: MenuItemImage[];
  longDescription?: string;
}

export type ApiCategory = { id: string; name: string };

export type ProductType = 'mainItem' | 'sideItem' | 'beverage' | 'dessert' | 'sauce' | 'addOn';

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
}

export interface DetailedProductResponse {
  success: boolean;
  message?: string;
  data: DetailedProduct;
  errors?: string[];
}
