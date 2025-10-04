"use client";

import { useCallback, useEffect, useState } from "react";
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

  // Load categories once
  useEffect(() => {
    const init = async () => {
      try {
        const response = await getCategories();
        if (response.success && Array.isArray(response.data.items)) {
          setCategories(response.data.items as ApiCategory[]);
        } else {
          setCategories([]);
        }
      } catch (e) {
        console.error("Failed to load categories", e);
        setCategories([]);
      }
    };
    init();
  }, []);

  const fetchProducts = useCallback(async () => {
    setIsLoading(true);
    setError(null);
    setItems([]);
    try {
      const categoryId = selectedView === ALL_ITEMS_KEY ? null : selectedView;
      const response = await getProducts(1, 200, categoryId || undefined);
      if (!response.success) throw new Error(response.message || "Failed to fetch products");
      const mapped: MenuItem[] = (response.data?.items || []).map((p: any) => {
        const primaryImage = p.imageUrl || (Array.isArray(p.images) && p.images.length > 0 ? p.images[0].url : "/images/placeholder-falafel.jpeg");
        const gallery = Array.isArray(p.images)
          ? p.images.map((img: any) => ({ url: img.url, alt: img.altText || p.name }))
          : [];
        const hasContent = p.content && typeof p.content === 'object';
        const normalizedContent = hasContent
          ? Object.keys(p.content).reduce((acc: any, lang: string) => {
              const v = p.content[lang] || {};
              acc[lang] = { name: v.name || p.name, description: v.description || '' };
              return acc;
            }, {})
          : { en: { name: p.name, description: p.description || '' } };
        return {
          id: p.id,
          content: normalizedContent,
          price: typeof p.basePrice === "number" ? p.basePrice : parseFloat(p.basePrice || "0"),
          image: primaryImage,
          dietaryTags: [],
          categoryKey: categoryId || undefined,
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
      console.error("Failed to fetch products", e);
      setError(e?.message || "Failed to fetch products");
      setItems([]);
    } finally {
      setIsLoading(false);
    }
  }, [selectedView]);

  // Fetch products when selection changes
  useEffect(() => {
    if (!selectedView) return;
    fetchProducts();
  }, [selectedView, fetchProducts]);

  return {
    categories,
    selectedView,
    setSelectedView,
    items,
    isLoading,
    error,
    refetch: fetchProducts,
  } as const;
}
