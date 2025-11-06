import { useState, useEffect, useCallback } from 'react';
import type { MenuItem } from '@/types/menu';
import type { LanguageCode } from '@/components/LanguageSwitcher';
import { getMenuItemImages } from '@/utils/imageHelpers';

export function useImageGallery(currentLanguage: LanguageCode) {
  const [enlargedImageItem, setEnlargedImageItem] = useState<MenuItem | null>(null);
  const [currentImageIndex, setCurrentImageIndex] = useState(0);

  const currentEnlargedGalleryImages = getMenuItemImages(enlargedImageItem, currentLanguage);

  const handleImageClick = useCallback((item: MenuItem, imageIndex: number = 0) => {
    setEnlargedImageItem(item);
    const initialImageIndex = item.images && item.images.length > imageIndex ? imageIndex : 0;
    setCurrentImageIndex(initialImageIndex);
  }, []);

  const handleCloseEnlargedImage = useCallback(() => {
    setEnlargedImageItem(null);
    setCurrentImageIndex(0);
  }, []);

  const showNextImage = useCallback(() => {
    setCurrentImageIndex(
      (prevIndex) => (prevIndex + 1) % currentEnlargedGalleryImages.length
    );
  }, [currentEnlargedGalleryImages.length]);

  const showPrevImage = useCallback(() => {
    setCurrentImageIndex(
      (prevIndex) =>
        (prevIndex - 1 + currentEnlargedGalleryImages.length) %
        currentEnlargedGalleryImages.length
    );
  }, [currentEnlargedGalleryImages.length]);

  // Keyboard navigation
  useEffect(() => {
    const handleKeyDown = (event: KeyboardEvent) => {
      if (!enlargedImageItem) return;

      if (event.key === "ArrowRight" && currentEnlargedGalleryImages.length > 1) {
        showNextImage();
      }
      if (event.key === "ArrowLeft" && currentEnlargedGalleryImages.length > 1) {
        showPrevImage();
      }
      if (event.key === "Escape") {
        handleCloseEnlargedImage();
      }
    };

    window.addEventListener("keydown", handleKeyDown);
    return () => window.removeEventListener("keydown", handleKeyDown);
  }, [enlargedImageItem, showNextImage, showPrevImage, handleCloseEnlargedImage, currentEnlargedGalleryImages.length]);

  return {
    enlargedImageItem,
    currentImageIndex,
    currentEnlargedGalleryImages,
    handleImageClick,
    handleCloseEnlargedImage,
    showNextImage,
    showPrevImage,
  };
}
