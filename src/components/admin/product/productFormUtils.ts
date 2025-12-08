import { UseFormSetError, UseFormReset } from 'react-hook-form';
import { FormData, EditFormData } from './schemas';
import { createProduct, createMenuBundle, updateMenuBundle } from '@/services/menuService';
import { updateProduct, uploadBulkProductImages } from '@/services/productService';
import { createGlobalIngredient, searchGlobalIngredients } from '@/services/globalIngredientService';

interface SubmitProductFormParams {
  data: FormData;
  imageFiles: File[];
  currentLanguage: string;
  detailedIngredients?: any[];
  setSubmissionStatus: (status: 'idle' | 'creating' | 'uploading') => void;
  setError: UseFormSetError<FormData>;
  onProductCreated: () => void;
  onClose: () => void;
  reset: UseFormReset<FormData>;
  setImageFiles: (files: File[]) => void;
}

interface SubmitEditProductFormParams {
  data: EditFormData;
  product: any;
  imageFiles: File[];
  detailedIngredients?: any[];
  setIsSubmitting: (status: boolean) => void;
  setError: UseFormSetError<EditFormData>;
  onProductUpdated: () => void;
  onClose: () => void;
}

export const submitProductForm = async ({
  data,
  imageFiles,
  currentLanguage,
  detailedIngredients,
  setSubmissionStatus,
  setError,
  onProductCreated,
  onClose,
  reset,
  setImageFiles,
}: SubmitProductFormParams) => {
  setSubmissionStatus('creating');
  try {
    // Format content for the API
    const content: { [key: string]: { name: string; description: string } } = {};

    // Automatically add the main product data to content using the current user language
    content[currentLanguage] = {
      name: data.name,
      description: data.description || ''
    };

    // Add any additional multilingual content
    data.content?.forEach(item => {
      if (item.language && item.language !== currentLanguage) {
        content[item.language] = {
          name: item.name,
          description: item.description || ''
        };
      }
    });

    // Process ingredients: check for new ones and create them globally
    const processedIngredients = await Promise.all((detailedIngredients || []).map(async (ing: any) => {
      // If it doesn't have a globalIngredientId, it might be new
      if (!ing.globalIngredientId && ing.name && ing.name.trim().length > 0) {
        // First check if it already exists (case-insensitive)
        try {
          const searchResponse = await searchGlobalIngredients(ing.name) as { success: boolean; data?: any[] };
          if (searchResponse.success && searchResponse.data) {
            const existing = searchResponse.data.find((item: any) => 
              item.defaultName.toLowerCase() === ing.name.toLowerCase()
            );
            if (existing) {
              return { ...ing, globalIngredientId: existing.id };
            }
          }
        } catch (e) {
          console.error("Failed to search global ingredient:", e);
        }

        // If not found, create it
        // Prepare translations
        const translations = [];
        if (ing.content) {
          for (const [lang, content] of Object.entries(ing.content)) {
            if ((content as any).name) {
              translations.push({
                languageCode: lang,
                name: (content as any).name
              });
            }
          }
        }
        
        // Also add the default name as English translation if not present, or just rely on defaultName
        if (translations.length > 0) {
           try {
             const newGlobalIngResponse = await createGlobalIngredient({
               defaultName: ing.name,
               translations: translations
             }) as { success: boolean; data?: { id: string } };

             if (newGlobalIngResponse.success && newGlobalIngResponse.data?.id) {
               return { ...ing, globalIngredientId: newGlobalIngResponse.data.id };
             }
           } catch (e) {
             console.error("Failed to auto-create global ingredient:", e);
             // Continue without ID, backend might handle or just save as local ingredient
           }
        }
      }
      return ing;
    }));

    // Clean detailedIngredients - remove temporary IDs for new ingredients
    const cleanedIngredients = processedIngredients.map((ing: any) => {
      const cleaned = { ...ing };
      // If ID starts with "temp-", it's a new ingredient - remove the ID
      if (typeof cleaned.id === 'string' && cleaned.id.startsWith('temp-')) {
        delete cleaned.id;
      }
      return cleaned;
    });



// ...

    // Format the product data
    const productData = {
      ...data,
      content,
      primaryCategoryId: data.primaryCategoryId || null,
      variations: data.variations || [],
      detailedIngredients: cleanedIngredients,
      menuDefinition: data.menuDefinition ? {
        ...data.menuDefinition,
        id: data.menuDefinition.id || null,
        sections: data.menuDefinition.sections?.map((section: any) => ({
          ...section,
          id: (section.id && (section.id.startsWith('temp-') || section.id === '')) ? null : section.id,
        })) || [],
        startTime: data.menuDefinition.startTime ? (data.menuDefinition.startTime.length === 5 ? `${data.menuDefinition.startTime}:00` : data.menuDefinition.startTime) : null,
        endTime: data.menuDefinition.endTime ? (data.menuDefinition.endTime.length === 5 ? `${data.menuDefinition.endTime}:00` : data.menuDefinition.endTime) : null,
      } : undefined
    };

    let productResponse;
    if (data.menuDefinition) {
       // It's a menu bundle
       productResponse = await createMenuBundle(productData) as { success: boolean; data?: { id: string }; message?: string };
    } else {
       productResponse = await createProduct(productData) as { success: boolean; data?: { id: string }; message?: string };
    }
    if (productResponse.success && productResponse.data?.id) {
      if (imageFiles.length > 0) {
        setSubmissionStatus('uploading');
        const imageResponse = await uploadBulkProductImages(productResponse.data.id, imageFiles) as { success: boolean; message?: string };
        if (!imageResponse.success) {
          // eslint-disable-next-line no-console
          console.error("Image upload failed:", imageResponse.message);
        }
      }
      onProductCreated();
      onClose();
      reset();
      setImageFiles([]);
    } else {
      setError('root', { message: productResponse.message || 'Failed to create product' });
    }
  } catch (error: any) {
    console.error("Submit error:", error);
    // Extract meaningful error message from backend response
    let errorMessage = 'An unexpected error occurred.';
    if (error?.response?.data) {
        if (error.response.data.errors) {
            // Combine validation errors
            errorMessage = Object.values(error.response.data.errors).flat().join(', ');
        } else if (error.response.data.title) {
            errorMessage = error.response.data.title;
        } else if (error.response.data.message) {
            errorMessage = error.response.data.message;
        }
    } else if (error?.message) {
        errorMessage = error.message;
    }
    setError('root', { message: errorMessage });
  } finally {
    setSubmissionStatus('idle');
  }
};

export const submitEditProductForm = async ({
  data,
  product,
  imageFiles,
  detailedIngredients,
  setIsSubmitting,
  setError,
  onProductUpdated,
  onClose,
}: SubmitEditProductFormParams) => {
  setIsSubmitting(true);
  try {
    const parseNum = (val: any, fallback: number): number => {
      const num = parseFloat(String(val || '').trim());
      return isNaN(num) ? fallback : num;
    };

    // Clean content array and format for API
    const cleanedContentArray = (data.content || [])
      .filter((e: any) => e?.language?.trim() && e?.name?.trim())
      .map((e: any) => ({
        language: String(e.language).trim(),
        name: String(e.name || '').trim(),
        description: (e.description ?? '').toString(),
      }));

    const formattedContent = cleanedContentArray.length > 0
      ? cleanedContentArray.reduce((acc: any, curr: any) => {
          acc[curr.language] = {
            name: curr.name,
            description: curr.description
          };
          return acc;
        }, {})
      : undefined;

    const categoryIds = Array.isArray(data.categoryIds) ? data.categoryIds.filter(Boolean) as string[] : [];
    let primaryCategoryId = (data.primaryCategoryId || '') as string;
    if (categoryIds.length > 0 && !categoryIds.includes(primaryCategoryId)) {
      primaryCategoryId = categoryIds[0];
    }

    const cleanedVariations = (data.variations || [])
      .filter(v => (v?.name || '').trim().length > 0)
      .map(v => ({
        id: v.id,
        name: (v.name || '').trim(),
        description: v.description ?? '',
        priceModifier: parseNum(v.priceModifier, 0),
        isActive: v.isActive ?? true,
        displayOrder: Number.isInteger(v.displayOrder as any) ? (v.displayOrder as any) : 0,
        content: v.content,
      }));

    // Process ingredients: check for new ones and create them globally
    const processedIngredients = await Promise.all((detailedIngredients || []).map(async (ing: any) => {
      // If it doesn't have a globalIngredientId, it might be new
      if (!ing.globalIngredientId && ing.name && ing.name.trim().length > 0) {
        // First check if it already exists (case-insensitive)
        try {
          const searchResponse = await searchGlobalIngredients(ing.name) as { success: boolean; data?: any[] };
          if (searchResponse.success && searchResponse.data) {
            const existing = searchResponse.data.find((item: any) => 
              item.defaultName.toLowerCase() === ing.name.toLowerCase()
            );
            if (existing) {
              return { ...ing, globalIngredientId: existing.id };
            }
          }
        } catch (e) {
          console.error("Failed to search global ingredient:", e);
        }

        // If not found, create it
        // Prepare translations
        const translations = [];
        if (ing.content) {
          for (const [lang, content] of Object.entries(ing.content)) {
            if ((content as any).name) {
              translations.push({
                languageCode: lang,
                name: (content as any).name
              });
            }
          }
        }
        
        // Also add the default name as English translation if not present, or just rely on defaultName
        if (translations.length > 0) {
           try {
             const newGlobalIngResponse = await createGlobalIngredient({
               defaultName: ing.name,
               translations: translations
             }) as { success: boolean; data?: { id: string } };

             if (newGlobalIngResponse.success && newGlobalIngResponse.data?.id) {
               return { ...ing, globalIngredientId: newGlobalIngResponse.data.id };
             }
           } catch (e) {
             console.error("Failed to auto-create global ingredient:", e);
             // Continue without ID
           }
        }
      }
      return ing;
    }));

    // Clean detailedIngredients - remove temporary IDs for new ingredients
    const cleanedIngredients = processedIngredients.map((ing: any) => {
      const cleaned = { ...ing };
      // If ID starts with "temp-", it's a new ingredient - remove the ID
      if (typeof cleaned.id === 'string' && cleaned.id.startsWith('temp-')) {
        delete cleaned.id;
      }
      return cleaned;
    });

    const productData = {
      ...data,
      id: product.id,
      name: (data.name || '').trim(),
      description: (data.description ?? '').toString(),
      basePrice: parseNum(data.basePrice, 0),
      preparationTimeMinutes: typeof data.preparationTimeMinutes === 'number' ? data.preparationTimeMinutes : parseInt(String(data.preparationTimeMinutes || '0'), 10) || 0,
      allergens: Array.isArray(data.allergens) ? data.allergens.filter(Boolean) : [],

      categoryIds: categoryIds || [],
      primaryCategoryId: primaryCategoryId || null,
      variations: cleanedVariations,
      content: formattedContent,
      detailedIngredients: cleanedIngredients,
      menuDefinition: data.menuDefinition ? {
        ...data.menuDefinition,
        id: data.menuDefinition.id || null,
        sections: data.menuDefinition.sections?.map((section: any) => ({
          ...section,
          id: (section.id && (section.id.startsWith('temp-') || section.id === '')) ? null : section.id,
        })) || [],
        startTime: data.menuDefinition.startTime ? (data.menuDefinition.startTime.length === 5 ? `${data.menuDefinition.startTime}:00` : data.menuDefinition.startTime) : null,
        endTime: data.menuDefinition.endTime ? (data.menuDefinition.endTime.length === 5 ? `${data.menuDefinition.endTime}:00` : data.menuDefinition.endTime) : null,
      } : undefined
    } as any;

    const response = await updateProduct(product.id, productData) as { success: boolean; message?: string };
    if (response.success) {
      if (imageFiles.length > 0) {
        await uploadBulkProductImages(product.id, imageFiles);
      }
      onProductUpdated();
      onClose();
    } else {
      setError('root', { message: response.message || 'Failed to update product' });
    }
  } catch {
    setError('root', { message: 'An unexpected error occurred.' });
  } finally {
    setIsSubmitting(false);
  }
};
