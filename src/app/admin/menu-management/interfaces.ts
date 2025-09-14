// src/interfaces/Product.ts

export interface ProductImage {
  url: string;
  altText: string;
  isPrimary: boolean;
}

export interface SideItem {
  id: string;
  name: string;
  description: string;
  price: number;
  isRequired: boolean;
}

export interface Variation {
  name: string;
  priceModifier: number;
  finalPrice: number;
  isActive: boolean;
}

export interface ProductCategory {
  categoryName: string;
  isPrimary: boolean;
}

export interface ProductDetails {
  id: string;
  name: string;
  description: string;
  basePrice: number;
  isActive: boolean;
  isAvailable: boolean;
  preparationTimeMinutes: number;
  type: string;
  ingredients: string[];
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
  basePrice: number;
  isActive: boolean;
  isAvailable: boolean;
}

export interface Category {
  id: string;
  name: string;
}
