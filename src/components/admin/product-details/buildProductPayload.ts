import { ProductDetails, Variation, ProductCategory, SideItem } from '@/app/admin/menu-management/interfaces';

export function buildProductPayload(product: ProductDetails) {
  // For backwards compatibility, handle both id/categoryName
  const categoryIds = (product.categories || []).map((c: ProductCategory & { categoryId?: string }) => c.categoryId || c.categoryName || '').filter(Boolean);
  const primaryCategoryId = (product.categories || []).find((c: ProductCategory) => c.isPrimary)?.categoryName || '';

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
    suggestedSideItems: (product.suggestedSideItems || []).map((s: SideItem) => ({
      id: s.id,
      name: s.name,
      description: s.description,
      price: s.price,
      isRequired: s.isRequired,
    })),
    content,
  };
}

