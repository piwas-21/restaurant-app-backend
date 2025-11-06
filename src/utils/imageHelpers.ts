import type { MenuItem } from '@/types/menu';
import type { LanguageCode } from '@/components/LanguageSwitcher';

/**
 * Gets fallback image for menu items
 */
export const FALLBACK_IMAGE = '/images/placeholder-falafel.jpeg';

export function setFallbackImage(menuItem: MenuItem): void {
  if (menuItem && menuItem.image !== FALLBACK_IMAGE) {
    menuItem.image = FALLBACK_IMAGE;
  }
}

/**
 * Gets image gallery for a menu item
 */
export function getMenuItemImages(
  menuItem: MenuItem | null,
  currentLanguage: LanguageCode
): Array<{ url: string; alt: string }> {
  if (!menuItem) return [];

  if (menuItem.images && menuItem.images.length > 0) {
    return menuItem.images;
  }

  const altText =
    menuItem.content?.[currentLanguage]?.name ||
    menuItem.content?.en?.name ||
    menuItem.name ||
    'Menu item image';

  return [{ url: menuItem.image, alt: altText }];
}
