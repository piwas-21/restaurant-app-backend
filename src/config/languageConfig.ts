// Centralized language configuration for the application
// This is the single source of truth for all supported languages

export const SUPPORTED_LANGUAGES = [
  { code: "en", name: "English", nativeName: "English", flag: "/flags/en.svg" },
  { code: "tr", name: "Turkish", nativeName: "Türkçe", flag: "/flags/tr.svg" },
  { code: "es", name: "Spanish", nativeName: "Español", flag: "/flags/es.svg" },
  { code: "ar", name: "Arabic", nativeName: "العربية", flag: "/flags/ar.svg" },
  { code: "de", name: "German", nativeName: "Deutsch", flag: "/flags/de.svg" },
  { code: "fr", name: "French", nativeName: "Français", flag: "/flags/fr.svg" },
  { code: "it", name: "Italian", nativeName: "Italiano", flag: "/flags/it.svg" },
  { code: "ru", name: "Russian", nativeName: "Русский", flag: "/flags/ru.svg" },
  { code: "zh", name: "Chinese", nativeName: "中文", flag: "/flags/zh.svg" },
] as const;

// Extract just the language codes for type safety
export const LANGUAGE_CODES = SUPPORTED_LANGUAGES.map(lang => lang.code);

// Type for language codes
export type LanguageCode = typeof SUPPORTED_LANGUAGES[number]['code'];

// Helper function to get language details by code
export function getLanguageByCode(code: string) {
  return SUPPORTED_LANGUAGES.find(lang => lang.code === code);
}

// Helper function to get language name by code
export function getLanguageName(code: string): string {
  return getLanguageByCode(code)?.name || code;
}

// Helper function to get native language name by code
export function getLanguageNativeName(code: string): string {
  return getLanguageByCode(code)?.nativeName || code;
}
