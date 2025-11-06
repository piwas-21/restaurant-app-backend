"use client";

import { useCallback, useEffect, useState, useRef } from "react";
import { getCategories } from "@/services/categoryService";
import { getProducts } from "@/services/menuService";
import type { ApiCategory, MenuItem } from "@/types/menu";

export const ALL_ITEMS_KEY = "all" as const;

export function usePublicMenu() {
  const [categories, setCategories] = useState<ApiCategory[]>([]);
  const [selectedView, setSelectedView] = useState<string | typeof ALL_ITEMS_KEY | null>(ALL_ITEMS_KEY);
  const [items, setItems] = useState<MenuItem[]>([]);
  const [isLoading, setIsLoading] = useState(false);
  const [error, setError] = useState<string | null>(null);
  const [currentPage, setCurrentPage] = useState(1);
  const [totalPages, setTotalPages] = useState(1);
  const [totalCount, setTotalCount] = useState(0);
  const pageSize = 10;

  // Use ref to track the latest selectedView without causing re-renders
  const selectedViewRef = useRef(selectedView);

  useEffect(() => {
    selectedViewRef.current = selectedView;
  }, [selectedView]);

  // Load categories once - fetch all categories for menu display
  useEffect(() => {
    const init = async () => {
      try {
        const response = await getCategories(1, 100) as { success: boolean; data?: { items: any[] } };
        if (response.success && response.data?.items && Array.isArray(response.data.items)) {
          setCategories(response.data.items as ApiCategory[]);
        } else {
          setCategories([]);
        }
      } catch (e) {
        // eslint-disable-next-line no-console
        console.error("Failed to load categories", e);
        setCategories([]);
      }
    };
    init();
  }, []);

  const fetchProducts = useCallback(async (page: number, categoryId: string | typeof ALL_ITEMS_KEY | null) => {
    setIsLoading(true);
    setError(null);
    setItems([]);
    try {
      const catId = categoryId === ALL_ITEMS_KEY ? null : categoryId;
      const response = await getProducts(page, pageSize, catId || undefined);
      if (!response.success) throw new Error(response.message || "Failed to fetch products");

      // Update pagination metadata
      setTotalPages(response.data?.totalPages || 1);
      setTotalCount(response.data?.totalCount || 0);
      setCurrentPage(page);

      const mapped: MenuItem[] = (response.data?.items || []).map((p: any) => {
        const primaryImage = p.imageUrl || (Array.isArray(p.images) && p.images.length > 0 ? p.images[0].url : "/images/placeholder-falafel.jpeg");
        const gallery = Array.isArray(p.images)
          ? p.images.map((img: any) => ({ url: img.url, alt: img.altText || p.name }))
          : [];
        const hasContent = p.content && typeof p.content === 'object';
        const normalizedContent = hasContent
          ? Object.keys(p.content).reduce((acc: any, lang: string) => {
              const v = p.content[lang] || {};
              acc[lang] = {
                name: v.name || p.name,
                description: v.description || '',
                ingredient: v.ingredient || ''
              };
              return acc;
            }, {})
          : { en: { name: p.name, description: p.description || '', ingredient: '' } };
        return {
          id: p.id,
          name: p.name || 'Unnamed Item',
          description: p.description || '',
          ingredients: Array.isArray(p.ingredients) ? p.ingredients : [],
          detailedIngredients: Array.isArray(p.detailedIngredients) ? p.detailedIngredients : [],
          content: normalizedContent,
          price: typeof p.basePrice === "number" ? p.basePrice : parseFloat(p.basePrice || "0"),
          image: primaryImage,
          dietaryTags: [],
          allergens: Array.isArray(p.allergens) ? p.allergens : [],
          preparationTimeMinutes: typeof p.preparationTimeMinutes === "number" ? p.preparationTimeMinutes : undefined,
          variations: Array.isArray(p.variations) ? p.variations : [],
          suggestedSideItems: Array.isArray(p.suggestedSideItems) ? p.suggestedSideItems : [],
          categoryKey: catId || undefined,
          isSpecial: p.isSpecial,
          isActive: p.isActive,
          isAvailable: p.isAvailable,
          images: gallery,
          longDescription: p.description || "",
        };
      });
      const filtered = mapped.filter(i => i.isActive !== false && i.isAvailable !== false);
      setItems(filtered);
    } catch (e: any) {
      // eslint-disable-next-line no-console
      console.error("Failed to fetch products", e);
      setError(e?.message || "Failed to fetch products");
      setItems([]);
    } finally {
      setIsLoading(false);
    }
  }, [pageSize]);

  // Fetch products when selection changes (reset to page 1)
  useEffect(() => {
    if (!selectedView) return;
    setCurrentPage(1);
    fetchProducts(1, selectedView);
  }, [selectedView, fetchProducts]);

  const handlePageChange = useCallback((page: number) => {
    fetchProducts(page, selectedViewRef.current);
    // Scroll to top of menu section
    window.scrollTo({ top: 0, behavior: 'smooth' });
  }, [fetchProducts]);

  return {
    categories,
    selectedView,
    setSelectedView,
    items,
    isLoading,
    error,
    currentPage,
    totalPages,
    totalCount,
    pageSize,
    onPageChange: handlePageChange,
    refetch: () => fetchProducts(currentPage, selectedViewRef.current),
  } as const;
}
