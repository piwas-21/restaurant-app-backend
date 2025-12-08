import React from 'react';
import { useTranslation } from 'react-i18next';
import type { LanguageCode } from '@/components/LanguageSwitcher';
import type { MenuItem } from '@/types/menu';
import ImageModal from '@/components/menu/ImageModal';
import ProductDetailsModal from '@/components/menu/ProductDetailsModal';
import CustomizationModal from '@/components/menu/CustomizationModal';
import type { FeaturedSpecial, ProductCustomization } from '@/types/menu';
import { setFallbackImage } from '@/utils/imageHelpers';

interface MenuModalsProps {
  // Image Modal
  enlargedImageItem: MenuItem | null;
  currentImageIndex: number;
  currentEnlargedGalleryImages: Array<{ url: string; alt: string }>;
  onCloseEnlargedImage: () => void;
  onNextImage: () => void;
  onPrevImage: () => void;
  currentLanguage: LanguageCode;

  // Featured Special Modals
  featuredSpecial: FeaturedSpecial | null;
  showFeaturedDetails: boolean;
  showFeaturedCustomization: boolean;
  onCloseFeaturedDetails: () => void;
  onCloseFeaturedCustomization: () => void;
  onFeaturedCustomizationConfirm: (customization: ProductCustomization) => Promise<void>;
}

export default function MenuModals({
  enlargedImageItem,
  currentImageIndex,
  currentEnlargedGalleryImages,
  onCloseEnlargedImage,
  onNextImage,
  onPrevImage,
  currentLanguage,
  featuredSpecial,
  showFeaturedDetails,
  showFeaturedCustomization,
  onCloseFeaturedDetails,
  onCloseFeaturedCustomization,
  onFeaturedCustomizationConfirm,
}: MenuModalsProps) {
  const { t } = useTranslation();

  return (
    <>
      {/* Image Gallery Modal */}
      {enlargedImageItem && currentEnlargedGalleryImages.length > 0 && (
        <ImageModal
          isOpen={true}
          images={currentEnlargedGalleryImages}
          currentIndex={currentImageIndex}
          onClose={onCloseEnlargedImage}
          onNext={onNextImage}
          onPrev={onPrevImage}
          altBase={
            enlargedImageItem.content?.[currentLanguage]?.name ||
            enlargedImageItem.content?.en?.name ||
            enlargedImageItem.id
          }
          onImageError={() => setFallbackImage(enlargedImageItem)}
          previousLabel={t("previous_image_button_label")}
          nextLabel={t("next_image_button_label")}
          closeLabel={t("close_image_modal_button", "Close image modal")}
        />
      )}

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
            variations: featuredSpecial.variations || [], // Ensure these variations have their content property populated if available in FeaturedSpecial
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
