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
  content: z.record(z.string(), z.object({
    name: z.string().optional(),
    description: z.string().optional()
  })).optional().default({}),
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
  kitchenType: z.enum(['None', 'FrontKitchen', 'BackKitchen']).default('None'),
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

// Menu Definition Schemas
const menuSectionItemSchema = z.object({
  productId: z.string().min(1, 'Product is required'),
  additionalPrice: z.coerce.number().min(0).default(0),
  displayOrder: z.coerce.number().int().default(0),
  isDefault: z.boolean().default(false),
});

const menuSectionSchema = z.object({
  id: z.string().optional(),
  name: z.string().min(1, 'Section name is required'),
  description: z.string().optional(),
  displayOrder: z.coerce.number().int().default(0),
  isRequired: z.boolean().default(true),
  minSelection: z.coerce.number().int().min(0).default(1),
  maxSelection: z.coerce.number().int().min(1).default(1),
  items: z.array(menuSectionItemSchema).default([]),
});

const menuDefinitionSchema = z.object({
  id: z.string().optional(),
  isAlwaysAvailable: z.boolean().default(true),
  startTime: z.string().nullable().optional(),
  endTime: z.string().nullable().optional(),
  availableMonday: z.boolean().default(true),
  availableTuesday: z.boolean().default(true),
  availableWednesday: z.boolean().default(true),
  availableThursday: z.boolean().default(true),
  availableFriday: z.boolean().default(true),
  availableSaturday: z.boolean().default(true),
  availableSunday: z.boolean().default(true),
  sections: z.array(menuSectionSchema).default([]),
});

export const createProductSchema = baseProductSchema.extend({
  menuDefinition: menuDefinitionSchema.optional(),
});

// Dedicated schema for Menu Bundles (cleaner, no redundant fields)
const baseMenuBundleSchema = z.object({
  name: z.string().min(1, 'Name is required'),
  description: z.string().optional(),
  basePrice: z.coerce.number().min(0),
  isActive: z.boolean().default(true),
  isAvailable: z.boolean().default(true),
  isSpecial: z.boolean().default(false),
  type: z.literal('menu'),
  preparationTimeMinutes: z.coerce.number().min(0).default(0),
  displayOrder: z.coerce.number().int().default(0),
  content: z.array(contentSchema).default([]).refine(items => {
    if (!items) return true;
    const languages = items.map(item => item.language);
    return new Set(languages).size === languages.length;
  }, { message: 'Each language can only be used once' }),
  menuDefinition: menuDefinitionSchema,
});

export const createMenuBundleSchema = baseMenuBundleSchema;

export const editProductSchema = baseProductSchema.extend({
  id: z.string().optional(),
  preparationTimeMinutes: z.coerce.number().optional(),
  displayOrder: z.coerce.number().optional(),
  menuDefinition: menuDefinitionSchema.optional(),
}).refine(d => !d.categoryIds || d.categoryIds.length === 0 || !!d.primaryCategoryId, {
  path: ['primaryCategoryId'],
  message: 'Primary category is required when categories are selected',
});

export const editMenuBundleSchema = baseMenuBundleSchema.extend({
  id: z.string().optional(),
});

export type FormData = z.infer<typeof createProductSchema>;
export type EditFormData = z.infer<typeof editProductSchema>;
export type MenuBundleFormData = z.infer<typeof createMenuBundleSchema>;
export type EditMenuBundleFormData = z.infer<typeof editMenuBundleSchema>;
