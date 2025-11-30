// Product-related types and interfaces
import { LANGUAGE_CODES } from '@/config/languageConfig';

export const productTypes = ['mainItem', 'beverage', 'dessert', 'sauce', 'addOn', 'menu'] as const;
export const supportedLanguages = LANGUAGE_CODES;

export type ProductType = typeof productTypes[number];
export type SupportedLanguage = typeof supportedLanguages[number];

export interface Category {
  id: string;
  name: string;
}

export interface CreateProductModalProps {
  isOpen: boolean;
  onClose: () => void;
  onProductCreated: () => void;
  categoryId?: string | null;
}

export interface EditProductModalProps {
  isOpen: boolean;
  onClose: () => void;
  onProductUpdated: () => void;
  product: any; // Product entity from API
}

export interface Variation {
  id?: string; // Optional for create, required for edit
  name: string;
  description?: string;
  priceModifier: number;
  isActive: boolean;
  displayOrder: number;
}

export interface ContentItem {
  language: string;
  name: string;
  description?: string;
  ingredient?: string;
}

export interface ProductFormData {
  name: string;
  description?: string;
  basePrice: number;
  isActive: boolean;
  isAvailable: boolean;
  isSpecial: boolean;
  type: ProductType;
  ingredients?: string;
  allergens?: string[];
  categoryIds: string[];
  primaryCategoryId: string;
  variations: Variation[];
  content: ContentItem[];
  preparationTimeMinutes: number;
  suggestedSideItemIds: string[];
}

// Extended interface for edit operations that includes additional fields
export interface EditProductFormData extends Omit<ProductFormData, 'preparationTimeMinutes'> {
  id?: string;
  preparationTimeMinutes?: number;
  displayOrder?: number;
}

// Generic interfaces for shared components
export interface BaseProductFormData {
  name: string;
  description?: string;
  basePrice: number;
  isActive: boolean;
  isAvailable: boolean;
  isSpecial: boolean;
  type: ProductType;
  ingredients?: string;
  allergens?: string[];
  categoryIds: string[];
  primaryCategoryId: string;
  variations: Variation[];
  content: ContentItem[];
  preparationTimeMinutes: number;
  suggestedSideItemIds: string[];
}

// Component prop interfaces that work with both create and edit
export interface ProductBasicInfoProps {
  register: any;
  errors: any;
  categories: Category[];
  selectedCategoryIds: string[];
  control: any;
}

export interface ProductDetailsProps {
  register: any;
  errors: any;
  control: any;
  imageFiles: File[];
  setImageFiles: (files: File[]) => void;
}

export interface MultilingualContentProps {
  register: any;
  errors: any;
  control: any;
  contentFields: any[];
  appendContent: (content: ContentItem) => void;
  removeContent: (index: number) => void;
  watch: any;
  currentLanguage: string;
}

export interface ProductVariationsProps {
  register: any;
  errors: any;
  variationFields: any[];
  appendVariation: (variation: Variation) => void;
  removeVariation: (index: number) => void;
}

// Interface for the new suggested side items component
export interface SuggestedSideItemsPickerProps {
  errors: any;
  control: any;
  selectedSideItemIds: string[];
  onChange: (selectedIds: string[]) => void;
}

// Product search result interface
export interface ProductSearchResult {
  id: string;
  name: string;
  description?: string;
  basePrice: number;
  type: ProductType;
}
