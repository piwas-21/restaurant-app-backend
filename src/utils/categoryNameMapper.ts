/**
 * Maps API category names to translation keys
 * Used for localizing category names from the backend
 */
export function mapCategoryNameToTranslationKey(apiCategoryName: string): string {
  const mapping: Record<string, string> = {
    'Starters': 'starters',
    'Grills': 'grill',
    'Grill': 'grill',
    'Dessert': 'dessert',
    'Desserts': 'dessert',
    'Dürüm Wraps': 'durum',
    'Durum Wraps': 'durum',
    'Hot Drinks': 'hotDrink',
    'Cold Drinks': 'coldDrink',
    'Drinks': 'hotDrink',
    'Pizza': 'pizza',
    'Pide': 'pide',
    'Turkish Specialties': 'turkishSpecialty',
    'Oriental Specialties': 'orientalSpecialty',
    'Special of the Day': 'specialOfTheDay',
    'Soups': 'soups'
  };

  return mapping[apiCategoryName] || apiCategoryName.toLowerCase();
}

/**
 * Gets the display name for a category
 * Returns translated name if available, otherwise returns API name
 */
export function getCategoryDisplayName(
  categoryName: string,
  translationFunction: (key: string) => string
): string {
  const translationKey = mapCategoryNameToTranslationKey(categoryName);
  const translatedName = translationFunction(translationKey);

  // If translation exists and is different from the key, use it; otherwise use API name
  return translatedName !== translationKey ? translatedName : categoryName;
}
