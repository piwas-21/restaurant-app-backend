import { ProductDetails, Variation, ProductCategory, SideItem } from '@/app/admin/menu-management/interfaces';

interface Category {
  id: string;
  name: string;
}

// Helper function to detect if a string looks like a UUID
const isUUID = (str: string): boolean => {
  const uuidRegex = /^[0-9a-f]{8}-[0-9a-f]{4}-[1-5][0-9a-f]{3}-[89ab][0-9a-f]{3}-[0-9a-f]{12}$/i;
  return uuidRegex.test(str);
};

export function buildProductPayload(product: ProductDetails, categories?: Category[]) {
  // Convert category names to IDs if categories are provided
  let categoryIds: string[];
  let primaryCategoryId: string;

  if (categories && categories.length > 0) {
    categoryIds = (product.categories || [])
      .map((c: ProductCategory) => categories.find(cat => cat.name === c.categoryName)?.id || '')
      .filter(Boolean);
    primaryCategoryId = (product.categories || [])
      .find((c: ProductCategory) => c.isPrimary)
      ? categories.find(cat => cat.name === (product.categories || []).find(c => c.isPrimary)?.categoryName)?.id || ''
      : '';
  } else {
    // Try to detect if categoryName is actually an ID (UUID pattern)
    const categoryNames = (product.categories || []).map((c: ProductCategory & { categoryId?: string }) => c.categoryId || c.categoryName || '').filter(Boolean);
    categoryIds = categoryNames;

    const primaryCategoryName = (product.categories || []).find((c: ProductCategory) => c.isPrimary)?.categoryName || '';

    // If the primary category name looks like a UUID, use it as-is; otherwise, it's a name
    primaryCategoryId = isUUID(primaryCategoryName) ? primaryCategoryName : primaryCategoryName;
  }

  type LocalizedContent = {
    name: string;
    description: string;
  };

  const content = product.content
    ? Object.fromEntries(
        Object.entries(product.content as Record<string, LocalizedContent>)
          .map(([lang, v]) => [lang, { name: v.name, description: v.description || '' }])
      )
    : undefined;

  return {
    id: product.id,
    name: product.name,
    description: product.description,
    basePrice: product.basePrice,
    isActive: product.isActive,
    isAvailable: product.isAvailable,
    isSpecial: (product as any).isSpecial ?? false,
    preparationTimeMinutes: product.preparationTimeMinutes,
    type: product.type,
    ingredients: product.ingredients || [],
    allergens: product.allergens || [],
    categoryIds,
    primaryCategoryId,
    variations: (product.variations || []).map((v: Variation) => ({
      id: v.id,
      name: v.name,
      priceModifier: v.priceModifier,
      isActive: v.isActive,
      displayOrder: 0,
      description: (v as any).description
    })),
    suggestedSideItemIds: (product.suggestedSideItems || []).map((s: SideItem) => s.id),
    content,
  };
}

