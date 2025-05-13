
import type { Language } from '@/context/LanguageContext';

// Define the structure for content in different languages
export interface MenuItemContent {
  name: string;
  description: string;
  ingredients?: string[]; // Optional
}

// Define possible dietary tags
export type DietaryTag = 'vegan' | 'halal' | 'gluten-free' | 'vegetarian';

// Define the structure for a single menu item
export interface MenuItem {
  id: string;
  content: Record<Language, MenuItemContent>;
  price: number; // Assumed to be in a consistent currency
  image: string; // URL for the primary image
  dietaryTags: DietaryTag[];
  categoryKey: string; // Key referencing a category in CategoryTranslations
  isSpecial?: boolean; // Optional flag for special items
  images?: { url: string; alt: string }[]; // Optional array for multiple images
}

// Define the structure for category translations across languages
export interface CategoryTranslations {
  en: Record<string, string>;
  tr: Record<string, string>;
  de: Record<string, string>;
  // Add other languages if needed
}

// Category keys type derived from a sample language (e.g., English)
// This helps enforce that categoryKey in MenuItem matches a valid category defined in translations.
// Note: This requires categories.json to be structured correctly.
// If using dynamic categories from API, this might need adjustment.
export type MenuCategoryKey = keyof CategoryTranslations['en'];
