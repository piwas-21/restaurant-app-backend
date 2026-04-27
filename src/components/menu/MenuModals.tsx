import React from 'react';
import ProductDetailsModal from '@/components/menu/ProductDetailsModal';
import CustomizationModal from '@/components/menu/CustomizationModal';
import type { FeaturedSpecial, ProductCustomization } from '@/types/menu';

interface MenuModalsProps {
  // Featured Special Modals
  featuredSpecial: FeaturedSpecial | null;
  showFeaturedDetails: boolean;
  showFeaturedCustomization: boolean;
  onCloseFeaturedDetails: () => void;
  onCloseFeaturedCustomization: () => void;
  onFeaturedCustomizationConfirm: (customization: ProductCustomization) => Promise<void>;
}

export default function MenuModals({
  featuredSpecial,
  showFeaturedDetails,
  showFeaturedCustomization,
  onCloseFeaturedDetails,
  onCloseFeaturedCustomization,
  onFeaturedCustomizationConfirm,
}: MenuModalsProps) {
  return (
    <>
      {/* Featured Special Details Modal */}
      {showFeaturedDetails && featuredSpecial && (
        <ProductDetailsModal
          isOpen={showFeaturedDetails}
          item={{
            id: featuredSpecial.id,
            name: featuredSpecial.name,
            description: featuredSpecial.description || '',
            price: featuredSpecial.basePrice,
            image: featuredSpecial.imageUrl || '',
            preparationTimeMinutes: featuredSpecial.preparationTimeMinutes,
            allergens: featuredSpecial.allergens,
            ingredients: featuredSpecial.ingredients,
            dietaryTags: [],
            content: {},
            images: featuredSpecial.imageUrl ? [{ url: featuredSpecial.imageUrl, alt: featuredSpecial.name }] : [],
            categoryKey: 'specials',
            isSpecial: true,
          }}
          onClose={onCloseFeaturedDetails}
        />
      )}

      {/* Featured Special Customization Modal */}
      {showFeaturedCustomization && featuredSpecial && (
        <CustomizationModal
          isOpen={showFeaturedCustomization}
          product={{
            id: featuredSpecial.id,
            name: featuredSpecial.name,
            description: featuredSpecial.description || '',
            basePrice: featuredSpecial.basePrice,
            imageUrl: featuredSpecial.imageUrl || '',
            preparationTimeMinutes: featuredSpecial.preparationTimeMinutes,
            allergens: featuredSpecial.allergens || [],
            ingredients: featuredSpecial.ingredients || [],
            detailedIngredients: featuredSpecial.detailedIngredients || [],
            displayOrder: 0,
            type: 'mainItem',
            isActive: true,
            isAvailable: true,
            isSpecial: true,
            content: {},
            images: featuredSpecial.images || (featuredSpecial.imageUrl ? [{ url: featuredSpecial.imageUrl, alt: featuredSpecial.name }] : []),
            variations: featuredSpecial.variations || [],
            suggestedSideItems: featuredSpecial.suggestedSideItems || [],
            categories: [],
          }}
          onClose={onCloseFeaturedCustomization}
          onAddToCart={onFeaturedCustomizationConfirm}
        />
      )}
    </>
  );
}
