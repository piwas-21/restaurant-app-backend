export interface MenuItemContent {
  name: string;
  description: string;
}

export type DietaryTag = "vegan" | "halal" | "gluten-free" | "vegetarian" | string;

export interface MenuItemImage {
  url: string;
  alt: string;
}

export interface MenuItem {
  id: string;
  content: Partial<Record<string, MenuItemContent>> & {
    en?: MenuItemContent;
  };
  price: number;
  image: string;
  dietaryTags: DietaryTag[];
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
