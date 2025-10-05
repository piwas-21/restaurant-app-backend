import { z } from 'zod';
import { productTypes } from './types';

// Zod Schemas for validation
export const variationSchema = z.object({
  id: z.string().optional(), // For edit operations
  name: z.string().min(1, 'Variation name is required'),
  description: z.string().optional(),
  priceModifier: z.coerce.number(),
  isActive: z.boolean().default(true),
  displayOrder: z.coerce.number().int().default(0),
});

export const contentSchema = z.object({
  language: z.string().min(1, 'Language is required'),
  name: z.string().min(1, 'Name is required for this language'),
  description: z.string().optional(),
});

// Base product schema shared by both create and edit
const baseProductSchema = z.object({
  name: z.string().min(1),
  description: z.string().optional(),
  basePrice: z.coerce.number().min(0),
  isActive: z.boolean().default(true),
  isAvailable: z.boolean().default(true),
  isSpecial: z.boolean().default(false),
  type: z.enum(productTypes),
  ingredients: z.string().optional(),
  allergens: z.array(z.string()).optional(),
  categoryIds: z.array(z.string()).min(1, 'Select at least one category'),
  primaryCategoryId: z.string().min(1, 'Primary category is required'),
  variations: z.array(variationSchema).default([]),
  content: z.array(contentSchema).default([]).refine(items => {
    if (!items) return true;
    const languages = items.map(item => item.language);
    return new Set(languages).size === languages.length;
  }, { message: 'Each language can only be used once' }),
  preparationTimeMinutes: z.coerce.number().min(0).default(0),
  suggestedSideItemIds: z.array(z.string()).default([]),
});

export const createProductSchema = baseProductSchema;

export const editProductSchema = baseProductSchema.extend({
  id: z.string().optional(),
  preparationTimeMinutes: z.coerce.number().optional(),
  displayOrder: z.coerce.number().optional(),
}).refine(d => !d.categoryIds || d.categoryIds.length === 0 || !!d.primaryCategoryId, {
  path: ['primaryCategoryId'],
  message: 'Primary category is required when categories are selected',
});

export type FormData = z.infer<typeof createProductSchema>;
export type EditFormData = z.infer<typeof editProductSchema>;
