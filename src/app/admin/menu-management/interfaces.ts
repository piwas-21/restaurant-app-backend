// src/interfaces/Product.ts

export interface ProductImage {
  id: string;
  url: string;
  altText: string;
  isPrimary: boolean;
  sortOrder: number;
}

export interface SideItem {
  id: string;
  name: string;
  description: string;
  price: number;
  isRequired: boolean;
}

export interface Variation {
  id?: string;
  name: string;
  description?: string;
  priceModifier: number;
  finalPrice: number;
  isActive: boolean;
  displayOrder?: number;
}

export interface ProductCategory {
  categoryName: string;
  isPrimary: boolean;
}

export interface ProductIngredient {
  id: string;
  name: string;
  isOptional: boolean;
  price: number;
  isActive: boolean;
  displayOrder: number;
  content?: {
    [languageCode: string]: {
      name: string;
      description: string;
    };
  };
}

export interface ProductDetails {
  id: string;
  name: string;
  description: string;
  basePrice: number;
  isActive: boolean;
  isAvailable: boolean;
  isSpecial?: boolean;
  preparationTimeMinutes: number;
  type: string;
  ingredients: string[]; // Legacy field - kept for backward compatibility
  detailedIngredients?: ProductIngredient[];
  allergens: string[];
  categories: ProductCategory[];
  variations: Variation[];
  images: ProductImage[];
  suggestedSideItems: SideItem[];
  content?: any; // To match the full product object for the edit modal
}

export interface Product {
  id: string;
  name: string;
  description: string;
  basePrice: number;
  isActive: boolean;
  isAvailable: boolean;
  isSpecial?: boolean;
  imageUrl: string | null;
  images: ProductImage[];
}

export interface Category {
  id: string;
  name: string;
  description?: string | null;
  isActive: boolean;
  displayOrder: number;
}
